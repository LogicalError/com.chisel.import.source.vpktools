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

        private VPKParser m_Parser;

        public VPKResource( string path )
        {
            m_Parser = new VPKParser( path );
        }

        public Material GetMaterial( string vpkInternalPath )
        {
            string   fixedLocation = vpkInternalPath; // will be overwritten if vpkInternalPath isn't found, so we'll set it to what the user asked for
            VMTData  vmt;
            Material material;

            using( m_Parser )
            {
                bool isValid = m_Parser.IsValid();

                if( !m_Parser.FileExists( vpkInternalPath ) )
                    fixedLocation = VMTData.FixLocation( m_Parser, vpkInternalPath );

                string vmtPath = m_Parser.LocateInArchive( fixedLocation );

                vmt = VMTData.GrabVMT( m_Parser, vmtPath, true );
            }

            return vmt.GetMaterial();
        }
    }
}
