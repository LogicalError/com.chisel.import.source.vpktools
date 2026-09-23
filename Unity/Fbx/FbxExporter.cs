using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools.Fbx
{
	/// <summary>
	/// Writes a Unity hierarchy - meshes, skinning, materials and animation - out as an FBX file.
	/// <para>
	/// Deliberately knows nothing about Source or MDL: it takes a GameObject hierarchy and a list of
	/// sampled takes. Everything specific to the game happens before this point, which keeps the
	/// awkward half (FBX's conventions) separate from the other awkward half (Source's).
	/// </para>
	/// <para>
	/// The files it writes are reused by later imports until the version they record changes, so a
	/// change to what this writes has to bump the version in <c>ModelImporter</c> that covers it, or no
	/// file already on disk ever sees the change.
	/// </para>
	/// </summary>
	public static class FbxExporter
	{
		/// <summary>FBX counts time in these units; one second is exactly this many.</summary>
		const long kFbxTimePerSecond = 46186158000L;

		/// <summary>FBX's key interpolation flag for a straight line between keys.</summary>
		const int  kInterpolationLinear = 4;

		/// <summary>
		/// The packed default tangent weights an FBX key carries. Unused for linear keys, but the
		/// SDK expects the four-float attribute record to be present and well formed.
		/// </summary>
		const double kDefaultKeyWeights = 218434821;

		public sealed class Result
		{
			/// <summary>Material names as written into the file, in the order they were emitted.</summary>
			public string[]   MaterialNames;
			/// <summary>The Unity material each name came from, for remapping the imported model back onto them.</summary>
			public Material[] Materials;
			public int        MeshCount;
			public int        BoneCount;
			public int        TakeCount;
		}

		#region Coordinate conversion

		// Unity is left handed; FBX is right handed. Negating X is the conversion Unity's own FBX
		// tooling uses, and it is the one Unity's importer undoes on the way back in, so a file
		// written this way round-trips to the hierarchy it came from.
		//
		// Mirroring one axis reverses the sense of a rotation and the winding of a triangle, so both
		// are flipped alongside it. Getting either one wrong produces a model that looks right until
		// it is lit (inside-out normals) or animated (rotations running backwards).

		static double[] ToFbxPosition(Vector3 value) => new double[] { -value.x, value.y, value.z };

		static Quaternion ToFbxRotation(Quaternion value) => new(value.x, -value.y, -value.z, value.w);

		static Matrix4x4 ToFbxMatrix(Matrix4x4 matrix)
		{
			// M * matrix * M, with M = diag(-1, 1, 1, 1) - which is its own inverse, so conjugating
			// by it maps the whole transform into the mirrored space consistently.
			var mirror = Matrix4x4.Scale(new Vector3(-1, 1, 1));
			return mirror * matrix * mirror;
		}

		static void AppendMatrix(List<double> target, Matrix4x4 matrix)
		{
			// FBX stores matrices column-major, which is also how Unity indexes m[row, column], so
			// the columns go out one after another.
			for (int column = 0; column < 4; column++)
				for (int row = 0; row < 4; row++)
					target.Add(matrix[row, column]);
		}

		#endregion

		#region Node bookkeeping

		sealed class NodeRecord
		{
			public Transform	Transform;
			public long			Id;
			public string		Name;
			public bool			IsBone;
			public Mesh			Mesh;
			public Material[]	Materials;
			public Matrix4x4[]	BindPoses;
			public Transform[]	SkinBones;
			public long			GeometryId;
			public long			AttributeId;
			/// <summary>Transform relative to the exported hierarchy's root, accumulated once while walking.</summary>
			public Matrix4x4	GlobalMatrix;
		}

		/// <summary>
		/// Everything the write passes need to look each other up. Built once: a character model has
		/// dozens of bones and hundreds of takes, and resolving a node by scanning for it turns that
		/// into a quadratic amount of searching for no reason.
		/// </summary>
		sealed class Hierarchy
		{
			public List<NodeRecord>                   Nodes   = new();
			public Dictionary<Transform, NodeRecord>  ByTransform = new();
			public Dictionary<string, long>           IdByName = new();
		}

		sealed class IdSource
		{
			// Any distinct positive numbers work as object ids. Starting high and counting up keeps
			// them out of the way of the reserved 0 (the scene root).
			long next = 1000000;
			public long Next() => ++next;
		}

		#endregion

		public static Result Export(string path, GameObject root, IReadOnlyList<FbxAnimationTake> takes = null)
		{
			if (root == null) throw new ArgumentNullException(nameof(root));
			if (string.IsNullOrEmpty(path)) throw new ArgumentException("No output path", nameof(path));

			var ids       = new IdSource();
			var hierarchy = CollectNodes(root, ids);
			var nodes     = hierarchy.Nodes;

			MarkAnimatedNodesAsBones(hierarchy, takes, ids);

			var materialNames = new List<string>();
			var materials     = new List<Material>();
			var materialIds   = new List<long>();
			var missingMaterial = CollectMaterials(nodes, ids, materialNames, materials, materialIds);

			var objects     = new FbxNode("Objects") { AlwaysBlock = true };
			var connections = new FbxNode("Connections") { AlwaysBlock = true };

			WriteNodeObjects(objects, connections, hierarchy, root.transform);
			WriteMaterialObjects(objects, materialNames, materialIds);
			WriteMaterialConnections(connections, nodes, materialNames, materialIds, missingMaterial);
			WriteSkins(objects, connections, hierarchy, ids);
			WriteBindPose(objects, nodes, ids);

			var takeNames = WriteAnimation(objects, connections, hierarchy, takes, ids, out var longestTakeSeconds);

			var document = new FbxNode("Root");
			WriteHeader(document);
			WriteGlobalSettings(document, longestTakeSeconds);
			WriteDocuments(document, ids, takeNames.Count > 0 ? takeNames[0] : "Take 001");
			document.Add(new FbxNode("References") { AlwaysBlock = true });
			WriteDefinitions(document, objects);
			document.Add(objects);
			document.Add(connections);
			WriteTakes(document, takeNames);

			FbxDocument.Write(path, document);

			int meshCount = 0, boneCount = 0;
			foreach (var node in nodes)
			{
				if (node.Mesh != null) meshCount++;
				if (node.IsBone)       boneCount++;
			}

			return new Result
			{
				MaterialNames = materialNames.ToArray(),
				Materials     = materials.ToArray(),
				MeshCount     = meshCount,
				BoneCount     = boneCount,
				TakeCount     = takeNames.Count
			};
		}

		#region Collection

		static Hierarchy CollectNodes(GameObject root, IdSource ids)
		{
			var hierarchy = new Hierarchy();
			var nodes     = hierarchy.Nodes;
			var lookup    = hierarchy.ByTransform;

			// Every transform under the root, but NOT the root itself: Unity's model importer creates
			// a root GameObject named after the file, so emitting ours as well would bury the meshes
			// one level deeper - which, among other things, is where the _LOD naming stops being
			// recognised.
			void Walk(Transform transform, Matrix4x4 parentGlobal)
			{
				for (int i = 0; i < transform.childCount; i++)
				{
					var child  = transform.GetChild(i);
					var local  = Matrix4x4.TRS(child.localPosition, child.localRotation, child.localScale);
					var global = parentGlobal * local;
					var record = new NodeRecord
					{
						Transform    = child,
						Id           = ids.Next(),
						Name         = child.name,
						GlobalMatrix = global
					};
					nodes.Add(record);
					lookup[child] = record;
					hierarchy.IdByName[child.name] = record.Id;
					Walk(child, global);
				}
			}
			Walk(root.transform, Matrix4x4.identity);

			// A node is a bone if some skin binds to it. Asking the skins is the only reliable test:
			// nothing about a Transform says "bone", and guessing from names or from having no
			// renderer would catch every empty grouping object too.
			foreach (var record in nodes)
			{
				var skinned = record.Transform.GetComponent<SkinnedMeshRenderer>();
				if (skinned != null && skinned.sharedMesh != null)
				{
					record.Mesh       = skinned.sharedMesh;
					record.Materials  = skinned.sharedMaterials;
					record.SkinBones  = skinned.bones;
					record.BindPoses  = skinned.sharedMesh.bindposes;
					record.GeometryId = ids.Next();

					foreach (var bone in skinned.bones)
					{
						if (bone != null && lookup.TryGetValue(bone, out var boneRecord))
							boneRecord.IsBone = true;
					}
					continue;
				}

				var filter = record.Transform.GetComponent<MeshFilter>();
				if (filter != null && filter.sharedMesh != null)
				{
					record.Mesh       = filter.sharedMesh;
					record.GeometryId = ids.Next();

					var renderer = record.Transform.GetComponent<Renderer>();
					record.Materials = (renderer != null) ? renderer.sharedMaterials : Array.Empty<Material>();
				}
			}

			// A bone's parents are part of the skeleton too, even where nothing binds to them
			// directly, or the chain arrives in Unity as a mix of bones and plain transforms.
			foreach (var record in nodes)
			{
				if (!record.IsBone)
					continue;
				var parent = record.Transform.parent;
				while (parent != null && lookup.TryGetValue(parent, out var parentRecord))
				{
					if (parentRecord.Mesh != null || parentRecord.IsBone)
						break;
					parentRecord.IsBone = true;
					parent = parent.parent;
				}
			}

			return hierarchy;
		}

		/// <summary>
		/// Marks the nodes an animation drives as bones, and gives every bone its skeleton attribute.
		/// <para>
		/// Being bound by a skin is not the only thing that makes a node a bone. A file holding
		/// nothing but a skeleton and animation - which is how Source shares one set of animations
		/// across every character on the same rig - has no skin to ask, so without this every node
		/// would be written as a plain null, the file would declare no skeleton, and an importer would
		/// have nothing to hang the animation on. That failure is silent: the file loads, and contains
		/// no animation.
		/// </para>
		/// </summary>
		static void MarkAnimatedNodesAsBones(Hierarchy hierarchy, IReadOnlyList<FbxAnimationTake> takes, IdSource ids)
		{
			if (takes != null)
			{
				foreach (var take in takes)
				{
					if (take?.Nodes == null)
						continue;
					foreach (var curveSet in take.Nodes)
					{
						if (curveSet?.Node == null)
							continue;
						if (hierarchy.ByTransform.TryGetValue(curveSet.Node, out var record) && record.Mesh == null)
							record.IsBone = true;
					}
				}
			}

			foreach (var record in hierarchy.Nodes)
			{
				if (record.IsBone && record.AttributeId == 0)
					record.AttributeId = ids.Next();
			}
		}

		/// <summary>
		/// The name given to a submesh whose renderer slot is empty, so that it is visibly a missing material
		/// rather than silently Unity's white default.
		/// </summary>
		const string kMissingMaterialName = "MissingMaterial";

		/// <summary>
		/// Emits one FBX material per distinct Unity material, and returns the index of the placeholder that
		/// stands in for empty slots, or -1 when there are none.
		/// </summary>
		/// <remarks>
		/// AN EMPTY SLOT STILL OWNS ITS SUBMESH INDEX. The geometry writes the raw submesh number into
		/// LayerElementMaterial (<see cref="WriteGeometry"/>), and a node's material-layer indices are simply the
		/// ORDER its materials are connected in - so skipping a null used to slide every later submesh onto its
		/// neighbour's material and leave the last one indexing past the end of the list, where Unity substitutes a
		/// white default material of its own. That is where the jeep's white windows came from, and the shift is the
		/// worse half of it: the surfaces that still had a material were wearing the wrong one, which looks like
		/// nothing at all whenever two materials happen to be similar.
		/// One placeholder, emitted before everything else, keeps submesh N on connection N.
		/// </remarks>
		static int CollectMaterials(List<NodeRecord> nodes, IdSource ids,
									List<string> names, List<Material> materials, List<long> materialIds)
		{
			var seen = new Dictionary<Material, int>();
			var used = new HashSet<string>();

			var missing = -1;
			foreach (var node in nodes)
			{
				if (node.Materials == null)
					continue;
				foreach (var material in node.Materials)
					if (material == null) { missing = 0; break; }
				if (missing == 0)
					break;
			}
			if (missing == 0)
			{
				used.Add(kMissingMaterialName);
				names.Add(kMissingMaterialName);
				// Null: there is no Unity material to remap onto, and ModelImporter skips a null rather than
				// clearing the remap, so the model keeps whatever Unity imported for this name.
				materials.Add(null);
				materialIds.Add(ids.Next());
			}

			foreach (var node in nodes)
			{
				if (node.Materials == null)
					continue;

				foreach (var material in node.Materials)
				{
					if (material == null || seen.ContainsKey(material))
						continue;

					// FBX has no material ids, only names, and the name is also what the model
					// importer keys its remapping on - so two materials that share a name would end
					// up sharing an assignment. Uniqueness is forced here rather than hoped for.
					var name = string.IsNullOrEmpty(material.name) ? "Material" : material.name;
					var unique = name;
					var suffix = 1;
					while (!used.Add(unique))
						unique = $"{name}_{suffix++}";

					seen[material] = names.Count;
					names.Add(unique);
					materials.Add(material);
					materialIds.Add(ids.Next());
				}
			}
			return missing;
		}

		#endregion

		#region Header and settings

		static void WriteHeader(FbxNode document)
		{
			var header = document.Add(new FbxNode("FBXHeaderExtension") { AlwaysBlock = true });
			header.Add("FBXHeaderVersion", FbxProperty.Int(1003));
			header.Add("FBXVersion", FbxProperty.Int(7400));
			header.Add("EncryptionType", FbxProperty.Int(0));

			var now = DateTime.Now;
			var stamp = header.Add(new FbxNode("CreationTimeStamp") { AlwaysBlock = true });
			stamp.Add("Version", FbxProperty.Int(1000));
			stamp.Add("Year", FbxProperty.Int(now.Year));
			stamp.Add("Month", FbxProperty.Int(now.Month));
			stamp.Add("Day", FbxProperty.Int(now.Day));
			stamp.Add("Hour", FbxProperty.Int(now.Hour));
			stamp.Add("Minute", FbxProperty.Int(now.Minute));
			stamp.Add("Second", FbxProperty.Int(now.Second));
			stamp.Add("Millisecond", FbxProperty.Int(now.Millisecond));

			header.Add("Creator", FbxProperty.String("Chisel Source importer"));
		}

		static void WriteGlobalSettings(FbxNode document, double longestTakeSeconds)
		{
			var settings = document.Add(new FbxNode("GlobalSettings") { AlwaysBlock = true });
			settings.Add("Version", FbxProperty.Int(1000));

			var properties = settings.Add(new FbxNode("Properties70") { AlwaysBlock = true });
			// Y up, Z front, X right - the axis system Unity's importer expects, and the one the
			// negated X above converts into.
			properties.P("UpAxis", "int", "Integer", "", FbxProperty.Int(1));
			properties.P("UpAxisSign", "int", "Integer", "", FbxProperty.Int(1));
			properties.P("FrontAxis", "int", "Integer", "", FbxProperty.Int(2));
			properties.P("FrontAxisSign", "int", "Integer", "", FbxProperty.Int(1));
			properties.P("CoordAxis", "int", "Integer", "", FbxProperty.Int(0));
			properties.P("CoordAxisSign", "int", "Integer", "", FbxProperty.Int(1));
			properties.P("OriginalUpAxis", "int", "Integer", "", FbxProperty.Int(1));
			properties.P("OriginalUpAxisSign", "int", "Integer", "", FbxProperty.Int(1));

			// UnitScaleFactor is how many centimetres one file unit is. The hierarchy handed to this
			// exporter is already in Unity metres, so declaring the unit to BE the metre means the
			// numbers go out untouched and come back the same size, with no scale factor anywhere to
			// get backwards.
			properties.P("UnitScaleFactor", "double", "Number", "", FbxProperty.Double(100));
			properties.P("OriginalUnitScaleFactor", "double", "Number", "", FbxProperty.Double(100));

			properties.P("AmbientColor", "ColorRGB", "Color", "",
						 FbxProperty.Double(0), FbxProperty.Double(0), FbxProperty.Double(0));
			properties.P("DefaultCamera", "KString", "", "", FbxProperty.String("Producer Perspective"));
			properties.P("TimeMode", "enum", "", "", FbxProperty.Int(11));   // eCustom
			properties.P("TimeSpanStart", "KTime", "Time", "", FbxProperty.Long(0));
			properties.P("TimeSpanStop", "KTime", "Time", "",
						 FbxProperty.Long((long)(longestTakeSeconds * kFbxTimePerSecond)));
			properties.P("CustomFrameRate", "double", "Number", "", FbxProperty.Double(-1));
		}

		static void WriteDocuments(FbxNode document, IdSource ids, string activeTake)
		{
			var documents = document.Add(new FbxNode("Documents") { AlwaysBlock = true });
			documents.Add("Count", FbxProperty.Int(1));

			var scene = documents.Add(new FbxNode("Document"));
			scene.Prop(FbxProperty.Long(ids.Next()), FbxProperty.String(""), FbxProperty.String("Scene"));

			var properties = scene.Add(new FbxNode("Properties70") { AlwaysBlock = true });
			properties.P("SourceObject", "object", "", "");
			properties.P("ActiveAnimStackName", "KString", "", "", FbxProperty.String(activeTake));

			scene.Add("RootNode", FbxProperty.Long(0));
		}

		static void WriteDefinitions(FbxNode document, FbxNode objects)
		{
			var counts = new Dictionary<string, int>();
			foreach (var child in objects.Children)
			{
				counts.TryGetValue(child.Name, out var count);
				counts[child.Name] = count + 1;
			}

			var definitions = document.Add(new FbxNode("Definitions") { AlwaysBlock = true });
			definitions.Add("Version", FbxProperty.Int(100));

			var total = 1;
			foreach (var count in counts.Values)
				total += count;
			definitions.Add("Count", FbxProperty.Int(total));

			var globals = definitions.Add(new FbxNode("ObjectType"));
			globals.Prop(FbxProperty.String("GlobalSettings"));
			globals.Add("Count", FbxProperty.Int(1));

			foreach (var pair in counts)
			{
				var entry = definitions.Add(new FbxNode("ObjectType"));
				entry.Prop(FbxProperty.String(pair.Key));
				entry.Add("Count", FbxProperty.Int(pair.Value));
			}
		}

		static void WriteTakes(FbxNode document, List<string> takeNames)
		{
			var takes = document.Add(new FbxNode("Takes") { AlwaysBlock = true });
			takes.Add("Current", FbxProperty.String(takeNames.Count > 0 ? takeNames[0] : ""));
		}

		#endregion

		#region Models and geometry

		static void WriteNodeObjects(FbxNode objects, FbxNode connections, Hierarchy hierarchy, Transform root)
		{
			var lookup = hierarchy.ByTransform;

			foreach (var node in hierarchy.Nodes)
			{
				var kind = (node.Mesh != null) ? "Mesh" : (node.IsBone ? "LimbNode" : "Null");

				var model = objects.Add(new FbxNode("Model"));
				model.Prop(FbxProperty.Long(node.Id), FbxProperty.String("Model::" + node.Name), FbxProperty.String(kind));
				model.Add("Version", FbxProperty.Int(232));

				var properties = model.Add(new FbxNode("Properties70") { AlwaysBlock = true });
				properties.P("RotationActive", "bool", "", "", FbxProperty.Int(1));
				// 0 is eEulerXYZ, which is the order FbxEuler decomposes into. Stated rather than
				// left to the default, because the default is exactly the sort of thing that changes
				// the meaning of every rotation in the file if it is ever not what was assumed.
				properties.P("RotationOrder", "enum", "", "", FbxProperty.Int(0));
				properties.P("InheritType", "enum", "", "", FbxProperty.Int(0));
				properties.P("ScalingMax", "Vector3D", "Vector", "",
							 FbxProperty.Double(0), FbxProperty.Double(0), FbxProperty.Double(0));
				properties.P("DefaultAttributeIndex", "int", "Integer", "", FbxProperty.Int(0));

				var translation = ToFbxPosition(node.Transform.localPosition);
				var rotation    = FbxEuler.ToEulerXYZ(ToFbxRotation(node.Transform.localRotation));
				var scale       = node.Transform.localScale;

				properties.P("Lcl Translation", "Lcl Translation", "", "A",
							 FbxProperty.Double(translation[0]), FbxProperty.Double(translation[1]), FbxProperty.Double(translation[2]));
				properties.P("Lcl Rotation", "Lcl Rotation", "", "A",
							 FbxProperty.Double(rotation.x), FbxProperty.Double(rotation.y), FbxProperty.Double(rotation.z));
				properties.P("Lcl Scaling", "Lcl Scaling", "", "A",
							 FbxProperty.Double(scale.x), FbxProperty.Double(scale.y), FbxProperty.Double(scale.z));

				model.Add("Shading", FbxProperty.Raw("Y"));
				model.Add("Culling", FbxProperty.String("CullingOff"));

				// Parent it. A node whose parent is the hierarchy root connects to 0, the FBX scene
				// root, because the root itself is not exported.
				var parent   = node.Transform.parent;
				var parentId = (parent != null && parent != root && lookup.TryGetValue(parent, out var parentRecord))
							   ? parentRecord.Id : 0;
				connections.Add("C", FbxProperty.String("OO"), FbxProperty.Long(node.Id), FbxProperty.Long(parentId));

				if (node.IsBone)
				{
					var attribute = objects.Add(new FbxNode("NodeAttribute"));
					attribute.Prop(FbxProperty.Long(node.AttributeId),
								   FbxProperty.String("NodeAttribute::"),
								   FbxProperty.String("LimbNode"));
					var attributeProperties = attribute.Add(new FbxNode("Properties70") { AlwaysBlock = true });
					attributeProperties.P("Size", "double", "Number", "", FbxProperty.Double(1));
					attribute.Add("TypeFlags", FbxProperty.String("Skeleton"));

					connections.Add("C", FbxProperty.String("OO"),
									FbxProperty.Long(node.AttributeId), FbxProperty.Long(node.Id));
				}

				if (node.Mesh != null)
				{
					WriteGeometry(objects, node);
					connections.Add("C", FbxProperty.String("OO"),
									FbxProperty.Long(node.GeometryId), FbxProperty.Long(node.Id));
				}
			}
		}

		static void WriteGeometry(FbxNode objects, NodeRecord node)
		{
			var mesh = node.Mesh;

			var geometry = objects.Add(new FbxNode("Geometry"));
			geometry.Prop(FbxProperty.Long(node.GeometryId),
						  FbxProperty.String("Geometry::" + node.Name),
						  FbxProperty.String("Mesh"));

			var vertices = mesh.vertices;
			var points   = new double[vertices.Length * 3];
			for (int i = 0; i < vertices.Length; i++)
			{
				points[(i * 3) + 0] = -vertices[i].x;
				points[(i * 3) + 1] =  vertices[i].y;
				points[(i * 3) + 2] =  vertices[i].z;
			}
			geometry.Add("Vertices", FbxProperty.Array(points));

			// Gather every submesh's triangles into one polygon list, remembering which submesh each
			// polygon came from so the material layer below can reproduce the split.
			var subMeshCount     = Mathf.Max(1, mesh.subMeshCount);
			var polygonIndices   = new List<int>();
			var polygonMaterials = new List<int>();
			var cornerVertex     = new List<int>();

			for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
			{
				var triangles = mesh.GetTriangles(subMesh);
				for (int i = 0; i + 2 < triangles.Length; i += 3)
				{
					// Reversed: mirroring X flipped which side of each triangle faces out, and the
					// winding is what says so.
					var a = triangles[i + 2];
					var b = triangles[i + 1];
					var c = triangles[i + 0];

					cornerVertex.Add(a);
					cornerVertex.Add(b);
					cornerVertex.Add(c);

					polygonIndices.Add(a);
					polygonIndices.Add(b);
					// FBX marks the last corner of a polygon by storing its bitwise complement.
					polygonIndices.Add(~c);
					polygonMaterials.Add(subMesh);
				}
			}

			geometry.Add("PolygonVertexIndex", FbxProperty.Array(polygonIndices.ToArray()));
			geometry.Add("GeometryVersion", FbxProperty.Int(124));

			// Every mesh is written with normals and a tangent frame. The models this writes are imported with both
			// imported rather than recalculated, to keep what studiomdl authored, and Unity warns about every mesh that
			// is missing either. A mesh with no normals of its own - a collision mesh, which has no texture coordinates
			// for a tangent frame to follow either - gets its faces': the face's normal, and a tangent along its first
			// edge, written for each polygon corner so no vertex is split for them.
			var normals         = mesh.normals;
			var tangents        = mesh.tangents;
			var meshHasNormals  = normals != null && normals.Length == vertices.Length;
			// A tangent frame of the mesh's own is only written together with the normals it is built on.
			var meshHasTangents = meshHasNormals && tangents != null && tangents.Length == vertices.Length;
			var hasTangents     = meshHasTangents || !meshHasNormals;

			var normalValues   = new double[cornerVertex.Count * 3];
			var tangentValues  = new double[hasTangents ? cornerVertex.Count * 3 : 0];
			var binormalValues = new double[hasTangents ? cornerVertex.Count * 3 : 0];
			var faceNormal     = Vector3.up;
			var faceTangent    = Vector3.right;
			for (int i = 0; i < cornerVertex.Count; i++)
			{
				Vector3 normal, tangent, binormal;
				if (meshHasNormals)
				{
					var index = cornerVertex[i];
					normal   = normals[index];
					tangent  = Vector3.zero;
					binormal = Vector3.zero;
					if (meshHasTangents)
					{
						var own = tangents[index];
						tangent  = new Vector3(own.x, own.y, own.z);
						// Unity keeps handedness in tangent.w and builds the bitangent from it. FBX has no equivalent
						// field that importers agree on, so the bitangent is written out explicitly - that is what
						// carries the handedness across.
						binormal = Vector3.Cross(normal, tangent) * own.w;
					}
				}
				else
				{
					if (i % 3 == 0)
					{
						// The corners were stored reversed above; in the mesh's own winding the triangle is c, b, a.
						var a      = vertices[cornerVertex[i]];
						var b      = vertices[cornerVertex[i + 1]];
						var c      = vertices[cornerVertex[i + 2]];
						var cross  = Vector3.Cross(b - c, a - c);
						var edge   = b - c;
						var area   = cross.magnitude;
						var length = edge.magnitude;
						if (area > 0 && length > 0)
						{
							faceNormal  = cross / area;
							faceTangent = edge / length;
						}
						else
						{
							faceNormal  = Vector3.up;
							faceTangent = Vector3.right;
						}
					}
					normal   = faceNormal;
					tangent  = faceTangent;
					binormal = Vector3.Cross(normal, tangent);
				}

				normalValues[(i * 3) + 0] = -normal.x;
				normalValues[(i * 3) + 1] =  normal.y;
				normalValues[(i * 3) + 2] =  normal.z;
				if (hasTangents)
				{
					tangentValues[(i * 3) + 0]  = -tangent.x;
					tangentValues[(i * 3) + 1]  =  tangent.y;
					tangentValues[(i * 3) + 2]  =  tangent.z;
					binormalValues[(i * 3) + 0] = -binormal.x;
					binormalValues[(i * 3) + 1] =  binormal.y;
					binormalValues[(i * 3) + 2] =  binormal.z;
				}
			}

			var normalLayer = geometry.Add(new FbxNode("LayerElementNormal"));
			normalLayer.Prop(FbxProperty.Int(0));
			normalLayer.Add("Version", FbxProperty.Int(101));
			normalLayer.Add("Name", FbxProperty.String(""));
			normalLayer.Add("MappingInformationType", FbxProperty.String("ByPolygonVertex"));
			normalLayer.Add("ReferenceInformationType", FbxProperty.String("Direct"));
			normalLayer.Add("Normals", FbxProperty.Array(normalValues));

			if (hasTangents)
			{
				var tangentLayer = geometry.Add(new FbxNode("LayerElementTangent"));
				tangentLayer.Prop(FbxProperty.Int(0));
				tangentLayer.Add("Version", FbxProperty.Int(101));
				tangentLayer.Add("Name", FbxProperty.String(""));
				tangentLayer.Add("MappingInformationType", FbxProperty.String("ByPolygonVertex"));
				tangentLayer.Add("ReferenceInformationType", FbxProperty.String("Direct"));
				tangentLayer.Add("Tangents", FbxProperty.Array(tangentValues));

				var binormalLayer = geometry.Add(new FbxNode("LayerElementBinormal"));
				binormalLayer.Prop(FbxProperty.Int(0));
				binormalLayer.Add("Version", FbxProperty.Int(101));
				binormalLayer.Add("Name", FbxProperty.String(""));
				binormalLayer.Add("MappingInformationType", FbxProperty.String("ByPolygonVertex"));
				binormalLayer.Add("ReferenceInformationType", FbxProperty.String("Direct"));
				binormalLayer.Add("Binormals", FbxProperty.Array(binormalValues));
			}

			var uvs = mesh.uv;
			var hasUvs = uvs != null && uvs.Length == vertices.Length;
			if (hasUvs)
				WriteUvLayer(geometry, cornerVertex, uvs, 0, "UVMap");

			// A second set, for the iris uv of an eyeball mesh (ModelImporter.EyeProjection). Unity takes the sets
			// in order, so this one arrives as uv2 - which is also where it would put generated lightmap uvs, but
			// ConfigureImportedModel leaves generateSecondaryUV off, so nothing overwrites it. A mesh with no eyes
			// has no uv2 and no layer here.
			var eyeUvs = mesh.uv2;
			var hasEyeUvs = eyeUvs != null && eyeUvs.Length == vertices.Length;
			if (hasEyeUvs)
				WriteUvLayer(geometry, cornerVertex, eyeUvs, 1, "EyeUVMap");

			{
				var layer = geometry.Add(new FbxNode("LayerElementMaterial"));
				layer.Prop(FbxProperty.Int(0));
				layer.Add("Version", FbxProperty.Int(101));
				layer.Add("Name", FbxProperty.String(""));
				layer.Add("MappingInformationType", FbxProperty.String("ByPolygon"));
				layer.Add("ReferenceInformationType", FbxProperty.String("IndexToDirect"));
				layer.Add("Materials", FbxProperty.Array(polygonMaterials.ToArray()));
			}

			// The layer lists exactly the elements written above. A mesh can lack uvs - a collision mesh has none - or a
			// tangent frame of its own, and a layer naming an element the geometry does not have leaves the importer to
			// make sense of a reference to nothing.
			var layers = geometry.Add(new FbxNode("Layer"));
			layers.Prop(FbxProperty.Int(0));
			layers.Add("Version", FbxProperty.Int(100));
			AddLayerElement(layers, "LayerElementNormal");
			if (hasTangents)
			{
				AddLayerElement(layers, "LayerElementTangent");
				AddLayerElement(layers, "LayerElementBinormal");
			}
			AddLayerElement(layers, "LayerElementMaterial");
			if (hasUvs)
				AddLayerElement(layers, "LayerElementUV");

			// A second uv set is a second Layer, not a second element of the first: a Layer holds at most one
			// element of each type, and the set's index is the Layer's.
			if (hasEyeUvs)
			{
				var eyeLayer = geometry.Add(new FbxNode("Layer"));
				eyeLayer.Prop(FbxProperty.Int(1));
				eyeLayer.Add("Version", FbxProperty.Int(100));
				AddLayerElement(eyeLayer, "LayerElementUV", 1);
			}
		}

		static void WriteUvLayer(FbxNode geometry, List<int> cornerVertex, Vector2[] uvs, int index, string name)
		{
			var values = new double[cornerVertex.Count * 2];
			for (int i = 0; i < cornerVertex.Count; i++)
			{
				var uv = uvs[cornerVertex[i]];
				values[(i * 2) + 0] = uv.x;
				values[(i * 2) + 1] = uv.y;
			}

			var layer = geometry.Add(new FbxNode("LayerElementUV"));
			layer.Prop(FbxProperty.Int(index));
			layer.Add("Version", FbxProperty.Int(101));
			layer.Add("Name", FbxProperty.String(name));
			layer.Add("MappingInformationType", FbxProperty.String("ByPolygonVertex"));
			layer.Add("ReferenceInformationType", FbxProperty.String("Direct"));
			layer.Add("UV", FbxProperty.Array(values));
		}

		static void AddLayerElement(FbxNode layer, string type, int typedIndex = 0)
		{
			var entry = layer.Add(new FbxNode("LayerElement") { AlwaysBlock = true });
			entry.Add("Type", FbxProperty.String(type));
			entry.Add("TypedIndex", FbxProperty.Int(typedIndex));
		}

		#endregion

		#region Materials

		static void WriteMaterialObjects(FbxNode objects, List<string> names, List<long> ids)
		{
			for (int i = 0; i < names.Count; i++)
			{
				var material = objects.Add(new FbxNode("Material"));
				material.Prop(FbxProperty.Long(ids[i]), FbxProperty.String("Material::" + names[i]), FbxProperty.String(""));
				material.Add("Version", FbxProperty.Int(102));
				material.Add("ShadingModel", FbxProperty.String("phong"));
				material.Add("MultiLayer", FbxProperty.Int(0));

				// The material itself carries nothing worth exporting: the real ones already exist as
				// Unity assets and the importer is pointed straight back at them. This exists so the
				// mesh has something to name its submeshes after.
				var properties = material.Add(new FbxNode("Properties70") { AlwaysBlock = true });
				properties.P("DiffuseColor", "Color", "", "A",
							 FbxProperty.Double(0.8), FbxProperty.Double(0.8), FbxProperty.Double(0.8));
			}
		}

		static void WriteMaterialConnections(FbxNode connections, List<NodeRecord> nodes,
											 List<string> names, List<long> ids, int missing)
		{
			var indexByName = new Dictionary<string, int>();
			for (int i = 0; i < names.Count; i++)
				indexByName[names[i]] = i;

			// The order materials are connected to a node IS their index in that node's material
			// layer, so these have to go out per node in submesh order - INCLUDING the empty slots,
			// which is what CollectMaterials' placeholder is for. Leaving one out does not drop a
			// submesh, it renumbers every submesh after it.
			var lookup = new Dictionary<Material, int>();
			var order  = (missing >= 0) ? 1 : 0;
			foreach (var node in nodes)
			{
				if (node.Materials == null)
					continue;
				foreach (var material in node.Materials)
				{
					if (material != null && !lookup.ContainsKey(material))
						lookup[material] = order++;
				}
			}

			foreach (var node in nodes)
			{
				if (node.Materials == null)
					continue;

				foreach (var material in node.Materials)
				{
					int index;
					if (material == null)
					{
						if (missing < 0)
							continue;
						index = missing;
					}
					else if (!lookup.TryGetValue(material, out index))
						continue;
					connections.Add("C", FbxProperty.String("OO"),
									FbxProperty.Long(ids[index]), FbxProperty.Long(node.Id));
				}
			}
		}

		#endregion

		#region Skinning

		static void WriteSkins(FbxNode objects, FbxNode connections, Hierarchy hierarchy, IdSource ids)
		{
			var lookup = hierarchy.ByTransform;

			foreach (var node in hierarchy.Nodes)
			{
				if (node.Mesh == null || node.SkinBones == null || node.SkinBones.Length == 0)
					continue;

				var boneWeights = node.Mesh.boneWeights;
				if (boneWeights == null || boneWeights.Length == 0)
					continue;

				var skinId = ids.Next();
				var skin = objects.Add(new FbxNode("Deformer"));
				skin.Prop(FbxProperty.Long(skinId), FbxProperty.String("Deformer::Skin " + node.Name), FbxProperty.String("Skin"));
				skin.Add("Version", FbxProperty.Int(101));
				skin.Add("Link_DeformAcuracy", FbxProperty.Double(50));

				connections.Add("C", FbxProperty.String("OO"), FbxProperty.Long(skinId), FbxProperty.Long(node.GeometryId));

				// One cluster per bone, listing the control points it moves. Bones that move nothing
				// are skipped: an empty cluster is legal but makes some importers create a bone that
				// exists only to be ignored.
				var indices = new List<int>[node.SkinBones.Length];
				var weights = new List<double>[node.SkinBones.Length];

				for (int vertex = 0; vertex < boneWeights.Length; vertex++)
				{
					var boneWeight = boneWeights[vertex];
					AddInfluence(indices, weights, boneWeight.boneIndex0, boneWeight.weight0, vertex);
					AddInfluence(indices, weights, boneWeight.boneIndex1, boneWeight.weight1, vertex);
					AddInfluence(indices, weights, boneWeight.boneIndex2, boneWeight.weight2, vertex);
					AddInfluence(indices, weights, boneWeight.boneIndex3, boneWeight.weight3, vertex);
				}

				// The mesh's own place in the world at bind time. Everything this importer builds
				// puts meshes at the hierarchy root with no transform of their own, but deriving it
				// rather than assuming identity keeps the exporter honest for any other hierarchy.
				var meshGlobal = node.GlobalMatrix;

				for (int bone = 0; bone < node.SkinBones.Length; bone++)
				{
					if (indices[bone] == null || indices[bone].Count == 0)
						continue;

					var boneTransform = node.SkinBones[bone];
					if (boneTransform == null || !lookup.TryGetValue(boneTransform, out var boneRecord))
						continue;

					var clusterId = ids.Next();
					var cluster = objects.Add(new FbxNode("Deformer"));
					cluster.Prop(FbxProperty.Long(clusterId),
								 FbxProperty.String("SubDeformer::Cluster " + boneRecord.Name),
								 FbxProperty.String("Cluster"));
					cluster.Add("Version", FbxProperty.Int(100));
					cluster.Add("UserData", FbxProperty.String(""), FbxProperty.String(""));
					cluster.Add("Indexes", FbxProperty.Array(indices[bone].ToArray()));
					cluster.Add("Weights", FbxProperty.Array(weights[bone].ToArray()));

					// Unity's bindpose is boneWorldToLocal * meshLocalToWorld, so the bone's own
					// position at bind time - which is what FBX wants in TransformLink - is the mesh
					// transform times the inverse of that.
					var bindPose  = (node.BindPoses != null && bone < node.BindPoses.Length)
									? node.BindPoses[bone]
									: boneRecord.GlobalMatrix.inverse * meshGlobal;
					var boneGlobal = meshGlobal * bindPose.inverse;

					// Transform is the OFFSET matrix - the bind pose itself, carrying the mesh into this
					// bone's space - and NOT the mesh's global transform. This used to write the mesh's
					// global, which for every model this importer builds is the identity; the reader took
					// that as the bind pose, so every skinned model imported with identity bind poses and
					// rendered as a heap (characters shredded, escalator steps strewn in a pattern).
					// Settled by measurement, not by reading documentation: rewriting only this matrix to
					// inverse(TransformLink) made a reimported rig consistent, and a reference file from
					// the Autodesk SDK carries a different Transform on each cluster of a single mesh,
					// which a per-mesh global could never do.
					var transform = new List<double>(16);
					AppendMatrix(transform, ToFbxMatrix(bindPose));
					cluster.Add("Transform", FbxProperty.Array(transform.ToArray()));

					var transformLink = new List<double>(16);
					AppendMatrix(transformLink, ToFbxMatrix(boneGlobal));
					cluster.Add("TransformLink", FbxProperty.Array(transformLink.ToArray()));

					connections.Add("C", FbxProperty.String("OO"), FbxProperty.Long(clusterId), FbxProperty.Long(skinId));
					connections.Add("C", FbxProperty.String("OO"), FbxProperty.Long(boneRecord.Id), FbxProperty.Long(clusterId));
				}
			}
		}

		static void AddInfluence(List<int>[] indices, List<double>[] weights, int bone, float weight, int vertex)
		{
			if (weight <= 0 || bone < 0 || bone >= indices.Length)
				return;
			indices[bone] ??= new List<int>();
			weights[bone] ??= new List<double>();
			indices[bone].Add(vertex);
			weights[bone].Add(weight);
		}

		static void WriteBindPose(FbxNode objects, List<NodeRecord> nodes, IdSource ids)
		{
			var posed = new List<KeyValuePair<long, Matrix4x4>>();
			foreach (var node in nodes)
			{
				if (!node.IsBone && node.Mesh == null)
					continue;
				posed.Add(new KeyValuePair<long, Matrix4x4>(node.Id, node.GlobalMatrix));
			}
			if (posed.Count == 0)
				return;

			var pose = objects.Add(new FbxNode("Pose"));
			pose.Prop(FbxProperty.Long(ids.Next()), FbxProperty.String("Pose::BIND_POSES"), FbxProperty.String("BindPose"));
			pose.Add("Type", FbxProperty.String("BindPose"));
			pose.Add("Version", FbxProperty.Int(100));
			pose.Add("NbPoseNodes", FbxProperty.Int(posed.Count));

			foreach (var entry in posed)
			{
				var poseNode = pose.Add(new FbxNode("PoseNode") { AlwaysBlock = true });
				poseNode.Add("Node", FbxProperty.Long(entry.Key));
				var matrix = new List<double>(16);
				AppendMatrix(matrix, ToFbxMatrix(entry.Value));
				poseNode.Add("Matrix", FbxProperty.Array(matrix.ToArray()));
			}
		}

		#endregion

		#region Animation

		static List<string> WriteAnimation(FbxNode objects, FbxNode connections, Hierarchy hierarchy,
										   IReadOnlyList<FbxAnimationTake> takes, IdSource ids,
										   out double longestTakeSeconds)
		{
			var names = new List<string>();
			longestTakeSeconds = 0;
			if (takes == null || takes.Count == 0)
				return names;

			var used = new HashSet<string>();

			foreach (var take in takes)
			{
				if (take == null || take.FrameCount <= 0 || take.Nodes.Count == 0)
					continue;

				var frameRate = (take.FrameRate > 0) ? take.FrameRate : 30f;
				// A single-frame animation is a held pose - Source uses them constantly, for gesture layers and
				// for most props' idle - and a take has to span a frame for it, not an instant: one that starts
				// and ends at the same time imports as an empty clip, with a warning for every such clip.
				var duration  = Math.Max(take.FrameCount - 1, 1) / frameRate;
				if (duration > longestTakeSeconds)
					longestTakeSeconds = duration;

				var name   = string.IsNullOrEmpty(take.Name) ? "Take" : take.Name;
				var unique = name;
				var suffix = 1;
				while (!used.Add(unique))
					unique = $"{name}_{suffix++}";
				names.Add(unique);

				var stackId = ids.Next();
				var stack = objects.Add(new FbxNode("AnimationStack"));
				stack.Prop(FbxProperty.Long(stackId), FbxProperty.String("AnimStack::" + unique), FbxProperty.String(""));
				var stackProperties = stack.Add(new FbxNode("Properties70") { AlwaysBlock = true });
				stackProperties.P("LocalStop", "KTime", "Time", "", FbxProperty.Long((long)(duration * kFbxTimePerSecond)));
				stackProperties.P("ReferenceStop", "KTime", "Time", "", FbxProperty.Long((long)(duration * kFbxTimePerSecond)));

				var layerId = ids.Next();
				// AlwaysBlock: an object with nothing inside it still has to be written as a block.
				// Printed as a bare line it parses as a property of the enclosing node rather than as
				// an object, and the whole take then hangs off a layer that does not exist - which
				// loses the animation silently, with no error from the importer.
				var layer = objects.Add(new FbxNode("AnimationLayer") { AlwaysBlock = true });
				layer.Prop(FbxProperty.Long(layerId), FbxProperty.String("AnimLayer::BaseLayer"), FbxProperty.String(""));
				connections.Add("C", FbxProperty.String("OO"), FbxProperty.Long(layerId), FbxProperty.Long(stackId));

				foreach (var curveSet in take.Nodes)
				{
					if (curveSet == null || curveSet.Node == null)
						continue;

					if (!hierarchy.ByTransform.TryGetValue(curveSet.Node, out var record))
						continue;
					var nodeId = record.Id;

					if (curveSet.Positions != null && curveSet.Positions.Length == take.FrameCount)
					{
						var x = new double[take.FrameCount];
						var y = new double[take.FrameCount];
						var z = new double[take.FrameCount];
						for (int frame = 0; frame < take.FrameCount; frame++)
						{
							var position = ToFbxPosition(curveSet.Positions[frame]);
							x[frame] = position[0];
							y[frame] = position[1];
							z[frame] = position[2];
						}
						WriteCurveNode(objects, connections, ids, layerId, nodeId, "Lcl Translation",
									   x, y, z, take.FrameCount, frameRate);
					}

					if (curveSet.Rotations != null && curveSet.Rotations.Length == take.FrameCount)
					{
						var x = new double[take.FrameCount];
						var y = new double[take.FrameCount];
						var z = new double[take.FrameCount];
						var previous = Vector3.zero;
						for (int frame = 0; frame < take.FrameCount; frame++)
						{
							var euler = FbxEuler.ToEulerXYZ(ToFbxRotation(curveSet.Rotations[frame]));
							if (frame > 0)
								euler = FbxEuler.MakeContinuous(euler, previous);
							previous = euler;

							x[frame] = euler.x;
							y[frame] = euler.y;
							z[frame] = euler.z;
						}
						WriteCurveNode(objects, connections, ids, layerId, nodeId, "Lcl Rotation",
									   x, y, z, take.FrameCount, frameRate);
					}
				}
			}

			return names;
		}

		static void WriteCurveNode(FbxNode objects, FbxNode connections, IdSource ids,
								   long layerId, long nodeId, string property,
								   double[] x, double[] y, double[] z, int frameCount, float frameRate)
		{
			var curveNodeId = ids.Next();
			var curveNode = objects.Add(new FbxNode("AnimationCurveNode"));
			curveNode.Prop(FbxProperty.Long(curveNodeId),
						   FbxProperty.String("AnimCurveNode::" + (property == "Lcl Rotation" ? "R" : "T")),
						   FbxProperty.String(""));
			var properties = curveNode.Add(new FbxNode("Properties70") { AlwaysBlock = true });
			properties.P("d|X", "Number", "", "A", FbxProperty.Double(x[0]));
			properties.P("d|Y", "Number", "", "A", FbxProperty.Double(y[0]));
			properties.P("d|Z", "Number", "", "A", FbxProperty.Double(z[0]));

			connections.Add("C", FbxProperty.String("OO"), FbxProperty.Long(curveNodeId), FbxProperty.Long(layerId));
			connections.Add("C", FbxProperty.String("OP"), FbxProperty.Long(curveNodeId), FbxProperty.Long(nodeId),
							FbxProperty.String(property));

			WriteCurve(objects, connections, ids, curveNodeId, "d|X", x, frameCount, frameRate);
			WriteCurve(objects, connections, ids, curveNodeId, "d|Y", y, frameCount, frameRate);
			WriteCurve(objects, connections, ids, curveNodeId, "d|Z", z, frameCount, frameRate);
		}

		/// <summary>
		/// How far apart two values of a channel have to be before it counts as moving. Curves are
		/// positions in metres and angles in degrees, so this is well below anything visible.
		/// </summary>
		const double kConstantChannelEpsilon = 1e-6;

		static void WriteCurve(FbxNode objects, FbxNode connections, IdSource ids,
							   long curveNodeId, string channel, double[] values, int frameCount, float frameRate)
		{
			// A channel that never changes needs its value once, not once per frame. This is not an
			// approximation - a single key IS the constant curve - and it is most of the file: in
			// Source's animation the vast majority of bones only rotate, so all three of their
			// position channels are constant, and a bone turning about one axis has two constant
			// rotation channels as well. Writing them out in full put one shared NPC animation model
			// at well over a hundred megabytes of text.
			var isConstant = true;
			for (int frame = 1; frame < frameCount; frame++)
			{
				if (Math.Abs(values[frame] - values[0]) > kConstantChannelEpsilon)
				{
					isConstant = false;
					break;
				}
			}
			// FBX time is an integer count, so times are rounded rather than truncated - a frame landing one
			// unit short of where the importer expects it is a frame resampled slightly wrong, all the way
			// along the take. The take spans at least one frame (see WriteAnimation).
			var endTime = Math.Round((Math.Max(frameCount - 1, 1) / (double)frameRate) * kFbxTimePerSecond);

			double[] times;
			if (isConstant)
			{
				// One value, stated at both ends of the take rather than left to extrapolation - which also
				// covers the single-frame take, whose one key would otherwise sit alone at time zero.
				values     = new[] { values[0], values[0] };
				times      = new[] { 0.0, endTime };
				frameCount = 2;
			} else
			{
				times = new double[frameCount];
				for (int frame = 0; frame < frameCount; frame++)
					times[frame] = Math.Round((frame / (double)frameRate) * kFbxTimePerSecond);
			}

			var curveId = ids.Next();
			var curve = objects.Add(new FbxNode("AnimationCurve"));
			curve.Prop(FbxProperty.Long(curveId), FbxProperty.String("AnimCurve::"), FbxProperty.String(""));
			curve.Add("Default", FbxProperty.Double(values.Length > 0 ? values[0] : 0));
			curve.Add("KeyVer", FbxProperty.Int(4008));
			curve.Add("KeyTime", FbxProperty.Array(times));
			curve.Add("KeyValueFloat", FbxProperty.Array(values));
			curve.Add("KeyAttrFlags", FbxProperty.Array(new[] { kInterpolationLinear }));
			curve.Add("KeyAttrDataFloat", FbxProperty.Array(new[] { 0.0, 0.0, kDefaultKeyWeights, 0.0 }));
			curve.Add("KeyAttrRefCount", FbxProperty.Array(new[] { frameCount }));

			connections.Add("C", FbxProperty.String("OP"), FbxProperty.Long(curveId), FbxProperty.Long(curveNodeId),
							FbxProperty.String(channel));
		}

		#endregion
	}
}
