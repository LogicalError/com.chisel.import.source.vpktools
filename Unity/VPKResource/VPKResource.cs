/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKResource.cs

License: MIT (https://tldrlegal.com/license/mit-license)
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
    public class VPKResource
    {
        private Dictionary<string, Material> m_CachedMaterials = new Dictionary<string, Material>();

        private readonly VPKParser m_Parser;

        public VPKResource( string path )
        {
            m_Parser = new VPKParser( path );
            Debug.Log( $"Loaded a VPK resource with the path [{path}]" );
        }

        public Material GetMaterial( string path )
        {
            SourceMaterial material = null;

            using( m_Parser )
            {
                if( m_Parser.IsValid() )
                {
                    Debug.Log( $"VPKParser was valid. Loading new SourceMaterial with the path [materials/{path.ToLower()}]." );
                    material = new SourceMaterial( m_Parser, $"materials/{path.ToLower()}" );

                    if(!m_CachedMaterials.ContainsKey( material.Name ))
                    {
                        Debug.Log( $"Cached material [{material.Name}]" );
                        m_CachedMaterials.Add( material.Name, material.GetMaterial() );
                    }
                }
            }

            return material.GetMaterial();
        }
    }
}
