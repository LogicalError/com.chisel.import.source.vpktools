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

		[Tooltip("Build the mip chain. Off for a texture the VTF flagged NOMIP.")]
		public bool generateMipmaps = true;

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
			byte[] bytes;
			try
			{
				bytes = File.ReadAllBytes(ctx.assetPath);
			}
			catch (Exception exception)
			{
				ctx.LogImportError($"Could not read {ctx.assetPath}: {exception.Message}");
				return;
			}

			// LoadImage needs a texture to load into; its size and format are replaced by the file's.
			var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
			if (!source.LoadImage(bytes, markNonReadable: false))
			{
				UnityEngine.Object.DestroyImmediate(source);
				ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)} is not a readable image.");
				return;
			}

			int width = source.width, height = source.height;
			var level0 = source.GetPixels32();
			UnityEngine.Object.DestroyImmediate(source);

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
				levels = wantMips ? SourceMipmaps.Generate(level0, width, height, normalMap: false)
								  : new[] { level0 };
			}
			catch (Exception exception)
			{
				ctx.LogImportError($"Could not build mipmaps for {Path.GetFileName(ctx.assetPath)}: {exception.Message}");
				return;
			}

			var texture = new Texture2D(width, height, TextureFormat.RGBA32, levels.Length, linear: !sRGB);
			for (int level = 0; level < levels.Length; level++)
				texture.SetPixelData(levels[level], level);
			texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

			if (compress)
			{
				// Alpha decides the format exactly as Unity's own importer would: a texture with no alpha
				// spends half the memory in DXT1. Every level is compressed, not just level 0.
				bool hasAlpha = false;
				foreach (var texel in levels[0])
				{
					if (texel.a == 255)
						continue;
					hasAlpha = true;
					break;
				}
				EditorUtility.CompressTexture(texture, hasAlpha ? TextureFormat.DXT5 : TextureFormat.DXT1,
											  TextureCompressionQuality.Normal);
			}

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
}
