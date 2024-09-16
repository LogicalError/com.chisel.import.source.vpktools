using System.IO;

using Color = UnityEngine.Color;
using Debug = UnityEngine.Debug;
using Mathf = UnityEngine.Mathf;

namespace Chisel.Import.Source.VPKTools
{
    public class VTF
    {
        public static bool averageTextures = false;
        public static int  maxTextureSize  = 4096;

		public Frame[] Frames       { get; private set; }
		public  int Width           { get; private set; }
        public  int Height          { get; private set; }
		public VTFImageFlag Flags   { get; private set; }
		public bool HasAlpha        { get; private set; }


        public struct Frame
        {
            public Color[] Pixels;
		}

        public static VTF Read( Stream stream )
        {
			var frames = LoadVTFFile(stream, out int width, out int height, out var flags, out var hasAlpha);
			
            // TODO: why? when we export textures and import them through unity, unity can handle this for us?
			/*
			if (pixels != null)
            {
                if (averageTextures)
                {
                    pixels = MakePlain( AverageTexture( pixels ), 4, 4 );
                    width  = 4;
                    height = 4;
                } else
                    pixels = DecreaseTextureSize( pixels, width, height, maxTextureSize, out width, out height );
            }*/

			return new VTF
			{
				Frames = frames,
				Width  = width,
				Height = height,
				Flags = flags,
				HasAlpha = hasAlpha
			};
        }

#if false
        public static Color[] DecreaseTextureSize( Color[] pixels, int origWidth, int origHeight, int maxSize, out int decreasedWidth, out int decreasedHeight )
        {
            Color[] decreased = pixels;
            decreasedWidth  = origWidth;
            decreasedHeight = origHeight;
            if( Mathf.Max( origWidth, origHeight ) > maxSize )
            {
                float ratio = Mathf.Max( origWidth, origHeight ) / (float) maxSize;
                decreasedWidth  = (int) ( origWidth              / ratio );
                decreasedHeight = (int) ( origHeight             / ratio );

                decreased = DecreaseTextureSize( pixels, origWidth, origHeight, decreasedWidth, decreasedHeight );
            }

            return decreased;
        }

        public static Color[] DecreaseTextureSize( Color[] pixels, int origWidth, int origHeight, int newWidth, int newHeight )
        {
            Color[] scaledTexture = null;
            if( newWidth < origWidth && newHeight < origHeight )
            {
                int divX = Mathf.FloorToInt( (float) Mathf.Max( origWidth,  newWidth )  / Mathf.Min( origWidth,  newWidth ) );
                int divY = Mathf.FloorToInt( (float) Mathf.Max( origHeight, newHeight ) / Mathf.Min( origHeight, newHeight ) );

                scaledTexture = new Color[newWidth * newHeight];
                for( int col = 0; col < newWidth; col++ )
                {
                    for( int row = 0; row < newHeight; row++ )
                    {
                        float red        = 0, green = 0, blue = 0, alpha = 0;
                        int   pixelCount = 0;
                        for( int x = -( divX - 1 ); x <= ( divX - 1 ); x++ )
                        {
                            for( int y = -( divY - 1 ); y <= ( divY - 1 ); y++ )
                            {
                                int mappedCol   = ( col + 0 ) * divX;
                                int mappedRow   = ( row + 0 ) * divY;
                                int mappedIndex = ( ( mappedRow + y ) * origWidth ) + mappedCol + x;
                                if( mappedIndex >= 0 && mappedIndex < pixels.Length )
                                {
                                    Color currentColor = pixels[mappedIndex];
                                    red   += currentColor.r;
                                    green += currentColor.g;
                                    blue  += currentColor.b;
                                    alpha += currentColor.a;
                                    pixelCount++;
                                }
                            }
                        }

                        Color avgColor = new Color( red / pixelCount, green / pixelCount, blue / pixelCount, alpha / pixelCount );
                        scaledTexture[( row             * newWidth ) + col] = avgColor;
                    }
                }
            }

            return scaledTexture;
        }
#endif

        public static Color AverageTexture(Color[] pixels)
        {
            Color allColorsInOne = new();
            foreach (Color color in pixels)
            {
                allColorsInOne.r += color.r;
                allColorsInOne.g += color.g;
                allColorsInOne.b += color.b;
                allColorsInOne.a += color.a;
            }

            allColorsInOne.r /= pixels.Length;
            allColorsInOne.g /= pixels.Length;
            allColorsInOne.b /= pixels.Length;
            allColorsInOne.a /= pixels.Length;

            return allColorsInOne;
        }

