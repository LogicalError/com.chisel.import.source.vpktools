//#define USE_MdlModelComponent 
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System;
using System.Text;
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
		public bool		   isDoubleSided;
		public string	   Name;
		public SourceModel    Model;
		public Studioflags Flags;
		public Renderer	   Renderer;
		public GameObject  MeshObject;
		public Animation   Animation;
		public Mesh		   Mesh;

		public readonly List<Vector3>		Vertices	= new();
		public readonly List<Vector4>		Tangents	= new();
		public readonly List<Vector3>		Normals		= new();
		public readonly List<Vector2>		Uvs			= new();
		public readonly List<BoneWeight>	BoneWeights = new();
		public readonly List<MdlMeshEntry>	SubMeshes   = new();
		
		public void GenerateMesh(GameObject parentGameObject, MdlSkeleton skeleton, bool staticPropHack)
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
				var transformation = SourceEngineUnits.VmfSourceToUnity;

				if (staticPropHack)
				{
					transformation = new Matrix4x4(
							new Vector4( 1, 0, 0, 0),
							new Vector4( 0, 0,-1, 0),
							new Vector4( 0, 1, 0, 0),
							new Vector4( 0, 0, 0, 1)) * transformation;
					transformation = SourceEngineUnits.InvModelMatrix * transformation;
				} else
				{
					var rotation = Quaternion.identity;
					rotation.eulerAngles = new Vector3(90, 0, 90);
					transformation = new Matrix4x4(
							new Vector4( 0, 0, 1, 0),
							new Vector4( 1, 0, 0, 0),
							new Vector4( 0, 1, 0, 0),
							new Vector4( 0, 0, 0, 1)) *
							Matrix4x4.Rotate(rotation) *
							transformation;
					transformation = SourceEngineUnits.InvModelMatrix * transformation;
				}

				for (int i = 0; i < vertices.Length; i++)
				{
					vertices[i] = transformation.MultiplyPoint(vertices[i]);
				}
				for (int i = 0; i < normals.Length; i++)
				{ 
					normals[i] = transformation.MultiplyVector(normals[i]);
				}
				if (tangents != null)
				{
					for (int i = 0; i < tangents.Length; i++)
					{
						tangents[i] = transformation.MultiplyVector(tangents[i]);
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
					subMeshCount = SubMeshes.Count,
					name	     = Name,
					bounds		 = Model.MDL.ViewBounds
				};

				for (var i = 0; i < SubMeshes.Count; i++)
				{
					Mesh.SetTriangles(SubMeshes[i].Indices.ToArray(), i);
				}

				if (tangents == null)
					Mesh.RecalculateTangents();
				Mesh.RecalculateBounds();

				if (skeleton != null && skeleton.Bones.Count > 0)
				{
					var skinnedMeshRenderer = MeshObject.AddComponent<SkinnedMeshRenderer>();
					skinnedMeshRenderer.sharedMesh			= Mesh;
					skinnedMeshRenderer.bones				= skeleton.Bones.ToArray();
					skinnedMeshRenderer.updateWhenOffscreen = true;
					if ((Model.MDL.Flags & Studioflags.DoNotCastShadows) == Studioflags.DoNotCastShadows)
						skinnedMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					else
					if (isDoubleSided ||
						(Model.MDL.Flags & Studioflags.TranslucentTwopass) == Studioflags.TranslucentTwopass)
						skinnedMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

					// TODO: figure out the root-bones (zombie -> headcrab needs a different rootbone than body? maybe?)
					//skinnedMeshRenderer.rootBone = skeleton.Bones[0];
					Renderer = skinnedMeshRenderer;
					Animation = MeshObject.AddComponent<Animation>();
				} else
				{
					var meshFilter = MeshObject.AddComponent<MeshFilter>();
					var meshRenderer = MeshObject.AddComponent<MeshRenderer>();
					meshFilter.sharedMesh = Mesh;
					if ((Model.MDL.Flags & Studioflags.DoNotCastShadows) == Studioflags.DoNotCastShadows)
						meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					else
					if (isDoubleSided ||
						(Model.MDL.Flags & Studioflags.TranslucentTwopass) == Studioflags.TranslucentTwopass)
						meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

					if (MeshObject.GetComponent<MeshCollider>() == null)
						MeshObject.AddComponent<MeshCollider>();
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
			if (skin == 0)
			{
				var destinationPath = Path.ChangeExtension(outputPath, ".prefab");
				var foundAsset = UnityAssets.Load<GameObject>(destinationPath);
				if (foundAsset != null)
					return foundAsset;
			} else
			{
				var destinationPath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + "[0].prefab");
				var foundAsset = UnityAssets.Load<GameObject>(destinationPath);
				if (foundAsset != null)
					return foundAsset;
			}

			var skinnedModels = Import(gameResources, model, outputPath);
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

		public static GameObject[] Import(GameResources gameResources, SourceModel model, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".prefab");
			var foundAsset = UnityAssets.Load<GameObject>(destinationPath);
			if (foundAsset != null)
			{
				return new GameObject[1] { foundAsset };
			}

			{
				var skinPath0 = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + "[0].prefab");
				foundAsset = UnityAssets.Load<GameObject>(skinPath0);
				if (foundAsset != null)
				{
					List<GameObject> foundAssets = new();
					int index = 0;
					do
					{
						foundAssets.Add(foundAsset);
						index++;
						var skinPathN = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + $"[{index}].prefab");
						foundAsset = UnityAssets.Load<GameObject>(skinPathN);
					} while (foundAsset != null);
					return foundAssets.ToArray();
				}
			}


			if (model == null)
				return null;

			var modelMaterials = ImportModelTextures(gameResources, model);

			//var modelEntries = ImportModelLods(gameResources, model, modelMaterials, out MdlSkeleton skeleton, skin);
			var modelEntries = ImportModelLods(gameResources, model, out MdlSkeleton skeleton);
			if (modelEntries == null)
			{
				Debug.LogError("Failed to load model");
				return null;
			}

			// TODO: do this in a temporary scene so we don't dirty the scene or something?
			// TODO: ideally we'd be exporting to fbx and importing that instead of a prefab

			string assetPath = UnityAssets.GetAssetPath(destinationPath);
			PackagePath.EnsureDirectoriesExist(outputPath);
			GameObject prefabGameObject = new();

			var haveMultipleSkins = modelMaterials.skins != null && modelMaterials.skins.Length > 0;
			var defaultSkin = (!haveMultipleSkins || modelMaterials.skins[0] == null) ? modelMaterials : modelMaterials.skins[0];

			var skinPath = assetPath;
			if (haveMultipleSkins)
			{
				skinPath = Path.Combine(Path.GetDirectoryName(assetPath), Path.GetFileNameWithoutExtension(assetPath) + "[0].prefab");
			}

			// prefabs are wack. we need to save prefabs several times to be able to attach the meshes that it uses to it
			var prefab = PrefabUtility.SaveAsPrefabAsset(prefabGameObject, skinPath);
			
			var instantiatedPrefab = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
			PopulatePrefab(instantiatedPrefab, model, modelEntries, skeleton);
			foreach (var modelEntry in modelEntries)
			{
				if (modelEntry.Mesh != null)
					AssetDatabase.AddObjectToAsset(modelEntry.Mesh, prefab);
			}
			AssetDatabase.SaveAssets();

			SetSkin(instantiatedPrefab, modelEntries, defaultSkin);

			var prefab2 = PrefabUtility.SaveAsPrefabAsset(instantiatedPrefab, skinPath);
			AssetDatabase.SaveAssets();

			GameObject.DestroyImmediate(prefabGameObject);
			GameObject.DestroyImmediate(instantiatedPrefab);

			GameObject[] prefabs;
			if (haveMultipleSkins)
			{
				prefabs = new GameObject[modelMaterials.skins.Length];
				prefabs[0] = prefab2;
				for (int i = 1; i < modelMaterials.skins.Length; i++)
				{
					if (modelMaterials.skins[i] == null)
						continue;
					var basePrefab = PrefabUtility.InstantiatePrefab(prefab2) as GameObject;
					SetSkin(basePrefab, modelEntries, modelMaterials.skins[i]);
					skinPath = Path.Combine(Path.GetDirectoryName(assetPath), Path.GetFileNameWithoutExtension(outputPath) + $"[{i}].prefab");
					PrefabUtility.SaveAsPrefabAsset(basePrefab, skinPath);
					GameObject.DestroyImmediate(basePrefab);
					prefabs[i] = UnityAssets.Load<GameObject>(skinPath);
				}
			} else
			{
				prefabs = new GameObject[1];
				prefabs[0] = prefab2;
			}

			return prefabs;
		}

		public static void SetSkin(GameObject parentGO, MdlModelEntry[] modelEntries, MdlMaterialTable skinMaterials)
		{
			if (skinMaterials == null)
				return;

			// We can't use the Renderers in modelEntries directly, since it might point to a Renderer in another prefab
			// So we lookup the Renderers by name, assuming that all meshes are uniquely named 
			// (since they're originated from fbx files this is more likely than not, but we probably should double check this / fixup when necessary)
			var rendererLookup = new Dictionary<string, Renderer>();
			var allChildRenderers = parentGO.GetComponentsInChildren<Renderer>();
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

				var modelRenderer = rendererLookup[modelEntry.Name];
				modelRenderer.sharedMaterials = materials;
			}
		}

		public static void PopulatePrefab(GameObject prefab, SourceModel model, MdlModelEntry[] modelEntries, MdlSkeleton skeleton)
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

			var clips = new List<AnimationClip>();
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

			bool staticPropHack = (model.MDL.Flags & Studioflags.StaticProp) == Studioflags.StaticProp;

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

				modelEntry.GenerateMesh(prefab, skeleton, staticPropHack);
				if (modelEntry.Animation)
				{
					if (clips.Count > 0)
					{
						foreach (var clip in clips)
						{
							clip.legacy = true;
							modelEntry.Animation.AddClip(clip, clip.name);
						}
						modelEntry.Animation.clip = clips[0];
					}
				}
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
			var mdlHeader = model.MDL;

			var sourceMaterials = new VMT[mdlHeader.TextureFilenames.Length];
			var outputPaths = new string[mdlHeader.TextureFilenames.Length];
			var unityMaterials = new Material[mdlHeader.TextureFilenames.Length];
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

			for (var i = 0; i < sourceMaterials.Length; i++)
			{
				if (sourceMaterials[i] == null)
					continue;

				var entry = gameResources.GetEntry(outputPaths[i], PackagePath.DefaultMaterialPaths);
				if (entry != null)
				{
					var outputPath = PackagePath.GetOutputPath(entry.keyname);
					unityMaterials[i] = MaterialImporter.Import(gameResources, sourceMaterials[i], outputPath);
				} else
					unityMaterials[i] = null;
			}

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

			{ 
				skeleton = null;
				bool hasBones = mdlHeader.Bones.Length > 1;
				if (hasBones)
				{
					skeleton = new MdlSkeleton();
					for (int i = 0; i < mdlHeader.Bones.Length; i++)
					{
						var bone = mdlHeader.Bones[i];
						if (string.IsNullOrWhiteSpace(bone.Name))
						{
							if (i == 0)
								bone.Name = "Root";
							else
								bone.Name = "Bone";
						}
						var boneObject = new GameObject(bone.Name);
						boneObject.SetActive(false);
						var boneObjectTransform = boneObject.transform;
						skeleton.Bones.Add(boneObjectTransform);
					}

					for (int i = 0; i < mdlHeader.Bones.Length; i++)
					{
						var bone = mdlHeader.Bones[i];
						var parentIndex = bone.ParentBone;
						if (parentIndex >= 0 && parentIndex < mdlHeader.Bones.Length)
						{
							/*
							var lineDrawer = skeleton.Bones[i].gameObject.AddComponent<BoneConnectionDrawer>();
							lineDrawer.parent = skeleton.Bones[parentIndex];
							*/
							skeleton.Bones[i].SetParent(skeleton.Bones[parentIndex], true);
						}


						// TODO: figure out PositionScale/RotationScale, are those necessary?
						var translation = bone.Position;// new Vector3(bone.Position.x * bone.PositionScale.x, bone.Position.y * bone.PositionScale.y, bone.Position.z * bone.PositionScale.z);
						var scale = Vector3.one;//bone.RotationScale 
						var rotation = bone.Quaternion;

						//if (parentIndex < 0)
						{
							var m = Matrix4x4.TRS(translation, rotation, scale);
							m = SourceEngineUnits.VmfSourceToUnity * m;
							m.Decompose(out translation, out rotation, out scale);
						}

						skeleton.Bones[i].SetLocalPositionAndRotation(translation, rotation);
						skeleton.Bones[i].localScale = scale;

						skeleton.Bones[i].gameObject.SetActive(true);
					}
				}
			}

			
			for (int bodyID = 0; bodyID < mdlHeader.Bodyparts.Length; ++bodyID)
			{
				var vtxBodyPart	= vtxHeader.BodyPartHeaders[bodyID];
				var mdlBodyPart	= mdlHeader.Bodyparts[bodyID];

				for (int vtxModelID = 0; vtxModelID < mdlBodyPart.Models.Length; ++vtxModelID)
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

							//var isTransparent  = sourceMaterial.HaveTransparency;
							var isDoubleSided  = //material.NoCull || 
												 (//isTransparent & 
												((model.MDL.Flags & Studioflags.TranslucentTwopass) == Studioflags.TranslucentTwopass));

							modelEntry.isDoubleSided = isDoubleSided;

							modelEntry.Name = mdlModel.Name + " +" + mdlBodyPart.Name + "[" + mdlMesh.MaterialIndex + "]".Trim();

							var indexLookup			= new Dictionary<int, int>();
							var inverseIndexLookup	= new Dictionary<int, int>();
							
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

											{
												if (!indexLookup.TryGetValue(vertex1, out int index1)) index1 = -1;
												if (!indexLookup.TryGetValue(vertex2, out int index2)) index2 = -1;
												if (!indexLookup.TryGetValue(vertex3, out int index3)) index3 = -1;


												if (index1 == -1)
												{
													index1 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex1].Position);
													modelEntry.Normals.Add(vertices[vertex1].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex1].TexCoord));
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex1]));
													if (tangents != null) modelEntry.Tangents.Add(tangents[vertex1]);
													indexLookup[vertex1] = index1;
												}

												if (index2 == -1)
												{
													index2 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex2].Position);
													modelEntry.Normals.Add(vertices[vertex2].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex2].TexCoord));
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex2]));
													if (tangents != null) modelEntry.Tangents.Add(tangents[vertex2]);
													indexLookup[vertex2] = index2;
												}

												if (index3 == -1)
												{
													index3 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex3].Position);
													modelEntry.Normals.Add(vertices[vertex3].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex3].TexCoord));
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

											if (isDoubleSided)
											{
												if (!inverseIndexLookup.TryGetValue(vertex1, out int index1)) index1 = -1;
												if (!inverseIndexLookup.TryGetValue(vertex2, out int index2)) index2 = -1;
												if (!inverseIndexLookup.TryGetValue(vertex3, out int index3)) index3 = -1;

												if (index1 == -1)
												{
													index1 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex1].Position);
													modelEntry.Normals.Add(-vertices[vertex1].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex1].TexCoord));
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex1]));
													if (tangents != null) modelEntry.Tangents.Add(-tangents[vertex1]);
													inverseIndexLookup[vertex1] = index1;
												}

												if (index3 == -1)
												{
													index3 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex3].Position);
													modelEntry.Normals.Add(-vertices[vertex3].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex3].TexCoord));
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex3]));
													if (tangents != null) modelEntry.Tangents.Add(-tangents[vertex3]);
													inverseIndexLookup[vertex3] = index3;
												}

												if (index2 == -1)
												{
													index2 = modelEntry.Vertices.Count;
													modelEntry.Vertices.Add(vertices[vertex2].Position);
													modelEntry.Normals.Add(-vertices[vertex2].Normal);
													modelEntry.Uvs.Add(SourceEngineUnits.VmfFixTexcoord(vertices[vertex2].TexCoord));
													if (hasBones) modelEntry.BoneWeights.Add(ToUnityBoneWeight(vertices[vertex2]));
													if (tangents != null) modelEntry.Tangents.Add(-tangents[vertex2]);
													inverseIndexLookup[vertex2] = index2;
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

		// TODO: clean up below

		private static StudioSequenceDescription pSeqdesc(MDL header, int i )
		{
			var localSequences = header.LocalSequences;
			Debug.Assert( ( i >= 0 && i < localSequences.Length ) || ( i == 1 && localSequences.Length <= 1 ) );
			if ( i < 0 || i >= localSequences.Length )
			{
				if ( localSequences.Length <= 0 )
				{
					// Return a zero'd out struct reference if we've got nothing.
					// C_BaseObject::StopAnimGeneratedSounds was crashing due to this function
					//	returning a reference to garbage. It should now see numevents is 0,
					//	and bail.
					return StudioSequenceDescription.empty;
				}

				// Avoid reading random memory.
				i = 0;
			}
	
			//if (m_pVModel == null)
			//{
			//	return localSequences[i];
			//}

			MDL pStudioHdr = header;//GroupStudioHdr( m_pVModel.m_seq[i].group );

			return pStudioHdr.LocalSequences[i];// m_pVModel.m_seq[i].index ];
		}

		private static StudioAnimationDescription pLocalAnimdesc(MDL header, int i )
		{
			var localAnimationDescriptions = header.LocalAnimationDescriptions;
			if (i < 0 || i >= localAnimationDescriptions.Length) i = 0;
			return localAnimationDescriptions[i];
		}

		private static StudioAnimationDescription pAnimdesc(MDL header,  int i ) 
		{ 
			if (header.IncludeModels.Length == 0)
			{
				return pLocalAnimdesc(header, i );
			}
			var localAnimationDescriptions = header.LocalAnimationDescriptions;
			if (i < 0 || i >= localAnimationDescriptions.Length) i = 0;
			return localAnimationDescriptions[i];
			/*
			virtualmodel_t *pVModel = (virtualmodel_t *)header.GetVirtualModel();
			virtualgroup_t			pGroup		= &pVModel.m_group[ pVModel.m_anim[i].group ];
			MdlHeader		pStudioHdr	= pGroup.GetStudioHdr();
			return pLocalAnimdesc(pStudioHdr, pVModel.m_anim[i].index );*/
		}
		/*
		static void ExtractAnimValue( int frame, StudioAnimValue panimvalue, float scale, out float v1)
		{
			if ( panimvalue == null )
			{
				v1 = 0;
				return;
			}

			int k = frame;

			while (panimvalue.Total <= k)
			{
				k -= panimvalue.Total;
				panimvalue += panimvalue.Valid + 1;
				if ( panimvalue.Total == 0 )
				{
					Debug.Assert(false); // running off the end of the animation stream is bad
					v1 = 0;
					return;
				}
			}
			if (panimvalue.Valid > k)
			{
				v1 = panimvalue[k+1].value * scale;
			}
			else
			{
				// get last valid data block
				v1 = panimvalue[panimvalue.Valid].value * scale;
			}
		}

		//-----------------------------------------------------------------------------
		// Purpose: return a sub frame rotation for a single bone
		//-----------------------------------------------------------------------------
		static void CalcBoneQuaternion(int frame, float s, 
									   Quaternion baseQuat, Vector3 baseRot, Vector3 baseRotScale, 
									   StudioBoneFlags iBaseFlags, Quaternion baseAlignment, 
									   StudioAnimation panim, out Quaternion q)
		{
			if ( (panim.Flags & StudioAnimationFlags.RawRotation) != 0 ||
				 (panim.Flags & StudioAnimationFlags.RawRotation2) != 0 )
			{
				q = panim.Rotation;
				return;
			}

			if ( (panim.Flags & StudioAnimationFlags.AnimRotation) == 0 )
			{
				if ((panim.Flags & StudioAnimationFlags.Delta) != 0)
				{
					q = Quaternion.identity;
				}
				else
				{
					q = baseQuat;
				}
				return;
			}

			var values = panim.AnimValues;

			if (s > 0.001f)
			{
				Quaternion	q1, q2;
				Vector3		eulerAngle1, eulerAngle2;

				ExtractAnimValue( frame, values[0], baseRotScale.x, eulerAngle1.x, eulerAngle2.x );
				ExtractAnimValue( frame, values[1], baseRotScale.y, eulerAngle1.y, eulerAngle2.y );
				ExtractAnimValue( frame, values[2], baseRotScale.z, eulerAngle1.z, eulerAngle2.z );

				if ((panim.Flags & StudioAnimationFlags.Delta) == 0)
				{
					eulerAngle1.x = eulerAngle1.x + baseRot.x;
					eulerAngle1.y = eulerAngle1.y + baseRot.y;
					eulerAngle1.z = eulerAngle1.z + baseRot.z;
					eulerAngle2.x = eulerAngle2.x + baseRot.x;
					eulerAngle2.y = eulerAngle2.y + baseRot.y;
					eulerAngle2.z = eulerAngle2.z + baseRot.z;
				}
				
				if (eulerAngle1.x != eulerAngle2.x || eulerAngle1.y != eulerAngle2.y || eulerAngle1.z != eulerAngle2.z)
				{
					AngleQuaternion( eulerAngle1, q1 );
					AngleQuaternion( eulerAngle2, q2 );
					
					QuaternionBlend( q1, q2, s, q );
				}
				else
				{
					AngleQuaternion( eulerAngle1, q );
				}
			}
			else
			{
				Vector3 angle;

				ExtractAnimValue( frame, values[0], baseRotScale.x, out angle.x );
				ExtractAnimValue( frame, values[1], baseRotScale.y, out angle.y );
				ExtractAnimValue( frame, values[2], baseRotScale.z, out angle.z );
				
				if ((panim.Flags & StudioAnimationFlags.Delta) == 0)
				{
					angle.x = angle.x + baseRot.x;
					angle.y = angle.y + baseRot.y;
					angle.z = angle.z + baseRot.z;
				}
				
				AngleQuaternion( angle, q );
			}
			
			// align to unified bone
			if ((panim.Flags & StudioAnimationFlags.Delta) == 0 && 
				(iBaseFlags & StudioBoneFlags.FixedAlignment) != 0)
			{
				QuaternionAlign( baseAlignment, q, q );
			}
		}

		static void CalcBoneQuaternion( int frame, float s, 
								StudioBone pBone,
								StudioAnimation panim, 
								out Quaternion q)
		{
			CalcBoneQuaternion( frame, s, pBone.Quaternion, pBone.RadianEulerRotation, pBone.RotationScale, pBone.Flags, pBone.QuaternionAlignment, panim, out q );
		}
		*/

		private static void SetupSingleBoneMatrix(MDL					 header, 
										  StudioSequenceDescription	 seqdesc, 
										  StudioAnimationDescription animdesc, 
										  StudioAnimation			 panim, 
										  StudioBone				 pbone,
										  int iBone, 
										  out Vector3 bonePos, out Quaternion boneQuat)
		{
			//int iLocalFrame = iFrame;
			//float s = 0;
			
			// search for bone
			while ((panim != null) && panim.Bone != iBone)
			{
				panim = panim.Next;
			}

			// look up animation if found, if not, initialize
			if (panim != null && seqdesc.boneWeights[iBone] > 0)
			{
				throw new NotImplementedException(); // code beyond this has not been implemented/tested
				//CalcBoneQuaternion( iLocalFrame, s, pbone, null, panim, boneQuat );
				//CalcBonePosition  ( iLocalFrame, s, pbone, null, panim, bonePos );
			}
			else if ((animdesc.flags & AnimationType.Delta) != 0)
			{
				boneQuat = Quaternion.identity;
				bonePos = Vector3.zero;
			}
			else
			{
				boneQuat = pbone.Quaternion;
				bonePos = pbone.Position;
			}
		}

		static readonly Dictionary<string, AnimationClip[]> modelAnimationClips = new Dictionary<string, AnimationClip[]>();

		static List<AnimationClip> localClips = new List<AnimationClip>();

		private static AnimationClip[] ModelToClips(MDL header, string modelName, string modelLabel)
		{
			AnimationClip[] modelClips;
			if (modelAnimationClips.TryGetValue(modelName + ":" + modelLabel, out modelClips))
				return modelClips;

			//return new AnimationClip[0];
			var bones			= header.Bones;
			var localSequences	= header.LocalSequences;

			localClips.Clear();

			var xPositionKeys = new List<Keyframe>();
			var yPositionKeys = new List<Keyframe>();
			var zPositionKeys = new List<Keyframe>();

			var xRotationKeys = new List<Keyframe>();
			var yRotationKeys = new List<Keyframe>();
			var zRotationKeys = new List<Keyframe>();
			var wRotationKeys = new List<Keyframe>();

			for (int s = 0; s < localSequences.Length; s++)
			{
				var localSequence	= localSequences[s];
				var name			= localSequence.label;

				if (!string.IsNullOrEmpty(modelLabel) && name != modelLabel)
					continue;

				StudioAnimationDescription animdesc = pAnimdesc(header, localSequence.anim[0, 0]);

				//Debug.Log(animdesc.numframes);
				
				WrapMode wrapMode = WrapMode.Default;
				if ((localSequence.flags & AnimationType.Looping) != 0)
					wrapMode = WrapMode.Loop;

				var animationClip = new AnimationClip() { name = name };
				for (int b = 0; b < bones.Length; b++)
				{
					if (localSequence.boneWeights[b] <= 0)
						continue;

					xPositionKeys.Clear();
					yPositionKeys.Clear();
					zPositionKeys.Clear();

					xRotationKeys.Clear();
					yRotationKeys.Clear();
					zRotationKeys.Clear();
					wRotationKeys.Clear();

					Vector3    prevBonePos = Vector3.zero;
					Quaternion prevBoneQuat = Quaternion.identity;
					StudioBone pbone = header.Bones[b];

					float fps = (animdesc.fps > 0) ? animdesc.fps : 30;

					for (int f = 0; f < animdesc.numframes; f++)
					{
						var time = f / fps;
						Vector3    bonePos;
						Quaternion boneQuat;

						StudioAnimation panim = animdesc.pAnim(header, ref f);
						SetupSingleBoneMatrix(header, localSequence, animdesc, panim, pbone, b, out bonePos, out boneQuat);

						if (f == 0 || bonePos.x  != prevBonePos.x ) xPositionKeys.Add(new Keyframe(time, bonePos.x));
						if (f == 0 || bonePos.y  != prevBonePos.y ) yPositionKeys.Add(new Keyframe(time, bonePos.y));
						if (f == 0 || bonePos.z  != prevBonePos.z ) zPositionKeys.Add(new Keyframe(time, bonePos.z));

						if (f == 0 || boneQuat.x != prevBoneQuat.x) xRotationKeys.Add(new Keyframe(time, boneQuat.x));
						if (f == 0 || boneQuat.y != prevBoneQuat.y) yRotationKeys.Add(new Keyframe(time, boneQuat.y));
						if (f == 0 || boneQuat.z != prevBoneQuat.z) zRotationKeys.Add(new Keyframe(time, boneQuat.z));
						if (f == 0 || boneQuat.w != prevBoneQuat.w) wRotationKeys.Add(new Keyframe(time, boneQuat.w));

						prevBonePos = bonePos;
						prevBoneQuat = boneQuat;
					}

					if (xPositionKeys.Count > 0)
					{
						var xPositionCurve = new AnimationCurve(xPositionKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localPosition.x", xPositionCurve);
					}

					if (yPositionKeys.Count > 0)
					{
						var yPositionCurve = new AnimationCurve(yPositionKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localPosition.y", yPositionCurve);
					}

					if (zPositionKeys.Count > 0)
					{
						var zPositionCurve = new AnimationCurve(zPositionKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localPosition.z", zPositionCurve);
					}

					if (xRotationKeys.Count > 0)
					{
						var xRotationCurve = new AnimationCurve(xRotationKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localRotation.x", xRotationCurve);
					}

					if (yRotationKeys.Count > 0)
					{
						var yRotationCurve = new AnimationCurve(yRotationKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localRotation.y", yRotationCurve);
					}

					if (zRotationKeys.Count > 0)
					{
						var zRotationCurve = new AnimationCurve(zRotationKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localRotation.z", zRotationCurve);
					}

					if (wRotationKeys.Count > 0)
					{
						var wRotationCurve = new AnimationCurve(wRotationKeys.ToArray()) { postWrapMode = wrapMode, preWrapMode = wrapMode };
						animationClip.SetCurve("", typeof(Transform), "localRotation.z", wRotationCurve);
					}
				}
				localClips.Add(animationClip);
				//break;
			}

			modelClips = localClips.ToArray();
			modelAnimationClips[modelName + ":" + modelLabel] = modelClips;
			return modelClips;
		}
	}
}
