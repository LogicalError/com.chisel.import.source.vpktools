/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKEntry.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */


namespace Chisel.Import.Source.VPKTools
{
    public struct VPKEntry
    {
        public string fileName;
        public string directoryName;
        public string typeName;
        public uint   CRC32;
        public uint   size;
        public uint   offset;
        public ushort archiveIndex;
        public byte[] smallData;

        //v2 stuff
        public ushort preloadBytes;
        public byte[] data;
    }
}
