using System.Collections.Generic;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools.Fbx
{
	/// <summary>The sampled local motion of one node over a take.</summary>
	public sealed class FbxAnimationCurveSet
	{
		public Transform	Node;
		/// <summary>Local position per frame. Must be the take's frame count long, or null for a node that does not translate.</summary>
		public Vector3[]	Positions;
		/// <summary>Local rotation per frame. Must be the take's frame count long, or null for a node that does not rotate.</summary>
		public Quaternion[]	Rotations;
	}

	/// <summary>
	/// One animation take, sampled densely - a value for every node on every frame.
	/// <para>
	/// Dense on purpose. A sparse representation has to state an interpolation rule, and the two
	/// sides of this export disagree about what that rule is: the source data is a per-frame sample
	/// with no curve behind it, while FBX keys carry tangents. Sampling every frame and marking the
	/// keys linear means the file says exactly what the source said, and leaves any resampling to
	/// the importer, which is the only place that knows what it wants.
	/// </para>
	/// </summary>
	public sealed class FbxAnimationTake
	{
		public string	Name;
		public float	FrameRate = 30;
		public int		FrameCount;
		public bool		Looping;

		public readonly List<FbxAnimationCurveSet> Nodes = new();
	}
}
