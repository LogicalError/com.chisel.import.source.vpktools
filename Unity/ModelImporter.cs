//#define USE_MdlModelComponent 
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System;
using System.Text;
using Chisel.Import.Source.VPKTools.Fbx;
using DebuggerDisplayAttribute = System.Diagnostics.DebuggerDisplayAttribute;

namespace Chisel.Import.Source.VPKTools
{
	#region Data (move out of this file)
	public class MdlMeshEntry
	{
		public int materialIndex;
		internal readonly List<int>	Indices				= new List<int>();
	}
		
	public class MdlSkeleton
	{
		public readonly List<Transform> Bones = new();
	}

	public class MdlMaterialTable
	{
		public VMT[] sourceMaterials;
		public Material[] unityMaterials;
	}

	public class MdlModelMaterials : MdlMaterialTable
	{
		public MdlMaterialTable[] skins;
	}

	[DebuggerDisplay("Name {Name}")]
	public class MdlModelEntry
	{
		public int		   LodIndex;
		public float       SwitchPoint;
		public string	   Name;
		public SourceModel    Model;
		public Studioflags Flags;
		public Renderer	   Renderer;
		public GameObject  MeshObject;
		public Mesh		   Mesh;

		public readonly List<Vector3>		Vertices	= new();
		public readonly List<Vector4>		Tangents	= new();
		public readonly List<Vector3>		Normals		= new();
		public readonly List<Vector2>		Uvs			= new();
		public readonly List<BoneWeight>	BoneWeights = new();
		public readonly List<MdlMeshEntry>	SubMeshes   = new();

		/// <summary>
		/// The iris uv of every vertex of an eyeball mesh, and zero everywhere else. See <see cref="EyeProjection"/>:
		/// it has to travel with the vertex rather than sit on the material, because these meshes are skinned and a
		/// skinned vertex reaches the shader already posed - object space is no longer the eyeball's.
		/// </summary>
		public readonly List<Vector2>		EyeUvs		= new();

		/// <summary>True once any eyeball mesh has contributed a uv, so a model without eyes writes no uv2 at all.</summary>
		public bool HasEyeUvs;
		

		public void GenerateMesh(GameObject parentGameObject, MdlSkeleton skeleton, Matrix4x4 sourceRestPose)
		{
			if (string.IsNullOrWhiteSpace(Name))
				Name = "unnamed mesh";

			Mesh = null;
			MeshObject = new GameObject(Name);
			if (Vertices.Count > 0)
			{
				// TODO: Is this per skeleton? Can we share this?
				//{{
				Matrix4x4[] bindposes = null;
				if (skeleton != null && skeleton.Bones.Count > 0)
				{
					bindposes = new Matrix4x4[skeleton.Bones.Count];
					for (int i = 0; i < skeleton.Bones.Count; i++)
						bindposes[i] = skeleton.Bones[i].worldToLocalMatrix;
				}
				//}}

				var vertices = Vertices.ToArray();
				var normals = Normals.ToArray();
				var tangents = (Tangents.Count != Vertices.Count) ? null : Tangents.ToArray();

				// The pose the engine actually draws this mesh in. Source renders every model as
				//     animation * poseToBone * vertex
				// and a model with no skeleton of its own still plays its default sequence on its single bone.
				// Unity poses a skinned model from its animation by itself, but a single-bone model is imported
				// as a plain mesh, so that bone's default pose has to go into the vertices here - or the mesh
				// sits at its reference pose instead of where the level was laid out against it
				// (props_canteen/soda_machine and props_vehicles/inbound_tram_door: +90 degrees about Source Z;
				// props_vehicles/cargo_tram_02 and props_office/office_phone: 120 degrees, a cyclic swap of the
				// axes). It is the identity for every model whose default pose is its reference pose, which is
				// most of them.
				//
				// This replaces ApplyHullAxisRotation, which compared the vertex bounds with the studiohdr hull
				// and permuted the axes to match. For the phone that permutation was exactly this rotation,
				// found through its side effect on the bounds; and it had to refuse every model whose rotation
				// swaps two axes as though it were a mirror, which a 90 degree turn is not.
				if (sourceRestPose != Matrix4x4.identity)
				{
					for (int i = 0; i < vertices.Length; i++)
						vertices[i] = sourceRestPose.MultiplyPoint3x4(vertices[i]);
					for (int i = 0; i < normals.Length; i++)
						normals[i] = sourceRestPose.MultiplyVector(normals[i]);
					if (tangents != null)
					{
						for (int i = 0; i < tangents.Length; i++)
						{
							var direction = sourceRestPose.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z));
							tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
						}
					}
				}

				// ONE basis for every model, skinned or not - and the same one the bones and their
				// animation go through (ConvertBoneTransform), because a skin only deforms correctly
				// when its vertices and its bones share a frame.
				//
				// There used to be two. The non-skinned one was InvModelMatrix * Rx(-90) *
				// VmfSourceToUnity, whose first two factors are exact inverses: it was always just
				// VmfSourceToUnity. The skinned one added Quaternion.Euler(90, 0, 90) and a swizzle,
				// which works out to exactly a +90 degree rotation about Source's up axis applied to
				// the vertices. That is not a property of skinned meshes. It is the rotation studiomdl
				// compiles into the root bone of a model's ANIMATION relative to its reference pose -
				// in 380 of 458 root-bone animations sampled across 230 multi-bone models it is exactly
				// +90 degrees about +Z - and
				// the engine renders the animated pose, which is what the level was laid out against.
				// Baking it into the vertices made the bind pose look right for as long as nothing was
				// animated. Once the model file carried its animation, Unity posed the root from it and
				// the rotation was applied a second time, turning every skinned model 90 degrees in the
				// map; and it had always been wrong for the models whose animation carries no such
				// rotation (the rest are 0 or 180 degrees, or genuinely move). With a single basis, a
				// model with a skeleton is posed exactly as Source renders it, whichever kind it is.
				var transformation = SourceEngineUnits.VmfSourceToUnity;

				for (int i = 0; i < vertices.Length; i++)
				{
					vertices[i] = transformation.MultiplyPoint(vertices[i]);
				}
				// transformation is a uniform scale times an orthogonal matrix, so it leaves
				// directions pointing where they were and only rescales them; normalizing
				// restores exactly the authored direction at unit length, which is what Unity
				// expects to find in a mesh's normals and tangents.
				for (int i = 0; i < normals.Length; i++)
				{
					normals[i] = transformation.MultiplyVector(normals[i]).normalized;
				}
				if (tangents != null)
				{
					for (int i = 0; i < tangents.Length; i++)
					{
						// GOTCHA: MultiplyVector takes a Vector3, so a Vector4 argument converts
						//		   down (dropping w) and the Vector3 result converts back up with
						//		   w = 0 - both implicitly, so it compiles and reads as correct.
						//		   w is Source's tangent-space handedness, and Unity builds the
						//		   bitangent as cross(normal, tangent.xyz) * tangent.w, so losing
						//		   it left every imported mesh with a zero bitangent and no usable
						//		   tangent frame. Carry the direction through here; w is recovered
						//		   below, once the triangles exist to derive it from.
						var direction = transformation.MultiplyVector(tangents[i]).normalized;
						tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
					}
				}

				Mesh = new Mesh
				{
					vertices	 = vertices,
					normals		 = normals,
					tangents	 = tangents,
					boneWeights	 = (skeleton == null || skeleton.Bones.Count == 0 || BoneWeights.Count != Vertices.Count) ? null : BoneWeights.ToArray(),
					bindposes	 = (skeleton == null || skeleton.Bones.Count == 0 || bindposes == null) ? null : bindposes,
					uv			 = Uvs.ToArray(),
					// uv2, because the importer leaves generateSecondaryUV off, so Unity will not overwrite it with
					// lightmap uvs. A model with no eyeball carries none of this at all.
					uv2			 = (EyeUvs.Count != Vertices.Count || !HasEyeUvs) ? null : EyeUvs.ToArray(),
					subMeshCount = SubMeshes.Count,
					name	     = Name,
					bounds		 = Model.MDL.ViewBounds
				};

				for (var i = 0; i < SubMeshes.Count; i++)
				{
					Mesh.SetTriangles(SubMeshes[i].Indices.ToArray(), i);
				}

				if (tangents == null)
				{
					Mesh.RecalculateTangents();
				} else
				{
					// Recover the handedness dropped above. Source's own w cannot simply be
					// carried across: the conversion mirrors the model (VmfSwizzle has
					// determinant -1) and VmfFixTexcoord negates v - each of which flips
					// handedness - and Source's bitangent convention need not match Unity's to
					// begin with. That is three sign conventions to get right by inspection, and
					// getting them wrong inverts normal mapping everywhere instead of just at a
					// seam, which is far harder to notice.
					// So derive w from the finished mesh instead: Unity's own tangent frame is by
					// definition the convention its shaders use. Take only its w and keep the
					// direction studiomdl authored, which carries the model's smoothing that a
					// per-triangle recalculation does not.
					Mesh.RecalculateTangents();
					var generated = Mesh.tangents;
					for (int i = 0; i < tangents.Length; i++)
					{
						var w = (generated[i].w < 0) ? -1 : 1;
						// A vertex whose uvs are degenerate gets no meaningful frame from either
						// side; a disagreement about which way u runs means the two frames only
						// line up with the handedness flipped along with the tangent.
						if (Vector3.Dot(tangents[i], generated[i]) < 0)
							w = -w;
						tangents[i].w = w;
					}
					Mesh.tangents = tangents;
				}
				Mesh.RecalculateBounds();

				if (skeleton != null && skeleton.Bones.Count > 0)
				{
					var skinnedMeshRenderer = MeshObject.AddComponent<SkinnedMeshRenderer>();
					skinnedMeshRenderer.sharedMesh			= Mesh;
					skinnedMeshRenderer.bones				= skeleton.Bones.ToArray();
					skinnedMeshRenderer.updateWhenOffscreen = true;
					if ((Model.MDL.Flags & Studioflags.DoNotCastShadows) == Studioflags.DoNotCastShadows)
						skinnedMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

					// TODO: figure out the root-bones (zombie -> headcrab needs a different rootbone than body? maybe?)
					//skinnedMeshRenderer.rootBone = skeleton.Bones[0];
					Renderer = skinnedMeshRenderer;
				} else
				{
					var meshFilter = MeshObject.AddComponent<MeshFilter>();
					var meshRenderer = MeshObject.AddComponent<MeshRenderer>();
					meshFilter.sharedMesh = Mesh;
					if ((Model.MDL.Flags & Studioflags.DoNotCastShadows) == Studioflags.DoNotCastShadows)
						meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

					Renderer = meshRenderer;
				}
			}

