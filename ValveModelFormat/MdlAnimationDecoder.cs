using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Decodes Source's compressed skeletal animation into plain per-frame samples.
	/// <para>
	/// Works directly on the file bytes rather than through parsed objects, because the format's
	/// central structure cannot be expressed as one. A channel is a run-length encoded array of
	/// <c>mstudioanimvalue_t</c> - a two-byte union that is either a (valid, total) run header or a
	/// value, decided by where you are in the walk - and the reader has to step through it counting
	/// frames. Modelling that as objects means allocating thousands of two-byte instances per
	/// animation to describe what is already a packed array.
	/// </para>
	/// <para>
	/// Everything here is in SOURCE's coordinate system and units. Converting to Unity's is the
	/// caller's job, and has to use the same conversion the rest of the skeleton does or the
	/// animation will not line up with the bind pose.
	/// </para>
	/// <para>
	/// What this decodes ends up in files that later imports reuse until the version they record
	/// changes, so a change to its output has to bump <c>ModelImporter</c>'s AnimationVersion, or no
	/// file already on disk ever sees the change.
	/// </para>
	/// </summary>
	public static class MdlAnimationDecoder
	{
		// mstudioanim_t flags
		const byte kRawPos  = 0x01;   // Vector48    - one position for the whole animation
		const byte kRawRot  = 0x02;   // Quaternion48
		const byte kAnimPos = 0x04;   // per-frame, run length encoded
		const byte kAnimRot = 0x08;   // per-frame, run length encoded
		const byte kDelta   = 0x10;   // values add to the bind pose rather than replacing it
		const byte kRawRot2 = 0x20;   // Quaternion64

		/// <summary>mstudioanim_valueptr_t: three short offsets, one per component.</summary>
		const int kValuePointerSize = 6;

		/// <summary>The sampled motion of one bone over one animation.</summary>
		public sealed class Channel
		{
			public int          BoneIndex;
			public Vector3[]    Positions;
			public Quaternion[] Rotations;
		}

		public sealed class Animation
		{
			/// <summary>Index into the model's local animation descriptions, which is how sequences refer to it.</summary>
			public int           Index;
			public string        Name;
			public float         FrameRate;
			public int           FrameCount;
			public bool          Looping;
			public List<Channel> Channels = new();
		}

		/// <summary>Where one bone's data sits within a chain, and how it is encoded.</summary>
		struct BoneRecord
		{
			public int  BoneIndex;
			public byte Flags;
			public int  RotationValues;   // offset of the mstudioanim_valueptr_t for rotation
			public int  PositionValues;
			public int  RawRotation;
			public int  RawPosition;
		}

		/// <summary>
		/// Decodes every animation in the model.
		/// </summary>
		/// <param name="mdlBytes">The raw .mdl file.</param>
		/// <param name="aniBytes">The raw external .ani file, or null when the model has none.</param>
		public static List<Animation> Decode(MDL header, byte[] mdlBytes, byte[] aniBytes)
		{
			var results = new List<Animation>();
			if (header?.Bones == null || header.LocalAnimationDescriptions == null || mdlBytes == null)
				return results;

			var bones   = header.Bones;
			var records = new List<BoneRecord>();

			for (int descriptionIndex = 0; descriptionIndex < header.LocalAnimationDescriptions.Length; descriptionIndex++)
			{
				var description = header.LocalAnimationDescriptions[descriptionIndex];
				if (description == null || description.numframes <= 0)
					continue;

				var animation = new Animation
				{
					Index      = descriptionIndex,
					Name       = string.IsNullOrEmpty(description.name) ? "anim" : description.name,
					FrameRate  = (description.fps > 0) ? description.fps : 30,
					FrameCount = description.numframes,
					Looping    = (description.flags & AnimationType.Looping) != 0
				};

				// Bones keep their bind pose on any frame the animation does not touch, so the
				// channels start there and only the frames the data covers are overwritten.
				var channels    = new Dictionary<int, Channel>();
				var lastSection = int.MinValue;

				for (int frame = 0; frame < description.numframes; frame++)
				{
					var buffer = ResolveFrame(header, description, frame, mdlBytes, aniBytes,
											  out var chainOffset, out var localFrame, out var section);
					if (buffer == null)
						break;

					// The chain layout is identical for every frame of a section, so it is only
					// re-read when the section changes rather than once per frame.
					if (section != lastSection)
					{
						lastSection = section;
						ReadChain(buffer, chainOffset, bones.Length, records);
					}
					if (records.Count == 0)
						break;

					foreach (var record in records)
					{
						var bone = bones[record.BoneIndex];
						if (!channels.TryGetValue(record.BoneIndex, out var channel))
						{
							channel = new Channel
							{
								BoneIndex = record.BoneIndex,
								Positions = new Vector3[description.numframes],
								Rotations = new Quaternion[description.numframes]
							};
							for (int i = 0; i < description.numframes; i++)
							{
								channel.Positions[i] = bone.Position;
								channel.Rotations[i] = bone.Quaternion;
							}
							channels.Add(record.BoneIndex, channel);
						}

						channel.Positions[frame] = DecodePosition(buffer, record, bone, localFrame);
						channel.Rotations[frame] = DecodeRotation(buffer, record, bone, localFrame);
					}
				}

				if (channels.Count == 0)
					continue;

				foreach (var channel in channels.Values)
					animation.Channels.Add(channel);
				results.Add(animation);
			}

			return results;
		}

		/// <summary>
		/// Finds the buffer and offset holding the animation chain for a frame, following the section
		/// table and the external animation blocks.
		/// </summary>
		static byte[] ResolveFrame(MDL header, StudioAnimationDescription description, int frame,
								   byte[] mdlBytes, byte[] aniBytes,
								   out int chainOffset, out int localFrame, out int section)
		{
			chainOffset = 0;
			localFrame  = frame;
			section     = 0;

			var block = description.animblock;
			var index = description.animindex;

			if (description.sectionFrames != 0)
			{
				// Long animations are cut into fixed spans, each pointing at its own block and
				// offset. The final frame of such an animation is stored on its own past the end.
				if (description.numframes > description.sectionFrames && frame == description.numframes - 1)
				{
					localFrame = 0;
					section    = (description.numframes / description.sectionFrames) + 1;
				} else
				{
					section    = frame / description.sectionFrames;
					localFrame = frame - (section * description.sectionFrames);
				}

				var sectionOffset = (int)description.fileOffset + description.sectionIndex + (section * 8);
				if (description.sectionIndex <= 0 || sectionOffset + 8 > mdlBytes.Length)
					return null;

				block = BitConverter.ToInt32(mdlBytes, sectionOffset);
				index = BitConverter.ToInt32(mdlBytes, sectionOffset + 4);
			}

			// -1 means the data was never compiled in; the engine treats it as a stall.
			if (block < 0)
				return null;

			if (block == 0)
			{
				chainOffset = (int)description.fileOffset + index;
				return (chainOffset > 0 && chainOffset < mdlBytes.Length) ? mdlBytes : null;
			}

			// Anything else lives in the external .ani, at the block's own start plus the offset.
			if (aniBytes == null || header.Animblocks == null ||
				block >= header.Animblocks.Length || header.Animblocks[block] == null)
				return null;

			chainOffset = header.Animblocks[block].DataStart + index;
			return (chainOffset > 0 && chainOffset < aniBytes.Length) ? aniBytes : null;
		}

		/// <summary>
		/// Reads the linked list of per-bone records that makes up one animation chain.
		/// </summary>
		static void ReadChain(byte[] data, int offset, int boneCount, List<BoneRecord> records)
		{
			records.Clear();

			var at = offset;
			// A chain has at most one record per bone; the bound stops a corrupt nextoffset from
			// walking the whole file.
			for (int guard = 0; guard <= boneCount && at > 0 && at + 4 <= data.Length; guard++)
			{
				var boneIndex  = data[at];
				var flags      = data[at + 1];
				var nextOffset = BitConverter.ToUInt16(data, at + 2);

				// 255 is the terminator studiomdl writes.
				if (boneIndex == 0xFF || boneIndex >= boneCount)
					break;

				var payload = at + 4;
				var record = new BoneRecord
				{
					BoneIndex      = boneIndex,
					Flags          = flags,
					RotationValues = -1,
					PositionValues = -1,
					RawRotation    = -1,
					RawPosition    = -1
				};

				// The payload holds whichever of these the flags call for, in this order.
				if ((flags & kAnimRot) != 0) record.RotationValues = payload;
				if ((flags & kAnimPos) != 0) record.PositionValues = payload + (((flags & kAnimRot) != 0) ? kValuePointerSize : 0);
				if ((flags & kRawRot)  != 0) record.RawRotation    = payload;
				if ((flags & kRawRot2) != 0) record.RawRotation    = payload;
				if ((flags & kRawPos)  != 0)
					record.RawPosition = payload + (((flags & kRawRot) != 0) ? 6 : 0)
											     + (((flags & kRawRot2) != 0) ? 8 : 0);

				records.Add(record);

				if (nextOffset == 0)
					break;
				at += nextOffset;
			}
		}

		static Quaternion DecodeRotation(byte[] data, in BoneRecord record, StudioBone bone, int frame)
		{
			var isDelta = (record.Flags & kDelta) != 0;

			if ((record.Flags & kRawRot) != 0 && record.RawRotation + 6 <= data.Length)
				return ReadQuaternion48(data, record.RawRotation);

			if ((record.Flags & kRawRot2) != 0 && record.RawRotation + 8 <= data.Length)
				return ReadQuaternion64(data, record.RawRotation);

			if ((record.Flags & kAnimRot) == 0)
				return isDelta ? Quaternion.identity : bone.Quaternion;

			var angles = new Vector3(
				ExtractValue(data, record.RotationValues, 0, frame, bone.RotationScale.x),
				ExtractValue(data, record.RotationValues, 1, frame, bone.RotationScale.y),
				ExtractValue(data, record.RotationValues, 2, frame, bone.RotationScale.z));

			if (!isDelta)
				angles += bone.RadianEulerRotation;

			var rotation = AngleQuaternion(angles);

			// Bones flagged as fixed-alignment are stored relative to a reference orientation, and
			// have to be brought onto the same hemisphere as it or they flip mid-animation.
			if (!isDelta && (bone.Flags & StudioBoneFlags.FixedAlignment) != 0)
			{
				if (Quaternion.Dot(bone.QuaternionAlignment, rotation) < 0)
					rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
			}
			return rotation;
		}

		static Vector3 DecodePosition(byte[] data, in BoneRecord record, StudioBone bone, int frame)
		{
			var isDelta = (record.Flags & kDelta) != 0;

			if ((record.Flags & kRawPos) != 0 && record.RawPosition + 6 <= data.Length)
			{
				return new Vector3(HalfToFloat(BitConverter.ToUInt16(data, record.RawPosition)),
								   HalfToFloat(BitConverter.ToUInt16(data, record.RawPosition + 2)),
								   HalfToFloat(BitConverter.ToUInt16(data, record.RawPosition + 4)));
			}

			if ((record.Flags & kAnimPos) == 0)
				return isDelta ? Vector3.zero : bone.Position;

			var position = new Vector3(
				ExtractValue(data, record.PositionValues, 0, frame, bone.PositionScale.x),
				ExtractValue(data, record.PositionValues, 1, frame, bone.PositionScale.y),
				ExtractValue(data, record.PositionValues, 2, frame, bone.PositionScale.z));

			if (!isDelta)
				position += bone.Position;
			return position;
		}

		/// <summary>
		/// Walks one run-length encoded channel to the value for a frame.
		/// <para>
		/// The stream is a sequence of runs. Each starts with a two-byte header: <c>total</c> frames
		/// the run covers, of which the first <c>valid</c> have a stored value; frames past that hold
		/// the run's last value. So the walk skips whole runs until it finds the one containing the
		/// frame, then either reads that frame's value or repeats the final one.
		/// </para>
		/// </summary>
		internal static float ExtractValue(byte[] data, int pointerOffset, int component, int frame, float scale)
		{
			if (pointerOffset < 0 || pointerOffset + kValuePointerSize > data.Length)
				return 0;

			// The three component offsets are relative to the pointer struct itself.
			var relative = BitConverter.ToInt16(data, pointerOffset + (component * 2));
			if (relative <= 0)
				return 0;

			var at        = pointerOffset + relative;
			var remaining = frame;

			// Bounded: a run covers at least one frame, so it cannot need more steps than there are
			// frames, and a malformed stream stops rather than reading the rest of the file.
			for (int guard = 0; guard < 65536; guard++)
			{
				if (at < 0 || at + 2 > data.Length)
					return 0;

				int valid = data[at];
				int total = data[at + 1];
				if (total == 0)
					return 0;
				if (total > remaining)
				{
					if (valid > remaining)
					{
						var valueAt = at + ((remaining + 1) * 2);
						return (valueAt + 2 <= data.Length) ? BitConverter.ToInt16(data, valueAt) * scale : 0;
					}
					var lastAt = at + (valid * 2);
					return (lastAt + 2 <= data.Length) ? BitConverter.ToInt16(data, lastAt) * scale : 0;
				}

				remaining -= total;
				at        += (valid + 1) * 2;
			}
			return 0;
		}

		/// <summary>Quaternion48: 16 bits each for x and y, 15 for z, and a sign bit for w.</summary>
		internal static Quaternion ReadQuaternion48(byte[] data, int at)
		{
			var rawX  = BitConverter.ToUInt16(data, at);
			var rawY  = BitConverter.ToUInt16(data, at + 2);
			var third = BitConverter.ToUInt16(data, at + 4);

			var rawZ      = third & 0x7FFF;
			var negativeW = (third & 0x8000) != 0;

			var x = (rawX - 32768) * (1.0f / 32768.0f);
			var y = (rawY - 32768) * (1.0f / 32768.0f);
			var z = (rawZ - 16384) * (1.0f / 16384.0f);
			return WithRecoveredW(x, y, z, negativeW);
		}

		/// <summary>Quaternion64: 21 bits each for x, y and z, and a sign bit for w.</summary>
		internal static Quaternion ReadQuaternion64(byte[] data, int at)
		{
			var packed = BitConverter.ToUInt64(data, at);

			var rawX = (long)(packed & 0x1FFFFF); packed >>= 21;
			var rawY = (long)(packed & 0x1FFFFF); packed >>= 21;
			var rawZ = (long)(packed & 0x1FFFFF); packed >>= 21;
			var negativeW = (packed & 1) != 0;

			var x = (rawX - 1048576) * (1.0f / 1048576.5f);
			var y = (rawY - 1048576) * (1.0f / 1048576.5f);
			var z = (rawZ - 1048576) * (1.0f / 1048576.5f);
			return WithRecoveredW(x, y, z, negativeW);
		}

		/// <summary>
		/// Rebuilds w from the other three components. Only its sign is stored, because a unit
		/// quaternion's fourth component is otherwise determined.
		/// </summary>
		static Quaternion WithRecoveredW(float x, float y, float z, bool negativeW)
		{
			var remainder = 1.0f - (x * x) - (y * y) - (z * z);
			var w = (remainder > 0) ? Mathf.Sqrt(remainder) : 0;
			if (negativeW)
				w = -w;
			return new Quaternion(x, y, z, w);
		}

		/// <summary>Source's AngleQuaternion, for a rotation given as radian Euler angles.</summary>
		internal static Quaternion AngleQuaternion(Vector3 angles)
		{
			var halfYaw   = angles.z * 0.5f;
			var halfPitch = angles.y * 0.5f;
			var halfRoll  = angles.x * 0.5f;

			var sy = Mathf.Sin(halfYaw);   var cy = Mathf.Cos(halfYaw);
			var sp = Mathf.Sin(halfPitch); var cp = Mathf.Cos(halfPitch);
			var sr = Mathf.Sin(halfRoll);  var cr = Mathf.Cos(halfRoll);

			return new Quaternion((sr * cp * cy) - (cr * sp * sy),
								  (cr * sp * cy) + (sr * cp * sy),
								  (cr * cp * sy) - (sr * sp * cy),
								  (cr * cp * cy) + (sr * sp * sy));
		}

		/// <summary>Source's float16, which is an IEEE half.</summary>
		internal static float HalfToFloat(ushort half)
		{
			var sign     = (half >> 15) & 1;
			var exponent = (half >> 10) & 0x1F;
			var mantissa = half & 0x3FF;

			float value;
			if (exponent == 0)
				value = (mantissa / 1024.0f) * Mathf.Pow(2, -14);
			else if (exponent == 31)
				value = (mantissa == 0) ? float.PositiveInfinity : float.NaN;
			else
				value = (1.0f + (mantissa / 1024.0f)) * Mathf.Pow(2, exponent - 15);

			return (sign != 0) ? -value : value;
		}
	}
}
