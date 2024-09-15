namespace Chisel.Import.Source.VPKTools
{
    public static class SourceGameUtils
    {
        /// <summary>
        /// Returns the friendly name of the corresponding <seealso cref="SourceGameTitle"/>. Used for UI.
        /// </summary>
        public static string GetNameForGameTitle( this SourceGameTitle title )
        {
			switch (title)
            {
                case SourceGameTitle.HalfLifeSource:               return "Half-Life: Source";
                case SourceGameTitle.HalfLife2:                    return "Half-Life 2";
                case SourceGameTitle.HalfLife2Episode2:            return "Half-Life 2: Episode 2";
                case SourceGameTitle.HalfLife2LostCoast:           return "Half-Life 2: Lost Coast";
                case SourceGameTitle.BlackMesa:                    return "Black Mesa";
                case SourceGameTitle.Portal:                       return "Portal";
                case SourceGameTitle.Portal2:                      return "Portal 2";
                case SourceGameTitle.CounterStrikeSource:          return "Counter-Strike: Source";
                case SourceGameTitle.CounterStrikeGlobalOffensive: return "Counter-Strike: Global Offensive";
                case SourceGameTitle.Insurgency2:                  return "Insurgency";
                case SourceGameTitle.DayOfInfamy:                  return "Day of Infamy";
            }

            return "Half-Life 2"; // default to HL2 as its the most commonly used.
        }

        public static int GetValue( this SourceGameTitle title )
        {
            return (int) title;
        }
    }
}