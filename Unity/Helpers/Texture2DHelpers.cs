using Chisel.Import.Source.VPKTools.Helpers;

using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public static class Texture2DHelpers
    {
        /// <summary>
        /// Off decodes DXT1, DXT3 and DXT5 in managed code only, as before: what the Burst jobs (<see cref="DxtBlockJobs"/>),
        /// which take an image whose sides are multiples of 4, are compared against.
        /// </summary>
        internal static bool BurstDecodeEnabled = true;

        /// <summary>
        /// Turns a Texture2D object to a Sprite object.
        /// </summary>
        /// <param name="texture">The original image</param>
        /// <returns>The texture as a sprite</returns>
        public static Sprite ToSprite( this Texture2D texture )
        {
            return Sprite.Create( texture, new Rect( 0, 0, texture.width, texture.height ), new Vector2( 0.5f, 0.5f ) );
        }

        /// <summary>
        /// Flips the texture horizontally.
        /// </summary>
        /// <param name="colors">Original texture colors</param>
        /// <param name="width">Texture width</param>
        /// <param name="height">Texture height</param>
        /// <returns>Flipped texture color array</returns>
        public static void FlipHorizontal( Color[] colors, ushort width, ushort height )
        {
            for( uint row = 0; row < height; row++ )
            {
                for( uint col = 0; col < ( width / 2 ); col++ )
                {
                    uint currentRowIndex = row * width;
					(colors[currentRowIndex + ( width - col - 1 )], colors[currentRowIndex + col]) = 
                        (colors[currentRowIndex + col], colors[currentRowIndex + ( width - col - 1 )]);
				}
			}
        }

        /// <summary>
        /// Flips the texture vertically.
        /// </summary>
        /// <param name="colors">Original texture colors</param>
        /// <param name="width">Texture width</param>
        /// <param name="height">Texture height</param>
        /// <returns>Flipped texture color array</returns>
        public static void FlipVertical( Color[] colors, ushort width, ushort height )
        {
            // Row by row, a row at a time
            var swap = new Color[width];
            for( int row = 0; row < ( height / 2 ); row++ )
            {
                int currentRowIndex  = row                  * width;
                int oppositeRowIndex = ( height - row - 1 ) * width;
                Array.Copy( colors, currentRowIndex, swap, 0, width );
                Array.Copy( colors, oppositeRowIndex, colors, currentRowIndex, width );
                Array.Copy( swap, 0, colors, oppositeRowIndex, width );
            }
        }

        /// <summary>
        /// Catch all decompress raw texture data function.
        /// </summary>
        /// <param name="data">Raw image byte data</param>
        /// <param name="width">Expected width of the image</param>
        /// <param name="height">Expected height of the image</param>
        /// <param name="textureFormat">The format of the data given</param>
        /// <returns>Pixel color data</returns>
        public static Color[] DecompressRawBytes( Stream data, ushort width, ushort height, uint dataSize, TextureFormat textureFormat )
        {
            Color[] colors = null;
			if (textureFormat == TextureFormat.I8)
				colors = DecompressOneByte(data, width, height, alpha: false);
			else if (textureFormat == TextureFormat.A8)
				colors = DecompressOneByte(data, width, height, alpha: true);
			else if (textureFormat == TextureFormat.IA88)
				colors = DecompressTwoBytes(data, width, height, luminanceAlpha: true);
			else if ( textureFormat == TextureFormat.RG88)
                colors = DecompressTwoBytes( data, width, height, luminanceAlpha: false );
            else if( textureFormat == TextureFormat.BGR888 )
                colors = DecompressBGR888( data, width, height );
            else if( textureFormat == TextureFormat.BGRA8888 )
                colors = DecompressBGRA8888( data, width, height );
            else if( textureFormat == TextureFormat.DXT1 )
                colors = DecompressDXT1( data, width, height, dataSize);
            else if( textureFormat == TextureFormat.DXT3 )
                colors = DecompressDXT3( data, width, height, dataSize);
            else if( textureFormat == TextureFormat.DXT5 )
                colors = DecompressDXT5( data, width, height, dataSize);
            else if( textureFormat == TextureFormat.RGBA16161616F )
                colors = DecompressRGBA16161616F( data, width, height );
            else
                Debug.LogError( "Texture2DHelpers: Texture format not supported " + textureFormat );

            return colors;
		}

		/// <summary>
		/// The bytes of an image, read at once. The decoders below read a field, or a byte, at a time, each read a call
		/// through the VPK's stream and, for a byte, an array of its own (Stream.ReadByte). A stream that ends early leaves the
		/// rest as <paramref name="fill"/>, which is what the reads gave there: 0 where a field was read into a new array
		/// (DataParser), 255 where a byte was (ReadByte's -1, as a byte).
		/// </summary>
		static byte[] ReadImageBytes(Stream data, long count, byte fill, out long read)
		{
			var bytes = new byte[count];
			read = 0;
			using (ImportSteps.Step("vtf: bytes read"))
			{
				while (read < count)
				{
					var n = data.Read(bytes, (int)read, (int)(count - read));
					if (n <= 0)
						break;
					read += n;
				}
			}
			if (fill != 0)
			{
				for (var i = read; i < count; i++)
					bytes[i] = fill;
			}
			return bytes;
		}

		/// <summary>
		/// Turn raw one-byte pixels to a Color array, as Direct3D 9 samples them, which is how Source's textures are made to
		/// be read: I8 is luminance, (I, I, I, 1), and A8 is alpha alone, (0, 0, 0, A).
		/// </summary>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <param name="alpha">A8 rather than I8</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressOneByte(Stream data, ushort width, ushort height, bool alpha)
		{
			Color[] texture2DColors = new Color[width * height];
			var bytes = ReadImageBytes(data, texture2DColors.Length, 255, out _);
			for (int i = 0; i < texture2DColors.Length; i++)
			{
				float value = bytes[i] / (float)byte.MaxValue;
				texture2DColors[i] = alpha ? new Color(0, 0, 0, value) : new Color(value, value, value, 1);
			}
			return texture2DColors;
		}

		/// <summary>
		/// Turn raw two-byte pixels to a Color array, as Direct3D 9 samples them: IA88 is luminance and alpha, (I, I, I, A);
		/// otherwise the two channels are red and green, (R, G, 1, 1), as UV88 keeps them.
		/// </summary>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <param name="luminanceAlpha">IA88 rather than two plain channels</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressTwoBytes(Stream data, ushort width, ushort height, bool luminanceAlpha)
		{
			Color[] texture2DColors = new Color[width * height];
			var bytes = ReadImageBytes(data, texture2DColors.Length * 2L, 255, out _);
			for (int i = 0; i < texture2DColors.Length; i++)
			{
				float first = bytes[i * 2] / (float)byte.MaxValue;
				float second = bytes[i * 2 + 1] / (float)byte.MaxValue;
				texture2DColors[i] = luminanceAlpha ? new Color(first, first, first, second) : new Color(first, second, 1, 1);
			}
			return texture2DColors;
		}

		/// <summary>
		/// Turn raw BGR888 bytes to a Color array that can be used in a Texture2D.
		/// </summary>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressBGR888( Stream data, ushort width, ushort height )
        {
            Color[] texture2DColors = new Color[width * height];
            var bytes = ReadImageBytes( data, texture2DColors.Length * 3L, 255, out _ );
            for( int i = 0; i < texture2DColors.Length; i++ )
            {
                byte blue  = bytes[i * 3];
                byte green = bytes[i * 3 + 1];
                byte red   = bytes[i * 3 + 2];
                texture2DColors[i] = new Color( ( (float) red ) / byte.MaxValue, ( (float) green ) / byte.MaxValue, ( (float) blue ) / byte.MaxValue );
            }
            return texture2DColors;
        }

        /// <summary>
        /// Turn raw BGRA8888 bytes to a Color array that can be used in a Texture2D.
        /// </summary>
        /// <param name="data">Raw image byte data</param>
        /// <param name="width">Expected width of the image</param>
        /// <param name="height">Expected height of the image</param>
        /// <returns>Pixel color data</returns>
        public static Color[] DecompressBGRA8888( Stream data, ushort width, ushort height )
        {
            Color[] texture2DColors = new Color[width * height];
            var bytes = ReadImageBytes( data, texture2DColors.Length * 4L, 255, out _ );
            for( int i = 0; i < texture2DColors.Length; i++ )
            {
                byte blue  = bytes[i * 4];
                byte green = bytes[i * 4 + 1];
                byte red   = bytes[i * 4 + 2];
                byte alpha = bytes[i * 4 + 3];
                texture2DColors[i] = new Color( ( (float) red )   / byte.MaxValue, ( (float) green ) / byte.MaxValue, ( (float) blue ) / byte.MaxValue,
                                                ( (float) alpha ) / byte.MaxValue );
            }
            return texture2DColors;
        }

        /// <summary>
        /// Turn raw RGBA16161616F bytes - four little-endian half floats a pixel, linear and unbounded, which is how Source
        /// stores HDR cubemaps - to a Color array.
        /// </summary>
        public static Color[] DecompressRGBA16161616F( Stream data, ushort width, ushort height )
        {
            var texture2DColors = new Color[width * height];
            var bytes = ReadImageBytes( data, texture2DColors.Length * 8L, 0, out var read );
            for( int i = 0; i < texture2DColors.Length; i++ )
            {
                if( ( i + 1 ) * 8L > read )
                {
                    Debug.LogError( "RGBA16161616F: the data ended before the image did" );
                    break;
                }
                texture2DColors[i] = new Color( Mathf.HalfToFloat( BitConverter.ToUInt16( bytes, i * 8 ) ), Mathf.HalfToFloat( BitConverter.ToUInt16( bytes, i * 8 + 2 ) ),
                                                Mathf.HalfToFloat( BitConverter.ToUInt16( bytes, i * 8 + 4 ) ), Mathf.HalfToFloat( BitConverter.ToUInt16( bytes, i * 8 + 6 ) ) );
            }
            return texture2DColors;
        }

        /// <summary>
        /// Turn raw DXT1 bytes to a Color array that can be used in a Texture2D.
        /// </summary>
        /// <param name="data">Raw image byte data</param>
        /// <param name="width">Expected width of the image</param>
        /// <param name="height">Expected height of the image</param>
        /// <returns>Pixel color data</returns>
        public static Color[] DecompressDXT1(Stream data, ushort width, ushort height, uint dataSize)
		{
			if (BurstDecodeEnabled && width % 4 == 0 && height % 4 == 0)
				return DxtBlockJobs.Decode(DxtBlockJobs.Kind.DXT1, ReadImageBytes(data, (long)(width / 4) * (height / 4) * 8, 0, out _), width, height);
            Color[] texture2DColors = new Color[width * height];
            DecompressColorBlocks( data, width, height, texture2DColors );
            return texture2DColors;
		}

		/// <summary>
		/// Turn raw DXT3 bytes to a Color array that can be used in a Texture2D: sixteen bytes a block, its alpha - sixteen
		/// four-bit values, row by row, the low nibble first - then its colour, a block as DXT5 has it, always of four colours.
		/// </summary>
		/// <remarks>
		/// DXT1 blocks are eight bytes; reading a DXT3 block as one would decode the first half of the image's alpha and colour as colour.
		/// </remarks>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressDXT3( Stream data, ushort width, ushort height, uint dataSize)
		{
			int blockCountX = (width + 3) / 4;
			int blockCountY = (height + 3) / 4;
			var bytes = ReadImageBytes(data, (long)blockCountX * blockCountY * 16, 0, out _);
			if (BurstDecodeEnabled && width % 4 == 0 && height % 4 == 0)
				return DxtBlockJobs.Decode(DxtBlockJobs.Kind.DXT3, bytes, width, height);
			var pixels = new Color[width * height];
			int offset = 0;
			for (int j = 0; j < blockCountY; j++)
			{
				for (int i = 0; i < blockCountX; i++)
				{
					DecompressBlockDXT3(i * 4, j * 4, width, height, bytes.AsSpan(offset, 16), pixels);
					offset += 16;
				}
			}
			return pixels;
		}

		// One DXT3 block: its explicit alpha, a nibble a pixel scaled to 0..255 (n * 17), over the colours DXT5 decodes
		internal static void DecompressBlockDXT3(int x, int y, int width, int height, Span<byte> blockStorage, Color[] image)
		{
			ushort color0 = (ushort)((ushort)blockStorage[ 8] | ((ushort)blockStorage[ 9] << 8));
			ushort color1 = (ushort)((ushort)blockStorage[10] | ((ushort)blockStorage[11] << 8));

			uint temp = (uint)(color0 >> 11) * 255 + 16;
			byte r0 = (byte)((temp/32 + temp)/32);
			temp = (uint)((color0 & 0x07E0) >> 5) * 255 + 32;
			byte g0 = (byte)((temp/64 + temp)/64);
			temp = (uint)(color0 & 0x001F) * 255 + 16;
			byte b0 = (byte)((temp/32 + temp)/32);

			temp = (uint)(color1 >> 11) * 255 + 16;
			byte r1 = (byte)((temp/32 + temp)/32);
			temp = (uint)((color1 & 0x07E0) >> 5) * 255 + 32;
			byte g1 = (byte)((temp/64 + temp)/64);
			temp = (uint)(color1 & 0x001F) * 255 + 16;
			byte b1 = (byte)((temp/32 + temp)/32);

			uint code = (uint)blockStorage[12] | ((uint)blockStorage[13] << 8) | ((uint)blockStorage[14] << 16) | ((uint)blockStorage[15] << 24);

			Span<float> reds   = stackalloc float[4] { r0 / 255.0f, r1 / 255.0f, ((2 * r0 + r1) / 3) / 255.0f, ((r0 + 2 * r1) / 3) / 255.0f };
			Span<float> greens = stackalloc float[4] { g0 / 255.0f, g1 / 255.0f, ((2 * g0 + g1) / 3) / 255.0f, ((g0 + 2 * g1) / 3) / 255.0f };
			Span<float> blues  = stackalloc float[4] { b0 / 255.0f, b1 / 255.0f, ((2 * b0 + b1) / 3) / 255.0f, ((b0 + 2 * b1) / 3) / 255.0f };

			for (int j = 0; j < 4; j++)
			{
				for (int i = 0; i < 4; i++)
				{
					int pixel = 4 * j + i;
					int alpha = ((blockStorage[pixel / 2] >> ((pixel & 1) * 4)) & 0x0F) * 17;
					int colorCode = (int)((code >> 2 * pixel) & 0x03);
					if (x + i < width && y + j < height)
						image[(y + j) * width + (x + i)] = new Color(reds[colorCode], greens[colorCode], blues[colorCode], alpha / 255.0f);
				}
			}
		}

        // DXT1's colour blocks, eight bytes each in rows of blocks: two RGB565 end points and sixteen two-bit indices. The
        // end points widen by shifting, and the colours between them round as (2a + b + 1) / 3, or (a + b) / 2 when the
        // first is below the second - and are opaque either way.
        static void DecompressColorBlocks( Stream data, ushort width, ushort height, Color[] texture2DColors )
        {
            long blocks = (long)( ( width + 3 ) / 4 ) * ( ( height + 3 ) / 4 );
            var bytes = ReadImageBytes( data, blocks * 8, 0, out _ );
            int offset = 0;
            for( int row = 0; row < height; row += 4 )
            {
                for( int col = 0; col < width; col += 4 )
                {
                    int color0Data = bytes[offset] | ( bytes[offset + 1] << 8 );
                    int color1Data = bytes[offset + 2] | ( bytes[offset + 3] << 8 );
                    uint bitmask   = (uint)( bytes[offset + 4] | ( bytes[offset + 5] << 8 ) | ( bytes[offset + 6] << 16 ) | ( bytes[offset + 7] << 24 ) );
                    offset += 8;

                    int r0 = ( ( color0Data >> 11 ) & 0x1F ) << 3, g0 = ( ( color0Data >> 5 ) & 0x3F ) << 2, b0 = ( color0Data & 0x1F ) << 3;
                    int r1 = ( ( color1Data >> 11 ) & 0x1F ) << 3, g1 = ( ( color1Data >> 5 ) & 0x3F ) << 2, b1 = ( color1Data & 0x1F ) << 3;

                    var palette0 = new Color( r0 / 255f, g0 / 255f, b0 / 255f );
                    var palette1 = new Color( r1 / 255f, g1 / 255f, b1 / 255f );
                    var palette2 = ( color0Data < color1Data )
                        ? new Color( ( ( r0 + r1 ) / 2 ) / 255f, ( ( g0 + g1 ) / 2 ) / 255f, ( ( b0 + b1 ) / 2 ) / 255f )
                        : new Color( ( ( r0 * 2 + r1 + 1 ) / 3 ) / 255f, ( ( g0 * 2 + g1 + 1 ) / 3 ) / 255f, ( ( b0 * 2 + b1 + 1 ) / 3 ) / 255f );
                    var palette3 = new Color( ( ( r1 * 2 + r0 + 1 ) / 3 ) / 255f, ( ( g1 * 2 + g0 + 1 ) / 3 ) / 255f, ( ( b1 * 2 + b0 + 1 ) / 3 ) / 255f );

                    int blockIndex = 0;
                    for( int blockY = 0; blockY < 4; blockY++ )
                    {
                        for( int blockX = 0; blockX < 4; blockX++ )
                        {
                            var colorInBlock = ( ( bitmask >> ( blockIndex * 2 ) ) & 0x03 ) switch
                            {
                                0 => palette0,
                                1 => palette1,
                                2 => palette2,
                                _ => palette3,
                            };
                            texture2DColors[( ( row * width ) + col ) + ( ( blockY * width ) + blockX )] = colorInBlock;
                            blockIndex++;
                        }
                    }
                }
            }
        }

		// from: https://github.com/Benjamin-Dobell/s3tc-dxt-decompression by Benjamin Dobell
		//
		// void DecompressBlockDXT5(): Decompresses one block of a DXT5 texture and stores the resulting pixels at the appropriate offset in 'image'.
		//
		// ulong x:                     x-coordinate of the first pixel in the block.
		// ulong y:                     y-coordinate of the first pixel in the block.
		// ulong width:                 width of the texture being decompressed.
		// ulong height:                height of the texture being decompressed.
		// char[] blockStorage:   pointer to the block to decompress.
		// ulong *image:                pointer to image where the decompressed pixel data should be stored.

		internal static void DecompressBlockDXT5(int x, int y, int width, Span<byte> blockStorage, Color[] image)
        {
            byte alpha0 = blockStorage[0];
			byte alpha1 = blockStorage[1];

            uint alphaCode1 = (uint)blockStorage[2 + 2] | ((uint)blockStorage[2 + 3] << 8) | ((uint)blockStorage[2 + 4] << 16) | ((uint)blockStorage[2 + 5] << 24);
            uint alphaCode2 = (uint)blockStorage[2 + 0] | ((uint)blockStorage[2 + 1] << 8);

			ushort color0 = (ushort)((ushort)blockStorage[ 8] | ((ushort)blockStorage[ 9] << 8));
            ushort color1 = (ushort)((ushort)blockStorage[10] | ((ushort)blockStorage[11] << 8));

            uint temp = (uint)(color0 >> 11) * 255 + 16;
			byte r0 = (byte)((temp/32 + temp)/32);
            temp = (uint)((color0 & 0x07E0) >> 5) * 255 + 32;
			byte g0 = (byte)((temp/64 + temp)/64);
            temp = (uint)(color0 & 0x001F) * 255 + 16;
			byte b0 = (byte)((temp/32 + temp)/32);

            temp = (uint)(color1 >> 11) * 255 + 16;
			byte r1 = (byte)((temp/32 + temp)/32);
            temp = (uint)((color1 & 0x07E0) >> 5) * 255 + 32;
			byte g1 = (byte)((temp/64 + temp)/64);
            temp = (uint)(color1 & 0x001F) * 255 + 16;
			byte b1 = (byte)((temp/32 + temp)/32);

			uint code = (uint)blockStorage[12] | ((uint)blockStorage[13] << 8) | ((uint)blockStorage[14] << 16) | ((uint)blockStorage[15] << 24);

			// The block's eight alphas and four colours, each worked out once rather than for every pixel that has it
			Span<float> alphas = stackalloc float[8];
			for (uint alphaCode = 0; alphaCode < 8; alphaCode++)
			{
				byte finalAlpha;
				if (alphaCode == 0)
				{
					finalAlpha = alpha0;
				}
				else if (alphaCode == 1)
				{
					finalAlpha = alpha1;
				}
				else
				{
					if (alpha0 > alpha1)
					{
						finalAlpha = (byte)(((8 - alphaCode) * alpha0 + (alphaCode - 1) * alpha1) / 7);
					}
					else
					{
						if (alphaCode == 6)
							finalAlpha = 0;
						else if (alphaCode == 7)
							finalAlpha = 255;
						else
							finalAlpha = (byte)(((6 - alphaCode) * alpha0 + (alphaCode - 1) * alpha1) / 5);
					}
				}
				alphas[(int)alphaCode] = finalAlpha / 255.0f;
			}
			Span<float> reds   = stackalloc float[4] { r0 / 255.0f, r1 / 255.0f, ((2 * r0 + r1) / 3) / 255.0f, ((r0 + 2 * r1) / 3) / 255.0f };
			Span<float> greens = stackalloc float[4] { g0 / 255.0f, g1 / 255.0f, ((2 * g0 + g1) / 3) / 255.0f, ((g0 + 2 * g1) / 3) / 255.0f };
			Span<float> blues  = stackalloc float[4] { b0 / 255.0f, b1 / 255.0f, ((2 * b0 + b1) / 3) / 255.0f, ((b0 + 2 * b1) / 3) / 255.0f };

            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 4; i++)
                {
                    int alphaCodeIndex = 3*(4*j+i);
                    uint alphaCode;

                    if (alphaCodeIndex <= 12)
                    {
                        alphaCode = (alphaCode2 >> alphaCodeIndex) & 0x07;
                    }
                    else if (alphaCodeIndex == 15)
                    {
                        alphaCode = (alphaCode2 >> 15) | ((alphaCode1 << 1) & 0x06);
                    }
                    else // alphaCodeIndex >= 18 && alphaCodeIndex <= 45
                    {
                        alphaCode = (alphaCode1 >> (alphaCodeIndex - 16)) & 0x07;
                    }

                    int colorCode = (int)((code >> 2 * (4 * j + i)) & 0x03);

                    if (x + i < width)
                        image[(y + j)*width + (x + i)] = new Color(reds[colorCode], greens[colorCode], blues[colorCode], alphas[(int)alphaCode]);
                }
            }
        }

        /// <summary>
        /// Turn raw DXT5 bytes to a Color array that can be used in a Texture2D.
        /// </summary>
        /// <param name="data">Raw image byte data</param>
        /// <param name="width">Expected width of the image</param>
        /// <param name="height">Expected height of the image</param>
        /// <returns>Pixel color data</returns>
        public static Color[] DecompressDXT5( Stream data, int width, int height, uint dataSize)
		{
			// All of it: one Read may hand over less than asked for, and what it left was decoded as zeros
			var byteBuffer = ReadImageBytes(data, dataSize, 0, out _);

			int blockCountX = (width + 3) / 4;
			int blockCountY = (height + 3) / 4;
			if (BurstDecodeEnabled && width % 4 == 0 && height % 4 == 0 && byteBuffer.Length >= (long)blockCountX * blockCountY * 16)
				return DxtBlockJobs.Decode(DxtBlockJobs.Kind.DXT5, byteBuffer, width, height);
			int blockWidth = (width < 4) ? width : 4;
			int blockHeight = (height < 4) ? height : 4;

			Color[] pixels = new Color[width * height];

			int offset = 0;
			for (int j = 0; j < blockCountY; j++)
			{
                for (int i = 0; i < blockCountX; i++) 
                    DecompressBlockDXT5(i * 4, j * 4, width, byteBuffer.AsSpan(offset + (i * 16), 16), pixels);
                offset += blockCountX * 16;
			}
			return pixels;
		}

        /// <summary>
        /// This enum only holds the formats this helper class can read.
        /// </summary>
        public enum TextureFormat
		{
			I8,     // luminance
			A8,     // alpha alone
			IA88,   // luminance and alpha
			RG88,   // two plain channels
			BGR888,
            BGRA8888,
            DXT1,
            DXT3,
            DXT5,
            RGBA16161616F,
        }
    }
}
