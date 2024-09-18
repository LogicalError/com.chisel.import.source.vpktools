using System.IO;

using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	public static class MaterialImporter
	{
		public static Material Import(GameResources gameResources, VMT sourceMaterial, string outputPath, bool isSprite = false)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".mat");
			var foundAsset = UnityAssets.Load<Material>(destinationPath);
			if (foundAsset != null)
				return foundAsset;

			string materialName = Path.GetFileNameWithoutExtension(destinationPath);
			foundAsset = CreateMaterial(gameResources, sourceMaterial, materialName, isSprite);
			UnityAssets.Save(foundAsset, destinationPath);
			return foundAsset;
		}

		internal static VMT ImportSkyboxSide(GameResources gameResources, string skyname)
		{
			var entry = gameResources.GetEntry(skyname, PackagePath.DefaultSkyBoxMaterialPaths);
			if (entry == null)
				return null;
			return gameResources.LoadVMT(entry);
		}

		public static Material ImportSkybox(GameResources gameResources, string skyname)
		{
			string outputPath = skyname;
			PackagePath.EnsurePathStart(ref outputPath, "materials/skybox");
			outputPath = Path.ChangeExtension(outputPath, string.Empty);
			
			var destinationPath = Path.ChangeExtension(outputPath, ".mat");
			destinationPath = PackagePath.GetOutputPath(destinationPath);
			var foundAsset = UnityAssets.Load<Material>(destinationPath);
			if (foundAsset != null)
				return foundAsset;

			var skysidename = skyname;
			if (skysidename.EndsWith($".{PackagePath.ExtensionVMT}"))
				skysidename = skysidename.Remove(skysidename.Length - (PackagePath.ExtensionVMT.Length + 1));

			var vmfMaterialFt = ImportSkyboxSide(gameResources, skysidename + "ft." + PackagePath.ExtensionVMT);
			var vmfMaterialBk = ImportSkyboxSide(gameResources, skysidename + "bk." + PackagePath.ExtensionVMT);
			var vmfMaterialLf = ImportSkyboxSide(gameResources, skysidename + "lf." + PackagePath.ExtensionVMT);
			var vmfMaterialRt = ImportSkyboxSide(gameResources, skysidename + "rt." + PackagePath.ExtensionVMT);
			var vmfMaterialUp = ImportSkyboxSide(gameResources, skysidename + "up." + PackagePath.ExtensionVMT);
			var vmfMaterialDn = ImportSkyboxSide(gameResources, skysidename + "dn." + PackagePath.ExtensionVMT);

			var frontVTF = gameResources.LoadVTF(vmfMaterialFt?.BaseTextureName ?? null, PackagePath.DefaultMaterialPaths);
			var backVTF  = gameResources.LoadVTF(vmfMaterialBk?.BaseTextureName ?? null, PackagePath.DefaultMaterialPaths);
			var upVTF    = gameResources.LoadVTF(vmfMaterialUp?.BaseTextureName ?? null, PackagePath.DefaultMaterialPaths);
			var downVTF  = gameResources.LoadVTF(vmfMaterialDn?.BaseTextureName ?? null, PackagePath.DefaultMaterialPaths);
			var leftVTF  = gameResources.LoadVTF(vmfMaterialLf?.BaseTextureName ?? null, PackagePath.DefaultMaterialPaths);
			var rightVTF = gameResources.LoadVTF(vmfMaterialRt?.BaseTextureName ?? null, PackagePath.DefaultMaterialPaths);

			PackagePath.EnsureDirectoriesExist(destinationPath);

			var cubemapPath = Path.ChangeExtension(destinationPath, ".png");
			var cubemap = Texture2DImporter.ImportSkybox(frontVTF, backVTF, upVTF, downVTF, leftVTF, rightVTF, cubemapPath);
			
			foundAsset = CreateSkyboxMaterial(gameResources, skyname, cubemap);
			UnityAssets.Save(foundAsset, destinationPath);
			return foundAsset;
		}

		private const string _standardShaderName				= "Standard (Specular setup)";
		private const string _unlitShaderName					= "Unlit/Texture";
		private const string _unlitTransparentShaderName		= "Unlit/Transparent";
		private const string _unlitTransparentCutoutShaderName	= "Unlit/Transparent Cutout";
		private const string _premultiplyShaderName				= "FX/Flare";
		private const string _additiveShaderName				= "Particles/Standard Unlit";

		private static Shader _standardShader;
		private static Shader _additiveShader;
		private static Shader _unlitShader;
		private static Shader _unlitTransparentShader;
		private static Shader _unlitTransparentCutoutShader;
		private static Shader _premultiplyShader;
		private static Shader _softParticleShader;

		private static Material CreateMaterial(GameResources gameResources, VMT sourceMaterial, string materialName, bool isSprite)
		{
			var haveCutout		 = sourceMaterial.HaveCutout;
			var translucency	 = sourceMaterial.HaveTranslucency;
			var additiveBlending = sourceMaterial.HaveAdditiveBlending;

			var complexShader = !string.IsNullOrEmpty(sourceMaterial.BumpMapName) ||
								!string.IsNullOrEmpty(sourceMaterial.SelfIlluminationMask) ||
								!string.IsNullOrEmpty(sourceMaterial.SelfIlluminationTexture) ||
								!string.IsNullOrEmpty(sourceMaterial.DetailTextureName);

			Shader shader;

			if (string.IsNullOrEmpty(sourceMaterial.MaterialTypeName))
				sourceMaterial.MaterialTypeName = "lightmappedgeneric";

			if (isSprite)
			{
				if (_softParticleShader == null)
				{
					_softParticleShader = Shader.Find("Particles/Additive (Soft) Camera Aligned");
				}
				shader = _softParticleShader;
			} else
			{
				switch (sourceMaterial.MaterialTypeName.ToLowerInvariant())
				{
					case "unlitgeneric":
					{
						if (!complexShader && translucency && additiveBlending)
						{
							if (!_premultiplyShader)
							{
								_premultiplyShader = Shader.Find(_premultiplyShaderName);
								if (!_premultiplyShader)
									Debug.LogWarning("premultiplyShader not found");
							}
							if (_premultiplyShader)
							{
								shader = _premultiplyShader;
								break;
							}
						}
						if (!complexShader && translucency && haveCutout)
						{
							if (!_unlitTransparentCutoutShader)
							{
								_unlitTransparentCutoutShader = Shader.Find(_unlitTransparentCutoutShaderName);
								if (!_unlitTransparentCutoutShader)
									Debug.LogWarning("unlitTransparentCutoutShader not found");
							}
							if (_unlitTransparentCutoutShader)
							{
								shader = _unlitTransparentCutoutShader;
								break;
							}
						}
						if (!complexShader && translucency)
						{
							if (!_unlitTransparentShader)
							{
								_unlitTransparentShader = Shader.Find(_unlitTransparentShaderName);
								if (!_unlitTransparentShader)
									Debug.LogWarning("unlitTransparentShader not found");
							}
							if (_unlitTransparentShader)
							{
								shader = _unlitTransparentShader;
								break;
							}
						}

						if (!complexShader && additiveBlending)
						{
							if (!_additiveShader)
							{
								_additiveShader = Shader.Find(_additiveShaderName);
								if (!_additiveShader)
									Debug.LogWarning("additiveShaderName not found");

							}
							if (_additiveShader)
							{
								shader = _additiveShader;
								break;
							}
						}

						if (!complexShader)
						{
							if (!_unlitShader)
							{
								_unlitShader = Shader.Find(_unlitShaderName);
								if (!_unlitShader)
									Debug.LogWarning("unlitShader not found");
							}
							if (_unlitShader)
							{
								shader = _unlitShader;
								break;
							}
						}

						if (!_standardShader)
						{
							_standardShader = Shader.Find(_standardShaderName);
							if (!_standardShader)
							{
								Debug.LogWarning("standardShader not found");
								return null;
							}
						}
						shader = _standardShader;
						break;
					}
					default:
					//case "vertexlitgeneric":
					//case "lightmappedgeneric":
					//case "worldvertextransition":
					{
						if (!complexShader && translucency && additiveBlending)
						{
							if (!_premultiplyShader)
							{
								_premultiplyShader = Shader.Find(_premultiplyShaderName);
								if (!_premultiplyShader)
									Debug.LogWarning("premultiplyShader not found");
							}
							if (_premultiplyShader)
							{
								shader = _premultiplyShader;
								break;
							}
						}

						if (!_standardShader)
						{
							_standardShader = Shader.Find(_standardShaderName);
							if (!_standardShader)
							{
								Debug.LogWarning("standardShader not found");
								return null;
							}
						}
						shader = _standardShader;
						break;
					}
				}
			}

			if (!shader)
			{
				Debug.Log("!shader");
				return null;
			}

			var unityMaterial = new Material(shader);
			if (!unityMaterial)
			{
				Debug.Log("!unityMaterial");
				return null;
			}
			unityMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
			unityMaterial.name = materialName.ToString();

			if (unityMaterial.HasProperty("_Glossiness")) unityMaterial.SetFloat("_Glossiness", 0);
			if (unityMaterial.HasProperty("_SmoothnessTextureChannel")) unityMaterial.SetInt("_SmoothnessTextureChannel", 0);

			Texture2D mainTexture = SetMaterialTexture(gameResources, unityMaterial, "_MainTex", sourceMaterial.BaseTextureName);
			Texture2D normalMap = SetMaterialTexture(gameResources, unityMaterial, "_BumpMap", sourceMaterial.BumpMapName);
			Texture2D selfIlluminationMap = SetMaterialTexture(gameResources, unityMaterial, "_EmissionMap", sourceMaterial.SelfIlluminationTexture);
			if (selfIlluminationMap == null)
				selfIlluminationMap = SetMaterialTexture(gameResources, unityMaterial, "_EmissionMap", sourceMaterial.SelfIlluminationMask);


			//var setSpecGlossMap	= SetMaterialTexture(resources, unityMaterial, "_MetallicGlossMap", PhongExponentTextureName) != null;
			var setSpecMap = SetMaterialTexture(gameResources, unityMaterial, "_SpecGlossMap", sourceMaterial.PhongExponentTextureName) != null;
			if (!setSpecMap && unityMaterial.HasProperty("_SpecColor"))
				unityMaterial.SetColor("_SpecColor", UnityEngine.Color.black);

			var setNormalMapOn = normalMap != null;

			var setDetailTextureOn = false;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(gameResources, unityMaterial, "_DetailAlbedoMap", sourceMaterial.DetailTextureName) != null;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(gameResources, unityMaterial, "_DetailAlbedoMap", sourceMaterial.BaseTexture2Name) != null;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(gameResources, unityMaterial, "_DetailMask", sourceMaterial.BlendModulateTextureName) != null;

			if (SetMaterialTexture(gameResources, unityMaterial, "_DetailNormalMap", sourceMaterial.BumpMap2Name) != null)
			{
				setDetailTextureOn = true;
				setNormalMapOn = true;
			}

			/*
			if (sourceMaterial.SelfIlluminationColor != null)
			{
				setEmissionOn = true;
				unityMaterial.SetColor("_EmissionColor", sourceMaterial.SelfIlluminationColor);
			}*/

			var selfIllumination = sourceMaterial.SelfIllumination.HasValue && sourceMaterial.SelfIllumination.Value && 
				(selfIlluminationMap != null || sourceMaterial.SelfIlluminationColor != null);
			var phong = sourceMaterial.Phong.HasValue && sourceMaterial.Phong.Value;

			var setEmissionOn = false;
			if (selfIllumination && !phong &&
				unityMaterial) // weird error where material gets destroyed while making it??
			{
				if (sourceMaterial.SelfIlluminationColor != null)
				{
					var color = sourceMaterial.SelfIlluminationColor;
					if (unityMaterial) // weird error where material gets destroyed while making it??
					{
						setEmissionOn = true;
						unityMaterial.SetColor("_EmissionColor", color);
					}
				}
				else
				{
					if (unityMaterial) // weird error where material gets destroyed while making it??
					{
						setEmissionOn = true;
						unityMaterial.SetColor("_EmissionColor", UnityEngine.Color.white);
					}
				}
				if ((mainTexture != null) &&
					(selfIlluminationMap == null) &&
					unityMaterial) // weird error where material gets destroyed while making it??
				{
					setEmissionOn = true;

					unityMaterial.SetTexture("_EmissionMap", mainTexture);
				}
			}

			if (!unityMaterial) // weird error where material gets destroyed while making it??
				return null;


			if (sourceMaterial.PhongExponentValue.HasValue)
			{
				var value = sourceMaterial.PhongExponentValue.Value / 255.0f;
				if (sourceMaterial.PhongTint != null)
				{
					sourceMaterial.PhongTint.r *= value;
					sourceMaterial.PhongTint.g *= value;
					sourceMaterial.PhongTint.b *= value;
				} else
				{
					sourceMaterial.PhongTint = new Color
					{
						r = value,
						g = value,
						b = value,
						a = 1.0f
					};
				}
			}

			if (sourceMaterial.PhongTint != null)
				unityMaterial.SetColor("_SpecColor", sourceMaterial.PhongTint);
			else
				unityMaterial.SetColor("_SpecColor", UnityEngine.Color.black);

			if (sourceMaterial.Color != null)
				unityMaterial.SetColor("_Color", sourceMaterial.Color);

			if (haveCutout && sourceMaterial.AlphaTestReference.HasValue)
				unityMaterial.SetFloat("_Cutoff", sourceMaterial.AlphaTestReference.Value);

			if ((haveCutout || translucency) ||
				(sourceMaterial.NoCull.HasValue && sourceMaterial.NoCull.Value))
			{
				unityMaterial.name += "_nocull";
				unityMaterial.SetFloat("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
			}

			if (_standardShader && shader == _standardShader)
			{
				if (haveCutout)
				{
					unityMaterial.SetFloat("_Mode", (int)BlendMode.Fade);
					ChangeRenderMode(unityMaterial, BlendMode.Fade);
				}
				else
				if (translucency)
				{
					unityMaterial.SetFloat("_Mode", (int)BlendMode.Transparent);
					ChangeRenderMode(unityMaterial, BlendMode.Transparent);
				}
			}
			if (_additiveShader && shader == _additiveShader)
			{
				unityMaterial.SetFloat("_Mode", 4); // additive
			}

			if (setNormalMapOn)
			{
				unityMaterial.EnableKeyword("_NORMALMAP");
				unityMaterial.SetFloat("_NORMALMAP", 1);
			}
			if (setEmissionOn)
			{
				unityMaterial.EnableKeyword("_EMISSION");
				unityMaterial.SetFloat("_EMISSION", 1);
				unityMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.AnyEmissive;
			}
			if (setDetailTextureOn)
			{
				unityMaterial.EnableKeyword("_DETAIL_MULX2");
				unityMaterial.SetFloat("_DETAIL_MULX2", 1);
			}
			/*
			if (setSpecGlossMap)
			{
				unityMaterial.EnableKeyword("_METALLICGLOSSMAP");
				unityMaterial.SetFloat("_METALLICGLOSSMAP", 1);
			}*/

			//unityMaterial.doubleSidedGI
			//unityMaterial.enableInstancing
			return unityMaterial;
		}

		private static Texture2D SetMaterialTexture(GameResources gameResources, Material material, string materialPropertyName, string textureName)
		{
			if (string.IsNullOrEmpty(textureName) || !material)
				return null;

			if (!material.HasProperty(materialPropertyName))
			{
				Debug.LogWarning("Unknown property " + materialPropertyName + " on shader " + material.shader.name, material);
				return null;
			}


			var images = gameResources.ImportVTF(textureName, PackagePath.DefaultMaterialPaths);
			if (images == null || images.Length == 0)
			{
				return null;
			}

			var image = images[0];
			try
			{
				material.SetTexture(materialPropertyName, image);
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex, material);
			}
			return image;
		}
		
		private enum BlendMode
		{
			Opaque,
			Cutout,
			Fade,
			Transparent
		}

		private static void ChangeRenderMode(Material standardShaderMaterial, BlendMode blendMode)
		{
			switch (blendMode)
			{
				case BlendMode.Opaque:
					standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
					standardShaderMaterial.SetInt("_ZWrite", 1);
					standardShaderMaterial.DisableKeyword("_ALPHATEST_ON");
					standardShaderMaterial.DisableKeyword("_ALPHABLEND_ON");
					standardShaderMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					standardShaderMaterial.renderQueue = -1;
					break;
				case BlendMode.Cutout:
					standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
					standardShaderMaterial.SetInt("_ZWrite", 1);
					standardShaderMaterial.EnableKeyword("_ALPHATEST_ON");
					standardShaderMaterial.DisableKeyword("_ALPHABLEND_ON");
					standardShaderMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					standardShaderMaterial.renderQueue = 2450;
					break;
				case BlendMode.Fade:
					standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
					standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					standardShaderMaterial.SetInt("_ZWrite", 0);
					standardShaderMaterial.DisableKeyword("_ALPHATEST_ON");
					standardShaderMaterial.EnableKeyword("_ALPHABLEND_ON");
					standardShaderMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					standardShaderMaterial.renderQueue = 3000;
					break;
				case BlendMode.Transparent:
					standardShaderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
					standardShaderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
					standardShaderMaterial.SetInt("_ZWrite", 0);
					standardShaderMaterial.DisableKeyword("_ALPHATEST_ON");
					standardShaderMaterial.DisableKeyword("_ALPHABLEND_ON");
					standardShaderMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
					standardShaderMaterial.renderQueue = 3000;
					break;
			} 
		}

		internal static Material GenerateEditorColorMaterial(Color color)
		{
			var name = "Color:" + color;

			var shader = Shader.Find("Unlit/Color");
			if (!shader)
				return null;

			var material = new Material(shader)
			{
				name = name.Replace(':', '_'),
				hideFlags = HideFlags.None | HideFlags.DontUnloadUnusedAsset
			};
			material.SetColor("_Color", color);
			return material;
		}

		public static Material GetColorMaterial(Color color)
		{
			string destinationPath = PackagePath.GetOutputPath("materials/colors/" + color.ToString().Replace(',','_').Replace('.', '_') + ".mat");
			Material colorMaterial = UnityAssets.Load<Material>(destinationPath);
			if (colorMaterial == null)
			{
				colorMaterial = GenerateEditorColorMaterial(color);
				if (!colorMaterial)
					return null;
				UnityAssets.Save(colorMaterial, destinationPath);
			}
			return colorMaterial;
		}

		private static Shader _skyboxShader;

		public static Material CreateSkyboxMaterial(GameResources gameResources, 
												    string materialName,
													Cubemap cubemap)
		{
			if (!_skyboxShader)
			{
				_skyboxShader = Shader.Find("Skybox/Surface Skybox");
				if (!_skyboxShader)
					return null;
			}

			var unityMaterial = new Material(_skyboxShader) {name = materialName};
			unityMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;

			try
			{
				unityMaterial.SetTexture("_MainTex", cubemap);

				// ... we use the rotation setting to rotate it back (cubemap has rotated surfaces), so it matches the original game
				unityMaterial.SetFloat("_Rotation", 270);
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex, unityMaterial);
			}

			return unityMaterial;
		}
	}
}