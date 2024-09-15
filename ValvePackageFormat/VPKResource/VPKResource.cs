/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.VPKResource.cs

License: MIT (https://tldrlegal.com/license/mit-license)
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using Debug = UnityEngine.Debug;

namespace Chisel.Import.Source.VPKTools
{
    public class VPKResource
    {
        private readonly VPKParser m_Parser;

        public VPKResource( string path )
        {
            m_Parser = new VPKParser( path );
            Debug.Log( $"Loaded a VPK resource with the path [{path}]" );
        }

		public bool LoadFileAsStream(string filePath, VPKParser.FileLoadDelegate streamActions)
        {
            if (!m_Parser.IsValid())
				return false;
				
            if (!m_Parser.FileExists(filePath))
                return false;
                    
            return m_Parser.LoadFileAsStream(filePath, streamActions);
		}
	}
}
