using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Imports a Source texture's .png, building the mipmaps the way Source builds them
	/// (<see cref="SourceMipmaps"/>) instead of letting Unity box-filter them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// It claims no extension of its own. A .png is an ordinary Unity texture until something assigns this
	/// importer to that one asset with <c>AssetDatabase.SetImporterOverride</c>, which
	/// <see cref="Texture2DImporter.Import"/> does for the textures it writes out of VTFs. Overriding the
	/// importer keeps the asset's path, so its GUID and every reference to it survive - which renaming the
	/// file to .vtf would also have done, via MoveAsset, but this way the file stays a .png that anything
	/// can open.
	/// </para>
	/// <para>
	/// The .png holds level 0 only. That is not a limitation worked around: Source regenerates every level
	/// from level 0 anyway (see SourceMipmaps), so level 0 is the whole input, and keeping only it means a
	/// hand-authored texture that never came from a VTF gets the same treatment.
	/// </para>
	/// <para>
	/// NORMAL MAPS DO NOT COME THROUGH HERE. <see cref="Texture2DImporter.MarkAsNormalMap"/> clears the
	/// override and lets Unity's own importer have them back, because Unity's NormalMap type re-encodes
	/// the texture (DXT5nm puts X in alpha) and every shader in the project unpacks that convention. The
	/// aliasing this exists to fix is an alpha-test problem, and a normal map has no alpha test, so the
	/// bounded change is the one worth making first.
	/// </para>
	/// </remarks>
	[ScriptedImporter(kVersion, null, new[] { "png" })]
	public sealed class SourceTextureImporter : ScriptedImporter
	{
		/// <summary>Raise to re-import every texture this importer owns: Unity re-runs a changed importer.</summary>
		public const int kVersion = 1;

		[Tooltip("The texels are gamma encoded. Decided by USE - a material claiming the texture for a colour " +
				 "slot - because nothing in the file can say; see Texture2DImporter.MarkAsColor.")]
		public bool sRGB;

		[Tooltip("Build the mip chain. Off only for a texture whose VTF really holds a single level.")]
		public bool generateMipmaps = true;

		[Tooltip("Above zero, this texture's alpha is a cut-out silhouette tested at this value. Set by a " +
				 "material claiming it; see Texture2DImporter.MarkAsAlphaTested.")]
		[Range(0.0f, 1.0f)] public float alphaTestReference;

		[Tooltip("Scale each mip level's alpha so the same fraction of texels passes the alpha test as at " +
				 "level 0 (Castano). OFF BY DEFAULT: it is a deliberate deviation - Source's own resampler " +
				 "does no such thing - and on this game's textures it did not read as an improvement. Turn " +
				 "it on per texture to try it. Does nothing unless alphaTestReference is set.")]
		public bool preserveAlphaCoverage;

		[Tooltip("Compress to DXT once the levels are built. Off keeps RGBA32, which costs four times the memory.")]
		public bool compress = true;

		public FilterMode filterMode = FilterMode.Bilinear;
		[Range(1, 16)] public int anisoLevel = 1;

		// Per axis, because a VTF clamps S and T independently (TEXTUREFLAGS_CLAMPS/CLAMPT) and collapsing
		// them to one mode would wrap a texture that was authored clamped, tiling its edge across a face.
		public TextureWrapMode wrapU = TextureWrapMode.Repeat;
		public TextureWrapMode wrapV = TextureWrapMode.Repeat;

		public override void OnImportAsset(AssetImportContext ctx)
		{
			// Each part is a step of the import batch running, if one is (ImportSteps): the textures' imports were 7.5 of
			// the 30 s a map's models took, with nothing to say which part of them
			byte[] bytes;
			try
			{
				using (ImportSteps.Step("texture imports: pngs read"))
					bytes = File.ReadAllBytes(ctx.assetPath);
			}
			catch (Exception exception)
			{
				ctx.LogImportError($"Could not read {ctx.assetPath}: {exception.Message}");
				return;
			}

			int width, height;
			Color32[] level0;
			using (ImportSteps.Step("texture imports: pngs decoded"))
			{
				// LoadImage needs a texture to load into; its size and format are replaced by the file's.
				var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
				if (!source.LoadImage(bytes, markNonReadable: false))
				{
					UnityEngine.Object.DestroyImmediate(source);
					ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)} is not a readable image.");
					return;
				}

				width = source.width;
				height = source.height;
				level0 = source.GetPixels32();
				UnityEngine.Object.DestroyImmediate(source);
			}

			// Valve's resampler indexes the source with `& (width - 1)`, which is only a wrap on a power of
			// two. Rather than produce quietly wrong texels, a texture that is not one keeps a single level.
			bool powerOfTwo = width > 0 && height > 0 &&
							  (width & (width - 1)) == 0 && (height & (height - 1)) == 0;
			bool wantMips = generateMipmaps && powerOfTwo && width > 1 && height > 1;
			if (generateMipmaps && !powerOfTwo)
				ctx.LogImportWarning($"{width}x{height} is not a power of two, so Source's filter cannot wrap " +
									 "over it; imported without mipmaps.");

			Color32[][] levels;
			try
			{
				using (ImportSteps.Step("texture imports: mipmaps"))
					levels = wantMips ? SourceMipmaps.Generate(level0, width, height, normalMap: false,
															   alphaTestReference: preserveAlphaCoverage
																				 ? alphaTestReference : 0.0f)
									  : new[] { level0 };
			}
			catch (Exception exception)
			{
				ctx.LogImportError($"Could not build mipmaps for {Path.GetFileName(ctx.assetPath)}: {exception.Message}");
				return;
			}

			Texture2D texture;
			using (ImportSteps.Step("texture imports: levels set"))
			{
				texture = new Texture2D(width, height, TextureFormat.RGBA32, levels.Length, linear: !sRGB);
				for (int level = 0; level < levels.Length; level++)
					texture.SetPixelData(levels[level], level);
				texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
			}

			using (ImportSteps.Step("texture imports: compressed"))
			{
				// Every level is compressed, not just level 0
				var format = FormatOf(levels[0], width, height, compress);
				if (format != TextureFormat.RGBA32)
					EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);
			}

			using (ImportSteps.Step("texture imports: finished"))
			{
				texture.name       = Path.GetFileNameWithoutExtension(ctx.assetPath);
				texture.filterMode = filterMode;
				texture.anisoLevel = anisoLevel;
				texture.wrapModeU  = wrapU;
				texture.wrapModeV  = wrapV;
				texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

				ctx.AddObjectToAsset("texture", texture);
				ctx.SetMainObject(texture);
			}
		}

		/// <summary>
		/// The format a texture is imported in. Compressed, alpha decides it exactly as Unity's own importer would: DXT5 with
		/// alpha, and DXT1, half the memory, without. But DXT works in blocks of 4x4 texels, and a texture whose sides are not
		/// both a multiple of 4 cannot be compressed: it stays RGBA32. Compressing it anyway failed with an error on every
		/// import (2x2 textures such as hand_dryer_button) and left it RGBA32 just the same.
		/// <para>
		/// Texture2DImporter's texture census asks this of a texture it has written but not yet imported, so a material built
		/// meanwhile reads the format the import is going to make.
		/// </para>
		/// </summary>
		internal static TextureFormat FormatOf(Color32[] level0, int width, int height, bool compress)
		{
			if (!compress || width % 4 != 0 || height % 4 != 0)
				return TextureFormat.RGBA32;
			foreach (var texel in level0)
			{
				if (texel.a != 255)
					return TextureFormat.DXT5;
			}
			return TextureFormat.DXT1;
		}
	}
}
