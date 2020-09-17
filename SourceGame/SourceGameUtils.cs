namespace Chisel.Import.Source.VPKTools
{
    public static class SourceGameUtils
    {
        /// <summary>
        /// Returns the friendly name of the corresponding <seealso cref="GameTitle"/>. Used for UI.
        /// </summary>
        public static string GetNameForGameTitle( this GameTitle title )
        {
            switch( title )
            {
                case GameTitle.HalfLifeSource:               return "Half-Life: Source";
                case GameTitle.HalfLife2:                    return "Half-Life 2";
                case GameTitle.HalfLife2Episode2:            return "Half-Life 2: Episode 2";
                case GameTitle.HalfLife2LostCoast:           return "Half-Life 2: Lost Coast";
                case GameTitle.BlackMesa:                    return "Black Mesa";
                case GameTitle.Portal:                       return "Portal";
                case GameTitle.Portal2:                      return "Portal 2";
                case GameTitle.CounterStrikeSource:          return "Counter-Strike: Source";
                case GameTitle.CounterStrikeGlobalOffensive: return "Counter-Strike: Global Offensive";
                case GameTitle.Insurgency2:                  return "Insurgency";
                case GameTitle.DayOfInfamy:                  return "Day of Infamy";
            }

            return "Half-Life 2"; // default to HL2 as its the most commonly used.
        }

        public static int GetValue( this GameTitle title )
        {
            return (int) title;
        }
    }
}