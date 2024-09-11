using UnityEditor;

using UnityEngine;

class BoneConnectionDrawer : MonoBehaviour
{
	public Transform parent;

	void OnDrawGizmosSelected()
	{
		if (!Selection.Contains(this.gameObject))
			return;
		
		// Display the explosion radius when selected
		Gizmos.color = Color.blue;
		if (parent != null)
		{
			Gizmos.DrawLine(transform.position, parent.position);
		}
		Gizmos.DrawSphere(transform.position, 0.01f);
	}


	void OnDrawGizmos()
	{
		if (Selection.Contains(this.gameObject))
			return;

		// Draw a yellow sphere at the transform's position
		Gizmos.color = Color.yellow;
		if (parent != null)
		{
			Gizmos.DrawLine(transform.position, parent.position);
		}
		Gizmos.DrawSphere(transform.position, 0.01f);
	}
}