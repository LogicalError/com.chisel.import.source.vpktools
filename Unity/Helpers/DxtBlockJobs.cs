using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// DXT1, DXT3 and DXT5 blocks decoded by a Burst job, a block at a time on every core, into exactly what
	/// <see cref="Texture2DHelpers"/>' own decoders make of them: the same integer arithmetic per block, and every channel
	/// the float a byte k makes, k / 255f, taken from a table made by that same expression in managed code
	/// (<see cref="kByteToFloat"/>), so no float is worked out any other way. Decoding the textures in managed code was 3.6
	/// of the 41 s of a map's first import, the most of what reading a texture takes.
	/// <para>
	/// Only for an image whose sides are multiples of 4: the managed decoders treat the blocks past another size's edge
	/// each their own way, and keep them.
	/// </para>
	/// </summary>
	static class DxtBlockJobs
	{
		internal enum Kind { DXT1, DXT3, DXT5 }

		// What the managed decoders make of a byte k: k / 255f
		static readonly float[] kByteToFloat = ByteToFloat();

		static float[] ByteToFloat()
		{
			var table = new float[256];
			for (int k = 0; k < 256; k++)
				table[k] = k / 255f;
			return table;
		}

		/// <summary>The image the blocks in <paramref name="bytes"/> make, row by row as the managed decoders write it.</summary>
		internal static Color[] Decode(Kind kind, byte[] bytes, int width, int height)
		{
			var pixels = new Color[width * height];
			int blocksX = width / 4;
			int blocks = blocksX * (height / 4);
			if (blocks == 0)
				return pixels;
			using var source = new NativeArray<byte>(bytes, Allocator.TempJob);
			using var table = new NativeArray<float>(kByteToFloat, Allocator.TempJob);
			using var destination = new NativeArray<Color>(pixels.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			new DecodeJob
			{
				kind        = kind,
				source      = source,
				table       = table,
				destination = destination,
				width       = width,
				blocksX     = blocksX,
			}.Schedule(blocks, 64).Complete();
			destination.CopyTo(pixels);
			return pixels;
		}

		[BurstCompile(CompileSynchronously = true)]
		struct DecodeJob : IJobParallelFor
		{
			public Kind kind;
			[ReadOnly] public NativeArray<byte> source;
			[ReadOnly] public NativeArray<float> table;
			// Each block writes its own 4x4 pixels
			[NativeDisableParallelForRestriction, WriteOnly] public NativeArray<Color> destination;
			public int width, blocksX;

			public void Execute(int block)
			{
				int x = (block % blocksX) * 4;
				int y = (block / blocksX) * 4;
				switch (kind)
				{
					case Kind.DXT1: Dxt1(block * 8, x, y); break;
					case Kind.DXT3: Dxt3(block * 16, x, y); break;
					default:        Dxt5(block * 16, x, y); break;
				}
			}

			// Texture2DHelpers.DecompressColorBlocks: end points widened by shifting; between them (2a + b + 1) / 3, or
			// (a + b) / 2 when the first is below the second; opaque either way
			void Dxt1(int o, int x, int y)
			{
				int color0Data = source[o] | (source[o + 1] << 8);
				int color1Data = source[o + 2] | (source[o + 3] << 8);
				uint bitmask = (uint)(source[o + 4] | (source[o + 5] << 8) | (source[o + 6] << 16) | (source[o + 7] << 24));

				int r0 = ((color0Data >> 11) & 0x1F) << 3, g0 = ((color0Data >> 5) & 0x3F) << 2, b0 = (color0Data & 0x1F) << 3;
				int r1 = ((color1Data >> 11) & 0x1F) << 3, g1 = ((color1Data >> 5) & 0x3F) << 2, b1 = (color1Data & 0x1F) << 3;

				var palette0 = new Color(table[r0], table[g0], table[b0], 1f);
				var palette1 = new Color(table[r1], table[g1], table[b1], 1f);
				var palette2 = (color0Data < color1Data)
					? new Color(table[(r0 + r1) / 2], table[(g0 + g1) / 2], table[(b0 + b1) / 2], 1f)
					: new Color(table[(r0 * 2 + r1 + 1) / 3], table[(g0 * 2 + g1 + 1) / 3], table[(b0 * 2 + b1 + 1) / 3], 1f);
				var palette3 = new Color(table[(r1 * 2 + r0 + 1) / 3], table[(g1 * 2 + g0 + 1) / 3], table[(b1 * 2 + b0 + 1) / 3], 1f);

				int index = 0;
				for (int blockY = 0; blockY < 4; blockY++)
				{
					for (int blockX = 0; blockX < 4; blockX++, index++)
					{
						var code = (bitmask >> (index * 2)) & 0x03;
						destination[(y + blockY) * width + x + blockX] =
							code == 0 ? palette0 : code == 1 ? palette1 : code == 2 ? palette2 : palette3;
					}
				}
			}

			// Texture2DHelpers.DecompressBlockDXT3: a nibble of alpha a pixel, n * 17, over DXT5's colours
			void Dxt3(int o, int x, int y)
			{
				Colours(o + 8, out var colours);
				uint code = (uint)source[o + 12] | ((uint)source[o + 13] << 8) | ((uint)source[o + 14] << 16) | ((uint)source[o + 15] << 24);
				for (int j = 0; j < 4; j++)
				{
					for (int i = 0; i < 4; i++)
					{
						int pixel = 4 * j + i;
						int alpha = ((source[o + pixel / 2] >> ((pixel & 1) * 4)) & 0x0F) * 17;
						int colorCode = (int)((code >> 2 * pixel) & 0x03);
						var colour = colours[colorCode];
						destination[(y + j) * width + x + i] = new Color(colour.x, colour.y, colour.z, table[alpha]);
					}
				}
			}

			// Texture2DHelpers.DecompressBlockDXT5
			void Dxt5(int o, int x, int y)
			{
				byte alpha0 = source[o];
				byte alpha1 = source[o + 1];
				uint alphaCode1 = (uint)source[o + 4] | ((uint)source[o + 5] << 8) | ((uint)source[o + 6] << 16) | ((uint)source[o + 7] << 24);
				uint alphaCode2 = (uint)source[o + 2] | ((uint)source[o + 3] << 8);

				Colours(o + 8, out var colours);
				uint code = (uint)source[o + 12] | ((uint)source[o + 13] << 8) | ((uint)source[o + 14] << 16) | ((uint)source[o + 15] << 24);

				var alphas = new FixedList64Bytes<float>();
				for (uint alphaCode = 0; alphaCode < 8; alphaCode++)
				{
					byte finalAlpha;
					if (alphaCode == 0)
						finalAlpha = alpha0;
					else if (alphaCode == 1)
						finalAlpha = alpha1;
					else if (alpha0 > alpha1)
						finalAlpha = (byte)(((8 - alphaCode) * alpha0 + (alphaCode - 1) * alpha1) / 7);
					else if (alphaCode == 6)
						finalAlpha = 0;
					else if (alphaCode == 7)
						finalAlpha = 255;
					else
						finalAlpha = (byte)(((6 - alphaCode) * alpha0 + (alphaCode - 1) * alpha1) / 5);
					alphas.Add(table[finalAlpha]);
				}

				for (int j = 0; j < 4; j++)
				{
					for (int i = 0; i < 4; i++)
					{
						int alphaCodeIndex = 3 * (4 * j + i);
						uint alphaCode;
						if (alphaCodeIndex <= 12)
							alphaCode = (alphaCode2 >> alphaCodeIndex) & 0x07;
						else if (alphaCodeIndex == 15)
							alphaCode = (alphaCode2 >> 15) | ((alphaCode1 << 1) & 0x06);
						else
							alphaCode = (alphaCode1 >> (alphaCodeIndex - 16)) & 0x07;
						int colorCode = (int)((code >> 2 * (4 * j + i)) & 0x03);
						var colour = colours[colorCode];
						destination[(y + j) * width + x + i] = new Color(colour.x, colour.y, colour.z, alphas[(int)alphaCode]);
					}
				}
			}

			// DXT3's and DXT5's four colours: end points widened as (v * 255 + half) / range, rounded; between them
			// (2a + b) / 3 and (a + 2b) / 3
			void Colours(int o, out FixedList64Bytes<Vector3> colours)
			{
				ushort color0 = (ushort)(source[o] | (source[o + 1] << 8));
				ushort color1 = (ushort)(source[o + 2] | (source[o + 3] << 8));

				uint temp = (uint)(color0 >> 11) * 255 + 16;
				byte r0 = (byte)((temp / 32 + temp) / 32);
				temp = (uint)((color0 & 0x07E0) >> 5) * 255 + 32;
				byte g0 = (byte)((temp / 64 + temp) / 64);
				temp = (uint)(color0 & 0x001F) * 255 + 16;
				byte b0 = (byte)((temp / 32 + temp) / 32);

				temp = (uint)(color1 >> 11) * 255 + 16;
				byte r1 = (byte)((temp / 32 + temp) / 32);
				temp = (uint)((color1 & 0x07E0) >> 5) * 255 + 32;
				byte g1 = (byte)((temp / 64 + temp) / 64);
				temp = (uint)(color1 & 0x001F) * 255 + 16;
				byte b1 = (byte)((temp / 32 + temp) / 32);

				colours = new FixedList64Bytes<Vector3>();
				colours.Add(new Vector3(table[r0], table[g0], table[b0]));
				colours.Add(new Vector3(table[r1], table[g1], table[b1]));
				colours.Add(new Vector3(table[(2 * r0 + r1) / 3], table[(2 * g0 + g1) / 3], table[(2 * b0 + b1) / 3]));
				colours.Add(new Vector3(table[(r0 + 2 * r1) / 3], table[(g0 + 2 * g1) / 3], table[(b0 + 2 * b1) / 3]));
			}
		}
	}
}
