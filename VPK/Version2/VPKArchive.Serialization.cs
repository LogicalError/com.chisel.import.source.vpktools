/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKArchive.Serialization.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chisel.Import.Source.VPKTools.Helpers;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public partial class VPKArchive
    {
        private void DeserializeV2( Stream stream, out string logInfo )
        {
            version = 2;
            StringBuilder                sb      = new StringBuilder();
            Dictionary<string, VPKEntry> entries = new Dictionary<string, VPKEntry>();

            // header
            VPKHeader header = new VPKHeader();

            uint sig = stream.ReadValueU32();
            if( sig != VPKHeader.Signature )
                throw new FormatException( $"VPK Signature was invalid, got [{sig}], expected [{VPKHeader.Signature}]" );

            header.Version  = stream.ReadValueU32();
            header.TreeSize = stream.ReadValueU32();

            if( header.Version != 2 )
                throw new FormatException( $"VPK version is not V2, got [{header.Version}]" );

            header.FileDataSectionSize   = stream.ReadValueU32();
            header.ArchiveMD5SectionSize = stream.ReadValueU32();
            header.OtherMD5SectionSize   = stream.ReadValueU32();
            header.SignatureSectionSize  = stream.ReadValueU32();
            headerSize                   = 28;

            sb.AppendLine( $"Header Data ------------------" );
            sb.AppendLine( $"VPK Version: {header.Version}" );
            sb.AppendLine( $"Tree Size: {header.TreeSize}" );
            sb.AppendLine( $"File Data Section Size: {header.FileDataSectionSize}" );
            sb.AppendLine( $"Archive MD5 Section Size: {header.ArchiveMD5SectionSize}" );
            sb.AppendLine( $"Other MD5 Section Size: {header.OtherMD5SectionSize}" );
            sb.AppendLine( $"Signature Section Size: {header.SignatureSectionSize}" );
            sb.AppendLine( Environment.NewLine ); // add spacer

            sb.AppendLine( $"Tree Data ------------------" );
            // tree
            while( stream.Position < header.TreeSize )
            {
                string extension = stream.ReadStringASCIIZ().ToLower();
                sb.AppendLine( $"Extension: {extension}" );

                while( true )
                {
                    string directory = stream.ReadStringASCIIZ().ToLower();
                    sb.AppendLine( $"Directory: {directory}" );

                    if( directory.Length <= 0 )
                        break; // $TODO: determine if we should throw an exception here or just break as-is

                    string fileName;
                    do
                    {
                        fileName = stream.ReadStringASCIIZ().ToLower();
                        sb.AppendLine( $"File Name: {fileName}" );
                        if( !string.IsNullOrEmpty( fileName ) )
                        {
                            VPKEntry entry = new VPKEntry();
                            entry.CRC32        = stream.ReadValueU32();
                            entry.preloadBytes = stream.ReadValueU16();
                            entry.archiveIndex = stream.ReadValueU16();
                            entry.offset       = stream.ReadValueU32();
                            entry.size         = stream.ReadValueU32();

                            ushort term = stream.ReadValueU16();

                            if( entry.offset == 0 && entry.archiveIndex == 32767 )
                                entry.offset = Convert.ToUInt32( stream.Position );
                            if( entry.size == 0 )
                                entry.size = entry.preloadBytes;

                            stream.Position += entry.preloadBytes;

                            if( !entries.ContainsKey( $"{directory}/{fileName}.{extension}" ) )
                                entries.Add( $"{directory}/{fileName}.{extension}", entry );

                            sb.AppendLine( $"\tEntry ------------------" );
                            sb.AppendLine( $"\tFile: [{directory}/{fileName}.{extension}]" );
                            sb.AppendLine( $"\tCRC: {entry.CRC32}" );
                            sb.AppendLine( $"\tPreload Bytes: {entry.preloadBytes}" );
                            sb.AppendLine( $"\tArchive Index: {entry.archiveIndex}" );
                            sb.AppendLine( $"\tEntry Offset: {entry.offset}" );
                            sb.AppendLine( $"\tEntry Length: {entry.size}" );
                        }
                    }
                    while( !string.IsNullOrEmpty( fileName ) );
                }
            }

            m_Entries = entries;
            logInfo   = sb.ToString();
        }

        public void DeserializeV1( Stream stream )
        {
            version = 1;

            if( stream.ReadValueU32() != 1437209140U )
                stream.Seek( -4L, SeekOrigin.Current );
            else
            {
                uint num  = stream.ReadValueU32();
                uint num2 = stream.ReadValueU32();

                if( num != 1 )
                    throw new FormatException( $"Unexpected version [{num}]" );
            }

            Dictionary<string, VPKEntry> entries = new Dictionary<string, VPKEntry>();

            while( true )
            {
                bool   flag     = true;
                string typeName = stream.ReadStringASCIIZ();

                if( typeName == string.Empty ) break;

                while( true )
                {
                    flag = true;
                    string directory = stream.ReadStringASCIIZ();
                    if( directory == string.Empty ) break;

                    while( true )
                    {
                        flag = true;
                        string fileName = stream.ReadStringASCIIZ();
                        if( fileName == string.Empty ) break;

                        VPKEntry entry = new VPKEntry();
                        entry.fileName      = fileName;
                        entry.directoryName = directory;
                        entry.typeName      = typeName;
                        entry.CRC32         = stream.ReadValueU32();
                        entry.smallData     = new byte[stream.ReadValueU16()];
                        entry.archiveIndex  = stream.ReadValueU16();
                        entry.offset        = stream.ReadValueU32();
                        entry.size          = stream.ReadValueU32();

                        ushort term = stream.ReadValueU16();

                        if( term != ushort.MaxValue ) throw new FormatException( $"Invalid terminator [{term}]" );

                        if( entry.smallData.Length > 0 ) stream.Read( entry.smallData, 0, entry.smallData.Length );

                        entries.Add( entry.fileName, entry );
                    }
                }
            }

            m_Entries = entries;

            Debug.Log( $"Found [{entries.Count}] entries in [{name}.vpk]" );
        }
    }
}
