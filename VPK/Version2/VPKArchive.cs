/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKArchive.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using System.IO;
using Chisel.Import.Source.VPKTools.Helpers;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public class VPKArchive
    {
        public readonly string name;

        private Dictionary<string, VPKEntry> m_Entries   = new Dictionary<string, VPKEntry>();
        private Dictionary<string, Material> m_Materials = new Dictionary<string, Material>();

        public VPKArchive( string vpk )
        {
            Stream stream = File.OpenRead( vpk );
            Deserialize( stream );

            name = Path.GetFileNameWithoutExtension( vpk );

            Debug.Log( $"Loaded VPK [{name}.vpk]" );
        }

        // $TODO: this is for V1, update to V2
        public void Deserialize( Stream stream )
        {
            if( stream.ReadValueU32() != 1437209140 )
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
