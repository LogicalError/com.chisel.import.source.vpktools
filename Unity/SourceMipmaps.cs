using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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
	/// Verified against textures Valve's own vtex compiled by predicting each stored level from level 0:
	/// 14 levels across two stock HL2 textures, none below 90% agreement on the alpha test, most above 98%.
	/// </para>
	/// <para>
	/// NOTE: Black Mesa's own textures are NOT built this way. Their chains are two-valued at every level
	/// with alpha coverage that GROWS with distance (fence_alpha: 0.137 -&gt; 0.414), a deliberate alpha-test
	/// dilation Valve's resampler does not do. So this reproduces Source, not every VTF shipping with the
	/// game - which is why <see cref="PreserveAlphaCoverage"/> exists as a separate, opt-in step.
	/// </para>
	/// </remarks>
	public static class SourceMipmaps
	{
		/// <summary>The gamma vtex passes for both source and destination (IVTFTexture::GenerateMipmaps).</summary>
		public const float kSourceGamma = 2.2f;

		/// <summary>Kernel diameter in DESTINATION pixels, as Valve measures it.</summary>
		const int kKernelDiameter = 6;

		/// <summary>
		/// Off resamples every tap through the gamma table, as each level did before (<see cref="ResampleJob"/>): what
		/// reading level 0 through it once (<see cref="LinearResampleJob"/>) is compared against.
		/// </summary>
		internal static bool LinearSourceEnabled = true;

		static readonly float[] kGammaToLinear = BuildGammaTable(kSourceGamma);

		// ConstructFloatGammaTable( table, srcGamma, 1.0f ): 255 * (i/255)^gamma, kept in 0..255 rather
		// than normalised, because the inverse in the job undoes exactly that.
		static float[] BuildGammaTable(float gamma)
		{
			var table = new float[256];
			for (int i = 0; i < 256; i++)
				table[i] = 255.0f * Mathf.Pow(i / 255.0f, gamma);
			return table;
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
		/// <param name="alphaTestReference">
		/// Above zero, run <see cref="PreserveAlphaCoverage"/> at this cutoff afterwards. Zero leaves alpha
		/// exactly as Source's filter produced it.
		/// </param>
		public static Color32[][] Generate(Color32[] level0, int width, int height, bool normalMap,
										   float alphaTestReference = 0.0f)
		{
			if (level0 == null || level0.Length != width * height)
				throw new ArgumentException($"level 0 is {level0?.Length ?? 0} texels, expected {width * height}");

			int levelCount = LevelCount(width, height);
			var levels = new Color32[levelCount][];
			levels[0] = level0;
			if (levelCount == 1)
				return levels;

			// Every level reads level 0 and writes its own buffer, so they are independent and all of them
			// are scheduled at once. That matters more than it looks: the deepest levels have only a handful
			// of destination texels, so parallelising WITHIN a level leaves most of the machine idle on them
			// while each still costs a full pass over the source. Across levels there is always work.
			var source = new NativeArray<Color32>(level0, Allocator.TempJob);
			var gamma = new NativeArray<float>(kGammaToLinear, Allocator.TempJob);
			var destinations = new NativeArray<Color32>[levelCount];
			var kernels = new NativeArray<float>[levelCount];
			var kernelTotals = new NativeArray<float>[levelCount];
			var kernelSpans = new NativeArray<int2>[levelCount];
			var handles = new NativeArray<JobHandle>(levelCount - 1, Allocator.Temp);
			// Level 0 through the gamma table once, for every level to read (LinearSourceEnabled)
			var linearSource = LinearSourceEnabled;
			NativeArray<float4> linear = default;
			JobHandle linearHandle = default;

			try
			{
				if (linearSource)
				{
					linear = new NativeArray<float4>(level0.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
					linearHandle = new LinearSourceJob
					{
						source = source,
						gammaToLinear = gamma,
						linear = linear,
						normalMap = normalMap,
					}.Schedule(level0.Length, 1024);
				}

				for (int level = 1; level < levelCount; level++)
				{
					int levelWidth = Mathf.Max(1, width >> level);
					int levelHeight = Mathf.Max(1, height >> level);
					int widthRatio = width / levelWidth;
					int heightRatio = height / levelHeight;

					// Valve's kernel is six DESTINATION pixels across, so at high reduction ratios it spans
					// more source texels than the image holds and wraps over itself. Below six destination
					// pixels it is narrowed to the destination instead: at 1x1 that is one tap per source
					// texel, the average of the whole image, which is the only answer a 1x1 level has. It
					// also bounds the work at kKernelDiameter^2 * width * height per level.
					int diameter = Mathf.Min(kKernelDiameter, Mathf.Min(levelWidth, levelHeight));
					int kernelWidth = diameter * widthRatio;
					int kernelHeight = diameter * heightRatio;

					kernels[level] = new NativeArray<float>(kernelWidth * kernelHeight, Allocator.TempJob,
														   NativeArrayOptions.UninitializedMemory);
					kernelTotals[level] = new NativeArray<float>(1, Allocator.TempJob);
					kernelSpans[level] = new NativeArray<int2>(kernelHeight, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
					destinations[level] = new NativeArray<Color32>(levelWidth * levelHeight, Allocator.TempJob,
																   NativeArrayOptions.UninitializedMemory);

					var kernelHandle = new NiceKernelJob
					{
						kernel = kernels[level],
						kernelWidth = kernelWidth,
						diameter = diameter,
						dx = 1.0f / widthRatio,
						dy = 1.0f / heightRatio,
					}.Schedule(kernels[level].Length, 256);

					var totalHandle = new KernelTotalJob
					{
						kernel = kernels[level],
						kernelWidth = kernelWidth,
						total = kernelTotals[level],
						spans = kernelSpans[level],
					}.Schedule(kernelHandle);

					if (linearSource)
					{
						handles[level - 1] = new LinearResampleJob
						{
							source = linear,
							kernel = kernels[level],
							kernelSpans = kernelSpans[level],
							kernelTotal = kernelTotals[level],
							destination = destinations[level],
							sourceWidth = width,
							destinationWidth = levelWidth,
							widthRatio = widthRatio,
							heightRatio = heightRatio,
							kernelWidth = kernelWidth,
							kernelHeight = kernelHeight,
							baseX = (widthRatio >> 1) - ((widthRatio * diameter) >> 1),
							baseY = (heightRatio >> 1) - ((heightRatio * diameter) >> 1),
							maskX = width - 1,
							maskY = height - 1,
							normalMap = normalMap,
							inverseGamma = 1.0f / kSourceGamma,
						}.Schedule(destinations[level].Length, 16, JobHandle.CombineDependencies(totalHandle, linearHandle));
						continue;
					}

					handles[level - 1] = new ResampleJob
					{
						source = source,
						gammaToLinear = gamma,
						kernel = kernels[level],
						kernelTotal = kernelTotals[level],
						destination = destinations[level],
						sourceWidth = width,
						destinationWidth = levelWidth,
						widthRatio = widthRatio,
						heightRatio = heightRatio,
						kernelWidth = kernelWidth,
						kernelHeight = kernelHeight,
						baseX = (widthRatio >> 1) - ((widthRatio * diameter) >> 1),
						baseY = (heightRatio >> 1) - ((heightRatio * diameter) >> 1),
						maskX = width - 1,
						maskY = height - 1,
						normalMap = normalMap,
						inverseGamma = 1.0f / kSourceGamma,
					}.Schedule(destinations[level].Length, 16, totalHandle);
				}

				JobHandle.CombineDependencies(handles).Complete();

				for (int level = 1; level < levelCount; level++)
					levels[level] = destinations[level].ToArray();
			}
			finally
			{
				handles.Dispose();
				if (source.IsCreated) source.Dispose();
				if (gamma.IsCreated) gamma.Dispose();
				if (linear.IsCreated) linear.Dispose();
				for (int level = 1; level < levelCount; level++)
				{
					if (destinations[level].IsCreated) destinations[level].Dispose();
					if (kernels[level].IsCreated) kernels[level].Dispose();
					if (kernelTotals[level].IsCreated) kernelTotals[level].Dispose();
					if (kernelSpans[level].IsCreated) kernelSpans[level].Dispose();
				}
			}

			if (alphaTestReference > 0.0f)
				PreserveAlphaCoverage(levels, alphaTestReference);
			return levels;
		}

		/// <summary>
		/// Scales each level's alpha so the same fraction of texels passes the alpha test as at level 0
		/// (Ignacio Castano, "Computing Alpha Mipmaps").
		/// </summary>
		/// <remarks>
		/// <para>
		/// Filtering alpha correctly still shrinks a cut-out with distance: a wire thinner than a texel
		/// averages down below the cutoff and stops being drawn at all, so a fence dissolves into loose
		/// pixels. Coverage is the fraction of texels at or above the cutoff; this bisects a multiplier for
		/// each level until its coverage matches level 0's, then applies it.
		/// </para>
		/// <para>
		/// This is NOT what Source does - Valve's resampler has no alpha-test step, and that was verified
		/// against vtex's own output rather than assumed. It is applied only where a material has claimed
		/// the texture as a cut-out, because Source keeps masks in base alpha far more often than opacity
		/// ($selfillum, $basealphaenvmapmask, $basemapalphaphongmask, $detail - 2673 opaque materials in
		/// this game's VMTs), and scaling one of those would corrupt it.
		/// </para>
		/// </remarks>
		public static void PreserveAlphaCoverage(Color32[][] levels, float reference)
		{
			if (levels == null || levels.Length < 2)
				return;

			byte cutoff = (byte)Mathf.Clamp(Mathf.RoundToInt(reference * 255.0f), 0, 255);
			float target = CoverageOf(levels[0], cutoff, 1.0f);
			if (target <= 0.0f)
				return;                     // nothing passes at level 0; there is no coverage to preserve

			for (int level = 1; level < levels.Length; level++)
			{
				// Coverage rises monotonically with the scale, so bisection converges. The upper bound is
				// generous rather than tuned: a wire one sixteenth of a texel wide needs a large multiplier
				// to survive, and a bound that is too small fails silently by clamping.
				float low = 1.0f, high = 32.0f, scale = 1.0f;
				for (int step = 0; step < 16; step++)
				{
					scale = 0.5f * (low + high);
					if (CoverageOf(levels[level], cutoff, scale) < target)
						low = scale;
					else
						high = scale;
				}

				// NEVER REDUCE. Castano's method preserves coverage exactly, which silently assumes coverage
				// FALLS as levels shrink - true at a 0.5 cutoff, false at a low one. At a 0.1 cutoff on a
				// chain-link fence, filtering spreads alpha so MORE texels clear the bar each level (0.142
				// at mip 0 rising to 0.519 by mip 4), and preserving that meant scaling alpha down 6.7x and
				// fading the fence out faster than doing nothing at all. The property actually wanted is
				// "never let the cut-out thin away", so a scale below one is a no-op.
				scale = Mathf.Max(1.0f, scale);
				if (scale - 1.0f < 0.01f)
					continue;
				var texels = levels[level];
				for (int i = 0; i < texels.Length; i++)
					texels[i].a = (byte)Mathf.Clamp(Mathf.RoundToInt(texels[i].a * scale), 0, 255);
			}
		}

		static float CoverageOf(Color32[] texels, byte cutoff, float scale)
		{
			if (texels == null || texels.Length == 0)
				return 0.0f;
			int passed = 0;
			for (int i = 0; i < texels.Length; i++)
			{
				if (texels[i].a * scale >= cutoff)
					passed++;
			}
			return (float)passed / texels.Length;
		}

		/// <summary>
		/// GenerateNiceFilter, imageloader.cpp: sinc(pi d) * 3 sin(pi d / 3) / (pi d) over the radius, zero
		/// past the diameter. Left UNNORMALISED - <see cref="ResampleJob"/> divides by the total instead,
		/// which is the same result and needs no second pass over the kernel.
		/// </summary>
		/// <remarks>
		/// The sample points sit half a source texel off centre, so d is never exactly zero and the sinc
		/// never divides by it. That is load-bearing, not incidental - centring the taps on the texels
		/// instead would put a 0/0 in the middle of the kernel.
		/// </remarks>
		[BurstCompile(CompileSynchronously = true)]
		struct NiceKernelJob : IJobParallelFor
		{
			[WriteOnly] public NativeArray<float> kernel;
			public int kernelWidth;
			public int diameter;
			public float dx, dy;

			public void Execute(int index)
			{
				int row = index / kernelWidth;
				int column = index - (row * kernelWidth);
				float y = -((diameter - dy) * 0.5f) + (dy * row);
				float x = -((diameter - dx) * 0.5f) + (dx * column);
				float d = math.sqrt((x * x) + (y * y));
				if (d > diameter * 0.5f)
				{
					kernel[index] = 0.0f;
					return;
				}
				float t = math.PI * d;
				kernel[index] = (math.sin(t) / t) * (3.0f * math.sin(t / 3.0f) / t);
			}
		}

		[BurstCompile(CompileSynchronously = true)]
		struct KernelTotalJob : IJob
		{
			[ReadOnly] public NativeArray<float> kernel;
			public int kernelWidth;
			[WriteOnly] public NativeArray<float> total;
			/// <summary>Each kernel row's first and last column with a weight that is not zero; (0, -1) for none.</summary>
			[WriteOnly] public NativeArray<int2> spans;

			public void Execute()
			{
				float sum = 0.0f;
				for (int i = 0; i < kernel.Length; i++)
					sum += kernel[i];
				total[0] = sum == 0.0f ? 1.0f : sum;

				for (int row = 0; row < spans.Length; row++)
				{
					int first = 0, last = -1;
					for (int column = 0; column < kernelWidth; column++)
					{
						if (kernel[row * kernelWidth + column] == 0.0f)
							continue;
						if (last < first)
							first = column;
						last = column;
					}
					spans[row] = new int2(first, last);
				}
			}
		}

		/// <summary>Level 0 as every level's taps read it: colour through the gamma table (raw for a normal map), alpha raw.</summary>
		[BurstCompile(CompileSynchronously = true)]
		struct LinearSourceJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<Color32> source;
			[ReadOnly] public NativeArray<float> gammaToLinear;
			[WriteOnly] public NativeArray<float4> linear;
			public bool normalMap;

			public void Execute(int index)
			{
				var texel = source[index];
				linear[index] = normalMap
					? new float4(texel.r, texel.g, texel.b, texel.a)
					: new float4(gammaToLinear[texel.r], gammaToLinear[texel.g], gammaToLinear[texel.b], texel.a);
			}
		}

		/// <summary>
		/// <see cref="ResampleJob"/>, reading level 0 through the gamma table once (<see cref="LinearSourceJob"/>) and each
		/// kernel row from its first weight to its last (<see cref="KernelTotalJob"/>'s spans): the same taps added in the
		/// same order, so the same sums. A zero weight between them adds a zero, which changes no sum: one starts at +0 and
		/// never becomes -0. What it saves is three table lookups and a test of the weight on every tap.
		/// </summary>
		[BurstCompile(CompileSynchronously = true)]
		struct LinearResampleJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float4> source;
			[ReadOnly] public NativeArray<float> kernel;
			[ReadOnly] public NativeArray<int2> kernelSpans;
			[ReadOnly] public NativeArray<float> kernelTotal;
			[WriteOnly] public NativeArray<Color32> destination;

			public int sourceWidth, destinationWidth;
			public int widthRatio, heightRatio;
			public int kernelWidth, kernelHeight;
			public int baseX, baseY;
			public int maskX, maskY;
			public bool normalMap;
			public float inverseGamma;

			public void Execute(int index)
			{
				int row = index / destinationWidth;
				int column = index - (row * destinationWidth);

				float4 sum = float4.zero;
				int sourceY = (heightRatio * row) + baseY;
				for (int k = 0; k < kernelHeight; k++, sourceY++)
				{
					// Source coordinates WRAP, as in ResampleJob
					var span = kernelSpans[k];
					int rowOffset = (sourceY & maskY) * sourceWidth;
					int kernelRow = k * kernelWidth;
					int sourceX = (widthRatio * column) + baseX + span.x;
					for (int l = span.x; l <= span.y; l++, sourceX++)
						sum += kernel[kernelRow + l] * source[rowOffset + (sourceX & maskX)];
				}

				float normalise = 1.0f / kernelTotal[0];
				sum *= normalise;
				destination[index] = Texels.Of(sum.x, sum.y, sum.z, sum.w, normalMap, inverseGamma);
			}
		}

		[BurstCompile(CompileSynchronously = true)]
		struct ResampleJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<Color32> source;
			[ReadOnly] public NativeArray<float> gammaToLinear;
			[ReadOnly] public NativeArray<float> kernel;
			[ReadOnly] public NativeArray<float> kernelTotal;
			[WriteOnly] public NativeArray<Color32> destination;

			public int sourceWidth, destinationWidth;
			public int widthRatio, heightRatio;
			public int kernelWidth, kernelHeight;
			public int baseX, baseY;
			public int maskX, maskY;
			public bool normalMap;
			public float inverseGamma;

			public void Execute(int index)
			{
				int row = index / destinationWidth;
				int column = index - (row * destinationWidth);

				float red = 0.0f, green = 0.0f, blue = 0.0f, alpha = 0.0f;
				int sourceY = (heightRatio * row) + baseY;
				for (int k = 0; k < kernelHeight; k++, sourceY++)
				{
					// Source coordinates WRAP, as `srcY & (srcHeight - 1)`, which is why Valve requires
					// powers of two; the caller guarantees the size.
					int rowOffset = (sourceY & maskY) * sourceWidth;
					int kernelRow = k * kernelWidth;
					int sourceX = (widthRatio * column) + baseX;
					for (int l = 0; l < kernelWidth; l++, sourceX++)
					{
						float weight = kernel[kernelRow + l];
						if (weight == 0.0f)
							continue;
						var texel = source[rowOffset + (sourceX & maskX)];
						if (normalMap)
						{
							red += weight * texel.r;
							green += weight * texel.g;
							blue += weight * texel.b;
						}
						else
						{
							red += weight * gammaToLinear[texel.r];
							green += weight * gammaToLinear[texel.g];
							blue += weight * gammaToLinear[texel.b];
						}
						alpha += weight * texel.a;
					}
				}

				float normalise = 1.0f / kernelTotal[0];
				red *= normalise; green *= normalise; blue *= normalise; alpha *= normalise;

				destination[index] = Texels.Of(red, green, blue, alpha, normalMap, inverseGamma);
			}
		}

		// What both resample jobs make of a destination texel's normalised sums
		static class Texels
		{
			internal static Color32 Of(float red, float green, float blue, float alpha, bool normalMap, float inverseGamma)
			{
				if (normalMap)
					return new Color32(Clamp(red), Clamp(green), Clamp(blue), Clamp(alpha));
				// A Lanczos kernel has negative lobes, so a hard edge can push the total below zero;
				// pow() of a negative is NaN, which would reach the texture as garbage.
				return new Color32(
					Clamp(255.0f * math.pow(math.max(0.0f, red) / 255.0f, inverseGamma)),
					Clamp(255.0f * math.pow(math.max(0.0f, green) / 255.0f, inverseGamma)),
					Clamp(255.0f * math.pow(math.max(0.0f, blue) / 255.0f, inverseGamma)),
					Clamp(alpha));
			}

			static byte Clamp(float value)
			{
				int rounded = (int)(value + 0.5f);
				return (byte)(rounded < 0 ? 0 : rounded > 255 ? 255 : rounded);
			}
		}
	}
}
