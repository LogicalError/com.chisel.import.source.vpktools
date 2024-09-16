using System.IO;

using UnityEngine;
using UnityEditor;

namespace Chisel.Import.Source.VPKTools
{
	public static class Texture2DImporter
	{
		public static Texture2D Import(VTF sourceTexture, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".png");
			var foundAsset = UnityAssets.Load<Texture2D>(destinationPath);
			if (foundAsset != null)
				return foundAsset;
			
			// Write texture as a png file
			SavePng(sourceTexture, destinationPath);

			// Import png file as an asset
			var assetPath = UnityAssets.GetAssetPath(destinationPath);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);
			
			// Configure the importer (can only be done after importing once) and re-import
			ConfigureTextureImporter(sourceTexture, assetPath);

			// Reload the asset
			return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
		}

		public static void SavePng(VTF texture, string destinationPath)
		{
			PackagePath.EnsureDirectoriesExist(destinationPath);
			var bytes = ImageConversion.EncodeArrayToPNG(texture.Pixels,
				//UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, 
				UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
				(uint)texture.Width, (uint)texture.Height);
			File.WriteAllBytes(destinationPath, bytes);
		}

		static bool CopyToTexture(VTF vtfTexture, out Texture2D unityTexture)
		{
			unityTexture = null;
			if (vtfTexture.Width == 0 || vtfTexture.Height == 0)
				return false;

			unityTexture = new Texture2D(vtfTexture.Width, vtfTexture.Height, TextureFormat.RGBA32, false);
			unityTexture.SetPixels(vtfTexture.Pixels);
			unityTexture.Apply();
			return true;
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