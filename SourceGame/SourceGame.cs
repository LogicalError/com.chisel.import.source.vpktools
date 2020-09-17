/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.SourceGame.cs

License: MIT (https://tldrlegal.com/license/mit-license)
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

namespace Chisel.Import.Source.VPKTools
{
    public struct SourceGame
    {
        /// <summary>
        /// Change this to change the search directory of VPK import
        /// </summary>
        public static string DefaultGameDir = @"C:\Program Files (x86)\Steam\steamapps\common\";

        private const string HL1S    = @"Half-Life 2\hl1\";
        private const string HL2     = @"Half-Life 2\hl2\";
        private const string HL2E2   = @"Half-Life 2\ep2\";
        private const string HL2LC   = @"Half-Life 2\lostcoast\";
        private const string BLKMSA  = @"Black Mesa\bms\";
        private const string PORTAL  = @"Portal\portal\";
        private const string PORTAL2 = @"Portal 2\portal2\";
        private const string CSS     = @"Counter-Strike Source\cstrike\";
        private const string CSGO    = @"Counter-Strike Global Offensive\csgo\"; // does 2006 support this? game was released in 2012
        private const string INS2    = @"insurgency2\insurgency\";               // does 2006 support this? game was released in 2014
        private const string DOI     = @"dayofinfamy\doi\";                      // does 2006 support this? game was released in 2017

        public static string GetDirForTitle( GameTitle title )
        {
            switch( title )
            {
                case GameTitle.HalfLifeSource:               return $"{DefaultGameDir}{HL1S}";
                case GameTitle.HalfLife2:                    return $"{DefaultGameDir}{HL2}";
                case GameTitle.HalfLife2Episode2:            return $"{DefaultGameDir}{HL2E2}";
                case GameTitle.HalfLife2LostCoast:           return $"{DefaultGameDir}{HL2LC}";
                case GameTitle.BlackMesa:                    return $"{DefaultGameDir}{BLKMSA}";
                case GameTitle.Portal:                       return $"{DefaultGameDir}{PORTAL}";
                case GameTitle.Portal2:                      return $"{DefaultGameDir}{PORTAL2}";
                case GameTitle.CounterStrikeSource:          return $"{DefaultGameDir}{CSS}";
                case GameTitle.CounterStrikeGlobalOffensive: return $"{DefaultGameDir}{CSGO}";
                case GameTitle.Insurgency2:                  return $"{DefaultGameDir}{INS2}";
                case GameTitle.DayOfInfamy:                  return $"{DefaultGameDir}{DOI}";
            }

            return $"{DefaultGameDir}{HL2}"; // default to HL2 as its the most commonly used.
        }

        /// <summary>
        /// <para>Gets the full path (or paths) to a resource VPK based on the game selected.</para>
        /// <para>See also: <seealso cref="GetDirForTitle"/></para>
        /// </summary>
        /// <param name="title">The selected game to get VPKs for</param>
        public static string[] GetVPKPathsForTitle( GameTitle title )
        {
            switch( title )
            {
                case GameTitle.HalfLifeSource:               return new[] { $"{GetDirForTitle( title )}hl1_pak_dir.vpk" };
                case GameTitle.HalfLife2:                    return new[] { $"{GetDirForTitle( title )}hl2_textures_dir.vpk" };
                case GameTitle.HalfLife2Episode2:            return new[] { $"{GetDirForTitle( title )}ep2_pak_dir.vpk", $"{GetDirForTitle( GameTitle.HalfLife2 )}hl2_textures_dir.vpk" };
                case GameTitle.HalfLife2LostCoast:           return new[] { $"{GetDirForTitle( title )}lostcoast_pak_dir.vpk", $"{GetDirForTitle( GameTitle.HalfLife2 )}hl2_textures_dir.vpk" };
                case GameTitle.BlackMesa:                    return new[] { $"{GetDirForTitle( title )}bms_textures_dir.vpk" };
                case GameTitle.Portal:                       return new[] { $"{GetDirForTitle( title )}portal_pak_dir.vpk" };
                case GameTitle.Portal2:                      return new[] { $"{GetDirForTitle( title )}pak01_dir.vpk" };
                case GameTitle.CounterStrikeSource:          return new[] { $"{GetDirForTitle( title )}cstrike_pak_dir.vpk" };
                case GameTitle.CounterStrikeGlobalOffensive: return new[] { $"{GetDirForTitle( title )}pak01_dir.vpk" };
                case GameTitle.Insurgency2:                  return new[] { $"{GetDirForTitle( title )}insurgency_materials_dir.vpk" };
                case GameTitle.DayOfInfamy:                  return new[] { $"{GetDirForTitle( title )}doi_materials_dir.vpk" };
            }

            return new[] { $"{GetDirForTitle( title )}hl2_textures_dir.vpk" };
        }
    }
}
