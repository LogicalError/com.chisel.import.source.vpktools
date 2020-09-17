/* * * * * * * * * * * * * * * * * * * * * *
com.chisel.import.source.vpktools.SourceMaterial.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public class SourceMaterial : IDisposable
    {
        public Texture2D MainTexture   => m_MainTex;
        public Texture2D NormalTexture => m_NormalTex;

        private Texture2D m_MainTex;
        private Texture2D m_NormalTex;

        private string m_Name;

        public string Name { get => m_Name; private set => m_Name = value; }

        private static readonly int Bump                     = Shader.PropertyToID( "_BumpMap" );
        private static readonly int MainTex                  = Shader.PropertyToID( "_MainTex" );
        private static readonly int SpecColor                = Shader.PropertyToID( "_SpecColor" );
        private static readonly int Glossiness               = Shader.PropertyToID( "_Glossiness" );
        private static readonly int SmoothnessTextureChannel = Shader.PropertyToID( "_SmoothnessTextureChannel" );

        private Material m_CachedMaterial = null;

        public SourceMaterial( VPKParser parser, string mainTexPath )
        {
            SourceTexture sourceNormalTex = null;
            SourceTexture sourceMainTex   = null;

            Debug.Log( $"Attempting to grab albedo texture [{mainTexPath}]." );
            sourceMainTex = SourceTexture.GrabTexture( parser, mainTexPath );

            if( parser.FileExists( $"{mainTexPath.Replace( ".vtf", "" )}_normal.vtf" ) )
            {
                Debug.Log( $"Attempting to grab normal texture [{mainTexPath.Replace( ".vtf", "" )}_normal.vtf]." );
                sourceNormalTex = SourceTexture.GrabTexture( parser, $"{mainTexPath.Replace( ".vtf", "" )}_normal.vtf" );
            }

            if( sourceMainTex != null )
            {
                Debug.Log( $"Found the albedo texture [{mainTexPath}]." );
                m_MainTex = sourceMainTex.GetTexture();
                m_Name    = m_MainTex.name;
            }

            if( sourceNormalTex != null )
            {
                Debug.Log( $"Found the normal texture [{mainTexPath.Replace( ".vtf", "" )}_normal.vtf]." );
                m_NormalTex = sourceNormalTex.GetTexture();
            }
        }

        public Material GetMaterial()
        {
            if( m_CachedMaterial != null )
                return m_CachedMaterial;
            else
            {
                Material material = new Material( Shader.Find( "Standard (Specular setup)" ) );
                material.name = m_MainTex.name;

                material.SetTexture( MainTex, m_MainTex );
                material.SetTexture( Bump,    m_NormalTex );
                material.SetFloat( Glossiness, 0 );
                material.SetColor( SpecColor, Color.black );
                material.SetInt( SmoothnessTextureChannel, 1 );

                m_CachedMaterial = material;

                return material;
            }
        }

        public void Dispose()
        {
            m_CachedMaterial = null;
            m_MainTex        = null;
            m_NormalTex      = null;
        }
    }
}
