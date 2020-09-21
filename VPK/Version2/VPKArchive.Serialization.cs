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

            sb.AppendLine( $"------------------ Header Data ------------------" );
            sb.AppendLine( $"VPK Version: {header.Version}" );
            sb.AppendLine( $"Tree Size: {header.TreeSize}" );
            sb.AppendLine( $"File Data Section Size: {header.FileDataSectionSize}" );
            sb.AppendLine( $"Archive MD5 Section Size: {header.ArchiveMD5SectionSize}" );
            sb.AppendLine( $"Other MD5 Section Size: {header.OtherMD5SectionSize}" );
            sb.AppendLine( $"Signature Section Size: {header.SignatureSectionSize}" );
            sb.AppendLine( Environment.NewLine ); // add spacer

            sb.AppendLine( $"------------------ Tree Data ------------------" );
            // tree
            while( stream.Position < header.TreeSize )
            {
                string extension = stream.ReadStringASCIIZ().ToLower();
                //sb.AppendLine( $"Extension: {extension}" );

                while( true )
                {
                    string directory = stream.ReadStringASCIIZ().ToLower();
                    if(directory.Length > 0)
                    {
                        sb.AppendLine( $"------------------------------------------------------------------------" );
                        sb.AppendLine( $"Directory: {directory}" );
                        sb.AppendLine( $"------------------------------------------------------------------------" );
                    }

                    if( directory.Length <= 0 )
                        break; // $TODO: determine if we should throw an exception here or just break as-is

                    string fileName;
                    int    index = 0;
                    do
                    {
                        fileName = stream.ReadStringASCIIZ().ToLower();
                        if( !string.IsNullOrEmpty( fileName ) )
                        {
                            index++;
                            sb.AppendLine( $">\t[#{index:00#}] File Name: {fileName}" );
                            sb.AppendLine( $"------------------------------------" );

                            VPKEntry entry = new VPKEntry();
                            // get CRC
                            entry.CRC32        = stream.ReadValueU32();
                            entry.preloadBytes = stream.ReadValueU16();
                            // get archive index marker (this marks which VPK this data is stored in)
                            entry.archiveIndex = stream.ReadValueU16();
                            entry.offset       = stream.ReadValueU32();
                            entry.size         = stream.ReadValueU32();
                            entry.data         = new byte[entry.preloadBytes];

                            ushort term = stream.ReadValueU16();

                            // if this entry is a reference to another PAK
                            if( entry.offset == 0 && entry.archiveIndex == 32767 )
                                entry.offset = Convert.ToUInt32( stream.Position );
                            if( entry.size == 0 )
                                entry.size = entry.preloadBytes;

                            stream.Position += entry.preloadBytes;

                            if( !entries.ContainsKey( $"{directory}/{fileName}.{extension}" ) )
                                entries.Add( $"{directory}/{fileName}.{extension}", entry );

                            //sb.AppendLine( $"\t\t> Entry ------------------" );
                            sb.AppendLine( $"\t|\tFile:          [/{directory}/{fileName}.{extension}]" );
                            sb.AppendLine( $"\t|\tCRC:           {entry.CRC32}" );
                            sb.AppendLine( $"\t|\tPreload Bytes: {entry.preloadBytes}" );
                            sb.AppendLine( $"\t|\tArchive Index: {entry.archiveIndex}" );
                            sb.AppendLine( $"\t|\tEntry Offset:  {entry.offset}" );
                            sb.AppendLine( $"\t|\tEntry Length:  {entry.size}" );
                            //sb.AppendLine( $"\t|\tEntry Data Length: {entry.data.Length}" );
                            sb.AppendLine( Environment.NewLine ); // add spacer
                        }
                    }
                    while( !string.IsNullOrEmpty( fileName ) );
                }
            }

            m_Entries = entries;
            logInfo   = sb.ToString();
        }
    }
}
