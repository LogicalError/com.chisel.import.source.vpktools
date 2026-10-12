using UnityEditor;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Gives the models this importer writes their collision: every collision node <see cref="ModelImporter"/> put in
	/// the file becomes a <see cref="MeshCollider"/> in the imported model, instead of the renderer Unity's model
	/// importer would otherwise give it.
	/// <para>
	/// A model file has no way to say that a mesh is a collider, so the nodes are marked by name, and only files that
	/// carry this importer's model stamp are touched. The name also says which collision set a node belongs to: the
	/// prop set's colliders are enabled and the ragdoll set's are not, so a model collides as a prop - the way most
	/// entities use one - until whatever places it chooses a set (<see cref="ModelCollision"/>).
	/// </para>
	/// <para>
	/// Each node is one convex piece of a Source collision solid - the unit vphysics collides with - and becomes a
	/// convex collider wherever PhysX reproduces the piece exactly as one. Where it does not, the collider keeps the
	/// piece's own triangles, which PhysX reproduces exactly whatever their shape. Measured in Unity 6000.6:
	/// </para>
	/// <list type="bullet">
	/// <item>A convex collider holds at most 255 triangles. For a piece with more, PhysX builds a smaller, partial hull
	/// - 3% of the size inside the surface on a sphere - and warns on every import.</item>
	/// <item>PhysX inflates a flat hull to a tenth of its size in thickness: hulls 3e-7 m thick or less were inflated
	/// at every size from 0.01 m to 5 m, hulls 1e-6 m thick were not. Source collides with such a piece as a plate of
	/// no thickness. Pieces that thin are flat to within the precision of the file.</item>
	/// </list>
	/// </summary>
	sealed class SourceCollisionPostprocessor : AssetPostprocessor
	{
		// Unity reimports the models this applies to whenever the version changes, so bump it with any change to
		// what this does to them.
		const uint kVersion = 1;

		/// <summary>The most triangles a convex MeshCollider holds.</summary>
		internal const int kMaxConvexTriangles = 255;

		/// <summary>
		/// The thinnest a piece can be and still become a convex collider, in metres: a hundred times the thickness
		/// below which PhysX inflates a hull. A piece thinner than this is a plate, and its own triangles are exact.
		/// </summary>
		internal const float kMinConvexThickness = 1e-4f;

		public override uint GetVersion()
		{
			return kVersion;
		}

		void OnPostprocessModel(GameObject root)
		{
			if (!ModelImporter.IsGeneratedModel(assetImporter))
				return;

			foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
			{
				if (!ModelImporter.IsCollisionNode(filter.name))
					continue;

				var mesh     = filter.sharedMesh;
				var collider = filter.gameObject.AddComponent<MeshCollider>();
				// Decided before the mesh is assigned, so the mesh is only ever cooked the way it is used.
				collider.convex     = IsExactAsConvex(mesh);
				collider.sharedMesh = mesh;
				collider.enabled    = ModelCollision.SetOf(filter.name) != ModelCollisionSet.Ragdoll;

				var renderer = filter.GetComponent<MeshRenderer>();
				if (renderer != null)
					Object.DestroyImmediate(renderer);
				Object.DestroyImmediate(filter);
			}
		}

		/// <summary>Whether PhysX reproduces the piece exactly as a convex collider.</summary>
		internal static bool IsExactAsConvex(Mesh mesh)
		{
			var triangles = mesh.triangles;
			if (triangles.Length / 3 > kMaxConvexTriangles)
				return false;
			return ThinnestExtent(mesh.vertices, triangles) >= kMinConvexThickness;
		}

		/// <summary>
		/// How far the piece reaches across the plane of the face it is thinnest across. For a convex piece that is its
		/// thickness as a plate: nothing for a flat piece, the thickness of a slab.
		/// </summary>
		internal static float ThinnestExtent(Vector3[] vertices, int[] triangles)
		{
			var thinnest = float.PositiveInfinity;
			for (int t = 0; t + 2 < triangles.Length; t += 3)
			{
				var corner = vertices[triangles[t]];
				var normal = Vector3.Cross(vertices[triangles[t + 1]] - corner, vertices[triangles[t + 2]] - corner);
				var length = normal.magnitude;
				if (length <= 0)
					continue;
				normal /= length;

				float lowest = float.PositiveInfinity, highest = float.NegativeInfinity;
				foreach (var vertex in vertices)
				{
					var distance = Vector3.Dot(normal, vertex);
					lowest  = Mathf.Min(lowest, distance);
					highest = Mathf.Max(highest, distance);
				}
				thinnest = Mathf.Min(thinnest, highest - lowest);
			}
			// Without a single triangle of any area there is no extent across anything.
			return float.IsPositiveInfinity(thinnest) ? 0 : thinnest;
		}
	}
}
