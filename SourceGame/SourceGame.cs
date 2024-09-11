/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.SourceGame.cs

License: MIT (https://tldrlegal.com/license/mit-license)
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System.IO;

namespace Chisel.Import.Source.VPKTools
{
    public struct SourceGame
    {
		/// <summary>
		/// Change this to change the search directory of VPK import
		/// </summary>
		// TODO: make this platform independent
		public static string DefaultGameDir = @"C:\Program Files (x86)\Steam\steamapps\common\";

        private const string HL1S    = @"Half-Life 2\hl1\";
        private const string HL2     = @"Half-Life 2\hl2\";
        private const string HL2E1   = @"Half-Life 2\episodic\";
        private const string HL2E2   = @"Half-Life 2\ep2\";
        private const string HL2LC   = @"Half-Life 2\lostcoast\";
        private const string BLKMSA  = @"Black Mesa\bms\";
        private const string PORTAL  = @"Portal\portal\";
        private const string PORTAL2 = @"Portal 2\portal2\";
        private const string CSS     = @"Counter-Strike Source\cstrike\";
        private const string CSGO    = @"Counter-Strike Global Offensive\csgo\"; // does 2006 support this? game was released in 2012
        private const string INS2    = @"insurgency2\insurgency\";               // does 2006 support this? game was released in 2014
        private const string DOI     = @"dayofinfamy\doi\";                      // does 2006 support this? game was released in 2017

        public static string GetDirForTitle( SourceGameTitle title )
        {
            switch( title )
            {
                case SourceGameTitle.HalfLifeSource:               return $"{DefaultGameDir}{HL1S}";
                case SourceGameTitle.HalfLife2:                    return $"{DefaultGameDir}{HL2}";
                case SourceGameTitle.HalfLife2Episode1:            return $"{DefaultGameDir}{HL2E1}";
                case SourceGameTitle.HalfLife2Episode2:            return $"{DefaultGameDir}{HL2E2}";
                case SourceGameTitle.HalfLife2LostCoast:           return $"{DefaultGameDir}{HL2LC}";
                case SourceGameTitle.BlackMesa:                    return $"{DefaultGameDir}{BLKMSA}";
                case SourceGameTitle.Portal:                       return $"{DefaultGameDir}{PORTAL}";
                case SourceGameTitle.Portal2:                      return $"{DefaultGameDir}{PORTAL2}";
                case SourceGameTitle.CounterStrikeSource:          return $"{DefaultGameDir}{CSS}";
                case SourceGameTitle.CounterStrikeGlobalOffensive: return $"{DefaultGameDir}{CSGO}";
                case SourceGameTitle.Insurgency2:                  return $"{DefaultGameDir}{INS2}";
                case SourceGameTitle.DayOfInfamy:                  return $"{DefaultGameDir}{DOI}";
            }

            return $"{DefaultGameDir}{HL2}"; // default to HL2 as its the most commonly used.
        }
        public static string[] GetVPKPathsForTitle( SourceGameTitle title )
        {
            // TODO: should really parse gameinfo.txt to figure out which paths to use
            switch( title )
            {
                case SourceGameTitle.HalfLifeSource:               return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.HalfLife2:                    return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.HalfLife2Episode1:            return new[] { GetDirForTitle( title ), GetDirForTitle( SourceGameTitle.HalfLife2 ) };
                case SourceGameTitle.HalfLife2Episode2:            return new[] { GetDirForTitle( title ), GetDirForTitle( SourceGameTitle.HalfLife2Episode1 ), GetDirForTitle( SourceGameTitle.HalfLife2 ) };
                case SourceGameTitle.HalfLife2LostCoast:           return new[] { GetDirForTitle( title ), GetDirForTitle( SourceGameTitle.HalfLife2 ) };
                case SourceGameTitle.BlackMesa:                    return new[] { GetDirForTitle( title ), Path.GetFullPath(GetDirForTitle(title) + @"..\hl2\"), Path.GetFullPath(GetDirForTitle(title) + @"..\platform\") };
                case SourceGameTitle.Portal:                       return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.Portal2:                      return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.CounterStrikeSource:          return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.CounterStrikeGlobalOffensive: return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.Insurgency2:                  return new[] { GetDirForTitle( title ) };
                case SourceGameTitle.DayOfInfamy:                  return new[] { GetDirForTitle( title ) };
            }

            return new[] { $"{GetDirForTitle( title )}hl2_textures_dir.vpk" };
        }

        /// <summary>
        /// <para>Gets the full path (or paths) to a resource VPK based on the game selected.</para>
        /// <para>See also: <seealso cref="GetDirForTitle"/></para>
        /// </summary>
        /// <param name="title">The selected game to get VPKs for</param>
        public static string[] GetVPKFilesForTitle( SourceGameTitle title )
        {
            switch( title )
            {
                case SourceGameTitle.HalfLifeSource:               return new[] { $"{GetDirForTitle( title )}hl1_pak_dir.vpk" };
                case SourceGameTitle.HalfLife2:                    return new[] { $"{GetDirForTitle( title )}hl2_textures_dir.vpk" };
                case SourceGameTitle.HalfLife2Episode2:            return new[] { $"{GetDirForTitle( title )}ep2_pak_dir.vpk", $"{GetDirForTitle( SourceGameTitle.HalfLife2 )}hl2_textures_dir.vpk" };
                case SourceGameTitle.HalfLife2LostCoast:           return new[] { $"{GetDirForTitle( title )}lostcoast_pak_dir.vpk", $"{GetDirForTitle( SourceGameTitle.HalfLife2 )}hl2_textures_dir.vpk" };
                case SourceGameTitle.BlackMesa:                    return new[] { $"{GetDirForTitle( title )}bms_textures_dir.vpk" };
                case SourceGameTitle.Portal:                       return new[] { $"{GetDirForTitle( title )}portal_pak_dir.vpk" };
                case SourceGameTitle.Portal2:                      return new[] { $"{GetDirForTitle( title )}pak01_dir.vpk" };
                case SourceGameTitle.CounterStrikeSource:          return new[] { $"{GetDirForTitle( title )}cstrike_pak_dir.vpk" };
                case SourceGameTitle.CounterStrikeGlobalOffensive: return new[] { $"{GetDirForTitle( title )}pak01_dir.vpk" };
                case SourceGameTitle.Insurgency2:                  return new[] { $"{GetDirForTitle( title )}insurgency_materials_dir.vpk" };
                case SourceGameTitle.DayOfInfamy:                  return new[] { $"{GetDirForTitle( title )}doi_materials_dir.vpk" };
            }

            return new[] { $"{GetDirForTitle( title )}hl2_textures_dir.vpk" };
        }
    }
}
