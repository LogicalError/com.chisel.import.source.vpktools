namespace Chisel.Import.Source.VPKTools
{
	public class SourceModel
	{
		public MDL MDL;
		public VVD VVD;
		public VTX VTX;
		/// <summary>The model's collision, or null when it has none.</summary>
		public PhyFile PHY;
		public GameEntry MDLEntry;
		public GameEntry VVDEntry;
		public GameEntry VTXEntry;
		public GameEntry PHYEntry;
	}
}