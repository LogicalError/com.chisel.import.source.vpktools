using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Mipmaps built the way Source builds them, rather than the way Unity does.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A port of <c>ImageLoader::ResampleRGBA8888</c> and <c>GenerateMipmapLevels</c>. Three things in
	/// there are not what a reader would assume, and each of them changes the output:
	/// </para>
	/// <list type="number">
	/// <item>
	/// EVERY level is resampled from level 0, never from the level above it. Filtering a filtered image
	/// compounds the blur; going back to the original each time does not.
	/// </item>
	/// <item>
	/// The filter is not a box. It is a radially symmetric Lanczos-3 - <c>sinc(pi d) * 3 sin(pi d / 3)/(pi d)</c>,
	/// clipped past d &gt; 3 - measured in DESTINATION pixels, so it reaches six destination pixels across
	/// and therefore 6x the reduction ratio in source texels. It is not separable; the weight depends on
	/// the radius, not on x and y independently.
	/// </item>
	/// <item>
	/// Colour is filtered in LINEAR space (gamma 2.2 in, 1/2.2 out) while alpha is filtered raw. Averaging
	/// gamma-encoded colour darkens it; averaging alpha through a gamma curve would move the alpha test.
	/// </item>
	/// </list>
	/// <para>
	/// Why it is worth doing at all: Unity generates mips with a box filter, and a box filter destroys thin
	/// high-contrast detail - which is exactly what a cut-out texture is made of. Alpha coverage at the 0.5
	/// cutoff, measured on the chain-link fence that started this, per level:
	/// </para>
	/// <code>
	///   mip       0      1      2      3      4
	///   Valve  0.137  0.111  0.161  0.191  0.067
	///   box    0.137  0.113  0.121  0.060  0.001
	/// </code>
	/// <para>
	/// By mip 4 the box filter has one texel in a thousand left above the cutoff, which is the fence
	/// dissolving into "random pixels hanging in the air" at distance. Valve's filter keeps two orders of
	/// magnitude more of it. Nothing in a shader can undo the box filter's loss, because by then the
	/// information is gone.
	/// </para>
	/// <para>
	/// Validated against textures Valve's own vtex compiled, by predicting each stored level from level 0
	/// and comparing: 96-100% of texels land on the same side of the alpha test on metalcombinegrate001a
	/// and trashdecal01a, and the best-fit threshold comes out at 0.50 on its own - i.e. the stored alpha
	/// simply IS this filter's output, with no alpha-test step after it.
	/// </para>
	/// <para>
	/// NOTE: Black Mesa's own textures are NOT built this way. Their chains are two-valued at every level
	/// with alpha coverage that GROWS with distance (fence_alpha: 0.137 -> 0.414), which is a deliberate
	/// alpha-test dilation Valve's resampler does not do. Black Mesa is a mod team and used other tooling.
	/// So this reproduces Source, not every VTF that ships with the game.
	/// </para>
	/// </remarks>
	public static class SourceMipmaps
	{
		/// <summary>The gamma vtex passes for both source and destination (IVTFTexture::GenerateMipmaps).</summary>
		public const float kSourceGamma = 2.2f;

		/// <summary>Kernel diameter in DESTINATION pixels, as Valve measures it.</summary>
		const int kKernelDiameter = 6;

		static readonly float[] kGammaToLinear = BuildGammaTable(kSourceGamma);

		// ConstructFloatGammaTable( table, srcGamma, 1.0f ): 255 * (i/255)^gamma, kept in 0..255 rather
		// than normalised, because the inverse below undoes exactly that.
		static float[] BuildGammaTable(float gamma)
		{
			var table = new float[256];
			for (int i = 0; i < 256; i++)
				table[i] = 255.0f * Mathf.Pow(i / 255.0f, gamma);
			return table;
		}

		static byte Clamp(float value)
		{
			int rounded = (int)(value + 0.5f);
			return (byte)(rounded < 0 ? 0 : rounded > 255 ? 255 : rounded);
		}

		/// <summary>How many levels a chain of this size has, down to 1x1.</summary>
		public static int LevelCount(int width, int height)
		{
			int levels = 1;
			while (width > 1 || height > 1)
			{
				width = Mathf.Max(1, width >> 1);
				height = Mathf.Max(1, height >> 1);
				levels++;
			}
			return levels;
		}

		/// <summary>
		/// The whole chain, level 0 first, each level resampled from <paramref name="level0"/>.
		/// </summary>
		/// <param name="normalMap">
		/// Skip the gamma conversion. A normal map's channels are a direction, not a brightness, so putting
		/// them through a gamma curve bends the vector.
		/// </param>
		public static Color32[][] Generate(Color32[] level0, int width, int height, bool normalMap)
		{
			if (level0 == null || level0.Length != width * height)
				throw new ArgumentException($"level 0 is {level0?.Length ?? 0} texels, expected {width * height}");

			int levelCount = LevelCount(width, height);
			var levels = new Color32[levelCount][];
			levels[0] = level0;
			for (int level = 1; level < levelCount; level++)
			{
				int levelWidth = Mathf.Max(1, width >> level);
				int levelHeight = Mathf.Max(1, height >> level);
				levels[level] = Resample(level0, width, height, levelWidth, levelHeight, normalMap);
			}
			return levels;
		}

		/// <summary>
		/// Valve's nice filter: sinc(pi d) * 3 sin(pi d / 3) / (pi d) over the radius, zero past the
		/// diameter, normalised to sum to one.
		/// </summary>
		/// <remarks>
		/// The sample points sit half a source texel off centre, so d is never exactly zero and the
		/// sinc never divides by it. That is load-bearing, not incidental - centring the taps on the
		/// texels instead would put a 0/0 in the middle of the kernel.
		/// </remarks>
		internal static float[] NiceKernel(int wratio, int hratio, int diameter)
		{
			int kernelWidth = diameter * wratio;
			int kernelHeight = diameter * hratio;
			var kernel = new float[kernelWidth * kernelHeight];

			float dx = 1.0f / wratio;
			float dy = 1.0f / hratio;
			float total = 0.0f;
			float y = -(diameter - dy) * 0.5f;
			for (int row = 0; row < kernelHeight; row++, y += dy)
			{
				float x = -(diameter - dx) * 0.5f;
				for (int column = 0; column < kernelWidth; column++, x += dx)
				{
					float d = Mathf.Sqrt((x * x) + (y * y));
					if (d > diameter * 0.5f)
						continue;
					float t = Mathf.PI * d;
					float weight = (Mathf.Sin(t) / t) * (3.0f * Mathf.Sin(t / 3.0f) / t);
					kernel[(row * kernelWidth) + column] = weight;
					total += weight;
				}
			}

			for (int i = 0; i < kernel.Length; i++)
				kernel[i] /= total;
			return kernel;
		}

		static Color32[] Resample(Color32[] src, int srcWidth, int srcHeight,
								  int dstWidth, int dstHeight, bool normalMap)
		{
			int wratio = srcWidth / dstWidth;
			int hratio = srcHeight / dstHeight;

			// Valve's kernel is six DESTINATION pixels across, so at high reduction ratios it spans more
			// source texels than the image has and wraps over itself repeatedly. Below six destination
			// pixels the kernel is narrowed to the destination instead: at 1x1 that is one tap per source
			// texel, the average of the whole image, which is the only answer a 1x1 level can have anyway.
			// It also bounds the work at kKernelDiameter^2 * srcWidth * srcHeight per level rather than
			// letting it grow without limit.
			int diameter = Mathf.Min(kKernelDiameter, Mathf.Min(dstWidth, dstHeight));
			var kernel = NiceKernel(wratio, hratio, diameter);
			int kernelWidth = diameter * wratio;
			int kernelHeight = diameter * hratio;

			int baseY = (hratio >> 1) - ((hratio * diameter) >> 1);
			int baseX = (wratio >> 1) - ((wratio * diameter) >> 1);

			// Source coordinates wrap rather than clamp, as `srcX & (srcWidth - 1)`, which is why Valve
			// requires powers of two. A non-power-of-two texture would silently index outside the image,
			// so it is masked here the same way and the caller is responsible for the size.
			int maskX = srcWidth - 1;
			int maskY = srcHeight - 1;

			var dst = new Color32[dstWidth * dstHeight];
			var gamma = kGammaToLinear;
			float inverseGamma = 1.0f / kSourceGamma;

			Parallel.For(0, dstHeight, row =>
			{
				for (int column = 0; column < dstWidth; column++)
				{
					float red = 0.0f, green = 0.0f, blue = 0.0f, alpha = 0.0f;
					int srcY = (hratio * row) + baseY;
					for (int k = 0; k < kernelHeight; k++, srcY++)
					{
						int sy = srcY & maskY;
						int kernelRow = k * kernelWidth;
						int srcRow = sy * srcWidth;
						int srcX = (wratio * column) + baseX;
						for (int l = 0; l < kernelWidth; l++, srcX++)
						{
							float weight = kernel[kernelRow + l];
							if (weight == 0.0f)
								continue;
							var texel = src[srcRow + (srcX & maskX)];
							if (normalMap)
							{
								red += weight * texel.r;
								green += weight * texel.g;
								blue += weight * texel.b;
							}
							else
							{
								red += weight * gamma[texel.r];
								green += weight * gamma[texel.g];
								blue += weight * gamma[texel.b];
							}
							alpha += weight * texel.a;
						}
					}

					// A Lanczos kernel has negative lobes, so the total can land outside 0..255 on a hard
					// edge; pow() of a negative number is NaN, which would reach the texture as garbage.
					if (normalMap)
					{
						dst[(row * dstWidth) + column] = new Color32(Clamp(red), Clamp(green), Clamp(blue),
																	 Clamp(alpha));
					}
					else
					{
						dst[(row * dstWidth) + column] = new Color32(
							Clamp(255.0f * Mathf.Pow(Mathf.Max(0.0f, red) / 255.0f, inverseGamma)),
							Clamp(255.0f * Mathf.Pow(Mathf.Max(0.0f, green) / 255.0f, inverseGamma)),
							Clamp(255.0f * Mathf.Pow(Mathf.Max(0.0f, blue) / 255.0f, inverseGamma)),
							Clamp(alpha));
					}
				}
			});
			return dst;
		}
	}
}
