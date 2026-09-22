using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Imports a VTF as Unity sees a texture, KEEPING VALVE'S OWN MIPMAPS.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A VTF carries the mipmap chain vtex produced, and for an alpha-tested surface that chain is authored, not
	/// filtered: the fraction of texels that pass the alpha test GROWS as the texture shrinks, so a chain-link fence
	/// becomes more solid with distance instead of dissolving. Measured on models/props_c17/fence_alpha (512x512,
	/// DXT5), fraction of texels at or above a 0.5 cutoff:
	/// </para>
	/// <code>
	///   mip      0      1      2      3      4
	///   Valve  0.137  0.168  0.221  0.284  0.414
	///   box    0.137  0.114  0.121  0.060  0.001
	/// </code>
	/// <para>
	/// The second row is what the texture pipeline produced before this importer: the .png it wrote could only carry
	/// the top level, so Unity regenerated the rest with a box filter, and by mip 4 one texel in a thousand survived
	/// the cutoff. That is the fence "becoming random pixels hanging in the air" at distance. Nothing in a shader can
	/// undo it, because by then the information is gone; TextureImporter's mipMapsPreserveCoverage would have held the
	/// line flat at 0.137, better but still not what the engine draws.
	/// </para>
	/// <para>
	/// For a block-compressed format the data is handed to Unity UNTOUCHED - Valve's own DXT blocks, every level - so
	/// what is drawn is bit-for-bit what vtex produced. Other formats are decoded per level.
	/// </para>
	/// <para>
	/// Assign it to an asset with <c>AssetDatabase.SetImporterOverride&lt;SourceTextureImporter&gt;(path)</c>, which
	/// keeps the asset's GUID, so everything already referencing the texture keeps referencing it.
	/// </para>
	/// </remarks>
	[ScriptedImporter(kVersion, "vtf")]
	public sealed class SourceTextureImporter : ScriptedImporter
	{
		/// <summary>Raise to re-import every VTF: Unity re-runs an importer whose version changed.</summary>
		public const int kVersion = 1;

		[Tooltip("The texels are gamma encoded. Decided by USE - a material claiming the texture for a colour slot - " +
				 "because nothing in the file can say; see Texture2DImporter.MarkAsColor.")]
		public bool sRGB;

		[Tooltip("Treat as a normal map. A VTF's own NORMAL flag is set on many textures that are not one, so this " +
				 "follows the VMT's slot rather than the file's flag.")]
		public bool normalMap;

		public FilterMode      filterMode = FilterMode.Bilinear;
		public TextureWrapMode wrapMode   = TextureWrapMode.Repeat;
		[Range(1, 16)] public int anisoLevel = 1;

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

			if (!VtfLevels.TryRead(bytes, out var levels, out var error))
			{
				ctx.LogImportError($"{Path.GetFileName(ctx.assetPath)}: {error}");
				return;
			}

			var texture = Build(levels, ctx);
			if (texture == null)
				return;

			texture.name       = Path.GetFileNameWithoutExtension(ctx.assetPath);
			texture.filterMode = filterMode;
			texture.wrapMode   = wrapMode;
			texture.anisoLevel = anisoLevel;
			texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

			ctx.AddObjectToAsset("texture", texture);
			ctx.SetMainObject(texture);
		}

		Texture2D Build(VtfLevels levels, AssetImportContext ctx)
		{
			// Unity stores mip 0 first and each level after it; a VTF stores the SMALLEST first and the largest last.
			// Getting that backwards produces a texture that is sharp close up and wrong at distance, or vice versa,
			// and looks like a filtering bug rather than an ordering one - which is why VtfLevels returns them in
			// Unity's order and a test pins it.
			if (levels.IsBlockCompressed)
			{
				var format = levels.BlockFormat;
				if (!SystemInfo.SupportsTextureFormat(format))
				{
					ctx.LogImportWarning($"{format} is not supported here; decoding instead.");
					return BuildDecoded(levels);
				}

				var texture = new Texture2D(levels.Width, levels.Height, format, levels.Count, linear: !sRGB);
				var raw = levels.RawLargestFirst();
				var data = texture.GetRawTextureData<byte>();
				if (data.Length != raw.Length)
				{
					// Unity's own idea of how big the chain should be disagreeing with the file's is the one failure
					// that would silently corrupt every texture, so it refuses rather than uploading a guess.
					ctx.LogImportError($"mip chain is {raw.Length} bytes, Unity expects {data.Length} " +
									   $"({levels.Width}x{levels.Height}, {levels.Count} levels, {format}). Not uploading.");
					UnityEngine.Object.DestroyImmediate(texture);
					return null;
				}
				data.CopyFrom(raw);
				return texture;
			}

			return BuildDecoded(levels);
		}

		Texture2D BuildDecoded(VtfLevels levels)
		{
			var texture = new Texture2D(levels.Width, levels.Height, TextureFormat.RGBA32, levels.Count, linear: !sRGB);
			for (int level = 0; level < levels.Count; level++)
				texture.SetPixelData(levels.DecodeLevel(level), level);
			return texture;
		}
	}
}