        public static Color[] MakePlain( Color mainColor, int width, int height )
        {
            Color[] plain = new Color[width * height];
            for( int i = 0; i < plain.Length; i++ )
                plain[i] = mainColor;
            return plain;
        }

        public static Frame[] LoadVTFFile(Stream stream, out int width, out int height, out VTFImageFlag flags, out bool hasAlpha)
		{
			width = 0;
			height = 0;
			flags = (VTFImageFlag)0;
            hasAlpha = false;
			Frame[] frames = null;
			if ( stream != null )
            {
                int signature = DataParser.ReadInt( stream );
                if( signature == VTFHeader.signature )
                {
                    #region Read Header
                    VTFHeader vtfHeader;
                    uint[]    version = new uint[] { DataParser.ReadUInt( stream ), DataParser.ReadUInt( stream ) };
                    vtfHeader.version       = ( version[0] ) + ( version[1] / 10f );
                    vtfHeader.headerSize    = DataParser.ReadUInt( stream );
                    vtfHeader.width         = DataParser.ReadUShort( stream );
                    vtfHeader.height        = DataParser.ReadUShort( stream );
                    vtfHeader.flags         = (VTFImageFlag)DataParser.ReadUInt( stream );
                    vtfHeader.frames        = DataParser.ReadUShort( stream );
                    vtfHeader.firstFrame    = DataParser.ReadUShort( stream );
                    vtfHeader.padding0      = DataParser.ReadUInt( stream );
                    vtfHeader.reflectivity  = new float[] { DataParser.ReadFloat( stream ), DataParser.ReadFloat( stream ), DataParser.ReadFloat( stream ) };
                    vtfHeader.padding1      = DataParser.ReadUInt(stream);
					vtfHeader.bumpmapScale       = DataParser.ReadFloat( stream );
                    vtfHeader.highResImageFormat = (VTFImageFormat) DataParser.ReadUInt( stream );
                    vtfHeader.mipmapCount        = DataParser.ReadByte( stream );
                    vtfHeader.lowResImageFormat  = (VTFImageFormat) DataParser.ReadUInt( stream );
                    vtfHeader.lowResImageWidth   = DataParser.ReadByte( stream );
                    vtfHeader.lowResImageHeight  = DataParser.ReadByte( stream );

                    vtfHeader.depth         = 1;
                    vtfHeader.resourceCount = 0;
                    vtfHeader.resources     = new VTFResource[0];

                    flags = vtfHeader.flags;


					if ( vtfHeader.version >= 7.2f )
                    {
                        vtfHeader.depth = DataParser.ReadUShort( stream );

                        if( vtfHeader.version >= 7.3 )
                        {
                            vtfHeader.padding2 = new byte[3];
                            stream.Read( vtfHeader.padding2, 0, 3 );
                            vtfHeader.resourceCount = DataParser.ReadUInt( stream );

                            if( vtfHeader.version >= 7.4 )
                            {
                                vtfHeader.padding3 = new byte[8];
                                stream.Read( vtfHeader.padding3, 0, 8 );
                                vtfHeader.resources = new VTFResource[vtfHeader.resourceCount];
                                for( int i = 0; i < vtfHeader.resources.Length; i++ )
                                {
                                    vtfHeader.resources[i].type = DataParser.ReadUInt( stream );
                                    vtfHeader.resources[i].data = DataParser.ReadUInt( stream );
                                }
                            }
                        }
                    }
                    #endregion

                    if (vtfHeader.frames == 0)
					{
						Debug.LogError("SourceTexture: Image has zero frames");
						return null;
                    }

					frames = new Frame[vtfHeader.frames];

					int thumbnailBufferSize = 0;
                    int imageBufferSize = (int) ComputeImageBufferSize( vtfHeader.width, vtfHeader.height, vtfHeader.depth, vtfHeader.mipmapCount, vtfHeader.highResImageFormat ) * vtfHeader.frames;
                    if( vtfHeader.lowResImageFormat != VTFImageFormat.IMAGE_FORMAT_NONE )
                        thumbnailBufferSize = (int) ComputeImageBufferSize( vtfHeader.lowResImageWidth, vtfHeader.lowResImageHeight, 1, vtfHeader.lowResImageFormat );

                    int thumbnailBufferOffset = 0, imageBufferOffset = 0;

                    #region Read Resource Directories
                    if( vtfHeader.resources.Length > 0 )
                    {
                        for( int i = 0; i < vtfHeader.resources.Length; i++ )
                        {
                            if( (VTFResourceEntryType) vtfHeader.resources[i].type == VTFResourceEntryType.VTF_LEGACY_RSRC_LOW_RES_IMAGE )
                                thumbnailBufferOffset = (int) vtfHeader.resources[i].data;
                            if( (VTFResourceEntryType) vtfHeader.resources[i].type == VTFResourceEntryType.VTF_LEGACY_RSRC_IMAGE )
                                imageBufferOffset = (int) vtfHeader.resources[i].data;
                        }
                    } else
                    {
                        thumbnailBufferOffset = (int) vtfHeader.headerSize;
                        imageBufferOffset     = thumbnailBufferOffset + thumbnailBufferSize;
                    }
                    #endregion

                    if( vtfHeader.highResImageFormat != VTFImageFormat.IMAGE_FORMAT_NONE )
					{
                        uint mipmapSize = ComputeImageBufferSize(vtfHeader.width, vtfHeader.height, vtfHeader.depth, vtfHeader.highResImageFormat);

						width = vtfHeader.width;
						height = vtfHeader.height;
						for (int i = 0; i < vtfHeader.frames; i++)
                        {
                            uint mipmapBufferOffset = mipmapSize * (uint)(vtfHeader.frames - i);

                            stream.Position = stream.Length - mipmapBufferOffset;
                            frames[i].Pixels = DecompressImage(stream, vtfHeader.width, vtfHeader.height, mipmapSize, vtfHeader.highResImageFormat);
						}


						hasAlpha = false;
                        if (vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_A8 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_ABGR8888 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_ARGB8888 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_BGRA8888 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_RGBA8888 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_BGRA4444 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_BGRA5551 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_DXT1_ONEBITALPHA ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_IA88 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_RGBA16161616 ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_RGBA16161616F ||
                            vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_RGBA32323232F ||
							vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_DXT1 ||
							vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_DXT3 ||
							vtfHeader.highResImageFormat == VTFImageFormat.IMAGE_FORMAT_DXT5)
                        {
                            for (int f = 0; f < frames.Length; f++)
                            {
                                var pixels = frames[f].Pixels;
                                for (int i = 0; i < pixels.Length; i++)
                                {
                                    var pixel = pixels[i];
                                    if (pixel.a != 1.0f)
                                    {
                                        hasAlpha = true;
                                        break;
                                    }
                                }
                            }
                        }
					} else
                        Debug.LogError( "SourceTexture: Image format given was none" );
                } else
                    Debug.LogError( "SourceTexture: Signature mismatch " + signature + " != " + VTFHeader.signature );
            } else
                Debug.LogError( "SourceTexture: Missing VTF data" );

            return frames;
        }

