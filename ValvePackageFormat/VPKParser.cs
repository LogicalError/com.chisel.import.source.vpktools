using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;

using Debug = UnityEngine.Debug;

namespace Chisel.Import.Source.VPKTools
{
    public class VPKParser : IDisposable
    {
        public delegate bool FileLoadDelegate(Stream stream);
        private const ushort    DIR_PAK = 0x7fff, NO_PAK = ushort.MaxValue;
        public        string    DirectoryLocation { get; private set; }
        public        string    VpkStartName      { get; private set; }

        private       VPKHeader header;
        private       int       headerSize;

        private Dictionary<string, Dictionary<string, Dictionary<string, VPKDirectoryEntry>>> tree = new();

        private List<ushort>     archivesNotFound = new();

        private Stream           preloadStream;
        private VPKOpenStreams[] openStreams = new VPKOpenStreams[7];
        private int              nextStreamIndex;

        public VPKParser( string _directoryLocation )
        {
            DirectoryLocation = _directoryLocation;
        }

        // Dispose() calls Dispose(true)
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }

        // NOTE: Leave out the finalizer altogether if this class doesn't
        // own unmanaged resources, but leave the other methods
        // exactly as they are.
        ~VPKParser()
        {
            // Finalizer calls Dispose(false)
            Dispose( false );
        }

        protected virtual void Dispose( bool disposing )
        {
            if (!disposing)
                return;
            
            preloadStream?.Dispose();
            foreach (var streamWrapper in openStreams)
                streamWrapper.stream?.Dispose();

            archivesNotFound?.Clear();
            archivesNotFound = null;

            header = null;
            if (tree != null)
            {
                foreach (var extPair in tree)
                {
                    if (extPair.Value != null)
                    {
                        foreach (var dirPair in extPair.Value)
                        {
                            if (dirPair.Value != null)
                            {
                                foreach (var entryPair in dirPair.Value)
                                    entryPair.Value.Dispose();
                                dirPair.Value.Clear();
                            }
                        }
                        extPair.Value.Clear();
                    }
                }
                tree.Clear();
            }
            tree = null;
        }

        public bool IsValid()
        {
            CheckHeader();
            return header != null && header.TreeSize > 0;
        }

        private void CheckHeader()
        {
            if( header == null )
                ParseHeader();
        }

        private void ParseHeader()
        {
            string archivePath = DirectoryLocation;

            if( File.Exists( archivePath ) )
            {
                header = new VPKHeader();

                preloadStream = new FileStream( archivePath, FileMode.Open, FileAccess.Read );

                uint signature = DataParser.ReadUInt( preloadStream );
                if( signature != VPKHeader.Signature )
                    return;

                header.Version  = DataParser.ReadUInt( preloadStream );
                header.TreeSize = DataParser.ReadUInt( preloadStream );
                headerSize      = 12;

                if( header.Version > 1 )
                {
                    header.FileDataSectionSize   =  DataParser.ReadUInt( preloadStream );
                    header.ArchiveMD5SectionSize =  DataParser.ReadUInt( preloadStream );
                    header.OtherMD5SectionSize   =  DataParser.ReadUInt( preloadStream );
                    header.SignatureSectionSize  =  DataParser.ReadUInt( preloadStream );
                    headerSize                   += 16;
                }

                ParseTree( preloadStream );
            }
        }

        private void ParseTree( Stream currentStream )
        {
            while( currentStream.Position < header.TreeSize )
            {
                string extension = DataParser.ReadNullTerminatedString( currentStream ).ToLower();
                Debug.Log( $"Extension [{extension}] VPKParser.ParseTree(Stream stream)" );
                if( extension.Length <= 0 )
                    extension = tree.Keys.ElementAt( tree.Count - 1 );
                else
                {
                    if( !tree.ContainsKey( extension ) ) { tree.Add( extension, new Dictionary<string, Dictionary<string, VPKDirectoryEntry>>() ); }
                }

                while( true )
                {
                    string directory = DataParser.ReadNullTerminatedString( currentStream ).ToLower();
                    if( directory.Length <= 0 )
                        break;
                    if( !tree[extension].ContainsKey( directory ) )
                        tree[extension].Add( directory, new Dictionary<string, VPKDirectoryEntry>() );

                    string fileName;
                    do
                    {
                        fileName = DataParser.ReadNullTerminatedString( currentStream ).ToLower();
                        if( !string.IsNullOrEmpty( fileName ) )
                        {
                            VPKDirectoryEntry dirEntry = new VPKDirectoryEntry();
                            dirEntry.CRC          = DataParser.ReadUInt( currentStream );
                            dirEntry.PreloadBytes = DataParser.ReadUShort( currentStream );
                            dirEntry.ArchiveIndex = DataParser.ReadUShort( currentStream );
                            dirEntry.EntryOffset  = DataParser.ReadUInt( currentStream );
                            dirEntry.EntryLength  = DataParser.ReadUInt( currentStream );
                            ushort terminator = DataParser.ReadUShort( currentStream );

                            if( dirEntry.EntryOffset == 0 && dirEntry.ArchiveIndex == DIR_PAK )
                                dirEntry.EntryOffset = Convert.ToUInt32( currentStream.Position );
                            if( dirEntry.EntryLength == 0 )
                                dirEntry.EntryLength = dirEntry.PreloadBytes;

                            currentStream.Position += dirEntry.PreloadBytes;

                            if( !tree[extension][directory].ContainsKey( fileName ) )
                                tree[extension][directory].Add( fileName, dirEntry );
                        }
                    }
                    while( !string.IsNullOrEmpty( fileName ) );
                }
            }
        }

