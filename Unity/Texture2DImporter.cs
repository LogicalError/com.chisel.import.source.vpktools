using System.IO;

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Chisel.Import.Source.VPKTools
{
	public static class Texture2DImporter
	{
		/// <summary>
		/// The version of what this writes for a texture: the pixels, the importer settings, and the
		/// conversions a material's claim applies to them (<see cref="MarkAsColor"/>,
		/// <see cref="MarkAsNormalMap"/>). Bump it whenever any of that changes. Every texture then
		/// rebuilds on its next import, and so does every material: a material records the texture
		/// version it was built with, because rebuilding a texture resets what its materials claimed it
		/// as, and a material claims its textures again as it rebuilds. See
		/// <see cref="GeneratedAssetVersion"/>.
		/// </summary>
		public static readonly (string generator, int version) GeneratedVersion = ("texture", 1);

		static bool IsCurrent(Texture texture)
		{
			return GeneratedAssetVersion.IsCurrent(AssetDatabase.GetAssetPath(texture), GeneratedVersion);
		}

		public static Texture2D[] Import(VTF sourceTexture, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".png");
			var foundAsset = UnityAssets.Load<Texture2D>(destinationPath);
			if (foundAsset != null && IsCurrent(foundAsset))
				return new Texture2D[] { foundAsset };

			var filePath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + "[0].png");
			foundAsset = UnityAssets.Load<Texture2D>(filePath);
			if (foundAsset != null && IsCurrent(foundAsset))
			{
				var frameTextureList = new List<Texture2D>();
				int index = 0;
				while (foundAsset != null)
				{
					frameTextureList.Add(foundAsset);
					index++;
					filePath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + $"[{index}].png");
					foundAsset = UnityAssets.Load<Texture2D>(filePath);
				}
				return frameTextureList.ToArray();
			}

			// Write texture as a png file - over the outdated one, if there is one, which keeps its .meta
			// and so its GUID and every reference to it
			var frameNames = SaveFramesAsPNG(sourceTexture, destinationPath);
			if (frameNames == null || frameNames.Length == 0)
				return null;

			var frameTextures = new Texture2D[frameNames.Length];
			for (int i = 0; i < frameNames.Length; i++)
			{
				// Import png file as an asset
				var assetPath = UnityAssets.GetAssetPath(frameNames[i]);
				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);

				// Configure the importer (can only be done after importing once) and re-import
				ConfigureTextureImporter(sourceTexture, assetPath);

				// Reload the asset
				frameTextures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
			}
			return frameTextures;
		}

		/// <summary>
		/// The version of the mask textures written from another texture's alpha channel (see
		/// <see cref="ImportAlphaMask"/>). Bump it when what one of them holds changes.
		/// </summary>
		public static readonly (string generator, int version) AlphaMaskVersion = ("alphamask", 1);

		// What a mask written from a texture's alpha is called, beside the texture it came from.
		internal const string kAlphaMaskSuffix = "_alphamask";

		/// <summary>
		/// Writes a texture's alpha channel as a mask texture of its own and returns it; null when there is
		/// no alpha to take.
		/// <para>
		/// Source keeps masks in alpha channels - the phong mask is the normal map's alpha - and Unity
		/// re-encodes normal maps (DXT5nm keeps X in the alpha), so that channel cannot be sampled from the
		/// imported normal map. Rather than import the normal map as something it is not, the channel is
		/// written beside it as data, and the material points at that.
		/// </para>
		/// </summary>
		public static Texture2D ImportAlphaMask(VTF sourceTexture, string outputPath)
		{
			if (sourceTexture == null || !sourceTexture.HasAlpha ||
				sourceTexture.Frames == null || sourceTexture.Frames.Length == 0 ||
				sourceTexture.Frames[0].Pixels == null)
				return null;

			return WriteAlphaMask(AlphaAsMask(sourceTexture.Frames[0].Pixels),
								  sourceTexture.Width, sourceTexture.Height, outputPath);
		}

		/// <summary>A texture's alpha channel as an opaque grey image.</summary>
		internal static Color[] AlphaAsMask(Color[] pixels)
		{
			var mask = new Color[pixels.Length];
			for (int i = 0; i < pixels.Length; i++)
				mask[i] = new Color(pixels[i].a, pixels[i].a, pixels[i].a, 1);
			return mask;
		}

		/// <summary>Writes mask pixels beside the texture they came from, and imports them as data.</summary>
		internal static Texture2D WriteAlphaMask(Color[] pixels, int width, int height, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, null) + kAlphaMaskSuffix + ".png";
			var foundAsset = UnityAssets.Load<Texture2D>(destinationPath);
			if (foundAsset != null &&
				GeneratedAssetVersion.IsCurrent(AssetDatabase.GetAssetPath(foundAsset), GeneratedVersion, AlphaMaskVersion))
				return foundAsset;

			PackagePath.EnsureDirectoriesExist(destinationPath);
			File.WriteAllBytes(destinationPath, ImageConversion.EncodeArrayToPNG(
				pixels, UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat, (uint)width, (uint)height));

			var assetPath = UnityAssets.GetAssetPath(destinationPath);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);
			if (AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter)
			{
				// A mask is data, not colour: read as sRGB, every value but 0 and 1 comes out wrong. It
				// carries no alpha of its own - the mask is in the colour channels - and it is never a normal
				// map, whichever slot ends up naming it.
				textureImporter.textureType         = TextureImporterType.Default;
				textureImporter.sRGBTexture         = false;
				textureImporter.alphaSource         = TextureImporterAlphaSource.None;
				textureImporter.alphaIsTransparency = false;
				textureImporter.userData = GeneratedAssetVersion.Stamp(textureImporter.userData, GeneratedVersion, AlphaMaskVersion);
				textureImporter.SaveAndReimport();
			}
			return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
		}

		public static Cubemap ImportSkybox(VTF frontVTF, VTF backVTF, VTF upVTF, VTF downVTF, VTF leftVTF, VTF rightVTF, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".png");
			var foundAsset = UnityAssets.Load<Cubemap>(destinationPath);
			if (foundAsset != null && IsCurrent(foundAsset))
				return foundAsset;

			// Write texture as a png file
			SaveCubemapAsPNG(frontVTF, backVTF, upVTF, downVTF, leftVTF, rightVTF, destinationPath);

			// Import png file as an asset
			var assetPath = UnityAssets.GetAssetPath(destinationPath);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);

			// Configure the importer (can only be done after importing once) and re-import
			ConfigureCubemapTextureImporter(assetPath);

			// Reload the asset
			return AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
		}

		static readonly Vector2Int[] placementRects = new Vector2Int[] { new (2, 1), new (0, 1), new (1, 2), new (1, 0), new (1, 1), new (3, 1) };

		public static void SaveCubemapAsPNG(VTF frontVTF, VTF backVTF, VTF upVTF, VTF downVTF, VTF leftVTF, VTF rightVTF, string destinationPath)
		{
			var vtffiles = new VTF[6] { frontVTF, backVTF, upVTF, downVTF, rightVTF, leftVTF };

			var size = 0;
			for (int i = 0; i < vtffiles.Length; i++)
			{
				var vtfFile = vtffiles[i];
				if (vtfFile == null)
					continue;

				size = Mathf.Max(size, vtfFile.Width);
				size = Mathf.Max(size, vtfFile.Height);
			}

			var sidePixels = new Color[6][];
			for (int i = 0; i < vtffiles.Length; i++)
			{
				var vtfFile = vtffiles[i];
				if (vtfFile == null || vtfFile.Frames == null || vtfFile.Frames.Length == 0)
				{
					sidePixels[i] = new Color[size * size];
					continue;
				}

				// TODO: make sure that all images (which are not null) are upscaled to the correct size, when they're not the correct size
				sidePixels[i] = vtfFile.Frames[0].Pixels;
			}

			bool isHDR = false;
			TextureFormat format = isHDR ? TextureFormat.RGBAFloat : TextureFormat.RGBA32;

			var cubeTexture = new Texture2D(size * 4, size * 3, format, false);
			for (int i = 0; i < 6; i++)
			{
				cubeTexture.SetPixels(placementRects[i].x * size, placementRects[i].y * size, size, size, sidePixels[i]);
			}

			// Save the texture to the specified path, and destroy the temporary object
			var bytes = isHDR ? cubeTexture.EncodeToEXR() : cubeTexture.EncodeToPNG();
			File.WriteAllBytes(destinationPath, bytes);
			Object.DestroyImmediate(cubeTexture);
		}

		public static string[] SaveFramesAsPNG(VTF texture, string destinationPath)
		{
			if (texture.Frames == null || texture.Frames.Length == 0)
				return null;

			PackagePath.EnsureDirectoriesExist(destinationPath);

			var frameNames = new string[texture.Frames.Length];
			if (texture.Frames.Length == 1)
			{
				// TODO: support both rgba8 and rgba float textures
				var bytes = ImageConversion.EncodeArrayToPNG(texture.Frames[0].Pixels,
					//UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 
					UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
					(uint)texture.Width, (uint)texture.Height);

				frameNames[0] = destinationPath;
				File.WriteAllBytes(destinationPath, bytes);
				return frameNames;
			}

			for (int i = 0; i < texture.Frames.Length; i++)
			{
				var bytes = ImageConversion.EncodeArrayToPNG(texture.Frames[i].Pixels,
					//UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 
					UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
					(uint)texture.Width, (uint)texture.Height);

				var filePath = Path.Combine(Path.GetDirectoryName(destinationPath), Path.GetFileNameWithoutExtension(destinationPath) + $"[{i}].png");
				frameNames[i] = filePath;
				File.WriteAllBytes(filePath, bytes);
			}
			return frameNames;
		}
		
		public static void ConfigureCubemapTextureImporter(string destinationPath)
		{
			var assetPath = Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, destinationPath));
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);

			TextureImporter textureImporter = TextureImporter.GetAtPath(assetPath) as TextureImporter;
			if (textureImporter == null)
				return;

			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);

			settings.textureShape = TextureImporterShape.TextureCube;
			// The faces are written exactly as the VTFs store them - gamma-encoded, like every color
			// texture (see MarkAsColor).
			settings.sRGBTexture = true;
			settings.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;

			// The faces were just written, so the cubemap is what the current code produces.
			textureImporter.userData = GeneratedAssetVersion.Stamp(textureImporter.userData, GeneratedVersion);

			textureImporter.SetTextureSettings(settings);
			textureImporter.SaveAndReimport();
		}

		// Stamped into the texture importer's userData once the pixel conversion below has been done,
		// so it happens exactly once per texture no matter how many materials claim it. The stamp
		// rather than "is it already typed NormalMap?" is the guard on purpose: textures imported
		// before this conversion existed ARE already typed NormalMap and still need converting, and
		// this is the only thing standing between them and a second, ruinous inversion.
		// It records work done to the pixels on disk; it is not a version. Never change the string:
		// every texture converted under it would be converted again. Changing the conversion changes
		// what a texture is, so it bumps GeneratedVersion instead, which rewrites the pixels from the
		// VTF and drops this stamp in the same write (see UserDataForRewrittenPixels).
		internal const string kNormalMapStamp = "chisel-sourcenormal-v1";

		/// <summary>
		/// The importer userData for pixels just written from the VTF: stamped current, and without the
		/// normal-map conversion stamp, which described the pixels these replaced. A material claiming
		/// the texture as a normal map then converts these exactly once, as it did those.
		/// </summary>
		internal static string UserDataForRewrittenPixels(string userData)
		{
			return GeneratedAssetVersion.Stamp(GeneratedAssetVersion.Remove(userData, kNormalMapStamp), GeneratedVersion);
		}

		// Valve's bumpvects.h: the three tangent-space directions an $ssbump's R/G/B channels carry
		// the lighting for. Source tangent space, so the result needs the same green flip below.
		static readonly Vector3[] kSourceBumpBasis =
		{
			new (-0.40824829f, -0.70710678f, 0.57735027f), // (-1/sqrt6, -1/sqrt2, 1/sqrt3)
			new (-0.40824829f,  0.70710678f, 0.57735027f), // (-1/sqrt6,  1/sqrt2, 1/sqrt3)
			new ( 0.81649658f,  0.00000000f, 0.57735027f), // ( 2/sqrt6,  0,       1/sqrt3)
		};

		/// <summary>
		/// Re-imports a texture as sRGB once a material has claimed it for a color slot (see
		/// MaterialImporter.IsColorSlot).
		/// </summary>
		/// <remarks>
		/// Decided by use, like normal maps, because nothing in the file can say. The .png holds the
		/// VTF's texels exactly as stored - compared against the decoded VTF they match to within DXT
		/// rounding - and a color texture stores them gamma-encoded; Source's own shaders then choose
		/// per sampler whether to decode (EnableSRGBRead). Imported linear in this Linear-space
		/// project, a mid-grey texel reads 2.3 times too bright and darker ones worse, which is the
		/// washed-out look. Only ever promotes: a texture any material samples as color is color,
		/// whatever else also uses it.
		/// </remarks>
		public static void MarkAsColor(Texture texture)
		{
			if (texture == null)
				return;

			var assetPath = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(assetPath))
				return;

			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
				return;

			// Idempotent, and a normal map is never color, whichever slot names it.
			if (textureImporter.sRGBTexture || textureImporter.textureType == TextureImporterType.NormalMap)
				return;

			textureImporter.sRGBTexture = true;
			textureImporter.SaveAndReimport();
		}

		/// <summary>
		/// Re-imports a texture as a normal map, converting the pixels out of Source's tangent-space
		/// conventions first. Called once a material has claimed the texture for a normal-map slot -
		/// which is the only reliable statement that it IS one, since a VTF's own normal flag is set
		/// on many textures that are not (see ConfigureTextureImporter).
		/// Unity's NormalMap type also forces the texture linear, which a normal map needs.
		/// </summary>
		/// <param name="selfShadowed">
		/// The VMT said <c>$ssbump 1</c>. An ssbump is NOT a normal map - it stores how much light
		/// reaches the texel from each of three fixed basis directions - so feeding it to a normal
		/// map slot unconverted produces nonsense. Only the VMT key can say: the filename convention
		/// disagrees with it on 62 of 253 materials, and the VTF's own SSBUMP flag is never set.
		/// </param>
		public static void MarkAsNormalMap(Texture2D texture, bool selfShadowed = false)
		{
			if (texture == null)
				return;

			var assetPath = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(assetPath))
				return;

			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
				return;

			// Idempotent: the same texture is claimed by every material that uses it, and a re-import
			// is expensive.
			if (textureImporter.userData != null && textureImporter.userData.Contains(kNormalMapStamp))
				return;

			if (ConvertSourceNormalMapFile(assetPath, selfShadowed))
				textureImporter.userData = string.IsNullOrEmpty(textureImporter.userData)
										 ? kNormalMapStamp
										 : textureImporter.userData + ";" + kNormalMapStamp;

			textureImporter.textureType = TextureImporterType.NormalMap;
			textureImporter.SaveAndReimport();
		}

		/// <summary>
		/// Rewrites the .png in place so it holds a Unity-convention tangent normal.
		/// <para>
		/// Source stores normal maps in the DirectX convention, where green measures -V while Unity
		/// measures +V. That was measured over this project's own imported set rather than assumed:
		/// correlating each base texture's luminance gradient (crevices read dark, from baked AO)
		/// against its normal map, using the red channel - which both conventions agree on - as a
		/// control, the agreement with Unity's convention falls monotonically as the control gets
		/// cleaner, reaching 0 of the 17 strongest pairs. So: invert green.
		/// </para>
		/// <para>
		/// Rewriting the FILE rather than compensating in the shader is deliberate. It keeps the
		/// asset correct for everything that reads it, including the materials that fall back to
		/// Unity's Standard shader, and writing the same path leaves the .meta - and therefore the
		/// GUID and every reference to it - untouched.
		/// </para>
		/// </summary>
		static bool ConvertSourceNormalMapFile(string assetPath, bool selfShadowed)
		{
			var fullPath = Path.GetFullPath(assetPath);
			if (!File.Exists(fullPath))
				return false;

			Texture2D scratch = null;
			try
			{
				// Loaded from the bytes rather than taken from the AssetDatabase so it is readable
				// regardless of the importer's settings, and unaffected by compression.
				scratch = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
				if (!scratch.LoadImage(File.ReadAllBytes(fullPath), markNonReadable: false))
					return false;

				var pixels = scratch.GetPixels32();
				for (int i = 0; i < pixels.Length; i++)
				{
					var p = pixels[i];
					if (selfShadowed)
					{
						// Reconstruct a normal from the three basis intensities. Approximate by
						// nature - the self-shadowing term is baked in and cannot be separated
						// again - but far closer than treating the channels as an xyz normal.
						var n = kSourceBumpBasis[0] * (p.r / 255.0f)
							  + kSourceBumpBasis[1] * (p.g / 255.0f)
							  + kSourceBumpBasis[2] * (p.b / 255.0f);
						n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.forward;
						p.r = (byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255.0f), 0, 255);
						p.g = (byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255.0f), 0, 255);
						p.b = (byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255.0f), 0, 255);
						p.a = 255;
					}
					// DirectX -> OpenGL. Exact in 8 bits: v encodes 2v/255-1, and 255-v encodes
					// its negation.
					p.g = (byte)(255 - p.g);
					pixels[i] = p;
				}

				scratch.SetPixels32(pixels);
				scratch.Apply(updateMipmaps: false);
				File.WriteAllBytes(fullPath, scratch.EncodeToPNG());
				return true;
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex);
				return false;
			}
			finally
			{
				if (scratch != null)
					Object.DestroyImmediate(scratch);
			}
		}

		public static void ConfigureTextureImporter(VTF sourceTexture, string destinationPath)
		{
			var assetPath = Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, destinationPath));

			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_HINT_DXT5) != (VTFImageFlag)0) stringBuilder.Append("HINT_DXT5 ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_NOLOD) != (VTFImageFlag)0) stringBuilder.Append("NOLOD ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_MINMIP) != (VTFImageFlag)0) stringBuilder.Append("MINMIP ");


			//Debug.Log($"assetpath: {assetPath}");
			TextureImporter textureImporter = TextureImporter.GetAtPath(assetPath) as TextureImporter;
			if (textureImporter == null)
				return;

			// A VTF's TEXTUREFLAGS_NORMAL is set on plenty of textures that are not normal maps,
			// so it cannot decide this. What a texture IS gets decided by how a material uses it:
			// the VMT names it under $bumpmap/$bumpmap2, and MarkAsNormalMap below is called from
			// there. Start every texture off as Default; a normal map is promoted when a material
			// claims it.
			textureImporter.textureType = TextureImporterType.Default;

			// TODO: handle these somehow
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_PROCEDURAL) != (VTFImageFlag)0) stringBuilder.Append("PROCEDURAL ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_VERTEXTEXTURE) != (VTFImageFlag)0) stringBuilder.Append("VERTEXTEXTURE ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_ENVMAP) != (VTFImageFlag)0) stringBuilder.Append("ENVMAP ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_SSBUMP) != (VTFImageFlag)0) stringBuilder.Append("SSBUMP ");


			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);

			// alphaSource decides whether the alpha channel is imported at all, which is safe to
			// decide from the VTF: it either has one or it does not.
			settings.alphaSource = sourceTexture.HasAlpha ? TextureImporterAlphaSource.FromInput
														  : TextureImporterAlphaSource.None;

			// alphaIsTransparency is a different question, and nothing in the VTF can answer it. It
			// makes Unity dilate the colour outward into transparent texels, rewriting the RGB it
			// ships - correct when the alpha really is opacity, destructive whenever it is not, and
			// in Source it very often is not: the base texture's alpha carries $selfillum masks,
			// $basealphaenvmapmask, $basemapalphaphongmask and detail blend factors. Only the VMT
			// knows, and by the time one is read the texture is already imported. So: never.
			settings.alphaIsTransparency = false;

			settings.mipmapEnabled = true;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_NOMIP) != (VTFImageFlag)0) settings.mipmapEnabled = false;

			settings.borderMipmap = false;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_BORDER) != (VTFImageFlag)0) settings.borderMipmap = true;

			// NOTE: TEXTUREFLAGS_ONEBITALPHA/EIGHTBITALPHA say the texture HAS an alpha channel, not
			//       that the channel is opacity, so they must not drive alphaIsTransparency - that is
			//       what alphaSource above is for.

			// Linear until a material claims the texture for a color slot (MarkAsColor). The VTF's
			// TEXTUREFLAGS_SRGB cannot decide it: it used to mean NOCOMPRESS, and in this game it is set
			// on only 191 of the 1468 textures materials sample as color, and on a few that hold masks.
			settings.sRGBTexture = false;

			settings.filterMode = FilterMode.Bilinear;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_POINTSAMPLE) != (VTFImageFlag)0) settings.filterMode = FilterMode.Point;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_TRILINEAR) != (VTFImageFlag)0) settings.filterMode = FilterMode.Trilinear;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_ANISOTROPIC) != (VTFImageFlag)0)
			{
				settings.filterMode = FilterMode.Trilinear;
				settings.aniso = 2;
			}

			settings.wrapMode = TextureWrapMode.Repeat;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPS) != (VTFImageFlag)0) settings.wrapModeU = TextureWrapMode.Clamp;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPT) != (VTFImageFlag)0) settings.wrapModeV = TextureWrapMode.Clamp;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPU) != (VTFImageFlag)0) settings.wrapModeW = TextureWrapMode.Clamp;

			// Only ever called for pixels Import has just written from the VTF.
			textureImporter.userData = UserDataForRewrittenPixels(textureImporter.userData);

			textureImporter.SetTextureSettings(settings);
			textureImporter.SaveAndReimport();
		}
	}

}