        private static Color[] DecompressImage( Stream data, ushort width, ushort height, uint dataSize, VTFImageFormat imageFormat )
		{
			Texture2DHelpers.TextureFormat format;
            switch(imageFormat)
            {
                case VTFImageFormat.IMAGE_FORMAT_DXT1:
                case VTFImageFormat.IMAGE_FORMAT_DXT1_ONEBITALPHA:  format = Texture2DHelpers.TextureFormat.DXT1; break;

                case VTFImageFormat.IMAGE_FORMAT_DXT3:  format = Texture2DHelpers.TextureFormat.DXT3; break;
                case VTFImageFormat.IMAGE_FORMAT_DXT5:  format = Texture2DHelpers.TextureFormat.DXT5; break;
				case VTFImageFormat.IMAGE_FORMAT_BGR888: format = Texture2DHelpers.TextureFormat.BGR888; break;
				case VTFImageFormat.IMAGE_FORMAT_RGB888: format = Texture2DHelpers.TextureFormat.BGR888; break;

				case VTFImageFormat.IMAGE_FORMAT_BGRA8888: 
                case VTFImageFormat.IMAGE_FORMAT_BGRX8888: format = Texture2DHelpers.TextureFormat.BGRA8888; break;
				case VTFImageFormat.IMAGE_FORMAT_RGBA8888: format = Texture2DHelpers.TextureFormat.BGRA8888; break;
				case VTFImageFormat.IMAGE_FORMAT_ABGR8888: format = Texture2DHelpers.TextureFormat.BGRA8888; break;
				case VTFImageFormat.IMAGE_FORMAT_ARGB8888: format = Texture2DHelpers.TextureFormat.BGRA8888; break;
                default:
                {
                    format = Texture2DHelpers.TextureFormat.BGR888;
                    Debug.LogError( "SourceTexture: Unsupported format " + imageFormat + ", will read as " + format );
                    break;
                }
            }

			Color[] vtfColors = Texture2DHelpers.DecompressRawBytes( data, width, height, dataSize, format );
            Texture2DHelpers.FlipVertical( vtfColors, width, height );

            
			if (imageFormat == VTFImageFormat.IMAGE_FORMAT_ABGR8888)
			{
				for (int i = 0; i < vtfColors.Length; i++)
                {
                    (vtfColors[i].a, vtfColors[i].b, vtfColors[i].g, vtfColors[i].r) = (vtfColors[i].b, vtfColors[i].g, vtfColors[i].r, vtfColors[i].a);
				}
			} else
			if (imageFormat == VTFImageFormat.IMAGE_FORMAT_RGBA8888)
			{
				for (int i = 0; i < vtfColors.Length; i++)
                {
                    (vtfColors[i].r, vtfColors[i].g, vtfColors[i].b, vtfColors[i].a) = (vtfColors[i].b, vtfColors[i].g, vtfColors[i].r, vtfColors[i].a);
				}
			} else
			if (imageFormat == VTFImageFormat.IMAGE_FORMAT_ARGB8888)
			{
				for (int i = 0; i < vtfColors.Length; i++)
                {
                    (vtfColors[i].a, vtfColors[i].r, vtfColors[i].g, vtfColors[i].b) = (vtfColors[i].b, vtfColors[i].g, vtfColors[i].r, vtfColors[i].a);
				}
			} else
			if (imageFormat == VTFImageFormat.IMAGE_FORMAT_RGB888)
			{
				for (int i = 0; i < vtfColors.Length; i++)
                {
                    (vtfColors[i].r, vtfColors[i].g, vtfColors[i].b) = (vtfColors[i].b, vtfColors[i].g, vtfColors[i].r);
				}
			}


			return vtfColors;
        }

