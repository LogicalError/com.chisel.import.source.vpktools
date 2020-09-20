/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKArchive.cs

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
        public readonly string name;

        private Dictionary<string, VPKEntry> m_Entries   = new Dictionary<string, VPKEntry>();
        private Dictionary<string, Material> m_Materials = new Dictionary<string, Material>();

        // V2 stuff
        private int headerSize;
        private int version;

        public VPKArchive( string vpk, int version )
        {
            string logInfo = "";
            Stream stream = File.OpenRead( vpk );

            if( version == 1 )
                DeserializeV1( stream );
            else if( version == 2 )
                DeserializeV2( stream, out logInfo );
            else
                throw new ArgumentException( $"Invalid VPK version, expected 1 or 2, got [{version}]" );

            name = Path.GetFileNameWithoutExtension( vpk );

            Debug.Log( $"Loaded VPK [{name}.vpk] with [{m_Entries.Count}] entries." );

            //foreach( KeyValuePair<string, VPKEntry> kvp in m_Entries ) { Debug.Log( $"Entry: [{kvp.Key}], File Name: [{kvp.Value.fileName}]" ); }

            File.WriteAllText( $"{Application.dataPath}\\vpk_log.txt", logInfo );
        }

        public VPKEntry GetEntry( string entryName )
        {
            if( m_Entries.ContainsKey( entryName ) )
                return m_Entries[entryName];
            else
                throw new FileNotFoundException( $"Could not find the entry [{entryName}]" );
        }

        public Material GetMaterial( string textureName )
        {
            Texture2D GetAbedo()
            {
                return new VTF( GetEntry( textureName  ), version ).GetTexture();
            }

            Texture2D GetNormal()
            {
                return new VTF( GetEntry( $"{textureName}_normal" ), version ).GetTexture();
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
