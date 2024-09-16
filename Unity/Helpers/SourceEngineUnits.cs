using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	public enum SourceEntity
	{
		Brush,
		Light,
		Model
	}

	public class SourceEngineUnits
	{
		public const float VmfMeters = 64.0f / 1.22f;
		public const float VmfInvMeters = 1.22f / 64.0f;


		public readonly static Matrix4x4 VmfSwizzle = new(
						new Vector4( 1, 0, 0, 0),
						new Vector4( 0, 0, 1, 0),
						new Vector4( 0, 1, 0, 0),
						new Vector4( 0, 0, 0, 1));
		public const bool InvertPlanes = false;

		public readonly static Matrix4x4 VmfScale = Matrix4x4.Scale(new Vector3(1.0f / SourceEngineUnits.VmfMeters, 1.0f / SourceEngineUnits.VmfMeters, 1.0f / SourceEngineUnits.VmfMeters));
		public readonly static Matrix4x4 VmfSourceToUnity = SourceEngineUnits.VmfSwizzle * SourceEngineUnits.VmfScale;

		public static Vector2 VmfFixTexcoord(Vector2 texCoord)
		{
			texCoord.y = -texCoord.y;
			return texCoord;
		}

		private enum RotationAxi
		{
			PITCH = 0,  // up / down
			YAW,        // left / right
			ROLL        // fall over
		};

		public static void AngleVectors(Vector3 angles, out Vector3 forward, out Vector3 right, out Vector3 up)
		{
			var sy = Mathf.Sin(angles[(int)RotationAxi.YAW] * Mathf.Deg2Rad);
			var cy = Mathf.Cos(angles[(int)RotationAxi.YAW] * Mathf.Deg2Rad);
			var sp = Mathf.Sin(angles[(int)RotationAxi.PITCH] * Mathf.Deg2Rad);
			var cp = Mathf.Cos(angles[(int)RotationAxi.PITCH] * Mathf.Deg2Rad);
			var sr = Mathf.Sin(angles[(int)RotationAxi.ROLL] * Mathf.Deg2Rad);
			var cr = Mathf.Cos(angles[(int)RotationAxi.ROLL] * Mathf.Deg2Rad);

			forward = new Vector3(
							cp * cy,
							cp * sy,
							-sp);

			right = new Vector3(
							(-1 * sr * sp * cy + -1 * cr * -sy),
							(-1 * sr * sp * sy + -1 * cr * cy),
							-1 * sr * cp);

			up = new Vector3(
							(cr * sp * cy + -sr * -sy),
							(cr * sp * sy + -sr * cy),
							cr * cp);
		}

		public static readonly Matrix4x4 ModelFixupMatrix = Matrix4x4.Rotate(Quaternion.AngleAxis(-90, Vector3.right));
		public static readonly Matrix4x4 InvModelMatrix = Matrix4x4.Inverse(ModelFixupMatrix);

		public static void SetUnityTransformWithValveCoordinates(Transform transform, Vector3 angles, Vector3 position, SourceEntity sourceEntity)
		{
			if (float.IsNaN(position.x) ||
				float.IsNaN(position.y) ||
				float.IsNaN(position.z))
				position = Vector3.zero;

			SourceEngineUnits.AngleVectors(angles, out Vector3 right, out Vector3 forward, out Vector3 up);

			Quaternion quaternion = Quaternion.identity;
			Vector3 scale = Vector3.one;
			var translation = SourceEngineUnits.VmfSourceToUnity.MultiplyPoint(position);
			right = SourceEngineUnits.VmfSwizzle.MultiplyPoint(right);
			forward = SourceEngineUnits.VmfSwizzle.MultiplyPoint(forward);
			up = SourceEngineUnits.VmfSwizzle.MultiplyPoint(up);

			switch (sourceEntity)
			{
				case SourceEntity.Light: quaternion = Quaternion.AngleAxis(180, Vector3.up) * Quaternion.LookRotation(-right, up); break;
				case SourceEntity.Model:
				{
					var matrix = Matrix4x4.identity;
					matrix.SetColumn(3, new Vector4(translation.x, translation.y, translation.z, 1));
					matrix.SetColumn(0, right);
					matrix.SetColumn(1, -forward);
					matrix.SetColumn(2, -up);
					matrix = matrix * ModelFixupMatrix;

					matrix.Decompose(out translation, out quaternion, out scale);
					break;
				}
				case SourceEntity.Brush:
				{
					var matrix = Matrix4x4.identity;
					matrix.SetColumn(3, new Vector4(translation.x, translation.y, translation.z, 1));
					matrix.SetColumn(0, right);
					matrix.SetColumn(1, -forward);
					matrix.SetColumn(2, -up);

					matrix.Decompose(out translation, out quaternion, out scale);
					break;
				}
			}

			transform.localScale = scale;
			transform.localPosition = translation;
			transform.localRotation = quaternion;
		}


		public static Vector3 Swizzle(Vector3 input) { return VmfSwizzle.MultiplyPoint(input); }
	}
}
