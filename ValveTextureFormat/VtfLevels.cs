using System;
using System.IO;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// A VTF's mipmap chain, as stored. <see cref="VTF"/> reads only the largest level ("The largest mip comes last")
	/// because it decodes to <see cref="Color"/>[] for a .png; this reads every level and hands the bytes over
	/// untouched, which is what <see cref="SourceTextureImporter"/> needs to keep the mips Valve authored.
	/// </summary>
	/// <remarks>
	/// Layout, which the file states and nothing else does: levels run SMALLEST FIRST, and within a level frame by
	/// frame and then face by face. Unity wants mip 0 first, so <see cref="RawLargestFirst"/> reverses them.
	/// Only frame 0, face 0 is taken; an animated or cube VTF keeps its first image.
	/// </remarks>
	public sealed class VtfLevels
	{
		public int    Width  { get; private set; }
		public int    Height { get; private set; }
		public int    Count  { get; private set; }

		VTFImageFormat format;
		byte[]         bytes;
		int[]          offsets;   // into bytes, level 0 = largest
		int[]          sizes;

		public bool IsBlockCompressed =>
			format == VTFImageFormat.IMAGE_FORMAT_DXT1 ||
			format == VTFImageFormat.IMAGE_FORMAT_DXT1_ONEBITALPHA ||
			format == VTFImageFormat.IMAGE_FORMAT_DXT5;

		/// <summary>The Unity format holding Valve's blocks unchanged. DXT3 is absent on purpose - Unity has no
		/// TextureFormat for it, so those decode instead.</summary>
		public TextureFormat BlockFormat =>
			(format == VTFImageFormat.IMAGE_FORMAT_DXT5) ? TextureFormat.DXT5 : TextureFormat.DXT1;

		/// <summary>Every level end to end, mip 0 first, exactly as Unity's raw texture data wants it.</summary>
		public byte[] RawLargestFirst()
		{
			int total = 0;
			for (int level = 0; level < Count; level++)
				total += sizes[level];
			var result = new byte[total];
			int at = 0;
			for (int level = 0; level < Count; level++)
			{
				Buffer.BlockCopy(bytes, offsets[level], result, at, sizes[level]);
				at += sizes[level];
			}
			return result;
		}

		/// <summary>One level decoded to RGBA32, for the formats Unity cannot take as they are.</summary>
		/// <remarks>
		/// A block format still occupies a whole 4x4 block below 4x4, so the last two levels of any chain are a 2x2
		/// and a 1x1 image inside a full block. Handing the decoder those dimensions walks off the end of what it
		/// allocated - it threw on exactly levels 8 and 9 of a 512x512 DXT5 - so the block is decoded at its real 4x4
		/// size and the corner cropped out. Only the fallback path reaches this; a DXT texture is normally handed to
		/// Unity compressed and never decoded at all, which is why it went unnoticed until a test walked every level.
		/// </remarks>
		public byte[] DecodeLevel(int level)
		{
			LevelSize(level, out var levelWidth, out var levelHeight);
			int decodeWidth = levelWidth, decodeHeight = levelHeight;
			if (IsBlockCompressed || format == VTFImageFormat.IMAGE_FORMAT_DXT3)
			{
				decodeWidth  = Mathf.Max(4, (levelWidth  + 3) & ~3);
				decodeHeight = Mathf.Max(4, (levelHeight + 3) & ~3);
			}

			using var stream = new MemoryStream(bytes, offsets[level], sizes[level], writable: false);
			var pixels = VTF.DecompressImageForLevels(stream, (ushort)decodeWidth, (ushort)decodeHeight,
													  (uint)sizes[level], format);

			var result = new byte[levelWidth * levelHeight * 4];
			for (int y = 0; y < levelHeight; y++)
			{
				for (int x = 0; x < levelWidth; x++)
				{
					int from = (y * decodeWidth) + x;
					if (from >= pixels.Length)
						continue;
					var pixel = pixels[from];
					int to = ((y * levelWidth) + x) * 4;
					result[to + 0] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.r * 255f), 0, 255);
					result[to + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.g * 255f), 0, 255);
					result[to + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.b * 255f), 0, 255);
					result[to + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixel.a * 255f), 0, 255);
				}
			}
			return result;
		}

		void LevelSize(int level, out int levelWidth, out int levelHeight)
		{
			levelWidth  = Mathf.Max(1, Width  >> level);
			levelHeight = Mathf.Max(1, Height >> level);
		}

		/// <summary>Bytes one level of this size occupies. Block formats round up to whole 4x4 blocks.</summary>
		public static int SizeOfLevel(int levelWidth, int levelHeight, VTFImageFormat imageFormat)
		{
			levelWidth  = Mathf.Max(1, levelWidth);
			levelHeight = Mathf.Max(1, levelHeight);
			switch (imageFormat)
			{
				case VTFImageFormat.IMAGE_FORMAT_DXT1:
				case VTFImageFormat.IMAGE_FORMAT_DXT1_ONEBITALPHA:
					return Mathf.Max(1, (levelWidth + 3) / 4) * Mathf.Max(1, (levelHeight + 3) / 4) * 8;
				case VTFImageFormat.IMAGE_FORMAT_DXT3:
				case VTFImageFormat.IMAGE_FORMAT_DXT5:
					return Mathf.Max(1, (levelWidth + 3) / 4) * Mathf.Max(1, (levelHeight + 3) / 4) * 16;
				default:
					return levelWidth * levelHeight * BytesPerPixel(imageFormat);
			}
		}

		static int BytesPerPixel(VTFImageFormat imageFormat)
		{
			switch (imageFormat)
			{
				case VTFImageFormat.IMAGE_FORMAT_I8:
				case VTFImageFormat.IMAGE_FORMAT_A8:
				case VTFImageFormat.IMAGE_FORMAT_P8:              return 1;
				case VTFImageFormat.IMAGE_FORMAT_IA88:
				case VTFImageFormat.IMAGE_FORMAT_RGB565:
				case VTFImageFormat.IMAGE_FORMAT_BGR565:
				case VTFImageFormat.IMAGE_FORMAT_BGRA4444:
				case VTFImageFormat.IMAGE_FORMAT_BGRA5551:
				case VTFImageFormat.IMAGE_FORMAT_BGRX5551:
				case VTFImageFormat.IMAGE_FORMAT_UV88:            return 2;
				case VTFImageFormat.IMAGE_FORMAT_RGB888:
				case VTFImageFormat.IMAGE_FORMAT_BGR888:
				case VTFImageFormat.IMAGE_FORMAT_RGB888_BLUESCREEN:
				case VTFImageFormat.IMAGE_FORMAT_BGR888_BLUESCREEN: return 3;
				case VTFImageFormat.IMAGE_FORMAT_RGBA16161616:
				case VTFImageFormat.IMAGE_FORMAT_RGBA16161616F:   return 8;
				default:                                          return 4;
			}
		}

		/// <summary>Reads a VTF's header and locates every level. False, with a reason, when the bytes are not one.</summary>
		public static bool TryRead(byte[] data, out VtfLevels levels, out string error)
		{
			levels = null;
			error  = null;
			if (data == null || data.Length < 64)
			{
				error = "not a VTF: too short";
				return false;
			}
			if (BitConverter.ToUInt32(data, 0) != 0x00465456u)   // "VTF\0"
			{
				error = "not a VTF: wrong signature";
				return false;
			}

			uint headerSize = BitConverter.ToUInt32(data, 12);
			int  width      = BitConverter.ToUInt16(data, 16);
			int  height     = BitConverter.ToUInt16(data, 18);
			int  frames     = BitConverter.ToUInt16(data, 24);
			var  imageFormat = (VTFImageFormat)BitConverter.ToUInt32(data, 52);
			int  mipmaps    = data[56];
			var  lowFormat  = (VTFImageFormat)BitConverter.ToUInt32(data, 57);
			int  lowWidth   = data[61];
			int  lowHeight  = data[62];

			if (width <= 0 || height <= 0 || mipmaps <= 0)
			{
				error = $"empty image ({width}x{height}, {mipmaps} mips)";
				return false;
			}
			if (frames <= 0)
				frames = 1;

			// The thumbnail sits between the header and the image data
			int at = (int)headerSize;
			if (lowFormat != VTFImageFormat.IMAGE_FORMAT_NONE)
				at += SizeOfLevel(lowWidth, lowHeight, lowFormat);

			// Faces are not read apart here: whatever the file holds, level sizes are per image, and taking the
			// first image of each level is the same walk with a stride.
			int faces = 1;

			var offsets = new int[mipmaps];
			var sizes   = new int[mipmaps];
			for (int stored = mipmaps - 1; stored >= 0; stored--)   // smallest first, as stored
			{
				int levelWidth  = Mathf.Max(1, width  >> stored);
				int levelHeight = Mathf.Max(1, height >> stored);
				int size        = SizeOfLevel(levelWidth, levelHeight, imageFormat);
				if (at + size > data.Length)
				{
					error = $"truncated: level {stored} ({levelWidth}x{levelHeight}) needs {size} bytes at {at}, " +
							$"file is {data.Length}";
					return false;
				}
				offsets[stored] = at;                                // index by Unity's level number
				sizes[stored]   = size;
				at += size * frames * faces;
			}

			levels = new VtfLevels
			{
				Width   = width,
				Height  = height,
				Count   = mipmaps,
				format  = imageFormat,
				bytes   = data,
				offsets = offsets,
				sizes   = sizes,
			};
			return true;
		}
	}
}
