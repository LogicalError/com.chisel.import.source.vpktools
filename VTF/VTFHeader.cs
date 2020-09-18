namespace Chisel.Import.Source.VPKTools
{
    struct VTFHeader
    {
        public const int            signature = 0x00465456; // File signature ("VTF\0"). (or as little-endian integer, 0x00465456)
        public       float          version;                // (Sizeof 2) version[0].version[1].
        public       uint           headerSize;             // Size of the header struct (16 byte aligned; currently 80 bytes).
        public       ushort         width;                  // Width of the largest mipmap in pixels. Must be a power of 2.
        public       ushort         height;                 // Height of the largest mipmap in pixels. Must be a power of 2.
        public       uint           flags;                  // VTF flags.
        public       ushort         frames;                 // Number of frames, if animated (1 for no animation).
        public       ushort         firstFrame;             // First frame in animation (0 based).
        public       byte[]         padding0;               // (Sizeof 4) reflectivity padding (16 byte alignment).
        public       float[]        reflectivity;           // (Sizeof 3) reflectivity vector.
        public       byte[]         padding1;               // (Sizeof 4) reflectivity padding (8 byte packing).
        public       float          bumpmapScale;           // Bumpmap scale.
        public       VTFImageFormat highResImageFormat;     // High resolution image format.
        public       byte           mipmapCount;            // Number of mipmaps.
        public       VTFImageFormat lowResImageFormat;      // Low resolution image format (always DXT1).
        public       byte           lowResImageWidth;       // Low resolution image width.
        public       byte           lowResImageHeight;      // Low resolution image height.

        public ushort depth; // Depth of the largest mipmap in pixels. Must be a power of 2. Can be 0 or 1 for a 2D texture (v7.2 only).

        public byte[] padding2;      // (Sizeof 3)
        public uint   resourceCount; // Number of image resources

        public byte[]        padding3;  // (Sizeof 8)
        public VTFResource[] resources; // (Sizeof VTF_RSRC_MAX_DICTIONARY_ENTRIES)
#pragma warning disable CS0649
        public VTFResourceData[] data; // (Sizeof VT_RSRC_MAX_DICTIONARY_ENTRIES)
#pragma warning restore CS0649
    }
}
