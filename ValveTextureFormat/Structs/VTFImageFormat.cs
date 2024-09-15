using System;

namespace Chisel.Import.Source.VPKTools
{
    [Flags]
    public enum VTFImageFormat
    {
        /// <summary> RGBA 32 bpp </summary>
        IMAGE_FORMAT_RGBA8888 = 0, //

        /// <summary> ABGR 32 bpp </summary>
        IMAGE_FORMAT_ABGR8888,

        /// <summary> RGB 24 bpp </summary>
        IMAGE_FORMAT_RGB888,

        /// <summary> BGR 24 bpp </summary>
        IMAGE_FORMAT_BGR888,

        /// <summary> RGB 16 bpp </summary>
        IMAGE_FORMAT_RGB565,

        /// <summary> Luminance 8 bpp </summary>
        IMAGE_FORMAT_I8,

        /// <summary> Luminance, Alpha 16 bpp </summary>
        IMAGE_FORMAT_IA88,

        /// <summary>  Paletted 8 bpp </summary>
        IMAGE_FORMAT_P8,

        /// <summary> Alpha 8 bpp </summary>
        IMAGE_FORMAT_A8,

        /// <summary> RGB "Bluescreen", Alpha 24 bpp </summary>
        IMAGE_FORMAT_RGB888_BLUESCREEN,

        /// <summary> BGR "Bluescreen", Alpha 24 bpp </summary>
        IMAGE_FORMAT_BGR888_BLUESCREEN,

        /// <summary> ARGB 32 bpp </summary>
        IMAGE_FORMAT_ARGB8888,

        /// <summary> BGRA 32 bpp </summary>
        IMAGE_FORMAT_BGRA8888,

        /// <summary> DXT1 Compressed 4 bpp </summary>
        IMAGE_FORMAT_DXT1,

        /// <summary> DXT3 Compressed 8 bpp </summary>
        IMAGE_FORMAT_DXT3,

        /// <summary> DXT5 Compressed 8 bpp </summary>
        IMAGE_FORMAT_DXT5,

        /// <summary> BGR, Unused 32 bpp </summary>
        IMAGE_FORMAT_BGRX8888,

        /// <summary> BGR 16 bpp </summary>
        IMAGE_FORMAT_BGR565,

        /// <summary> BGR, Unused 16 bpp </summary>
        IMAGE_FORMAT_BGRX5551,

        /// <summary> BGRA 16 bpp </summary>
        IMAGE_FORMAT_BGRA4444,

        /// <summary> DXT1 Compressed, 1-Bit Alpha 4 bpp </summary>
        IMAGE_FORMAT_DXT1_ONEBITALPHA,

        /// <summary> BGRA 16 bpp </summary>
        IMAGE_FORMAT_BGRA5551,

        /// <summary> 2 Channel DuDv/Normal 16 bpp </summary>
        IMAGE_FORMAT_UV88,

        /// <summary> 4 Channel DuDv/Normal 32 bpp </summary>
        IMAGE_FORMAT_UVWQ8888,

        /// <summary> RGBA 64 bpp </summary>
        IMAGE_FORMAT_RGBA16161616F,

        /// <summary> RGBA Mantissa 64 bpp </summary>
        IMAGE_FORMAT_RGBA16161616,

        /// <summary> 4 Channel DuDv/Normal 32 bpp </summary>
        IMAGE_FORMAT_UVLX8888,

        /// <summary> Luminance 32 bpp </summary>
        IMAGE_FORMAT_R32F,

        /// <summary> RGB 96 bpp </summary>
        IMAGE_FORMAT_RGB323232F,

        /// <summary> RGBA 128 bpp </summary>
        IMAGE_FORMAT_RGBA32323232F,


        // -------------------
        IMAGE_FORMAT_NV_DST16,
        IMAGE_FORMAT_NV_DST24,
        IMAGE_FORMAT_NV_INTZ,
        IMAGE_FORMAT_NV_RAWZ,
        IMAGE_FORMAT_ATI_DST16,
        IMAGE_FORMAT_ATI_DST24,
        IMAGE_FORMAT_NV_NULL,
        IMAGE_FORMAT_ATI2N,
        IMAGE_FORMAT_ATI1N,
        IMAGE_FORMAT_COUNT,
        IMAGE_FORMAT_NONE = -1
    }
}
