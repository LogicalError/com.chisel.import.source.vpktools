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

using Profiler = UnityEngine.Profiling.Profiler;

namespace Chisel.Import.Source.VPKTools
{
    public partial class VPKArchive
    {
        public static bool Logging = false;
        private void DeserializeV2( Stream stream, out string logInfo )
        {
            StringBuilder sb = Logging ? new() : null;
            Dictionary<string, VPKEntry> entries = new();

			// header
			Profiler.BeginSample("new VPKHeader");
			VPKHeader header = new();
			Profiler.EndSample();

			Profiler.BeginSample("read");
			uint sig = stream.ReadValueUInt32();
            if( sig != VPKHeader.Signature )
                throw new FormatException( $"VPK Signature was invalid, got [{sig}], expected [{VPKHeader.Signature}]" );

            header.Version  = stream.ReadValueUInt32();
            header.TreeSize = stream.ReadValueUInt32();

            if( header.Version != 2 )
                throw new FormatException( $"VPK version is not V2, got [{header.Version}]" );

            header.FileDataSectionSize   = stream.ReadValueUInt32();
            header.ArchiveMD5SectionSize = stream.ReadValueUInt32();
            header.OtherMD5SectionSize   = stream.ReadValueUInt32();
            header.SignatureSectionSize  = stream.ReadValueUInt32();
			Profiler.EndSample();

            if (Logging)
            {
                sb.AppendLine($"------------------ Header Data ------------------");
                sb.AppendLine($"VPK Version: {header.Version}");
                sb.AppendLine($"Tree Size: {header.TreeSize}");
                sb.AppendLine($"File Data Section Size: {header.FileDataSectionSize}");
                sb.AppendLine($"Archive MD5 Section Size: {header.ArchiveMD5SectionSize}");
                sb.AppendLine($"Other MD5 Section Size: {header.OtherMD5SectionSize}");
                sb.AppendLine($"Signature Section Size: {header.SignatureSectionSize}");
                sb.AppendLine(Environment.NewLine); // add spacer

                sb.AppendLine($"------------------ Tree Data ------------------");
            }

			// tree
			while ( stream.Position < header.TreeSize )
			{
				string extension = stream.ReadASCIINullString();
                //sb.AppendLine( $"Extension: {extension}" );

                while( true )
                {
                    string directory = stream.ReadASCIINullString();
                    if (directory.Length <= 0)
                        break; // $TODO: determine if we should throw an exception here or just break as-is

					if (directory.Length > 0)
					{
                        if (Logging)
                        {
                            sb.AppendLine($"------------------------------------------------------------------------");
                            sb.AppendLine($"Directory: {directory}");
                            sb.AppendLine($"------------------------------------------------------------------------");
                        }
					}

                    string filename;
                    int    index = 0;
                    do
                    {
                        filename = stream.ReadASCIINullString();
                        if (!string.IsNullOrEmpty(filename))
                        {
                            index++;
                            if (Logging)
                            {
                                sb.AppendLine($"[#{index:00#}]>\tFile Name: {filename}");
                                sb.AppendLine($"------------------------------------");
                            }

							Profiler.BeginSample("new VPKEntry");
							VPKEntry entry = new();
							Profiler.EndSample();

							Profiler.BeginSample("read");
							// get CRC
							entry.CRC32        = stream.ReadValueUInt32();
                            entry.preloadBytes = stream.ReadValueUInt16();
                            // get archive index marker (this marks which VPK this data is stored in)
                            entry.archiveIndex = stream.ReadValueUInt16();
                            entry.offset       = stream.ReadValueUInt32();
                            entry.size         = stream.ReadValueUInt32();
                            entry.data         = entry.preloadBytes > 0 ? new byte[entry.preloadBytes] : null;

                            ushort term = stream.ReadValueUInt16();
                            if (entry.preloadBytes != 0)
                            {
                                for (int i = 0; i < entry.data.Length; i++)
                                {
									entry.data[i] = (byte)stream.ReadByte();
								}
                            }
							Profiler.EndSample();

							// if this entry is a reference to another PAK
							if ( entry.offset == 0 && entry.archiveIndex == 32767 )
                                entry.offset = Convert.ToUInt32( stream.Position );
                            if( entry.size == 0 )
                                entry.size = entry.preloadBytes;

							Profiler.BeginSample("entries.Add");
							var keyname = $"{directory}/{filename}.{extension}".ToLower();
							if ( !entries.ContainsKey(keyname) )
                                entries.Add(keyname, entry );
							Profiler.EndSample();

							if (Logging)
                            {
                                //sb.AppendLine( $"\t\t> Entry ------------------" );
                                sb.AppendLine($"\t|\tFile:          [/{keyname}]");
                                sb.AppendLine($"\t|\tCRC:           {entry.CRC32}");
                                sb.AppendLine($"\t|\tPreload Bytes: {entry.preloadBytes}");
                                sb.AppendLine($"\t|\tArchive Index: {entry.archiveIndex}");
                                sb.AppendLine($"\t|\tEntry Offset:  {entry.offset}");
                                sb.AppendLine($"\t|\tEntry Length:  {entry.size}");
                                //sb.AppendLine( $"\t|\tEntry Data Length: {entry.data.Length}" );
                                sb.AppendLine(Environment.NewLine); // add spacer
                            }
						}
                    } while(!string.IsNullOrEmpty(filename));
                }
            }

            m_Entries = entries;
            logInfo = Logging ? sb.ToString() : null;
        }
    }
}
