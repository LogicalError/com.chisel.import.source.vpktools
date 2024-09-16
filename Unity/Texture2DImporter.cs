using System.IO;

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Chisel.Import.Source.VPKTools
{
	public static class Texture2DImporter
	{
		public static Texture2D[] Import(VTF sourceTexture, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".png");
			var foundAsset = UnityAssets.Load<Texture2D>(destinationPath);
			if (foundAsset != null)
				return new Texture2D[] { foundAsset };

			var filePath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + "[0].png");
			foundAsset = UnityAssets.Load<Texture2D>(filePath);
			if (foundAsset != null)
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

			// Write texture as a png file
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

		public static string[] SaveFramesAsPNG(VTF texture, string destinationPath)
		{
			if (texture.Frames == null || texture.Frames.Length == 0)
				return null;

			PackagePath.EnsureDirectoriesExist(destinationPath);

			var frameNames = new string[texture.Frames.Length];
			if (texture.Frames.Length == 1)
			{
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

			// TODO: unfortunately VTF files seem to have normal flags set when they're not a normal
			//		 so we'll have to check how they're actually used in a material, and set the normal flag THEN
			/*
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_NORMAL) != (VTFImageFlag)0)
				textureImporter.textureType = TextureImporterType.NormalMap;
			else*/
				textureImporter.textureType = TextureImporterType.Default;

			textureImporter.alphaIsTransparency = sourceTexture.HasAlpha;
			if (!sourceTexture.HasAlpha)
				textureImporter.alphaSource = TextureImporterAlphaSource.None;

			// TODO: handle these somehow
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_PROCEDURAL) != (VTFImageFlag)0) stringBuilder.Append("PROCEDURAL ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_VERTEXTEXTURE) != (VTFImageFlag)0) stringBuilder.Append("VERTEXTEXTURE ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_ENVMAP) != (VTFImageFlag)0) stringBuilder.Append("ENVMAP ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_SSBUMP) != (VTFImageFlag)0) stringBuilder.Append("SSBUMP ");


			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);

			if (!sourceTexture.HasAlpha)
			{
				settings.alphaSource = TextureImporterAlphaSource.None;
			}
			else
			{
				settings.alphaSource = TextureImporterAlphaSource.FromInput;
				settings.alphaIsTransparency = sourceTexture.HasAlpha;
			}

			settings.mipmapEnabled = true;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_NOMIP) != (VTFImageFlag)0) settings.mipmapEnabled = false;

			settings.borderMipmap = false;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_BORDER) != (VTFImageFlag)0) settings.borderMipmap = true;

			settings.alphaIsTransparency = false;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_ONEBITALPHA) != (VTFImageFlag)0 ||
				(sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_EIGHTBITALPHA) != (VTFImageFlag)0)
				settings.alphaIsTransparency = true;

			settings.sRGBTexture = false;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_SRGB) != (VTFImageFlag)0) settings.sRGBTexture = true;

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

			textureImporter.SetTextureSettings(settings);
			textureImporter.SaveAndReimport();
		}
	}

}