        public string LocateInArchive( string filePath )
        {
			PackagePath.DecomposePath(filePath, out string directory, out string fileName, out string extension);
			return LocateInArchive( directory, fileName, extension);
        }

        public string LocateInArchive(string directory, string fileName, string extension)
        {
            string archiveName = null;

            PackagePath.CleanPath(ref directory, ref fileName, ref extension);
            if (GetEntry(directory, fileName, extension, out VPKDirectoryEntry entry)) { archiveName = GetArchiveName(entry.ArchiveIndex); }

            return archiveName;
        }
        
		public bool LoadFileAsStream(string filePath, FileLoadDelegate streamActions)
		{
			PackagePath.DecomposePath(filePath, out string directory, out string fileName, out string extension);
            return LoadFileAsStream( directory, fileName, extension, streamActions);
        }

        public bool LoadFileAsStream( string directory, string fileName, string extension, FileLoadDelegate streamActions)
        {
            CheckHeader();

			PackagePath.CleanPath(ref directory, ref fileName, ref extension);
            if (GetEntry(directory, fileName, extension, out VPKDirectoryEntry entry))
            {
                Stream currentStream = GetStream(entry.ArchiveIndex);
                if (currentStream != null)
                {
                    try
                    {
                        currentStream.Position = 0;
                        return streamActions(new VPKStream(currentStream, entry.EntryOffset, entry.EntryLength));
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogException(ex);
                        throw ex;
                    }
                }
                return true;
            } else
            {
                UnityEngine.Debug.LogError($"VPKParser: Could not find entry {directory}/{fileName}.{extension}");
                return false;
            }
        }

        private Stream GetStream( ushort archiveIndex )
        {
            Stream currentStream = null;
            /*
            if( archiveIndex == DIR_PAK ) { currentStream = preloadStream; }
            else
            {
                for( int i = 0; i < openStreams.Length; i++ )
                {
                    if( openStreams[i].pakIndex == archiveIndex )
                    {
                        currentStream = openStreams[i].stream;
                        break;
                    }
                }
            }
            */
            //if( currentStream == null )
            {
                string archiveName   = GetArchiveName( archiveIndex );
                string archivePath   = Path.Combine( DirectoryLocation, Path.ChangeExtension(archiveName, PackagePath.ExtensionVPK));
                bool   archiveExists = File.Exists( archivePath );
                if( archiveExists )
                {
//                  openStreams[nextStreamIndex].stream?.Dispose();

                    currentStream = new FileStream( archivePath, FileMode.Open, FileAccess.Read );
//                  openStreams[nextStreamIndex].stream   = currentStream;
//                  openStreams[nextStreamIndex].pakIndex = archiveIndex;

//                  nextStreamIndex = ( nextStreamIndex + 1 ) % openStreams.Length;
                }/*
                else if( !archivesNotFound.Contains( archiveIndex ) )
                {
                    archivesNotFound.Add( archiveIndex );
                    UnityEngine.Debug.LogError( "VPKParser: Could not find archive " + archiveName + ", full path = '" + archivePath + "'" );
                }*/
            }

            return currentStream;
        }

        private string GetArchiveName( ushort archiveIndex )
        {
            string vpkPakDir = $"{DirectoryLocation.Replace($".{PackagePath.ExtensionVPK}", "" ).Replace( "_dir", "" )}_";
            /*if( archiveIndex      == DIR_PAK ) { vpkPakDir += "dir"; }
            else*/ if( archiveIndex < 1000 )
            {
                if( archiveIndex >= 0 && archiveIndex < 10 )
                    vpkPakDir += "00" + archiveIndex;
                else if( archiveIndex >= 10 && archiveIndex < 100 )
                    vpkPakDir += "0" + archiveIndex;
                else
                    vpkPakDir += archiveIndex;
            }

            return vpkPakDir; //vpkStartName + vpkPakDir;
        }

        private bool GetEntry(string directory, string fileName, string extension, out VPKDirectoryEntry entry)
        {
            CheckHeader();

			PackagePath.CleanPath(ref directory, ref fileName, ref extension);
            if (tree != null && tree.ContainsKey(extension) && tree[extension].ContainsKey(directory) && tree[extension][directory].ContainsKey(fileName))
            {
                entry = tree[extension][directory][fileName];
                return true;
            } else
            {
                entry = new VPKDirectoryEntry();
                return false;
            }
        }

        public bool FileExists(string filePath)
        {
            PackagePath.DecomposePath(filePath, out string directory, out string fileName, out string extension);
            return FileExists(directory, fileName, extension);
        }

        public bool FileExists(string directory, string fileName, string extension)
		{
			CheckHeader();

            PackagePath.CleanPath(ref directory, ref fileName, ref extension);
			return tree != null && tree.ContainsKey(extension) && 
                tree[extension].ContainsKey(directory) && 
                tree[extension][directory].ContainsKey(fileName);
        }

        public bool DirectoryExists( string directory )
		{
			CheckHeader();

			if (tree == null)
				return false;

			directory = PackagePath.CleanDirectory(directory);
            for (int i = 0; i < tree.Count; i++)
            {
                if (tree.ContainsKey(tree.Keys.ElementAt(i)) && 
                    tree[tree.Keys.ElementAt(i)].ContainsKey(directory)) 
                    return true;
            }
            return false;
        }
    }

    public struct VPKOpenStreams
    {
        public ushort pakIndex;
        public Stream stream;
    }
}