        private static uint ComputeImageBufferSize( uint width, uint height, uint depth, VTFImageFormat imageFormat )
        {
            uint tempWidth = width, tempHeight = height;

            if( imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT1 || imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT1_ONEBITALPHA )
            {
                if( tempWidth < 4 && tempWidth > 0 )
                    tempWidth = 4;

                if( tempHeight < 4 && tempHeight > 0 )
                    tempHeight = 4;

				return ( ( tempWidth + 3 ) / 4 ) * ( ( tempHeight + 3 ) / 4 ) * 8 * depth;
            }
            else if( imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT3 || imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT5 )
            {
                if( tempWidth < 4 && tempWidth > 0 )
                    tempWidth = 4;

                if( tempHeight < 4 && tempHeight > 0 )
                    tempHeight = 4;

                return ( ( tempWidth + 3 ) / 4 ) * ( ( tempHeight + 3 ) / 4 ) * 16 * depth;
            }
            else return (uint) ( tempWidth * tempHeight * depth * VTFImageConvertInfo[(int) imageFormat, (int) VTFImageConvertInfoIndex.bytesPerPixel] );
        }

        private static uint ComputeImageBufferSize( uint width, uint height, uint depth, uint mipmaps, VTFImageFormat imageFormat )
        {
            uint uiImageSize = 0, tempWidth = width, tempHeight = height;

            if( tempWidth > 0 && tempHeight > 0 && depth > 0 )
            {
                for( int i = 0; i < mipmaps; i++ )
                {
                    uiImageSize += ComputeImageBufferSize( tempWidth, tempHeight, depth, imageFormat );

                    tempWidth  >>= 1;
                    tempHeight >>= 1;
                    depth      >>= 1;

                    if( tempWidth < 1 )
                        tempWidth = 1;

                    if( tempHeight < 1 )
                        tempHeight = 1;

                    if( depth < 1 )
                        depth = 1;
                }
            }

            return uiImageSize;
        }

        private static void ComputeMipmapDimensions( uint width, uint height, uint depth, uint mipmapLevel, out uint mipmapWidth, out uint mipmapHeight, out uint mipmapDepth )
        {
            // work out the width/height by taking the orignal dimension
            // and bit shifting them down uiMipmapLevel times
            mipmapWidth  = width  >> (int) mipmapLevel;
            mipmapHeight = height >> (int) mipmapLevel;
            mipmapDepth  = depth  >> (int) mipmapLevel;

            // stop the dimension being less than 1 x 1
            if( mipmapWidth < 1 )
                mipmapWidth = 1;

            if( mipmapHeight < 1 )
                mipmapHeight = 1;

            if( mipmapDepth < 1 )
                mipmapDepth = 1;
        }

