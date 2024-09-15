using System.Runtime.InteropServices;

namespace Chisel.Import.Source.VPKTools
{
    [StructLayout(LayoutKind.Explicit)]
    struct VTFResource
    {
        [FieldOffset( 0 )]
        public uint type;

        [FieldOffset( 0 )]
        public byte[] id; // (Sizeof 3) Unique resource ID

        [FieldOffset( 3 )]
        public byte flags; // Resource flags

        [FieldOffset( 4 )]
        public uint data; // Resource data (e.g. for a CRC) or offset from start of the file
    }
}
