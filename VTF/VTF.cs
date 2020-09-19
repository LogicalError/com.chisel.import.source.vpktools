/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VTF.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.IO;
using Chisel.Import.Source.VPKTools.Helpers;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public class VTF
    {
        public readonly string name;

        private Texture2D texture;
        private Color[]   pixels;

        public VTF( VPKEntry entry )
        {
            using( MemoryStream stream = new MemoryStream( entry.smallData ) )
            {
                Color[]    pixels = null;
                Vector2Int dimensions;

                pixels = LoadVTF( stream, 0, out dimensions );

                texture = new Texture2D( dimensions.x, dimensions.y, TextureFormat.RGBA32, false );
                texture.SetPixels( pixels );
                texture.Apply();
            }

            name = entry.fileName;

            Debug.Log( $"Loaded VTF [{name}]" );
        }

        public Texture2D GetTexture()
        {
            return texture;
        }

        private Color[] LoadVTF( Stream stream, long vtfBytePos, out Vector2Int dimensions )
        {
            Color[] pix;
            dimensions = Vector2Int.zero;

            if( stream != null )
            {
                stream.Position = vtfBytePos;

                int sig = stream.ReadValueS32();
                if( sig == VTFHeader.signature )
                {
                    // read header
                    VTFHeader header;
                    uint[]    version = new uint[] { stream.ReadValueU32(), stream.ReadValueU32() };

                    header.version    = ( version[0] ) + ( version[1] / 10 );
                    header.headerSize = stream.ReadValueU32();
                    header.width      = stream.ReadValueU16();
                    header.height     = stream.ReadValueU16();
                    header.flags      = stream.ReadValueU32();
                    header.frames     = stream.ReadValueU16();
                    header.firstFrame = stream.ReadValueU16();
                    header.padding0   = new byte[4];
                    stream.Read( header.padding0, 0, 4 );
                    header.reflectivity = new float[] { stream.ReadValueF32(), stream.ReadValueF32(), stream.ReadValueF32() };
                    header.padding1     = new byte[4];
                    stream.Read( header.padding1, 0, 4 );
                    header.bumpmapScale       = stream.ReadValueF32();
                    header.highResImageFormat = (VTFImageFormat) stream.ReadValueU32();
                    header.mipmapCount        = stream.ReadValueU8();
                    header.lowResImageFormat  = (VTFImageFormat) stream.ReadValueU32();
                    header.lowResImageWidth   = stream.ReadValueU8();
                    header.lowResImageHeight  = stream.ReadValueU8();
                    header.depth              = 1;
                    header.resourceCount      = 0;
                    header.resources          = new VTFResource[0];

                    if( header.version >= 7.2f )
                    {
                        header.depth = stream.ReadValueU16();

                        if( header.version >= 7.3f )
                        {
                            header.padding2 = new byte[3];
                            stream.Read( header.padding2, 0, 3 );
                            header.resourceCount = stream.ReadValueU32();

                            if( header.version >= 7.4f )
                            {
                                header.padding3 = new byte[8];
                                stream.Read( header.padding3, 0, 8 );
                                header.resources = new VTFResource[header.resourceCount];

                                for( int i = 0; i < header.resources.Length; i++ )
                                {
                                    header.resources[i].type = stream.ReadValueU32();
                                    header.resources[i].data = stream.ReadValueU32();
                                }
                            }
                        }
                    }

                    // thumbnails
                    int thumbBufferSize = 0;
                    int imgBufferSize   = (int) ComputeImageBufferSize( header.width, header.height, header.depth, header.mipmapCount, header.highResImageFormat ) * header.frames;

                    if( header.lowResImageFormat != VTFImageFormat.IMAGE_FORMAT_NONE )
                    {
                        thumbBufferSize = (int) ComputeImageBufferSize( header.lowResImageWidth, header.lowResImageHeight, 1, header.lowResImageFormat );
                    }

                    int thumbBufferOffset = 0, imageBufferOffset = 0;

                    // resource dirs
                    if( header.resources.Length > 0 )
                    {
                        for( int i = 0; i < header.resources.Length; i++ )
                        {
                            if( (VTFResourceEntryType) header.resources[i].type == VTFResourceEntryType.VTF_LEGACY_RSRC_LOW_RES_IMAGE )
                                thumbBufferOffset = (int) header.resources[i].data;

                            if( (VTFResourceEntryType) header.resources[i].type == VTFResourceEntryType.VTF_LEGACY_RSRC_IMAGE )
                                imageBufferOffset = (int) header.resources[i].data;
                        }
                    }
                    else
                    {
                        thumbBufferOffset = (int) header.headerSize;
                        imageBufferOffset = thumbBufferOffset + thumbBufferSize;
                    }

                    if( header.highResImageFormat != VTFImageFormat.IMAGE_FORMAT_NONE )
                    {
                        int mipBufferOffset = 0;
                        for( uint i = 1; i <= header.mipmapCount; i++ ) { mipBufferOffset += (int) ComputeMipmapSize( header.width, header.height, header.depth, 1, header.highResImageFormat ); }

                        stream.Position = vtfBytePos + imageBufferOffset + mipBufferOffset;

                        pix        = DecompressImage( stream, header.width, header.height, header.highResImageFormat );
                        dimensions = new Vector2Int( header.width, header.height );
                    }
                    else { throw new FormatException( $"Image format = [{header.highResImageFormat.ToString()}]" ); }
                }
                else { throw new FormatException( $"Image signature mismatch [{sig} != {VTFHeader.signature}]" ); }
            }
            else { throw new FormatException( $"Image missing VTF data" ); }

            return pix;
        }

        private Color[] DecompressImage( Stream data, ushort width, ushort height, VTFImageFormat imageFormat )
        {
            Color[] vtfColors = new Color[width * height];

            Texture2DHelpers.TextureFormat format;
            if( imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT1 || imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT1_ONEBITALPHA )
                format = Texture2DHelpers.TextureFormat.DXT1;
            else if( imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT3 )
                format = Texture2DHelpers.TextureFormat.DXT3;
            else if( imageFormat == VTFImageFormat.IMAGE_FORMAT_DXT5 )
                format = Texture2DHelpers.TextureFormat.DXT5;
            else if( imageFormat == VTFImageFormat.IMAGE_FORMAT_BGR888 )
                format = Texture2DHelpers.TextureFormat.BGR888;
            else if( imageFormat == VTFImageFormat.IMAGE_FORMAT_BGRA8888 )
                format = Texture2DHelpers.TextureFormat.BGRA8888;
            else
            {
                format = Texture2DHelpers.TextureFormat.BGR888;
                Debug.LogError( "SourceTexture: Unsupported format " + imageFormat + ", will read as " + format );
            }

            vtfColors = Texture2DHelpers.DecompressRawBytes( data, width, height, format );
            Texture2DHelpers.FlipVertical( vtfColors, width, height );

            return vtfColors;
        }

        private uint ComputeImageBufferSize( uint width, uint height, uint depth, uint mipmaps, VTFImageFormat imageFormat )
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

        private uint ComputeImageBufferSize( uint w, uint h, uint depth, VTFImageFormat imageFormat )
        {
            uint tempWidth = w, tempHeight = h;

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

        private void ComputeMipmapDimensions( uint width, uint height, uint depth, uint mipmapLevel, out uint mipmapWidth, out uint mipmapHeight, out uint mipmapDepth )
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

        private uint ComputeMipmapSize( uint width, uint height, uint depth, uint mipmapLevel, VTFImageFormat ImageFormat )
        {
            // figure out the width/height of this MIP level
            uint uiMipmapWidth, uiMipmapHeight, uiMipmapDepth;
            ComputeMipmapDimensions( width, height, depth, mipmapLevel, out uiMipmapWidth, out uiMipmapHeight, out uiMipmapDepth );

            // return the memory requirements
            return ComputeImageBufferSize( uiMipmapWidth, uiMipmapHeight, uiMipmapDepth, ImageFormat );
        }

        private enum VTFImageConvertInfoIndex
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

        private static readonly short[,] VTFImageConvertInfo = new short[,]
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
    }
}
