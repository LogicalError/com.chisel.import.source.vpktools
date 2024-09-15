/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKArchive.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.IO;

using Profiler = UnityEngine.Profiling.Profiler;
using Debug = UnityEngine.Debug;

namespace Chisel.Import.Source.VPKTools
{
    public partial class VPKArchive
    {
        public readonly string name;

        public int EntryCount => m_Entries.Count;

        private Dictionary<string, VPKEntry> m_Entries   = new Dictionary<string, VPKEntry>();

        public VPKArchive( string vpk, string logFileName, int version )
		{
			Profiler.BeginSample("misc");
			string logInfo    = "";
            Stream stream     = File.OpenRead( vpk );
            bool   multichunk = Path.GetFileName( vpk ).Replace(PackagePath.VpkExtension, string.Empty ).EndsWith( "_dir" );
            Profiler.EndSample();

            if (version == 2)
			{
				Profiler.BeginSample("DeserializeV2");
				DeserializeV2(stream, out logInfo);
				Profiler.EndSample();
			} else
                throw new ArgumentException($"Invalid VPK version, expected 1 or 2, got [{version}]");

			Profiler.BeginSample("GetFileNameWithoutExtension");
			name = Path.GetFileNameWithoutExtension( vpk );
			Profiler.EndSample();

			Profiler.BeginSample("Log");
			Debug.Log( $"Loaded VPK [{name}{PackagePath.VpkExtension}] with [{m_Entries.Count}] entries." );
			Profiler.EndSample();

            //foreach( KeyValuePair<string, VPKEntry> kvp in m_Entries ) { Debug.Log( $"Entry: [{kvp.Key}], File Name: [{kvp.Value.fileName}]" ); }

            if (Logging)
            {
                File.WriteAllText(logFileName, logInfo);
            }
			stream.Close();
        }

        // $TODO: use string.Contains() to make finding more fuzzy. should allow searching for only the texture name instead.
        public VPKEntry GetEntry( string entryName )
        {
            if( m_Entries.ContainsKey( entryName ) )
                return m_Entries[entryName];
            else
            {
                Debug.LogError( $"Could not find the entry [{entryName}], skipping." );
                return new VPKEntry() { fileName = $"Could not find the entry [{entryName}]" };
            }

            //throw new FileNotFoundException( $"Could not find the entry [{entryName}]" );
        }

        public Dictionary<string, VPKEntry> GetEntries()
        {
            return m_Entries;
		}

    }
}
