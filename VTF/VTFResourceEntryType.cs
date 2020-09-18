namespace Chisel.Import.Source.VPKTools
{
    public enum VTFResourceEntryType
    {
        VTF_LEGACY_RSRC_LOW_RES_IMAGE   = ( ( 0x01 )       | ( 0          << 8 ) | ( 0          << 16 ) ),
        VTF_LEGACY_RSRC_IMAGE           = ( ( 0x30 )       | ( 0          << 8 ) | ( 0          << 16 ) ),
        VTF_RSRC_SHEET                  = ( ( 0x10 )       | ( 0          << 8 ) | ( 0          << 16 ) ),
        VTF_RSRC_CRC                    = ( ( (byte) 'C' ) | ( (byte) 'R' << 8 ) | ( (byte) 'C' << 16 ) | ( VTFResourceEntryFlag.RSRCF_HAS_NO_DATA_CHUNK << 24 ) ),
        VTF_RSRC_TEXTURE_LOD_SETTINGS   = ( ( (byte) 'L' ) | ( (byte) 'O' << 8 ) | ( (byte) 'D' << 16 ) | ( VTFResourceEntryFlag.RSRCF_HAS_NO_DATA_CHUNK << 24 ) ),
        VTF_RSRC_TEXTURE_SETTINGS_EX    = ( ( (byte) 'T' ) | ( (byte) 'S' << 8 ) | ( (byte) 'O' << 16 ) | ( VTFResourceEntryFlag.RSRCF_HAS_NO_DATA_CHUNK << 24 ) ),
        VTF_RSRC_KEY_VALUE_DATA         = ( ( (byte) 'K' ) | ( (byte) 'V' << 8 ) | ( (byte) 'D' << 16 ) ),
        VTF_RSRC_MAX_DICTIONARY_ENTRIES = 32
    }
}