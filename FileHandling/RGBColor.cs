using Color = UnityEngine.Color;

namespace Chisel.Import.Source.VPKTools
{
	public class RGBColor
	{
		public RGBColor() { }

		public RGBColor(Color color)
		{
			r = color.r;
			g = color.g;
			b = color.b;
		}

		public float r, g, b;

		public static implicit operator RGBColor(Color color) { return new RGBColor(color); }
		public static implicit operator Color(RGBColor color) { return new Color((float)color.r, (float)color.g, (float)color.b); }
	}
}