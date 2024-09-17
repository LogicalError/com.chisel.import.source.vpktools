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
            for( uint col = 0; col < width; col++ )
            {
                for( uint row = 0; row < ( height / 2 ); row++ )
                {
                    uint  currentRowIndex  = row                  * width;
                    uint  oppositeRowIndex = ( height - row - 1 ) * width;
					(colors[oppositeRowIndex + col], colors[currentRowIndex  + col]) = (colors[currentRowIndex  + col], colors[oppositeRowIndex + col]);
				}
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
			if (textureFormat == TextureFormat.R8)
				colors = DecompressR8(data, width, height);
			if ( textureFormat == TextureFormat.RG88)
                colors = DecompressRG88( data, width, height );
            if( textureFormat == TextureFormat.BGR888 )
                colors = DecompressBGR888( data, width, height );
            else if( textureFormat == TextureFormat.BGRA8888 )
                colors = DecompressBGRA8888( data, width, height );
            else if( textureFormat == TextureFormat.DXT1 )
                colors = DecompressDXT1( data, width, height, dataSize);
            else if( textureFormat == TextureFormat.DXT3 )
                colors = DecompressDXT3( data, width, height, dataSize);
            else if( textureFormat == TextureFormat.DXT5 )
                colors = DecompressDXT5( data, width, height, dataSize);
            else
                Debug.LogError( "Texture2DHelpers: Texture format not supported " + textureFormat );

            return colors;
		}

		/// <summary>
		/// Turn raw R8 bytes to a Color array that can be used in a Texture2D.
		/// </summary>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressR8(Stream data, ushort width, ushort height)
		{
			Color[] texture2DColors = new Color[width * height];

			bool exceededArray = false;
			for (int row = 0; row < height; row++)
			{
				for (int col = 0; col < width; col++)
				{
					byte red = data.ReadValueByte();

					int flattenedIndex = row * width + col;
					if (flattenedIndex < texture2DColors.Length)
						texture2DColors[flattenedIndex] = new Color(((float)red) / byte.MaxValue, 1.0f, 1.0f);
					else
					{
						Debug.LogError("BGR888: Exceeded expected texture size");
						exceededArray = true;
						break;
					}
				}

				if (exceededArray)
					break;
			}

			return texture2DColors;
		}

		/// <summary>
		/// Turn raw RG88 bytes to a Color array that can be used in a Texture2D.
		/// </summary>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressRG88(Stream data, ushort width, ushort height)
		{
			Color[] texture2DColors = new Color[width * height];

			bool exceededArray = false;
			for (int row = 0; row < height; row++)
			{
				for (int col = 0; col < width; col++)
				{
					byte red = data.ReadValueByte();
					byte green = data.ReadValueByte();

					int flattenedIndex = row * width + col;
					if (flattenedIndex < texture2DColors.Length)
						texture2DColors[flattenedIndex] = new Color(((float)red) / byte.MaxValue, ((float)green) / byte.MaxValue, 1.0f);
					else
					{
						Debug.LogError("BGR888: Exceeded expected texture size");
						exceededArray = true;
						break;
					}
				}

				if (exceededArray)
					break;
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

            bool exceededArray = false;
            for( int row = 0; row < height; row++ )
            {
                for( int col = 0; col < width; col++ )
                {
                    byte blue  = data.ReadValueByte();
                    byte green = data.ReadValueByte();
                    byte red   = data.ReadValueByte();

                    int flattenedIndex = row * width + col;
                    if( flattenedIndex < texture2DColors.Length )
                        texture2DColors[flattenedIndex] = new Color( ( (float) red ) / byte.MaxValue, ( (float) green ) / byte.MaxValue, ( (float) blue ) / byte.MaxValue );
                    else
                    {
                        Debug.LogError( "BGR888: Exceeded expected texture size" );
                        exceededArray = true;
                        break;
                    }
                }

                if( exceededArray )
                    break;
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

            bool exceededArray = false;
            for( int row = 0; row < height; row++ )
            {
                for( int col = 0; col < width; col++ )
                {
                    byte blue  = data.ReadValueByte();
                    byte green = data.ReadValueByte();
                    byte red   = data.ReadValueByte();
                    byte alpha = data.ReadValueByte();

                    int flattenedIndex = row * width + col;
                    if( flattenedIndex < texture2DColors.Length )
                        texture2DColors[flattenedIndex] = new Color( ( (float) red )   / byte.MaxValue, ( (float) green ) / byte.MaxValue, ( (float) blue ) / byte.MaxValue,
                                                                     ( (float) alpha ) / byte.MaxValue );
                    else
                    {
                        Debug.LogError( "BGRA8888: Exceeded expected texture size" );
                        exceededArray = true;
                        break;
                    }
                }

                if( exceededArray )
                    break;
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
            Color[] texture2DColors = new Color[width * height];

            for( int row = 0; row < height; row += 4 )
            {
                for( int col = 0; col < width; col += 4 )
                {
                    var color0Data = DataParser.ReadUShort( data );
					var color1Data = DataParser.ReadUShort( data );
					var bitmask    = DataParser.ReadUInt( data );

                    int[] colors0 = new int[] { ( ( color0Data >> 11 ) & 0x1F ) << 3, ( ( color0Data >> 5 ) & 0x3F ) << 2, ( color0Data & 0x1F ) << 3 };
                    int[] colors1 = new int[] { ( ( color1Data >> 11 ) & 0x1F ) << 3, ( ( color1Data >> 5 ) & 0x3F ) << 2, ( color1Data & 0x1F ) << 3 };

                    Color[] colorPalette = new Color[]
                    {
                        new( colors0[0]                                  / 255f, colors0[1]                                  / 255f, colors0[2]                                  / 255f ),
                        new( colors1[0]                                  / 255f, colors1[1]                                  / 255f, colors1[2]                                  / 255f ),
                        new( ( ( colors0[0] * 2 + colors1[0] + 1 ) / 3 ) / 255f, ( ( colors0[1] * 2 + colors1[1] + 1 ) / 3 ) / 255f, ( ( colors0[2] * 2 + colors1[2] + 1 ) / 3 ) / 255f ),
                        new( ( ( colors1[0] * 2 + colors0[0] + 1 ) / 3 ) / 255f, ( ( colors1[1] * 2 + colors0[1] + 1 ) / 3 ) / 255f, ( ( colors1[2] * 2 + colors0[2] + 1 ) / 3 ) / 255f )
                    };

                    if( color0Data < color1Data )
                    {
                        colorPalette[2] = new Color( ( ( colors0[0] + colors1[0] ) / 2 ) / 255f, ( ( colors0[1] + colors1[1] ) / 2 ) / 255f, ( ( colors0[2] + colors1[2] ) / 2 ) / 255f );
                        colorPalette[3] = new Color( ( ( colors1[0] * 2 + colors0[0] + 1 ) / 3 ) / 255f, ( ( colors1[1] * 2 + colors0[1] + 1 ) / 3 ) / 255f,
                                                     ( ( colors1[2] * 2 + colors0[2] + 1 ) / 3 ) / 255f );
                    }

                    int blockIndex = 0;
                    for( int blockY = 0; blockY < 4; blockY++ )
                    {
                        for( int blockX = 0; blockX < 4; blockX++ )
                        {
                            Color colorInBlock = colorPalette[( bitmask & ( 0x03 << blockIndex * 2 ) ) >> blockIndex * 2];
                            texture2DColors[( ( row * width ) + col ) + ( ( blockY * width ) + blockX )] = colorInBlock;
                            blockIndex++;
                        }
                    }
                }
            }

            return texture2DColors.ToArray();
		}

		/// <summary>
		/// Turn raw DXT3 bytes to a Color array that can be used in a Texture2D.
		/// </summary>
		/// <param name="data">Raw image byte data</param>
		/// <param name="width">Expected width of the image</param>
		/// <param name="height">Expected height of the image</param>
		/// <returns>Pixel color data</returns>
		public static Color[] DecompressDXT3( Stream data, ushort width, ushort height, uint dataSize)
		{
            Color[] texture2DColors = new Color[width * height];

            for( int row = 0; row < height; row += 4 )
            {
                for( int col = 0; col < width; col += 4 )
                {
                    //data.Seek(8, SeekOrigin.Current); //not sure if this is correct or not, had it before, but I think I never got to test a DXT3 image. I commented it out because it looks wrong
                    var color0Data = DataParser.ReadUShort( data );
                    var color1Data = DataParser.ReadUShort( data );
                    var bitmask    = DataParser.ReadUInt( data );

                    int[] colors0 = new int[] { ( ( color0Data >> 11 ) & 0x1F ) << 3, ( ( color0Data >> 5 ) & 0x3F ) << 2, ( color0Data & 0x1F ) << 3 };
                    int[] colors1 = new int[] { ( ( color1Data >> 11 ) & 0x1F ) << 3, ( ( color1Data >> 5 ) & 0x3F ) << 2, ( color1Data & 0x1F ) << 3 };

                    Color[] colorPalette = new Color[]
                    {
                        new( colors0[0]                                  / 255f, colors0[1]                                  / 255f, colors0[2]                                  / 255f ),
                        new( colors1[0]                                  / 255f, colors1[1]                                  / 255f, colors1[2]                                  / 255f ),
                        new( ( ( colors0[0] * 2 + colors1[0] + 1 ) / 3 ) / 255f, ( ( colors0[1] * 2 + colors1[1] + 1 ) / 3 ) / 255f, ( ( colors0[2] * 2 + colors1[2] + 1 ) / 3 ) / 255f ),
                        new( ( ( colors1[0] * 2 + colors0[0] + 1 ) / 3 ) / 255f, ( ( colors1[1] * 2 + colors0[1] + 1 ) / 3 ) / 255f, ( ( colors1[2] * 2 + colors0[2] + 1 ) / 3 ) / 255f )
                    };

                    if( color0Data < color1Data )
                    {
                        colorPalette[2] = new Color( ( ( colors0[0] + colors1[0] ) / 2 ) / 255f, ( ( colors0[1] + colors1[1] ) / 2 ) / 255f, ( ( colors0[2] + colors1[2] ) / 2 ) / 255f );
                        colorPalette[3] = new Color( ( ( colors1[0] * 2 + colors0[0] + 1 ) / 3 ) / 255f, ( ( colors1[1] * 2 + colors0[1] + 1 ) / 3 ) / 255f,
                                                     ( ( colors1[2] * 2 + colors0[2] + 1 ) / 3 ) / 255f );
                    }

                    int blockIndex = 0;
                    for( int blockY = 0; blockY < 4; blockY++ )
                    {
                        for( int blockX = 0; blockX < 4; blockX++ )
                        {
                            Color colorInBlock = colorPalette[( bitmask & ( 0x03 << blockIndex * 2 ) ) >> blockIndex * 2];
                            texture2DColors[( ( row * width ) + col ) + ( ( blockY * width ) + blockX )] = colorInBlock;
                            blockIndex++;
                        }
                    }
                }
            }

            return texture2DColors.ToArray();
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

                    byte colorCode = (byte)((code >> 2 * (4 * j + i)) & 0x03);

                    Color finalColor = Color.black;
                    switch (colorCode)
                    {
                        case 0:
                            finalColor = new Color(r0 / 255.0f, g0 / 255.0f, b0 / 255.0f, finalAlpha / 255.0f);
                            break;
                        case 1:
                            finalColor = new Color(r1 / 255.0f, g1 / 255.0f, b1 / 255.0f, finalAlpha / 255.0f);
                            break;
                        case 2:
                            finalColor = new Color(((2 * r0 + r1) / 3) / 255.0f, ((2 * g0 + g1) / 3) / 255.0f, ((2 * b0 + b1) / 3) / 255.0f, finalAlpha / 255.0f);
                            break;
                        case 3:
                            finalColor = new Color(((r0 + 2 * r1) / 3) / 255.0f, ((g0 + 2 * g1) / 3) / 255.0f, ((b0 + 2 * b1) / 3) / 255.0f, finalAlpha / 255.0f);
                            break;
                    }

                    if (x + i < width)
                        image[(y + j)*width + (x + i)] = finalColor;
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
			var byteBuffer = new byte[dataSize];
			data.Read(byteBuffer, 0, byteBuffer.Length);

			int blockCountX = (width + 3) / 4;
			int blockCountY = (height + 3) / 4;
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
			R8,
			RG88,
			BGR888,
            BGRA8888,
            DXT1,
            DXT3,
            DXT5,
        }
    }
}
