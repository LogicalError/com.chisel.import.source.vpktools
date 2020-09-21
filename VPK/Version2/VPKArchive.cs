/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKArchive.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public partial class VPKArchive
    {
        public readonly string name;

        private Dictionary<string, VPKEntry> m_Entries   = new Dictionary<string, VPKEntry>();
        private Dictionary<string, Material> m_Materials = new Dictionary<string, Material>();

        private int headerSize;

        public VPKArchive( string vpk, int version )
        {
            string logInfo    = "";
            Stream stream     = File.OpenRead( vpk );
            bool   multichunk = Path.GetFileName( vpk ).Replace( ".vpk", string.Empty ).EndsWith( "_dir" );

            if( version == 2 )
                DeserializeV2( stream, out logInfo );
            else
                throw new ArgumentException( $"Invalid VPK version, expected 1 or 2, got [{version}]" );

            name = Path.GetFileNameWithoutExtension( vpk );

            Debug.Log( $"Loaded VPK [{name}.vpk] with [{m_Entries.Count}] entries." );

            //foreach( KeyValuePair<string, VPKEntry> kvp in m_Entries ) { Debug.Log( $"Entry: [{kvp.Key}], File Name: [{kvp.Value.fileName}]" ); }

            File.WriteAllText( $"{Application.dataPath}\\vpk_log.txt", logInfo );
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

        public Material GetMaterial( string textureName )
        {
            Texture2D GetAbedo()
            {
                return new VTF( GetEntry( textureName ) ).GetTexture();
            }

            Texture2D GetNormal()
            {
                return new VTF( GetEntry( $"{textureName}_normal" ) ).GetTexture();
            }

            Debug.Log( $"Attempting to create material from the mainTexture [{textureName}]" );

            if( m_Materials.ContainsKey( textureName ) ) { return m_Materials[textureName]; }
            else
            {
                Material material = new Material( Shader.Find( "Standard (Specular setup)" ) );
                material.name = textureName;

                material.SetTexture( "_MainTex", GetAbedo() );
                material.SetTexture( "_BumpMap", GetNormal() );
                material.SetFloat( "_Glossiness", 0 );
                material.SetInt( "_SmoothnessTextureChannel", 1 );
                material.SetColor( "_SpecColor", Color.black );

                return material;
            }
        }
    }
}
