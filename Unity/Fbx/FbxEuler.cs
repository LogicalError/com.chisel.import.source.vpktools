using UnityEngine;

namespace Chisel.Import.Source.VPKTools.Fbx
{
	/// <summary>
	/// Quaternion to Euler conversion in FBX's own convention.
	/// <para>
	/// FBX animates rotation as three Euler angle curves, never as a quaternion, so every rotation
	/// has to be decomposed on the way out. Unity's <see cref="Quaternion.eulerAngles"/> cannot be
	/// used for this: it returns angles in Unity's ZXY order, while an FBX node whose RotationOrder
	/// is the default <c>eEulerXYZ</c> applies X first, then Y, then Z - a matrix of
	/// <c>Rz * Ry * Rx</c>. Feeding one order's angles to the other is wrong for every rotation that
	/// is not about a single axis, and it is wrong in a way that looks like "the animation is subtly
	/// broken" rather than like an error.
	/// </para>
	/// <para>
	/// Which of the two readings of "XYZ" FBX means was settled by measurement, not by reasoning:
	/// the round-trip test exports a rotated node, has Unity import it back and compares a known
	/// corner. Self-consistency between the two functions here is not evidence - they agreed with
	/// each other perfectly while disagreeing with FBX.
	/// </para>
	/// </summary>
	internal static class FbxEuler
	{
		/// <summary>
		/// Decomposes a rotation into XYZ Euler angles in degrees, such that the rotation is
		/// reproduced by <c>Rz * Ry * Rx</c> - the X rotation applied to a vector first, which is
		/// what FBX's <c>eEulerXYZ</c> means.
		/// </summary>
		public static Vector3 ToEulerXYZ(Quaternion rotation)
		{
			// Work from the matrix rather than the quaternion directly: the extraction below is then
			// a plain reading of the Rz*Ry*Rx product's entries, which is checkable by hand and by a
			// test that rebuilds the matrix, instead of a quaternion identity that is not.
			var matrix = Matrix4x4.Rotate(rotation);

			// Rz(c) * Ry(b) * Rx(a) =
			//   [  cc*cb   cc*sb*sa - sc*ca   cc*sb*ca + sc*sa ]
			//   [  sc*cb   sc*sb*sa + cc*ca   sc*sb*ca - cc*sa ]
			//   [ -sb      cb*sa              cb*ca            ]
			// so m20 = -sin(b) on its own, and the other two angles come from the pairs that share a
			// cos(b) factor.
			var m20 = Mathf.Clamp(matrix.m20, -1f, 1f);

			float x, y, z;
			// cos(b) reaching zero is gimbal lock: the X and Z rotations act about the same axis and
			// only their sum is determined. Anything below this and the two terms we would divide by
			// are both numerical noise, so the split is chosen rather than computed.
			if (Mathf.Abs(m20) < 0.9999999f)
			{
				y = Mathf.Asin(-m20);
				x = Mathf.Atan2(matrix.m21, matrix.m22);
				z = Mathf.Atan2(matrix.m10, matrix.m00);
			} else
			{
				y = Mathf.Asin(-m20);
				// Put the whole rotation on X and leave Z at zero; any split reproduces the same
				// orientation, and keeping one of them fixed avoids a discontinuity between frames.
				x = Mathf.Atan2(-matrix.m12, matrix.m11);
				z = 0f;
			}

			return new Vector3(x * Mathf.Rad2Deg, y * Mathf.Rad2Deg, z * Mathf.Rad2Deg);
		}

		/// <summary>
		/// Rebuilds the rotation that <see cref="ToEulerXYZ"/> decomposed, for tests and for the
		/// unwrapping below. Unity composes right to left, so this applies X first.
		/// </summary>
		public static Quaternion FromEulerXYZ(Vector3 degrees)
		{
			return Quaternion.AngleAxis(degrees.z, Vector3.forward) *
				   Quaternion.AngleAxis(degrees.y, Vector3.up) *
				   Quaternion.AngleAxis(degrees.x, Vector3.right);
		}

		/// <summary>
		/// Picks the representation of <paramref name="euler"/> nearest to <paramref name="previous"/>.
		/// <para>
		/// Every orientation has infinitely many Euler triples, and a per-frame decomposition happily
		/// jumps between them - 179 degrees to -181, or across the equivalent (x+180, 180-y, z+180)
		/// form. Each jump is invisible in the pose but becomes a full spin once an importer
		/// interpolates between two keys, so consecutive frames have to be made continuous.
		/// </para>
		/// </summary>
		public static Vector3 MakeContinuous(Vector3 euler, Vector3 previous)
		{
			var direct    = Nearest(euler, previous);
			// The other branch of the same orientation: flipping X and Z by half a turn and
			// reflecting Y about 180 degrees describes the identical rotation.
			var flipped   = Nearest(new Vector3(euler.x + 180f, 180f - euler.y, euler.z + 180f), previous);

			return (Distance(direct, previous) <= Distance(flipped, previous)) ? direct : flipped;
		}

		static Vector3 Nearest(Vector3 euler, Vector3 previous)
		{
			return new Vector3(NearestAngle(euler.x, previous.x),
							   NearestAngle(euler.y, previous.y),
							   NearestAngle(euler.z, previous.z));
		}

		static float NearestAngle(float angle, float previous)
		{
			// Shift by whole turns until the step from the previous frame is at most half a turn.
			// Written as a rounded division rather than a loop so a large jump costs the same as a
			// small one and cannot spin.
			return angle - 360f * Mathf.Round((angle - previous) / 360f);
		}

		static float Distance(Vector3 a, Vector3 b)
		{
			return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
		}
	}
}
