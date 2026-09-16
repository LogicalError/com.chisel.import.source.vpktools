using UnityEditor;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>Which of a model's collision sets an entity collides with.</summary>
	public enum ModelCollisionSet
	{
		/// <summary>None of the model's collision: the entity is not solid, or the game gives it other collision.</summary>
		None,

		/// <summary>The one solid a prop collides with, placed at the entity (PhysModelCreate).</summary>
		Prop,

		/// <summary>Every solid named after a bone, on that bone (RagdollCreate, and a prop's bone followers).</summary>
		Ragdoll
	}

	/// <summary>
	/// Chooses between the collision sets <see cref="ModelImporter"/> gives a model.
	/// <para>
	/// Source uses one collision model in two ways, decided by the entity rather than the model: a prop collides with a
	/// single solid placed at the entity, and a ragdoll - or a prop_dynamic that gets bone followers - with every solid
	/// on the bone it is named after. So an imported model carries both sets, the prop set enabled and the ragdoll set
	/// not, and whatever places an instance enables the set its entity uses.
	/// </para>
	/// </summary>
	public static class ModelCollision
	{
		/// <summary>Whether the instance has colliders of this set at all.</summary>
		public static bool HasSet(GameObject instance, ModelCollisionSet set)
		{
			if (instance == null || set == ModelCollisionSet.None)
				return false;

			foreach (var collider in instance.GetComponentsInChildren<MeshCollider>(true))
			{
				if (SetOf(collider.gameObject.name) == set)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Enables the colliders of one set on an instance and disables the others. On a prefab instance the change is
		/// recorded as an override, so it survives the model being reimported.
		/// </summary>
		public static void Use(GameObject instance, ModelCollisionSet set)
		{
			if (instance == null)
				return;

			foreach (var collider in instance.GetComponentsInChildren<MeshCollider>(true))
			{
				var nodeSet = SetOf(collider.gameObject.name);
				if (nodeSet == ModelCollisionSet.None)
					continue;

				var enabled = (nodeSet == set);
				if (collider.enabled == enabled)
					continue;

				collider.enabled = enabled;
				if (PrefabUtility.IsPartOfPrefabInstance(collider))
					PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
			}
		}

		// The set a collision node belongs to, by the name ModelImporter gave it; None for any other node.
		internal static ModelCollisionSet SetOf(string nodeName)
		{
			if (nodeName == null)
				return ModelCollisionSet.None;
			if (nodeName.StartsWith(ModelImporter.kPropCollisionPrefix, System.StringComparison.Ordinal))
				return ModelCollisionSet.Prop;
			if (nodeName.StartsWith(ModelImporter.kRagdollCollisionPrefix, System.StringComparison.Ordinal))
				return ModelCollisionSet.Ragdoll;
			return ModelCollisionSet.None;
		}
	}
}