			var transform = MeshObject.transform;
			transform.SetParent(parentGameObject.transform, false);
			transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			transform.localScale = Vector3.one;
		}
	}
	#endregion

	public static class ModelImporter
	{
		public static GameObject Import(GameResources gameResources, SourceModel model, string outputPath, int skin)
		{
			return SkinOf(Import(gameResources, model, outputPath), skin);
		}

		/// <summary>
		/// The prefab for one skin, of a model's prefabs (its file's, then its skins' variants): a skin the model does not have
		/// gives the default skin, as in the game, and one that could not be made the first that was.
		/// </summary>
		public static GameObject SkinOf(GameObject[] skinnedModels, int skin)
		{
			if (skinnedModels == null || skinnedModels.Length == 0)
				return null;

			if (skin < 0 || skin >= skinnedModels.Length)
				skin = 0;
			var prefab = skinnedModels[skin];
			if (prefab == null)
			{
				for (skin = 0; skin < skinnedModels.Length; skin++)
				{
					prefab = skinnedModels[skin];
					if (prefab != null)
						break;
				}
			}
			return prefab;
		}

		/// <summary>
		/// Models are written out as FBX and then imported by Unity's own model importer, rather than
		/// built directly as prefabs.
		/// <para>
		/// The prefab route could not express what a model actually is. LOD groups had to be assembled
		/// by hand, meshes and clips had to be bolted on as sub-assets of the prefab (which is why it
		/// had to be saved two and three times over), and animation had nowhere to live that Unity's
		/// animation tooling could see. Going through a model file instead means Unity builds the LOD
		/// group, the avatar and the clips itself, from a file that can be reimported, inspected and
		/// swapped out - the same as any other model in the project.
		/// </para>
		/// </summary>
		public const string kModelExtension = ".fbx";

		/// <summary>
		/// The versions of what this writes for a model. Bump the one that covers a change to the output,
		/// and every file it covers rebuilds, in place, on its next import (see
		/// <see cref="GeneratedAssetVersion"/>):
		/// <list type="bullet">
		/// <item><c>ModelVersion</c> - model files: meshes, LODs, the hierarchy, skins and skin variants,
		/// collision, and the default pose baked into single-bone meshes.</item>
		/// <item><c>AnimationVersion</c> - animation and what it rests on: MdlAnimationDecoder,
		/// BuildAnimationTakes, the skeleton, how FbxExporter writes nodes, skins and takes, and
		/// ConfigureImportedModel. Model files carry skeletons and takes as well, so they record this
		/// version too. The shared animation files record only this one, which keeps a model-only change
		/// from exporting them again - they are by far the slowest thing this importer writes.</item>
		/// </list>
		/// When unsure which one a change falls under, bump AnimationVersion: it rebuilds both.
		/// </summary>
		// 5: an empty material slot no longer renumbers the submeshes after it (FbxExporter.CollectMaterials).
		// Every model exported before this that had one is wearing shifted materials, with its last submesh drawn
		// by Unity's white default, so they all have to be written again rather than only the ones anyone noticed.
		// 6: eyeball meshes carry an iris uv in uv2 (EyeProjection), and mstudiomodel_t is stepped by its real 148
		// bytes rather than 124 - which made every bodypart after its first model readable, so the loop now says
		// out loud that it takes model 0. Every model with eyes has to be written again to gain the uv.
		// 7: that uv spans the eyeball's IRIS SIZE rather than its radius, which is a different number per model
		// and was drawing every iris too small.
		// 8: an eyeball vertex's normal points out from the eye's centre, as Source computes it per vertex,
		// instead of the mesh's own - which on these ten-triangle bands is not the sphere's.
		// 9: the iris uv's v runs up the eye. It ran down, which is Source's own D3D convention unflipped, so every
		// eye wore its iris and cornea textures upside down.
		internal static readonly (string generator, int version) ModelVersion     = ("model", 9);
		internal static readonly (string generator, int version) AnimationVersion = ("animation", 1);

		/// <summary>
		/// What every collision node's name starts with. A model file cannot mark a mesh as a collider, so
		/// <see cref="SourceCollisionPostprocessor"/> recognises the nodes by this.
		/// </summary>
		internal const string kCollisionNodePrefix = "#collision ";

		/// <summary>What the names of the prop set's nodes start with (<see cref="ModelCollisionSet.Prop"/>).</summary>
		internal const string kPropCollisionPrefix = kCollisionNodePrefix + "prop ";

		/// <summary>What the names of the ragdoll set's nodes start with (<see cref="ModelCollisionSet.Ragdoll"/>).</summary>
		internal const string kRagdollCollisionPrefix = kCollisionNodePrefix + "ragdoll ";

		/// <summary>
		/// <c>mstudiomesh_t.materialtype</c> for an eyeball, whose <c>materialparam</c> is then the index of the
		/// <see cref="StudioEyeball"/> it is drawn as. Every other mesh carries 0.
		/// </summary>
		internal const int kEyeballMaterialType = 1;

		internal static bool IsCollisionNode(string name)
		{
			return name != null && name.StartsWith(kCollisionNodePrefix, StringComparison.Ordinal);
		}

		/// <summary>Whether the model file this importer imports was written by <see cref="ModelImporter"/>.</summary>
		internal static bool IsGeneratedModel(AssetImporter importer)
		{
			return importer != null && GeneratedAssetVersion.HasGenerator(importer.userData, ModelVersion.generator);
		}

		public static GameObject[] Import(GameResources gameResources, SourceModel model, string outputPath)
		{
			return ImportAll(gameResources, new[] { new Request(() => model, outputPath) })[0];
		}

		/// <summary>A model <see cref="ImportAll"/> is asked for: where it is written, and its files, read when they are
		/// needed - null when they do not read.</summary>
		public readonly struct Request
		{
			public readonly Func<SourceModel> Read;
			public readonly string            OutputPath;

			public Request(Func<SourceModel> read, string outputPath)
			{
				Read       = read;
				OutputPath = outputPath;
			}
		}

		/// <summary>
		/// Imports a model for each request, each as <see cref="Import"/> did one at a time, but together: the materials of all
		/// of them first, in one MaterialImporter.ImportAll (one refresh for their textures, one for themselves); then every
		/// model file that has to be written is written with its .meta (ImportOnce's writing) and all of them are imported in
		/// ONE asset-editing block, where each took a refresh of its own;
		/// then what follows a model file's import: its skins' variants, all of them saved in one block more
		/// (<see cref="SaveSkinVariants"/>; 73 refreshes of their own on that map). The animation a model shares with others is
		/// written before the block, which a refresh it takes may import a model file early for, as written: one imported
		/// since is not imported twice.
		/// <para>
		/// A model is read where it is needed and let go of again: kept, every model of a map would be in memory at once. One
		/// whose files do not read comes back null; one current and still as written comes back as it is.
		/// </para>
		/// </summary>
		/// <param name="progress">Told the index of each model it writes and the count, for a progress bar.</param>
		/// <returns>For each request its prefabs - the model file's, then its skins' variants - or null.</returns>
		public static GameObject[][] ImportAll(GameResources gameResources, IReadOnlyList<Request> requests, Action<int, int> progress = null)
		{
			var results = new GameObject[requests.Count][];

			// The materials of every model, together, each model read only for as long as it takes to ask. A model's import
			// asks for its own again (ImportModelTextures), and finds them current; they are versioned on their own, so a
			// current model file asks too.
			PrepareMaterials(gameResources, ReadEach(requests));

			var written = new List<WrittenModel>();
			for (int i = 0; i < requests.Count; i++)
			{
				progress?.Invoke(i, requests.Count);
				var outputPath = requests[i].OutputPath;
				var destinationPath = Path.ChangeExtension(outputPath, kModelExtension);
				var assetPath = UnityAssets.GetAssetPath(destinationPath);
				var foundAsset = UnityAssets.Load<GameObject>(destinationPath);
				var model = requests[i].Read();
				var current = foundAsset != null && GeneratedAssetVersion.IsCurrent(assetPath, ModelVersion, AnimationVersion);
				var asWritten = current && ImportedAsWritten(assetPath);
				if (current && !asWritten && model != null)
					Debug.Log($"{assetPath}: its import settings were changed after it was written, so it is written again.");
				if (foundAsset != null && (model == null || asWritten))
				{
					// The materials are versioned on their own: an outdated one is rebuilt in place, where the model file's
					// links still find it, and a missing one is made again.
					if (model != null)
						ImportModelTextures(gameResources, model);

					var models = LoadSkinVariants(outputPath, foundAsset);
					// A model file can be current while the materials it links to are gone - deleted along with the
					// materials folder, say - and Unity would bring it in without them. A material made again is a new
					// asset, so the model file is built again, which links the materials anew.
					if (model == null || !LinksToMissingMaterials(assetPath, models))
					{
						// The animation this model shares with others is versioned on its own, so it can be out of
						// date when the model is not.
						ImportIncludedAnimations(gameResources, model);
						results[i] = models;
						continue;
					}
				}

				if (model == null)
					continue;

				var write = WriteModel(gameResources, model, outputPath);
				if (write == null)
					continue;
				write.Index = i;
				written.Add(write);
			}

			// Every model file written with its .meta, imported together
			var aheads = new List<MetaWritten>();
			foreach (var write in written)
			{
				if (write.Meta != null)
					aheads.Add(write.Meta);
			}
			ImportWritten(aheads);

			// What every model file imported comes out as, its skins' variants saved together
			var files = new List<SkinnedFile>();
			foreach (var write in written)
			{
				var imported = UnityAssets.Load<GameObject>(write.DestinationPath);
				if (imported == null)
				{
					Debug.LogError($"Failed to import the model written to {write.AssetPath}");
					continue;
				}
				files.Add(new SkinnedFile(imported, write.OutputPath, write.Materials, write.Index));
			}
			var skinned = SaveSkinVariants(files);
			for (int f = 0; f < files.Count; f++)
				results[files[f].Index] = skinned[f];
			return results;
		}

		// Each request's model, read as it is come to: the one before is let go of by then
		static IEnumerable<SourceModel> ReadEach(IReadOnlyList<Request> requests)
		{
			foreach (var request in requests)
				yield return request.Read();
		}

		static readonly (string generator, int version)[] s_ModelVersions = { ModelVersion, AnimationVersion };

		/// <summary>
		/// A model file written by <see cref="WriteModel"/>, and what follows its import: what it is checked against, and its
		/// materials, from which its skins' variants are made.
		/// </summary>
		sealed class WrittenModel
		{
			internal int Index;
			internal string OutputPath;
			internal string DestinationPath;
			internal string AssetPath;
			internal MdlModelMaterials Materials;
			/// <summary>Its .meta, written with it (ImportOnce's writing), for ImportAll to import it with the others; null
			/// when it was imported already, the slow way.</summary>
			internal MetaWritten Meta;
		}

		/// <summary>A model file written with its .meta (<see cref="WriteAhead"/>), for <see cref="ImportWritten"/> to import
		/// with others: what its import is checked against.</summary>
		internal sealed class MetaWritten
		{
			internal string AssetPath;
			internal FbxExporter.Result Exported;
			internal (string generator, int version)[] Versions;
			internal string UserData;
			internal string PreviousUserData;

			/// <summary>Imported since it was written, as written: a refresh in between came across it.</summary>
			internal bool ImportedSince() =>
				UserData != PreviousUserData && AssetImporter.GetAtPath(AssetPath) is AssetImporter importer && importer.userData == UserData;
		}

		/// <summary>
		/// Writes a model file's .meta as ImportOnce does, without importing it: for <see cref="ImportWritten"/> to import it
		/// with every other one written so. Null, with nothing written, when there is no template yet or a material is not
		/// one this writes.
		/// </summary>
		internal static MetaWritten WriteAhead(string assetPath, FbxExporter.Result exported, int lodCount, params (string generator, int version)[] versions)
		{
			if (!WriteMeta(assetPath, exported, lodCount, versions, out var userData, out var previousUserData))
				return null;
			return new MetaWritten { AssetPath = assetPath, Exported = exported, Versions = versions, UserData = userData, PreviousUserData = previousUserData };
		}

		/// <summary>
		/// Imports model files written ahead (<see cref="WriteAhead"/>) in ONE asset-editing block, where ImportOnce takes a
		/// refresh each, and puts right what did not import as written, as ImportOnce does. One a refresh in between imported
		/// already is not imported twice.
		/// </summary>
		internal static void ImportWritten(IReadOnlyList<MetaWritten> written)
		{
			if (written.Count == 0)
				return;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (var write in written)
				{
					if (!write.ImportedSince())
						AssetDatabase.ImportAsset(write.AssetPath, ImportAssetOptions.ForceSynchronousImport);
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (var write in written)
				RepairModel(write.AssetPath, write.Exported, write.Versions);
		}

		// Writes a model's file - its hierarchy built, the FBX exported - and its .meta when there is a template for it, for
		// ImportAll to import with the others; without one it is imported now, the slow way. The animation it shares with
		// others is written now too, so the model can be let go of. Null when its meshes do not load.
		static WrittenModel WriteModel(GameResources gameResources, SourceModel model, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, kModelExtension);
			var assetPath = UnityAssets.GetAssetPath(destinationPath);
			var modelMaterials = ImportModelTextures(gameResources, model);

			var modelEntries = ImportModelLods(gameResources, model, out MdlSkeleton skeleton);
			if (modelEntries == null)
			{
				Debug.LogError("Failed to load model");
				return null;
			}

			PackagePath.EnsureDirectoriesExist(outputPath);

			var haveMultipleSkins = modelMaterials.skins != null && modelMaterials.skins.Length > 0;
			var defaultSkin = (!haveMultipleSkins || modelMaterials.skins[0] == null) ? modelMaterials : modelMaterials.skins[0];

			// The hierarchy exists only long enough to be written out. It is built outside any scene
			// and destroyed below, so nothing is dirtied and nothing survives the import but the file.
			var root = new GameObject(model.MDL.Name);
			var collisionMeshes = new List<Mesh>();
			FbxExporter.Result exported;
			int lodCount;
			try
			{
				// A model with a skeleton is posed from its animation by Unity itself; one without has its
				// default pose put into the mesh.
				var restPose = (skeleton == null) ? DefaultPoseOfSingleBone(gameResources, model) : Matrix4x4.identity;
				BuildHierarchy(root, model, modelEntries, skeleton, restPose);
				// One LOD per _LOD name in the file, which is what Unity's importer builds the LODGroup from
				lodCount = root.TryGetComponent<LODGroup>(out var lodGroup) ? lodGroup.lodCount : 1;
				SetSkin(root, modelEntries, defaultSkin);
				BuildCollision(root, model, skeleton, restPose, collisionMeshes);
				var takes = BuildAnimationTakes(gameResources, model.MDL, model.MDLEntry, skeleton);
				exported = FbxExporter.Export(destinationPath, root, takes);
			}
			finally
			{
				// The meshes used to survive as sub-assets of the prefab. Nothing owns them now - the
				// FBX carries the geometry and Unity builds its own meshes from it - so they have to
				// be destroyed explicitly or every imported model leaks one per LOD per bodypart.
				foreach (var modelEntry in modelEntries)
				{
					if (modelEntry?.Mesh != null)
						Object.DestroyImmediate(modelEntry.Mesh);
				}
				foreach (var mesh in collisionMeshes)
					Object.DestroyImmediate(mesh);
				GameObject.DestroyImmediate(root);
			}

			var write = new WrittenModel
			{
				OutputPath      = outputPath,
				DestinationPath = destinationPath,
				AssetPath       = assetPath,
				Materials       = modelMaterials,
				Meta            = WriteAhead(assetPath, exported, lodCount, s_ModelVersions),
			};
			if (write.Meta == null)
			{
				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
				ConfigureImportedModel(assetPath, exported, ModelVersion, AnimationVersion);
			}

			// Whatever animation this model shares with others goes into its own file, once, rather
			// than being baked into every model that references it.
			ImportIncludedAnimations(gameResources, model);
			return write;
		}

		/// <summary>An imported model file whose skins' variants <see cref="SaveSkinVariants"/> saves: where it was written,
		/// and the materials of its skins.</summary>
		internal readonly struct SkinnedFile
		{
			internal readonly GameObject        Imported;
			internal readonly string            OutputPath;
			internal readonly MdlModelMaterials Materials;
			/// <summary>Which of ImportAll's requests it is for.</summary>
			internal readonly int               Index;

			internal SkinnedFile(GameObject imported, string outputPath, MdlModelMaterials materials, int index = 0)
			{
				Imported   = imported;
				OutputPath = outputPath;
				Materials  = materials;
				Index      = index;
			}
		}

		/// <summary>
		/// The prefabs of model files just imported: each file's, then a variant over it for each further skin - every variant
		/// of every file saved in ONE asset-editing block, where each SaveAsPrefabAsset was a refresh of its own
		///. Inside the block a variant is written, not imported, so each
		/// is loaded once the block has ended. A skin a variant could not be made for is null, as before.
		/// </summary>
		internal static GameObject[][] SaveSkinVariants(IReadOnlyList<SkinnedFile> files)
		{
			var results = new GameObject[files.Count][];
			var saved = new List<(int file, int skin, string assetPath)>();
			AssetDatabase.StartAssetEditing();
			try
			{
				for (int f = 0; f < files.Count; f++)
				{
					var imported = files[f].Imported;
					var modelMaterials = files[f].Materials;
					var haveMultipleSkins = modelMaterials?.skins != null && modelMaterials.skins.Length > 0;
					if (imported == null || !haveMultipleSkins)
					{
						results[f] = (imported == null) ? null : new GameObject[1] { imported };
						continue;
					}
					var defaultSkin = (modelMaterials.skins[0] == null) ? modelMaterials : modelMaterials.skins[0];

					// One file, one mesh - the skins differ only in which materials the renderers use, so the
					// rest become prefab variants over the imported model instead of copies of it.
					results[f] = new GameObject[modelMaterials.skins.Length];
					results[f][0] = imported;
					for (int i = 1; i < modelMaterials.skins.Length; i++)
					{
						if (modelMaterials.skins[i] == null)
							continue;

						var instance = PrefabUtility.InstantiatePrefab(imported) as GameObject;
						if (instance == null)
							continue;

						try
						{
							ApplySkinVariant(instance, defaultSkin, modelMaterials.skins[i]);
							var assetPath = UnityAssets.GetAssetPath(SkinVariantPath(files[f].OutputPath, i)).Replace('\\', '/');
							PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
							saved.Add((f, i, assetPath));
						}
						finally
						{
							GameObject.DestroyImmediate(instance);
						}
					}
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			foreach (var (f, i, assetPath) in saved)
				results[f][i] = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
			return results;
		}

		// A skin differs from the default skin only in which material each slot uses, and the
		// two tables are aligned slot for slot - so a variant is built by swapping one material
		// for the other wherever it appears, not by finding each renderer by name. Names do not
		// survive the model file: when a file has a single top-level node, Unity folds that node
		// into the file's root and renames it after the file, so './traffic_warninglight
		// +studio[0]' comes back as 'traffic_warninglight', every lookup missed, and every skin
		// variant of every such model silently kept the default skin's materials.
		static void ApplySkinVariant(GameObject instance, MdlMaterialTable fromSkin, MdlMaterialTable toSkin)
		{
			if (instance == null || fromSkin?.unityMaterials == null || toSkin?.unityMaterials == null)
				return;

			var replacements = new Dictionary<Material, Material>();
			var ambiguous    = new HashSet<Material>();
			var slots        = Mathf.Min(fromSkin.unityMaterials.Length, toSkin.unityMaterials.Length);
			for (int slot = 0; slot < slots; slot++)
			{
				var from = fromSkin.unityMaterials[slot];
				var to   = toSkin.unityMaterials[slot];
				if (from == null)
					continue;
				if (replacements.TryGetValue(from, out var existing))
				{
					// The same default material in two slots that this skin replaces differently
					// cannot be told apart by material alone. The first slot's replacement wins;
					// saying so beats a variant that is quietly half right.
					if (existing != to)
						ambiguous.Add(from);
					continue;
				}
				replacements.Add(from, to);
			}

			foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
			{
				var materials = renderer.sharedMaterials;
				var changed   = false;
				for (int i = 0; i < materials.Length; i++)
				{
					// A skin slot with no material of its own keeps the default one rather than
					// rendering with none.
					if (materials[i] == null || !replacements.TryGetValue(materials[i], out var replacement) || replacement == null)
						continue;
					materials[i] = replacement;
					changed = true;
				}
				if (changed)
					renderer.sharedMaterials = materials;
			}

			foreach (var material in ambiguous)
				Debug.LogWarning($"[Chisel] {instance.name}: material '{material.name}' fills more than one slot and this skin " +
								 "replaces them differently; the variant uses the first slot's replacement for both");
		}

		/// <summary>
		/// Builds the bone hierarchy of a model as Transforms.
		/// <para>
		/// Shared by the two kinds of model file Source has: one with a mesh, and one with nothing but
		/// a skeleton and animation, which is how the game shares a set of animations across every
		/// character that uses the same rig. Both need the identical skeleton - that is the whole
		/// basis on which the shared animation applies to the character.
		/// </para>
		/// <para>
		/// Returns null for a model with one bone or none. Source gives every model at least one bone
		/// whether or not it is skinned, so a single bone means "not really a skeleton".
		/// </para>
		/// </summary>
		/// <summary>
		/// The plane pair Source projects an eyeball's iris with, resolved once per eyeball into the model's own
		/// space so it can be evaluated per vertex.
		/// <para>
		/// Source rebuilds $IrisU and $IrisV every frame from the eye attachment, which is how an eye tracks what it
		/// is looking at, and an imported scene has no such runtime. What it does have is the eyeball itself:
		/// mstudioeyeball_t gives the centre, the up and forward vectors and the radius, in the eye bone's space.
		/// Resolving them at the bind pose gives the planes for an eye looking straight ahead, which is the pose the
		/// model was authored in.
		/// </para>
		/// <para>
		/// Everything here stays in SOURCE space, alongside the vertices it is measured against. The conversion to
		/// Unity swizzles two axes, which flips handedness, so a cross product taken after it would come out
		/// mirrored; taken before, and with the result being two dot products, the uv is simply carried through
		/// unchanged by whatever transform the vertices go on to have.
		/// </para>
		/// </summary>
		public readonly struct EyeProjection
		{
			readonly Vector3 origin;
			readonly Vector3 uAxis;
			readonly Vector3 vAxis;
			readonly float   span;      // how much of the eye the iris texture covers, in the model's units

			EyeProjection(Vector3 origin, Vector3 uAxis, Vector3 vAxis, float span)
			{
				this.origin = origin;
				this.uAxis  = uAxis;
				this.vAxis  = vAxis;
				this.span   = span;
			}

			/// <summary>The bind pose of one bone, in the model's space: its own transform and every parent's.</summary>
			static Matrix4x4 BoneToModel(MDL mdlHeader, int boneIndex)
			{
				var matrix = Matrix4x4.identity;
				if (mdlHeader?.Bones == null)
					return matrix;
				// A malformed parent chain must not spin here, and a bone is never its own ancestor in a sane file
				for (int guard = 0; guard <= mdlHeader.Bones.Length; guard++)
				{
					if (boneIndex < 0 || boneIndex >= mdlHeader.Bones.Length)
						break;
					var bone = mdlHeader.Bones[boneIndex];
					matrix = Matrix4x4.TRS(bone.Position, bone.Quaternion, Vector3.one) * matrix;
					boneIndex = bone.ParentBone;
				}
				return matrix;
			}

			public static EyeProjection Build(MDL mdlHeader, StudioEyeball eyeball)
			{
				var boneToModel = BoneToModel(mdlHeader, eyeball.Bone);
				var origin  = boneToModel.MultiplyPoint3x4(eyeball.Origin);
				var up      = boneToModel.MultiplyVector(eyeball.Up).normalized;
				var forward = boneToModel.MultiplyVector(eyeball.Forward).normalized;

				// u runs along the eye's LEFT and v along its up. The engine rewrites $IrisU and $IrisV every frame, so
				// their shader defaults ([0 1 0 0], [0 0 1 0]) say nothing; what pins the signs down is Valve's own
				// shader decoding the cornea texture. Its frame is T = cross(up, N), B = cross(N, T), and eye-cornea's
				// R rises toward the file's right and G toward its top: that reads as a convex dome only if u grows
				// toward the eye's left and v toward its bottom. In Source's frame - x forward, y left, z up -
				// cross(up, forward) is exactly that left: cross((0,0,1), (1,0,0)) is (0,1,0). Naming the axes rather
				// than copying numbers is what makes the projection follow an eye that looks somewhere else.
				var left = Vector3.Cross(up, forward);
				if (left.sqrMagnitude < 1e-12f)
					left = Vector3.Cross(up, Vector3.forward);      // degenerate up/forward: keep a usable pair

				// What the texture spans is the IRIS, not the eyeball. IrisScale is 1 / the iris size at the end
				// of the QC's eyeball command, and it is an independent number: across the 40 models in the game
				// with eyes, a radius of 0.5 appears with iris sizes of 0.63, 0.73, 0.83, 1.0 and 1.5, and the
				// ichthyosaur pairs radius 3.575 with 6.0. Spanning 2 * radius instead drew every iris too small
				// by a different factor per model - 1.26x on a scientist, 1.68x on the ichthyosaur, 2x on an
				// alien grunt.
				//
				// Twice the iris size, because this is Source's SPHERE uv: the cornea's uv spans the iris size
				// itself and vSphereUv = vCorneaUv * 0.5 + 0.25 (eye_refract_ps2x.fxc:205). That puts the iris
				// disc - uv radius 0.2, by Valve's own note - at half the eyeball's width on a scientist, which
				// is about where a human iris is.
				var span = (eyeball.IrisScale > 1e-6f) ? 2.0f / eyeball.IrisScale
													   : 2.0f * Mathf.Max(eyeball.Radius, 1e-4f);
				return new EyeProjection(origin, left.normalized, up, span);
			}

			/// <summary>
			/// The eye's surface normal at a vertex: straight out from the eyeball's centre. Source computes exactly
			/// this per vertex - "Normal = (Pos - Eye origin)", eye_refract_vs20.fxc:126 - rather than using the
			/// mesh's own normal, which keeps the eye reading as a ball when the mesh is a ten-triangle band. Baked
			/// here it is the same per-vertex value, and a skinned normal rides with the head the way the vertex does.
			/// </summary>
			public Vector3 SphereNormal(Vector3 position)
			{
				var outward = position - origin;
				// A vertex AT the centre has no direction of its own; face it the way the eye looks
				return (outward.sqrMagnitude > 1e-12f) ? outward.normalized : Vector3.Cross(uAxis, vAxis).normalized;
			}

			/// <summary>
			/// Where one vertex lands on the iris texture. Source's own relation between the two uvs it uses is
			/// vSphereUv = vCorneaUv * 0.5 + 0.25 (eye_refract_ps2x.fxc:205), so the eyeball's full width spanning
			/// [0,1] here is the SPHERE uv, and the cornea - a crop of the middle of the same picture - is the
			/// shader's business to derive from it.
			/// </summary>
			public Vector2 Project(Vector3 position)
			{
				var offset = position - origin;
				// v runs UP the eye. Source's own v runs down it - the engine builds $IrisV from the eye's up vector
				// negated, which is why eye_refract_vs20.fxc recovers "vEyeSocketUpVector" as normalize(-IrisV) - so
				// that a texture's top row, which is where D3D starts, lands at the top of the eye. A Unity texture
				// starts at its bottom row, and the flip VmfFixTexcoord applies to an authored uv lands the same
				// texel here: the iris's painted lid shadow on top, its lit lower half below.
				return new Vector2(Vector3.Dot(offset, uAxis) / span + 0.5f,
								   Vector3.Dot(offset, vAxis) / span + 0.5f);
			}
		}

		public static MdlSkeleton BuildSkeleton(MDL mdlHeader)
		{
			if (mdlHeader?.Bones == null || mdlHeader.Bones.Length <= 1)
				return null;

			var skeleton = new MdlSkeleton();
			for (int i = 0; i < mdlHeader.Bones.Length; i++)
			{
				var bone = mdlHeader.Bones[i];
				if (string.IsNullOrWhiteSpace(bone.Name))
					bone.Name = (i == 0) ? "Root" : "Bone";

				var boneObject = new GameObject(bone.Name);
				// Kept inactive until it has been placed, so nothing observes a bone at the origin.
				boneObject.SetActive(false);
				skeleton.Bones.Add(boneObject.transform);
			}

			for (int i = 0; i < mdlHeader.Bones.Length; i++)
			{
				var bone = mdlHeader.Bones[i];
				var parentIndex = bone.ParentBone;
				if (parentIndex >= 0 && parentIndex < mdlHeader.Bones.Length)
					skeleton.Bones[i].SetParent(skeleton.Bones[parentIndex], true);

				ConvertBoneTransform(bone.Position, bone.Quaternion,
									 out var translation, out var rotation, out var scale);

				skeleton.Bones[i].SetLocalPositionAndRotation(translation, rotation);
				skeleton.Bones[i].localScale = scale;
				skeleton.Bones[i].gameObject.SetActive(true);
			}
			return skeleton;
		}

		/// <summary>
		/// Converts one bone transform out of Source's coordinate system.
		/// <para>
		/// Shared by the skeleton and by the animation on purpose. The animation has to land on
		/// exactly the pose the bind skeleton was built from - frame 0 of a non-delta animation IS
		/// the bind pose, because Source stores those frames as offsets from it - so the two must go
		/// through the same conversion or every animation starts by snapping the model into a
		/// slightly different shape.
		/// </para>
		/// </summary>
		public static void ConvertBoneTransform(Vector3 sourcePosition, Quaternion sourceRotation,
												out Vector3 position, out Quaternion rotation, out Vector3 scale)
		{
			// A bone transform is LOCAL - it maps its own space into its parent's - so converting it
			// between coordinate systems is a similarity transform, S * m * inverse(S), not S * m.
			// Leaving the inverse out leaves S itself sitting in the result: every bone came out with
			// a localScale of (-0.019, -0.019, -0.019), which is mirrored and 52x too small, and
			// because a bone inherits its parents' scale that compounded to 2e-21 by the eleventh
			// joint. Decompose was being handed a matrix whose rotation part had determinant
			// -0.000007 and had to invent a rotation and a negative scale to explain it.
			//
			// This survived for as long as models were built as prefabs because bindposes are derived
			// from these very transforms: at the bind pose bones[i].localToWorld * bindposes[i] is the
			// identity whatever the bones say, so the mesh rendered exactly as authored and nothing
			// looked wrong. Writing the bones into a model file and letting Unity rebuild the skin
			// from them is what made it visible - tiny, mirrored, and collapsed to a line on anything
			// with a deep hierarchy.
			position = SourceEngineUnits.VmfSourceToUnity.MultiplyPoint(sourcePosition);

			// S is an axis swizzle times a uniform scale. The scale cancels against its own inverse,
			// so all that is left is the swizzle conjugating the rotation - and W * R * W has
			// determinant +1, an actual rotation, because the swizzle's own -1 appears twice.
			var swizzle = SourceEngineUnits.VmfSwizzle;
			rotation = (swizzle * Matrix4x4.Rotate(sourceRotation) * swizzle).rotation;

			// Source has no per-bone scale, and there is none left over now that the conversion is a
			// similarity transform.
			scale = Vector3.one;
		}

		/// <summary>
		/// Decodes the model's animations and samples them into the dense per-frame form the FBX
		/// exporter writes.
		/// </summary>
		static List<FbxAnimationTake> BuildAnimationTakes(GameResources gameResources, MDL header,
														  GameEntry mdlEntry, MdlSkeleton skeleton)
		{
			if (!ImportAnimations || gameResources == null || header == null || mdlEntry == null ||
				skeleton == null || skeleton.Bones.Count == 0)
				return null;

			if (header.LocalAnimationDescriptions == null || header.LocalAnimationDescriptions.Length == 0)
				return null;

			// The decoder walks the file by offset, so it needs the bytes rather than the parsed
			// structures. They are read here and dropped when this returns, instead of being held on
			// the model for the whole import.
			var mdlBytes = gameResources.LoadFileAsBytes(mdlEntry);
			if (mdlBytes == null)
				return null;

			// Most of Black Mesa's animation is inside the .mdl, but the big shared NPC sets live in
			// a companion .ani that the model names.
			byte[] aniBytes = null;
			if (!string.IsNullOrEmpty(header.AnimblocksName))
			{
				aniBytes = gameResources.LoadFileAsBytes(header.AnimblocksName);
				if (aniBytes == null)
					Debug.LogWarning($"[Chisel] {header.Name}: animation file '{header.AnimblocksName}' is missing, " +
									 "so the animations stored in it were skipped");
			}

			var decoded = MdlAnimationDecoder.Decode(header, mdlBytes, aniBytes);
			if (decoded.Count == 0)
				return null;

			var sequenceNames = BuildSequenceNameTable(header);

			var takes = new List<FbxAnimationTake>(decoded.Count);
			foreach (var animation in decoded)
			{
				var take = new FbxAnimationTake
				{
					Name       = sequenceNames.TryGetValue(animation.Index, out var label) ? label : animation.Name,
					FrameRate  = animation.FrameRate,
					FrameCount = animation.FrameCount,
					Looping    = animation.Looping
				};

				foreach (var channel in animation.Channels)
				{
					if (channel.BoneIndex < 0 || channel.BoneIndex >= skeleton.Bones.Count)
						continue;

					var positions = new Vector3[animation.FrameCount];
					var rotations = new Quaternion[animation.FrameCount];
					for (int frame = 0; frame < animation.FrameCount; frame++)
					{
						ConvertBoneTransform(channel.Positions[frame], channel.Rotations[frame],
											 out positions[frame], out rotations[frame], out _);
					}

					take.Nodes.Add(new FbxAnimationCurveSet
					{
						Node      = skeleton.Bones[channel.BoneIndex],
						Positions = positions,
						Rotations = rotations
					});
				}

				if (take.Nodes.Count > 0)
					takes.Add(take);
			}
			return takes;
		}

		/// <summary>
		/// Maps each animation to the name of the first sequence that plays it.
		/// <para>
		/// Sequences are what the game and the level files refer to ("idle01", "walk_all"); the
		/// animations underneath them are named after whatever file they were compiled from. Several
		/// sequences can share one animation, so this takes the first and leaves the rest - the data
		/// is exported once either way.
		/// </para>
		/// </summary>
		static Dictionary<int, string> BuildSequenceNameTable(MDL header)
		{
			var names = new Dictionary<int, string>();
			if (header.LocalSequences == null)
				return names;

			foreach (var sequence in header.LocalSequences)
			{
				if (sequence?.anim == null || string.IsNullOrEmpty(sequence.label))
					continue;
				if (sequence.anim.GetLength(0) == 0 || sequence.anim.GetLength(1) == 0)
					continue;

				var animationIndex = sequence.anim[0, 0];
				if (!names.ContainsKey(animationIndex))
					names.Add(animationIndex, sequence.label);
			}
			return names;
		}

		/// <summary>
		/// Imports the animation-only models a model shares its animation with, as FBX files holding
		/// a skeleton and takes but no geometry.
		/// <para>
		/// Source's <c>$includemodel</c> points a character at a separate model file that contains
		/// nothing but sequences, so that every character on the same rig plays from one set. In Black
		/// Mesa this is where most of the animation actually is: only 47 of 14,351 models use
		/// <c>$includemodel</c>, but the files they point at are the big ones -
		/// models/humans/male_choreo.mdl alone holds 526 sequences and is included by 9 models.
		/// Exporting them once, rather than baking them into every character that uses them, is the
		/// difference between 526 clips and around 4,700.
		/// </para>
		/// <para>
		/// The skeletons match (measured across every include in the game: every bone an animation
		/// model uses exists in the models that include it), which is what lets Unity play a clip from
		/// one of these files on a character imported from another.
		/// </para>
		/// </summary>
		static void ImportIncludedAnimations(GameResources gameResources, SourceModel model)
		{
			if (!ImportAnimations || model?.MDL?.IncludeModels == null)
				return;

			foreach (var include in model.MDL.IncludeModels)
			{
				// header is null when the referenced file is not shipped, which does happen.
				if (include?.header == null || string.IsNullOrEmpty(include.name))
					continue;

				try
				{
					ImportAnimationOnlyModel(gameResources, include.header, include.name);
				}
				catch (Exception exception)
				{
					Debug.LogError($"[Chisel] failed to import shared animations from '{include.name}': {exception.Message}");
				}
			}
		}

		/// <summary>
		/// Writes a model that has a skeleton and animation but no mesh out as an FBX, unless the file
		/// already there is current, and returns its asset path - or null when there is nothing to write.
		/// </summary>
		public static string ImportAnimationOnlyModel(GameResources gameResources, MDL header, string entryName)
		{
			if (gameResources == null || header == null)
				return null;

			var entry = gameResources.GetEntry(entryName);
			if (entry == null)
				return null;

			// Written under import/animations rather than next to the models: these are the slowest
			// files this importer produces and they are shared between characters, so deleting the
			// models to re-import them should not drag the animation along with it.
			var outputPath      = PackagePath.GetOutputPath(PackagePath.GetAnimationEntryName(entry.keyname));
			var destinationPath = Path.ChangeExtension(outputPath, kModelExtension);
			var assetPath       = UnityAssets.GetAssetPath(destinationPath);

			// Asked of the importer rather than by loading the file: every model that includes this
			// animation asks, on every import, and these files run to tens of megabytes.
			if (GeneratedAssetVersion.IsCurrent(assetPath, AnimationVersion) && ImportedAsWritten(assetPath))
				return assetPath;

			var skeleton = BuildSkeleton(header);
			if (skeleton == null)
				return null;

			PackagePath.EnsureDirectoriesExist(outputPath);

			var root = new GameObject(string.IsNullOrEmpty(header.Name) ? entryName : header.Name);
			FbxExporter.Result exported;
			try
			{
				foreach (var bone in skeleton.Bones)
				{
					if (bone.parent == null)
						bone.SetParent(root.transform, true);
				}

				var takes = BuildAnimationTakes(gameResources, header, entry, skeleton);
				if (takes == null || takes.Count == 0)
					return null;

				exported = FbxExporter.Export(destinationPath, root, takes);
			}
			finally
			{
				GameObject.DestroyImmediate(root);
			}

			if (!ImportOnce(assetPath, exported, 1, AnimationVersion))
			{
				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
				ConfigureImportedModel(assetPath, exported, AnimationVersion);
			}

			if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == null)
			{
				Debug.LogError($"[Chisel] failed to import the shared animations written to {assetPath}");
				return null;
			}
			return assetPath;
		}

		/// <summary>
		/// The transform the engine applies to a single-bone model's vertices when it draws it: the first
		/// frame of the default sequence on bone 0, relative to that bone's reference pose, in Source's own
		/// coordinates. The identity when the model has no animation, or its default pose is its reference.
		/// </summary>
		public static Matrix4x4 DefaultPoseOfSingleBone(GameResources gameResources, SourceModel model)
		{
			var header = model?.MDL;
			if (gameResources == null || model.MDLEntry == null || header?.Bones == null ||
				header.Bones.Length == 0 || header.Bones[0].ParentBone >= 0)
				return Matrix4x4.identity;
			if (header.LocalAnimationDescriptions == null || header.LocalAnimationDescriptions.Length == 0)
				return Matrix4x4.identity;

			// The sequence the engine plays when nothing names another one is the first.
			var animationIndex = 0;
			var sequences = header.LocalSequences;
			if (sequences != null && sequences.Length > 0 && sequences[0]?.anim != null &&
				sequences[0].anim.GetLength(0) > 0 && sequences[0].anim.GetLength(1) > 0)
				animationIndex = sequences[0].anim[0, 0];

			var mdlBytes = gameResources.LoadFileAsBytes(model.MDLEntry);
			if (mdlBytes == null)
				return Matrix4x4.identity;
			var aniBytes = string.IsNullOrEmpty(header.AnimblocksName) ? null : gameResources.LoadFileAsBytes(header.AnimblocksName);

			foreach (var animation in MdlAnimationDecoder.Decode(header, mdlBytes, aniBytes))
			{
				if (animation.Index != animationIndex)
					continue;
				foreach (var channel in animation.Channels)
				{
					if (channel.BoneIndex != 0 || channel.Positions == null || channel.Positions.Length == 0)
						continue;
					var bone = header.Bones[0];
					return Matrix4x4.TRS(channel.Positions[0], channel.Rotations[0], Vector3.one) *
						Matrix4x4.TRS(bone.Position, bone.Quaternion, Vector3.one).inverse;
				}
			}
			return Matrix4x4.identity;
		}

		static string SkinVariantPath(string outputPath, int skin)
		{
			return Path.Combine(Path.GetDirectoryName(outputPath),
								Path.GetFileNameWithoutExtension(outputPath) + $"[{skin}].prefab");
		}

		static GameObject[] LoadSkinVariants(string outputPath, GameObject imported)
		{
			var results = new List<GameObject> { imported };
			for (int i = 1; ; i++)
			{
				var variant = UnityAssets.Load<GameObject>(SkinVariantPath(outputPath, i));
				if (variant == null)
					break;
				results.Add(variant);
			}
			return results.ToArray();
		}

		const string kMaterialSlotPath = "m_Materials.Array.data[";

		/// <summary>
		/// Whether a model file, or one of its skin variants, links to a material asset that no longer exists. Every link
		/// this importer makes names a material it has just imported - the model file's remaps
		/// (<see cref="ConfigureImportedModel"/>) and the slots a skin variant overrides - so a link that comes back empty
		/// points at an asset that was deleted.
		/// </summary>
		/// <param name="models">The model file's prefab and its skin variants (<see cref="LoadSkinVariants"/>)</param>
		internal static bool LinksToMissingMaterials(string assetPath, GameObject[] models)
		{
			if (AssetImporter.GetAtPath(assetPath) is UnityEditor.ModelImporter importer)
			{
				foreach (var link in importer.GetExternalObjectMap())
				{
					if (link.Key.type == typeof(Material) && link.Value == null)
						return true;
				}
			}

			if (models == null)
				return false;
			for (int i = 1; i < models.Length; i++)
			{
				var modifications = models[i] != null ? PrefabUtility.GetPropertyModifications(models[i]) : null;
				if (modifications == null)
					continue;
				foreach (var modification in modifications)
				{
					if (modification.propertyPath.StartsWith(kMaterialSlotPath, StringComparison.Ordinal) &&
						modification.objectReference == null)
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Off takes every model file the slow way again - imported with Unity's defaults, then configured
		/// (<see cref="ConfigureImportedModel"/>), two imports - which is what <see cref="ImportOnce"/> is compared against.
		/// </summary>
		public static bool ImportOnceEnabled = true;

		// Said once per editor session, whatever the number of models it happens to
		static bool s_SaidRepaired;

		/// <summary>
		/// Imports a model file that was just written ONCE, configured as <see cref="ConfigureImportedModel"/> leaves it,
		/// and returns whether it did.
		/// <para>
		/// The slow way takes two imports, each its own refresh, and an import of a model is the file read and its meshes
		/// built: Unity's defaults first, then the configuration's SaveAndReimport.
		/// Here the .meta goes down first: a template Unity wrote for another generated model (all of them are one shape
		/// but for seven fields, see <see cref="GeneratedMeta.ModelFields"/>), with this model's GUID, stamp, material
		/// remaps, LOD transitions and rig in it, so the one import is the last one.
		/// </para>
		/// <para>
		/// False, with nothing written, when there is no template yet or a material is not an asset of its own: the slow
		/// way does it, as before. An import that did not come out as written is put right through the importer, and said
		/// once.
		/// </para>
		/// </summary>
		internal static bool ImportOnce(string assetPath, FbxExporter.Result exported, int lodCount, params (string generator, int version)[] versions)
		{
			if (!WriteMeta(assetPath, exported, lodCount, versions, out _, out _))
				return false;
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
			RepairModel(assetPath, exported, versions);
			return true;
		}

		/// <summary>
		/// ImportOnce's writing: the .meta of a model file just written, from a template Unity wrote for another generated
		/// model, with this one's GUID, stamp, material remaps, LOD transitions and rig in it - the userData written, and what
		/// the importer held before (null for a new file). False, with nothing written, when there is no template yet or a
		/// material is not one this writes; the slow way then takes the file.
		/// </summary>
		static bool WriteMeta(string assetPath, FbxExporter.Result exported, int lodCount, (string generator, int version)[] versions,
							  out string userData, out string previousUserData)
		{
			userData = previousUserData = null;
			if (!ImportOnceEnabled || exported == null)
				return false;
			var template = GeneratedMeta.Template(GeneratedMeta.Kind.Model);
			if (template == null)
				return false;

			// As AddRemap leaves them: each name onto its material, the last one written under a name winning
			var materials = new Dictionary<string, string>();
			for (int i = 0; exported.MaterialNames != null && i < exported.MaterialNames.Length; i++)
			{
				var material = (exported.Materials != null && i < exported.Materials.Length) ? exported.Materials[i] : null;
				if (material == null)
					continue;
				if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long fileId) || fileId != kMaterialFileId)
					return false;
				materials[exported.MaterialNames[i]] = guid;
			}
			var remaps = new List<(string name, string guid)>(materials.Count);
			foreach (var pair in materials)
				remaps.Add((pair.Key, pair.Value));
			var settings = new GeneratedMeta.ModelSettings
			{
				materials       = remaps,
				lodCount        = lodCount,
				rigged          = exported.BoneCount > 0 || exported.TakeCount > 0,
				importAnimation = exported.TakeCount > 0,
			};
			var existing = AssetImporter.GetAtPath(assetPath);
			var stamp = GeneratedAssetVersion.Stamp(existing?.userData, versions);
			var meta = GeneratedMeta.FillModel(template, GeneratedMeta.GuidFor(assetPath), stamp, settings);
			if (meta == null)
				return false;

			GeneratedMeta.Write(assetPath, meta);
			userData = stamp;
			previousUserData = existing?.userData;
			return true;
		}

		// A material asset's main object, which is what a remap names
		const long kMaterialFileId = 2100000;

		// The LOD transitions: ModelImporter has no property for them, only the serialized field its inspector edits
		static SerializedProperty LodScreenPercentages(SerializedObject importer) => importer.FindProperty("m_LODScreenPercentages");

		// The import came out as the .meta said, or is made to, through the importer. The LOD transitions are held to the
		// LODGroup Unity built from the file's _LOD names, not to the count they were written for.
		static void RepairModel(string assetPath, FbxExporter.Result exported, (string generator, int version)[] versions)
		{
			if (AssetImporter.GetAtPath(assetPath) is not UnityEditor.ModelImporter importer)
				return;
			var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
			var lodCount = (model != null && model.TryGetComponent<LODGroup>(out var lodGroup)) ? lodGroup.lodCount : 1;
			if (IsConfiguredAsWritten(importer, exported, lodCount) && GeneratedAssetVersion.IsCurrentUserData(importer.userData, versions))
				return;

			if (!s_SaidRepaired)
			{
				s_SaidRepaired = true;
				Debug.LogWarning($"{assetPath}: the import did not come out as the .meta written for it said, so it was configured " +
								 "with an import of its own. Every model written this way takes two imports instead of one until " +
								 "this is looked into.");
			}
			var serialized = new SerializedObject(importer);
			var transitions = LodScreenPercentages(serialized);
			var expected = (lodCount > 1) ? lodCount : 0;
			if (transitions != null && transitions.arraySize != expected)
			{
				transitions.arraySize = expected;
				for (int i = 0; i < expected; i++)
					transitions.GetArrayElementAtIndex(i).floatValue = GeneratedMeta.DefaultLodTransition(i, expected);
				serialized.ApplyModifiedPropertiesWithoutUndo();
			}
			ConfigureImportedModel(assetPath, exported, versions);
		}

		/// <summary>
		/// Whether a model file's importer is set as <see cref="ConfigureImportedModel"/> sets it for this export: the
		/// settings every model gets (<see cref="ImportedAsWritten"/>), its rig, its remaps and its LOD transitions.
		/// </summary>
		internal static bool IsConfiguredAsWritten(UnityEditor.ModelImporter importer, FbxExporter.Result exported, int lodCount)
		{
			if (!HasTheFixedSettings(importer))
				return false;
			var rigged = exported.BoneCount > 0 || exported.TakeCount > 0;
			if (importer.animationType != (rigged ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None) ||
				importer.importAnimation != (exported.TakeCount > 0) ||
				(rigged && importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel))
				return false;
			var transitions = LodScreenPercentages(new SerializedObject(importer));
			if (transitions == null || transitions.arraySize != ((lodCount > 1) ? lodCount : 0))
				return false;
			var remaps = importer.GetExternalObjectMap();
			for (int i = 0; exported.MaterialNames != null && i < exported.MaterialNames.Length; i++)
			{
				var material = (exported.Materials != null && i < exported.Materials.Length) ? exported.Materials[i] : null;
				if (material == null)
					continue;
				if (!remaps.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), exported.MaterialNames[i]), out var target) ||
					target != material)
					return false;
			}
			return true;
		}

		/// <summary>
		/// Whether a model file this importer wrote still has the import settings every model of it gets
		/// (<see cref="ConfigureImportedModel"/>). A stamp says which code wrote a model, not that nothing changed its
		/// settings since: one changed by hand, or by a script fixing models up, would stay that way under a current
		/// stamp for good. So a model reused with them changed is written again, at its import (as
		/// Texture2DImporter.ImportedAsWritten does for a texture).
		/// </summary>
		internal static bool ImportedAsWritten(string assetPath)
		{
			return AssetImporter.GetAtPath(assetPath) is UnityEditor.ModelImporter importer && HasTheFixedSettings(importer);
		}

		static bool HasTheFixedSettings(UnityEditor.ModelImporter importer)
		{
			return importer.importNormals == ModelImporterNormals.Import && importer.importTangents == ModelImporterTangents.Import &&
				   !importer.importBlendShapes && !importer.importVisibility && !importer.importCameras && !importer.importLights &&
				   importer.useFileScale && importer.materialImportMode == ModelImporterMaterialImportMode.ImportStandard;
		}

		/// <summary>
		/// Points Unity's model importer at the materials this importer already created, and tells it
		/// what the file does and does not contain.
		/// </summary>
		internal static void ConfigureImportedModel(string assetPath, FbxExporter.Result exported, params (string generator, int version)[] versions)
		{
			if (AssetImporter.GetAtPath(assetPath) is not UnityEditor.ModelImporter importer)
				return;

			// The normals and tangents in the file are the ones studiomdl authored, carrying the
			// model's own smoothing. Letting Unity recalculate them throws that away and reintroduces
			// exactly the seams this importer went to some trouble to remove.
			importer.importNormals  = ModelImporterNormals.Import;
			importer.importTangents = ModelImporterTangents.Import;

			importer.importBlendShapes = false;
			importer.importVisibility  = false;
			importer.importCameras     = false;
			importer.importLights      = false;
			importer.useFileScale      = true;

			// Materials are already assets in their own right, imported from the VMTs, so each name
			// in the file is remapped onto the one it came from. Remapping - not the long deprecated
			// "external materials" location - is what Unity supports for this now: the importer keeps
			// its default location and every name we remap resolves to our asset instead of to a new
			// empty material embedded in the model.
			importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
			// The file being configured is the one just written, so the links are exactly its materials: one left from an
			// earlier file, whose material may be gone since, would have the model rebuilt on every import
			// (LinksToMissingMaterials).
			foreach (var link in importer.GetExternalObjectMap())
			{
				if (link.Key.type == typeof(Material))
					importer.RemoveRemap(link.Key);
			}
			if (exported?.MaterialNames != null)
			{
				for (int i = 0; i < exported.MaterialNames.Length; i++)
				{
					var material = (exported.Materials != null && i < exported.Materials.Length) ? exported.Materials[i] : null;
					if (material == null)
						continue;
					importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), exported.MaterialNames[i]), material);
				}
			}

			var hasSkeleton = (exported != null && exported.BoneCount > 0);
			var hasTakes    = (exported != null && exported.TakeCount > 0);

			// Animation needs a rig type to bind to: leaving this at None imports the file with no
			// clips at all, however much animation is in it.
			importer.animationType   = (hasSkeleton || hasTakes) ? ModelImporterAnimationType.Generic
																 : ModelImporterAnimationType.None;
			importer.importAnimation = hasTakes;

			// Generic on its own still leaves avatarSetup at NoAvatar, so the model imports without
			// one. An Avatar is what lets a clip from one file play on a rig from another, which is
			// the entire point of exporting the shared animation separately.
			if (hasSkeleton || hasTakes)
				importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

			// What was just written is what the current code produces.
			importer.userData = GeneratedAssetVersion.Stamp(importer.userData, versions);

			importer.SaveAndReimport();
		}

		/// <summary>
		/// Adds the model's collision to the hierarchy: one mesh node per convex piece, which
		/// <see cref="SourceCollisionPostprocessor"/> turns into a collider once Unity has imported the file.
		/// <para>
		/// The game uses a model's collision in one of two ways, and which one depends on the entity rather than the
		/// model (see <see cref="ModelCollision"/>), so the model gets both:
		/// </para>
		/// <list type="bullet">
		/// <item>The prop set: the first solid, placed at the entity's origin and angles just as it is stored. The game
		/// applies no bone to it, whatever space studiomdl compiled it in (PhysModelCreate parses solid 0), so it goes
		/// under the root, without the default pose single-bone render meshes are given.</item>
		/// <item>The ragdoll set: every solid named after a bone, in that bone's space, moving with it as it animates.
		/// RagdollAddSolid, and CreatePhysicsFollower for a dynamic prop's bone followers, both find the bone by the
		/// solid's name, and place a solid named after no bone nowhere.</item>
		/// </list>
		/// </summary>
		static void BuildCollision(GameObject root, SourceModel model, MdlSkeleton skeleton, Matrix4x4 restPose, List<Mesh> meshes)
		{
			var solids = model?.PHY?.Solids;
			if (solids == null || solids.Count == 0)
				return;

			AddSolid(kPropCollisionPrefix, 0, root.transform, Matrix4x4.identity);

			var bones = model.MDL.Bones;
			for (int s = 0; s < solids.Count; s++)
			{
				var bone = FindBone(solids[s].Name);
				if (bone < 0)
				{
					// studiomdl names a solid it compiles into a bone's space after that bone.
					if (solids[s].Pieces.Count > 0 && solids[s].Pieces[0].BoneReference != 0)
						Debug.LogWarning($"[Chisel] {model.MDL.Name}: collision solid '{solids[s].Name}' is in a bone's space but not " +
										 "named after a bone, so the game could not place it on one either");
					continue;
				}

				if (skeleton != null)
					AddSolid(kRagdollCollisionPrefix, s, skeleton.Bones[bone], Matrix4x4.identity);
				else
					// A model with a single bone is imported without it, so the bone's pose goes into the vertices the
					// way it goes into the render mesh: the default pose, applied to the bone's reference.
					AddSolid(kRagdollCollisionPrefix, s, root.transform,
							 restPose * Matrix4x4.TRS(bones[bone].Position, bones[bone].Quaternion, Vector3.one));
			}

			void AddSolid(string prefix, int s, Transform parent, Matrix4x4 sourceToNode)
			{
				var pieces = solids[s].Pieces;
				for (int p = 0; p < pieces.Count; p++)
				{
					var piece    = pieces[p];
					var vertices = new Vector3[piece.Vertices.Length];
					for (int i = 0; i < vertices.Length; i++)
						vertices[i] = SourceEngineUnits.VmfSourceToUnity.MultiplyPoint(sourceToNode.MultiplyPoint3x4(piece.Vertices[i]));

					// IVP winds its triangles outward by the right-hand rule. The conversion into Unity mirrors - its
					// swizzle has determinant -1 - which turns them inward, so each triangle is wound back.
					var triangles = new int[piece.Triangles.Length];
					for (int i = 0; i + 2 < triangles.Length; i += 3)
					{
						triangles[i]     = piece.Triangles[i];
						triangles[i + 1] = piece.Triangles[i + 2];
						triangles[i + 2] = piece.Triangles[i + 1];
					}

					var name = $"{prefix}{s}.{p}";
					var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
					meshes.Add(mesh);

					var node = new GameObject(name);
					node.transform.SetParent(parent, false);
					node.AddComponent<MeshFilter>().sharedMesh = mesh;
				}
			}

			// Studio_BoneIndexByName, which the game finds a solid's bone with, compares names with Q_stricmp.
			int FindBone(string name)
			{
				if (bones == null || string.IsNullOrEmpty(name))
					return -1;
				for (int i = 0; i < bones.Length; i++)
				{
					if (string.Equals(bones[i].Name, name, StringComparison.OrdinalIgnoreCase))
						return i;
				}
				return -1;
			}
		}

		public static void SetSkin(GameObject parentGO, MdlModelEntry[] modelEntries, MdlMaterialTable skinMaterials)
		{
			if (skinMaterials == null)
				return;

			// We can't use the Renderers in modelEntries directly, since it might point to a Renderer in another prefab
			// So we lookup the Renderers by name, assuming that all meshes are uniquely named 
			// (since they're originated from fbx files this is more likely than not, but we probably should double check this / fixup when necessary)
			var rendererLookup = new Dictionary<string, Renderer>();
			var allChildRenderers = parentGO.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach(var renderer in allChildRenderers)
			{
				rendererLookup[renderer.gameObject.name] = renderer;
			}

			var unityMaterials = skinMaterials.unityMaterials;
			foreach (var modelEntry in modelEntries)
			{
				if (modelEntry.Name == null) // TODO: why do we have lots of empty modelEntries?
					continue;
				var subMeshes = modelEntry.SubMeshes;
				var materials = new Material[subMeshes.Count];
				for (var i = 0; i < subMeshes.Count; i++)
				{
					var materialIndex = subMeshes[i].materialIndex;
					materials[i] = (materialIndex >= 0 && materialIndex < unityMaterials.Length) ? unityMaterials[materialIndex] : null;
				}

				// A name that is not there is not fatal: once a model has been through a file and back,
				// the renderers are whatever the model importer produced, and a skin that cannot find
				// one of them should leave the rest correct rather than abort the whole import.
				if (!rendererLookup.TryGetValue(modelEntry.Name, out var modelRenderer))
				{
					Debug.LogWarning($"[Chisel] skin references '{modelEntry.Name}', which has no renderer on {parentGO.name}");
					continue;
				}
				modelRenderer.sharedMaterials = materials;
			}
		}

		/// <returns>The animation clips created for this model. They are referenced by the
		/// Animation components on the prefab but are not assets yet, so the caller has to add
		/// them to the prefab asset or they are lost when it is saved.</returns>
		/// <summary>
		/// Builds the GameObject hierarchy for a model - bones, and one object per mesh per LOD -
		/// ready to be written out. Animation is not built here: it goes into the model file as takes,
		/// so Unity creates the clips on import rather than this importer creating them by hand.
		/// </summary>
		public static void BuildHierarchy(GameObject prefab, SourceModel model, MdlModelEntry[] modelEntries, MdlSkeleton skeleton, Matrix4x4? sourceRestPose = null)
		{
			var entryName = model.MDL.Name;

			var hasMultipleLods = new HashSet<string>();
			for (var i = 0; i < modelEntries.Length; i++)
			{
				if (modelEntries[i] == null)
					continue;

				if (modelEntries[i].LodIndex > 0)
					hasMultipleLods.Add(modelEntries[i].Name);
			}

			prefab.name = entryName;

			var lods = new List<LOD>();
			LODGroup lodGroup = null;
			if (hasMultipleLods.Count > 0)
				lodGroup = prefab.AddComponent<LODGroup>();
			if (skeleton != null)
			{
				foreach (var bone in skeleton.Bones)
				{
					if (bone.transform.parent == null)
						bone.transform.SetParent(prefab.transform, true);
				}
			}

			for (var i = 0; i < modelEntries.Length; i++)
			{
				var modelEntry = modelEntries[i];
				if (modelEntry == null)
					continue;

				if (modelEntry.Vertices.Count == 0)
					continue;

				if (string.IsNullOrEmpty(modelEntry.Name))
					modelEntry.Name = entryName;

				if (hasMultipleLods.Contains(modelEntry.Name))
					modelEntry.Name += "_LOD" + modelEntry.LodIndex;

				modelEntry.GenerateMesh(prefab, skeleton, sourceRestPose ?? Matrix4x4.identity);
				if (lodGroup == null)
					continue;

				var renderer = modelEntry.MeshObject.GetComponent<Renderer>();
				if (!renderer)
					continue;

				while (modelEntry.LodIndex >= lods.Count)
					lods.Add(new LOD());

				var lod = lods[modelEntry.LodIndex];
				if (lod.renderers == null)
					lod.renderers = new Renderer[] { renderer };
				else
					ArrayUtility.Add(ref lod.renderers, renderer);

				lod.screenRelativeTransitionHeight = 1.0f / (2.0f + (modelEntry.LodIndex * 3));// SourceEngineUnits.VmfLodSwitchpointToUnityLodTransition(modelEntry.SwitchPoint);
				lods[modelEntry.LodIndex] = lod;
			}
			if (lods != null && lods.Count > 0)
			{
				var lastLod = lods[lods.Count - 1];
				lastLod.screenRelativeTransitionHeight = 0.0f;
				lods[lods.Count - 1] = lastLod;
			}
			if (lodGroup != null)
			{
				lods.Sort(delegate (LOD x, LOD y)
				{
					return y.screenRelativeTransitionHeight.CompareTo(x.screenRelativeTransitionHeight);
				});
				lodGroup.SetLODs(lods.ToArray());
			}
		}

		
		private static BoneWeight ToUnityBoneWeight(StudioVertex vertex)
		{
			if (vertex.BoneIndices.Length == 0)
				return new BoneWeight() { boneIndex0 = 0, weight0 = 1 };

			if (vertex.BoneIndices.Length == 1)
			{
				return new BoneWeight() { boneIndex0 = vertex.BoneIndices[0], weight0 = vertex.BoneWeights[0] };
			}
			if (vertex.BoneIndices.Length == 2)
			{
				return new BoneWeight()
				{
					boneIndex0 = vertex.BoneIndices[0],
					boneIndex1 = vertex.BoneIndices[1],

					weight0 = vertex.BoneWeights[0],
					weight1 = vertex.BoneWeights[1]
				};
			}
			return new BoneWeight()
			{
				boneIndex0 = vertex.BoneIndices[0],
				boneIndex1 = vertex.BoneIndices[1],
				boneIndex2 = vertex.BoneIndices[2],

				weight0 = vertex.BoneWeights[0],
				weight1 = vertex.BoneWeights[1],
				weight2 = vertex.BoneWeights[2]
			};
		}

		private static MdlModelMaterials ImportModelTextures(GameResources gameResources, SourceModel model)
		{
			// The model's materials are written together (MaterialImporter.ImportAll): one refresh between them where each
			// took one of its own. A reused model asks for them too, so after a material version bump every material of
			// every model a map uses is rebuilt here - unless they were asked for first, with every other model's
			// (PrepareMaterials), and are current by now.
			var asked = FindMaterials(gameResources, model);
			return MaterialsOf(model, asked, MaterialImporter.ImportAll(gameResources, asked.Requests));
		}

		/// <summary>
		/// Imports the materials of these models together, as each model's import asks for its own (ImportModelTextures): one
		/// MaterialImporter.ImportAll for all of them - one refresh for their textures and one for themselves, where each
		/// model took its own. The models' imports then find them current. a map's first import took 90
		/// refreshes for one material and some 100 for a few textures, most of them a model's.
		/// </summary>
		/// <param name="progress">Told the index of each material it builds and the count, for a progress bar.</param>
		public static void PrepareMaterials(GameResources gameResources, IEnumerable<SourceModel> models, Action<int, int> progress = null)
		{
			var requests = new List<MaterialImporter.Request>();
			foreach (var model in models)
				requests.AddRange(MaterialRequestsOf(gameResources, model));
			if (requests.Count > 0)
				MaterialImporter.ImportAll(gameResources, requests, progress);
		}

		/// <summary>
		/// The requests a model's import imports its materials by (ImportModelTextures), in its order: for asking for a map's
		/// models' materials together, first (GameResources.PrepareModels).
		/// </summary>
		internal static List<MaterialImporter.Request> MaterialRequestsOf(GameResources gameResources, SourceModel model)
		{
			return (model?.MDL?.TextureFilenames != null) ? FindMaterials(gameResources, model).Requests : new List<MaterialImporter.Request>();
		}

		/// <summary>
		/// The materials a model names, as its import finds them (FindMaterials): the VMT of each of the MDL's texture names,
		/// or null, and the request each one found is imported by, with the index of the texture it is for.
		/// </summary>
		sealed class ModelMaterialRequests
		{
			internal VMT[] SourceMaterials;
			internal List<MaterialImporter.Request> Requests;
			internal List<int> Requested;
		}

		static ModelMaterialRequests FindMaterials(GameResources gameResources, SourceModel model)
		{
			var mdlHeader = model.MDL;

			var sourceMaterials = new VMT[mdlHeader.TextureFilenames.Length];
			var outputPaths = new string[mdlHeader.TextureFilenames.Length];
			for (var i = 0; i < mdlHeader.TextureFilenames.Length; i++)
			{
				outputPaths[i] = default;
				sourceMaterials[i] = default;

				string fileName = Path.ChangeExtension(mdlHeader.TextureFilenames[i].Name, PackagePath.ExtensionVMT);
				string entryName = fileName;
				var sourceMaterial = gameResources.LoadVMT(entryName);
				if (sourceMaterials[i] != null)
				{
					sourceMaterials[i] = sourceMaterial;
					outputPaths[i] = entryName;
					continue;
				}

				entryName = PackagePath.Combine("materials", fileName);
				sourceMaterial = gameResources.LoadVMT(entryName);
				if (sourceMaterial != null)
				{
					sourceMaterials[i] = sourceMaterial;
					outputPaths[i] = entryName;
					continue;
				}

				for (int j = 0; j < mdlHeader.TextureDirs.Length; j++)
				{
					entryName = PackagePath.Combine(mdlHeader.TextureDirs[j], fileName);
					sourceMaterial = gameResources.LoadVMT(entryName);
					if (sourceMaterial != null)
					{
						sourceMaterials[i] = sourceMaterial;
						outputPaths[i] = entryName;
						break;
					}

					entryName = PackagePath.Combine("materials", mdlHeader.TextureDirs[j], fileName);
					sourceMaterial = gameResources.LoadVMT(entryName);
					if (sourceMaterial != null)
					{
						sourceMaterials[i] = sourceMaterial;
						outputPaths[i] = entryName;
						break;
					}
				}
			}

			var requests = new List<MaterialImporter.Request>(sourceMaterials.Length);
			var requested = new List<int>(sourceMaterials.Length);
			for (var i = 0; i < sourceMaterials.Length; i++)
			{
				if (sourceMaterials[i] == null)
					continue;

				var entry = gameResources.GetEntry(outputPaths[i], PackagePath.DefaultMaterialPaths);
				if (entry == null)
					continue;
				requests.Add(new MaterialImporter.Request(sourceMaterials[i], PackagePath.GetOutputPath(entry.keyname)));
				requested.Add(i);
			}
			return new ModelMaterialRequests { SourceMaterials = sourceMaterials, Requests = requests, Requested = requested };
		}

		// A model's materials as its import hands them on: the materials imported for the requests, at the texture each is
		// for, and each skin's table of them
		static MdlModelMaterials MaterialsOf(SourceModel model, ModelMaterialRequests asked, Material[] imported)
		{
			var mdlHeader = model.MDL;
			var sourceMaterials = asked.SourceMaterials;
			var unityMaterials = new Material[sourceMaterials.Length];
			for (int k = 0; k < asked.Requested.Count; k++)
				unityMaterials[asked.Requested[k]] = imported[k];

			MdlMaterialTable[] skins = null;

			var refTable = mdlHeader.SkinReferenceTable;
			if (refTable != null)
			{
				skins = new MdlMaterialTable[mdlHeader.SkinReferenceTable.Length];
				for (int s = 0; s < mdlHeader.SkinReferenceTable.Length; s++)
				{
					var skinLookup = mdlHeader.SkinReferenceTable[s];
					if (skinLookup == null)
					{
						skins[s] = null;
						continue;
					}

					var skinTable = new MdlMaterialTable
					{
						sourceMaterials = new VMT[skinLookup.Length],
						unityMaterials = new Material[skinLookup.Length]
					};
					for (int i = 0; i < skinLookup.Length; i++)
					{
						skinTable.sourceMaterials[i] = sourceMaterials[skinLookup[i]];
						skinTable.unityMaterials[i] = unityMaterials[skinLookup[i]];
					}
					skins[s] = skinTable;
				}
			}

			return new MdlModelMaterials
			{
				sourceMaterials = sourceMaterials,
				unityMaterials = unityMaterials,
				skins = skins
			};
		}

		private static MdlModelEntry[] ImportModelLods(GameResources gameResources, SourceModel model, //MdlModelMaterials modelMaterials, 
			out MdlSkeleton skeleton)//, int skin = 0)
		{
			var mdlHeader = model.MDL;
			var vvdHeader = model.VVD;
			var vtxHeader = model.VTX;
			/*
			var sourceMaterials = modelMaterials.sourceMaterials;
			var unityMaterials = modelMaterials.unityMaterials;

			var refTable = mdlHeader.SkinReferenceTable;
			if (refTable != null && skin < refTable.Length)
			{
				var skinLookup = mdlHeader.SkinReferenceTable[skin];
				if (skinLookup != null)
				{
					var skinMaterials = new Material[skinLookup.Length];
					for (int i = 0; i < skinMaterials.Length; i++)
						skinMaterials[i] = unityMaterials[skinLookup[i]];
					unityMaterials = skinMaterials;
				}
			}*/

			var modelEntries = new List<MdlModelEntry>();

			skeleton = BuildSkeleton(mdlHeader);

			
			for (int bodyID = 0; bodyID < mdlHeader.Bodyparts.Length; ++bodyID)
			{
				var vtxBodyPart	= vtxHeader.BodyPartHeaders[bodyID];
				var mdlBodyPart	= mdlHeader.Bodyparts[bodyID];

				// A bodypart is a CHOICE, not a set: Source shows one of its models at a time, picked by the
				// entity's body value, which is how one marine wears a gasmask and the next a helmet. Nothing here
				// reads a body value yet, and model 0 is what body 0 selects - so taking the first is the default
				// loadout rather than an arbitrary pick.
				//
				// It is also exactly what this loop has been producing. mstudiomodel_t was being stepped by 124
				// bytes instead of 148, so every model after the first in a bodypart was read from the middle of
				// its predecessor and came back with a garbage mesh count, which the count guards then dropped.
				// With the stride corrected those models are real, and importing all of them would put every
				// bodygroup variant on the model at once.
				for (int vtxModelID = 0; vtxModelID < Mathf.Min(1, mdlBodyPart.Models.Length); ++vtxModelID)
				{
					var vtxModel	= vtxBodyPart.ModelHeaders[vtxModelID];
					var mdlModel	= mdlBodyPart.Models[vtxModelID];
					
					// get the specified lod, assuming lod 0
					for (int nLod = 0; nLod < vtxModel.ModelLodHeaders.Length; nLod++)
					{
						var vtxLOD = vtxModel.ModelLodHeaders[nLod];
						if (string.IsNullOrWhiteSpace(mdlModel.Name))
						{
							mdlModel.Name = "Model";
						}
						var modelEntry = new MdlModelEntry()
						{
							LodIndex		= nLod,
							SwitchPoint		= vtxLOD.SwitchPoint,
							Flags			= mdlHeader.Flags,
							Model			= model
						};

						bool hasBones		= mdlHeader.Bones.Length > 1;

						var mdlVertexOffset = mdlModel.VertexIndex / 48;

						for (int meshIndex = 0; meshIndex < mdlModel.Meshes.Length; ++meshIndex)
						{
							var mdlMesh = mdlModel.Meshes[meshIndex];
							var vtxMesh = vtxLOD.MeshHeaders[meshIndex];

							if (vtxMesh.StripGroupHeaders.Length == 0)
								continue;

							var vertices = vvdHeader.Vertices;
							var tangents = vvdHeader.Tangents;

							if (vertices == null)
								continue;

							//var sourceMaterial = sourceMaterials[mdlMesh.MaterialIndex];


							modelEntry.Name = mdlModel.Name + " +" + mdlBodyPart.Name + "[" + mdlMesh.MaterialIndex + "]".Trim();

							var indexLookup			= new Dictionary<int, int>();

							// Source marks an eyeball mesh with materialtype 1 and puts the eyeball's own index in
							// materialparam. Worth reading rather than inferring from the order the meshes come in:
							// on marine.mdl the mesh with the eyeball_l material is eyeball 1, and eyeball_r is 0.
							EyeProjection? eyeProjection = null;
							if (mdlMesh.MaterialType == kEyeballMaterialType && mdlModel.Eyeballs != null &&
								(uint)mdlMesh.MaterialParam < (uint)mdlModel.Eyeballs.Length)
							{
								eyeProjection = EyeProjection.Build(mdlHeader, mdlModel.Eyeballs[mdlMesh.MaterialParam]);
								modelEntry.HasEyeUvs = true;
							}

							var mdlMeshVertexIndexStart = mdlMesh.VertexIndexStart;

							var meshEntry = new MdlMeshEntry()
							{
								materialIndex = mdlMesh.MaterialIndex
							};

							for (int nGroup = 0; nGroup < vtxMesh.StripGroupHeaders.Length; ++nGroup)
							{
								var vtxStripGroup		= vtxMesh.StripGroupHeaders[nGroup];
								var vtxVertexOffsets	= vtxStripGroup.VertexOffsets;
								var vtxIndices			= vtxStripGroup.Indices;

								//Debug.Log(modelEntry.Name + " " + nGroup + " " + vtxStripGroup.Flags);
								
								for (int nStrip = 0; nStrip < vtxStripGroup.StripHeaders.Length; nStrip++)
								{
									var pStrip = vtxStripGroup.StripHeaders[nStrip];

									if ((pStrip.Flags & StripFlags.STRIP_IS_TRILIST) > 0)
									{
										for (int i = 0; i < pStrip.IndexCount; i += 3)
										{
											var index = pStrip.IndexMeshOffset + i;
											//var vertexOffset = pStrip.VertexMeshIndex + i;

											var i0 = vtxIndices[index    ];
											var i1 = vtxIndices[index + 1];
											var i2 = vtxIndices[index + 2];

											var vertex1 = vtxVertexOffsets[i0].OriginalMeshVertexIndex + mdlVertexOffset + mdlMeshVertexIndexStart;
											var vertex2 = vtxVertexOffsets[i1].OriginalMeshVertexIndex + mdlVertexOffset + mdlMeshVertexIndexStart;
											var vertex3 = vtxVertexOffsets[i2].OriginalMeshVertexIndex + mdlVertexOffset + mdlMeshVertexIndexStart;

											if ((uint)vertex1 >= (uint)vertices.Length ||
												(uint)vertex2 >= (uint)vertices.Length ||
												(uint)vertex3 >= (uint)vertices.Length)
											{
												throw new InvalidDataException(
													$"Model {model.MDLEntry?.keyname ?? mdlHeader.Name} contains a VTX vertex outside its VVD " +
													$"(body {bodyID}, model {vtxModelID}, LOD {nLod}, mesh {meshIndex}, " +
													$"strip group {nGroup}, strip {nStrip}, triangle {i / 3}: " +
													$"[{vertex1}, {vertex2}, {vertex3}] vs {vertices.Length} vertices).");
											}

											{
												if (!indexLookup.TryGetValue(vertex1, out int index1)) index1 = -1;
												if (!indexLookup.TryGetValue(vertex2, out int index2)) index2 = -1;
												if (!indexLookup.TryGetValue(vertex3, out int index3)) index3 = -1;


												if (index1 == -1)
												{
													index1 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex1].Position);
													modelEntry.Normals.Add(eyeProjection?.SphereNormal(vertices[vertex1].Position) ?? vertices[vertex1].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex1].TexCoord));
													modelEntry.EyeUvs.Add(eyeProjection?.Project(vertices[vertex1].Position) ?? Vector2.zero);
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex1]));
													if (tangents != null) modelEntry.Tangents.Add(tangents[vertex1]);
													indexLookup[vertex1] = index1;
												}

												if (index2 == -1)
												{
													index2 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex2].Position);
													modelEntry.Normals.Add(eyeProjection?.SphereNormal(vertices[vertex2].Position) ?? vertices[vertex2].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex2].TexCoord));
													modelEntry.EyeUvs.Add(eyeProjection?.Project(vertices[vertex2].Position) ?? Vector2.zero);
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex2]));
													if (tangents != null) modelEntry.Tangents.Add(tangents[vertex2]);
													indexLookup[vertex2] = index2;
												}

												if (index3 == -1)
												{
													index3 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex3].Position);
													modelEntry.Normals.Add(eyeProjection?.SphereNormal(vertices[vertex3].Position) ?? vertices[vertex3].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex3].TexCoord));
													modelEntry.EyeUvs.Add(eyeProjection?.Project(vertices[vertex3].Position) ?? Vector2.zero);
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex3]));
													if (tangents != null) modelEntry.Tangents.Add(tangents[vertex3]);
													indexLookup[vertex3] = index3;
												}

												#pragma warning disable CS0162 // Unreachable code detected
												if (SourceEngineUnits.InvertPlanes)
												{
													meshEntry.Indices.Add(index1);
													meshEntry.Indices.Add(index3);
													meshEntry.Indices.Add(index2);
												} else
												{
													meshEntry.Indices.Add(index1);
													meshEntry.Indices.Add(index2);
													meshEntry.Indices.Add(index3);
												}
												#pragma warning restore CS0162 // Unreachable code detected
											}
										}
									}
								}
							}

							if (meshEntry.Indices.Count > 0)
							{
								modelEntry.SubMeshes.Add(meshEntry);
							}
						}						

						//if (modelEntry.Vertices.Count > 0 &&
						//	modelEntry.SubMeshes.Count > 0)
						{
							modelEntries.Add(modelEntry);
						}
					}
				}
			}
			return modelEntries.ToArray();
		}



		/// <summary>
		/// Whether to import MDL animations.
		/// <para>
		/// This used to be off because every sequence became an AnimationClip added as a sub-asset of
		/// the model's prefab, and a map full of NPCs ran the editor out of memory doing it. Animation
		/// now goes into the model file as takes and Unity creates the clips on import, so the cost is
		/// a larger file rather than thousands of serialized sub-assets.
		/// </para>
		/// <para>
		/// It is still not free. Most models have no sequences at all, but the shared NPC animation
		/// models are enormous - models/humans/male_choreo.mdl alone holds 526 of them over 56 bones.
		/// </para>
		/// </summary>
		public static bool ImportAnimations = true;

	}
}