        private static uint ComputeMipmapSize( uint width, uint height, uint depth, uint mipmapLevel, VTFImageFormat ImageFormat )
        {
			// figure out the width/height of this MIP level
			ComputeMipmapDimensions(width, height, depth, mipmapLevel, out uint uiMipmapWidth, out uint uiMipmapHeight, out uint uiMipmapDepth);

			// return the memory requirements
			return ComputeImageBufferSize( uiMipmapWidth, uiMipmapHeight, uiMipmapDepth, ImageFormat );
        }

        #region Image Convert Info

        enum VTFImageConvertInfoIndex
        {
            bitsPerPixel,      // Format bits per color.
            bytesPerPixel,     // Format bytes per pixel.
            redBitsPerPixel,   // Format conversion red bits per pixel.  0 for N/A.
            greenBitsPerPixel, // Format conversion green bits per pixel.  0 for N/A.
            blueBitsPerPixel,  // Format conversion blue bits per pixel.  0 for N/A.
            alphaBitsPerPixel, // Format conversion alpha bits per pixel.  0 for N/A.
            redIndex,          // "Red" index.
            greenIndex,        // "Green" index.
            blueIndex,         // "Blue" index.
            alphaIndex,        // "Alpha" index.
        }

        static readonly short[,] VTFImageConvertInfo = new short[,]
        {
                { 32, 4, 8, 8, 8, 8, 0, 1, 2, 3 },
                { 32, 4, 8, 8, 8, 8, 3, 2, 1, 0 },
                { 24, 3, 8, 8, 8, 0, 0, 1, 2, -1 },
                { 24, 3, 8, 8, 8, 0, 2, 1, 0, -1 },
                { 16, 2, 5, 6, 5, 0, 0, 1, 2, -1 },
                { 8, 1, 8, 8, 8, 0, 0, -1, -1, -1 },
                { 16, 2, 8, 8, 8, 8, 0, -1, -1, 1 },
                { 8, 1, 0, 0, 0, 0, -1, -1, -1, -1 },
                { 8, 1, 0, 0, 0, 8, -1, -1, -1, 0 },
                { 24, 3, 8, 8, 8, 8, 0, 1, 2, -1 },
                { 24, 3, 8, 8, 8, 8, 2, 1, 0, -1 },
                { 32, 4, 8, 8, 8, 8, 3, 0, 1, 2 },
                { 32, 4, 8, 8, 8, 8, 2, 1, 0, 3 },
                { 4, 0, 0, 0, 0, 0, -1, -1, -1, -1 },
                { 8, 0, 0, 0, 0, 8, -1, -1, -1, -1 },
                { 8, 0, 0, 0, 0, 8, -1, -1, -1, -1 },
                { 32, 4, 8, 8, 8, 0, 2, 1, 0, -1 },
                { 16, 2, 5, 6, 5, 0, 2, 1, 0, -1 },
                { 16, 2, 5, 5, 5, 0, 2, 1, 0, -1 },
                { 16, 2, 4, 4, 4, 4, 2, 1, 0, 3 },
                { 4, 0, 0, 0, 0, 1, -1, -1, -1, -1 },
                { 16, 2, 5, 5, 5, 1, 2, 1, 0, 3 },
                { 16, 2, 8, 8, 0, 0, 0, 1, -1, -1 },
                { 32, 4, 8, 8, 8, 8, 0, 1, 2, 3 },
                { 64, 8, 16, 16, 16, 16, 0, 1, 2, 3 },
                { 64, 8, 16, 16, 16, 16, 0, 1, 2, 3 },
                { 32, 4, 8, 8, 8, 8, 0, 1, 2, 3 },
                { 32, 4, 32, 0, 0, 0, 0, -1, -1, -1 },
                { 96, 12, 32, 32, 32, 0, 0, 1, 2, -1 },
                { 128, 16, 32, 32, 32, 32, 0, 1, 2, 3 },
                { 16, 2, 16, 0, 0, 0, 0, -1, -1, -1 },
                { 24, 3, 24, 0, 0, 0, 0, -1, -1, -1 },
                { 32, 4, 0, 0, 0, 0, -1, -1, -1, -1 },
                { 24, 3, 0, 0, 0, 0, -1, -1, -1, -1 },
                { 16, 2, 16, 0, 0, 0, 0, -1, -1, -1 },
                { 24, 3, 24, 0, 0, 0, 0, -1, -1, -1 },
                { 32, 4, 0, 0, 0, 0, -1, -1, -1, -1 },
                { 4, 0, 0, 0, 0, 0, -1, -1, -1, -1 },
                { 8, 0, 0, 0, 0, 0, -1, -1, -1, -1 }
        };

        #endregion
    }
}
