using System.IO;

using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	public static class MaterialImporter
	{
		public static Material Import(GameResources gameResources, VmfMaterial sourceMaterial, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".mat");
			var foundAsset = UnityAssets.Load<Material>(destinationPath);
			if (foundAsset != null)
				return foundAsset;

			string materialName = Path.GetFileNameWithoutExtension(destinationPath);
			foundAsset = CreateUnityMaterial(gameResources, sourceMaterial, materialName);
			UnityAssets.Save(foundAsset, destinationPath);
			return foundAsset;
		}
		
		private const string _standardShaderName				= "Standard (Specular setup)";
		private const string _unlitShaderName					= "Unlit/Texture";
		private const string _unlitTransparentShaderName		= "Unlit/Transparent";
		private const string _unlitTransparentCutoutShaderName	= "Unlit/Transparent Cutout";
		private const string _premultiplyShaderName				= "Particles/Alpha Blended Premultiply";

		private static Shader _standardShader;
		private static Shader _unlitShader;
		private static Shader _unlitTransparentShader;
		private static Shader _unlitTransparentCutoutShader;
		private static Shader _premultiplyShader;

		private static Material CreateUnityMaterial(GameResources resources, VmfMaterial sourceMaterial, string materialName)
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

			Texture2D mainTexture = SetMaterialTexture(resources, unityMaterial, "_MainTex", sourceMaterial.BaseTextureName);
			Texture2D normalMap = SetMaterialTexture(resources, unityMaterial, "_BumpMap", sourceMaterial.BumpMapName);
			Texture2D selfIlluminationMap = SetMaterialTexture(resources, unityMaterial, "_EmissionMap", sourceMaterial.SelfIlluminationTexture);
			if (selfIlluminationMap == null)
				selfIlluminationMap = SetMaterialTexture(resources, unityMaterial, "_EmissionMap", sourceMaterial.SelfIlluminationMask);


			//var setSpecGlossMap	= SetMaterialTexture(resources, unityMaterial, "_MetallicGlossMap", PhongExponentTextureName) != null;
			var setSpecMap = SetMaterialTexture(resources, unityMaterial, "_SpecGlossMap", sourceMaterial.PhongExponentTextureName) != null;
			if (!setSpecMap && unityMaterial.HasProperty("_SpecColor"))
				unityMaterial.SetColor("_SpecColor", UnityEngine.Color.black);

			var setNormalMapOn = normalMap != null;

			var setDetailTextureOn = false;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(resources, unityMaterial, "_DetailAlbedoMap", sourceMaterial.DetailTextureName) != null;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(resources, unityMaterial, "_DetailAlbedoMap", sourceMaterial.BaseTexture2Name) != null;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(resources, unityMaterial, "_DetailMask", sourceMaterial.BlendModulateTextureName) != null;

			if (SetMaterialTexture(resources, unityMaterial, "_DetailNormalMap", sourceMaterial.BumpMap2Name) != null)
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

			if (shader == _standardShader)
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
		
		private static Texture2D SetMaterialTexture(GameResources resources, Material material, string materialPropertyName, string textureName)
		{
			if (string.IsNullOrEmpty(textureName) || !material)
				return null;

			if (!material.HasProperty(materialPropertyName))
			{
				Debug.LogWarning("Unknown property " + materialPropertyName + " on shader " + material.shader.name, material);
				return null;
			}

			var image = resources.ImportTexture(textureName);
			if (image == null)
			{
				return null;
			}

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
			string destinationPath = GameResources.GetOutputPath("materials/colors/" + color.ToString().Replace(',','_').Replace('.', '_') + ".mat");
			Material colorMaterial = UnityAssets.Load<Material>(destinationPath);
			if (colorMaterial == null)
			{
				colorMaterial = GenerateEditorColorMaterial(color);
				if (!colorMaterial)
					return null;
				UnityAssets.Save<Material>(colorMaterial, destinationPath);
			}
			return colorMaterial;
		}

		// TODO: support "skybox" in Chisel, where we can tag a special material as skybox, 
		//			and then replace it with the RenderSettings.skybox when building meshes
		public static Material GetSkyBoxMaterial()
		{
			var skybox = RenderSettings.skybox;
			if (!skybox)
				skybox = GetColorMaterial(Color.white);
			return skybox;
		}

		public static Vector2? GetMaterialResolution(Material unityMaterial)
		{
			if (!unityMaterial)
				return null;
			
			Texture2D texture = null;
			if (unityMaterial.HasProperty("_MainTex")) texture = unityMaterial.GetTexture("_MainTex") as Texture2D;
			if (!texture && unityMaterial.HasProperty("_FrontTex")) texture = unityMaterial.GetTexture("_FrontTex") as Texture2D;
			if (!texture && unityMaterial.HasProperty("_BackTex" )) texture = unityMaterial.GetTexture("_BackTex") as Texture2D;
			if (!texture && unityMaterial.HasProperty("_LeftTex" )) texture = unityMaterial.GetTexture("_LeftTex") as Texture2D;
			if (!texture && unityMaterial.HasProperty("_RightTex")) texture = unityMaterial.GetTexture("_RightTex") as Texture2D;
			if (!texture && unityMaterial.HasProperty("_UpTex"   )) texture = unityMaterial.GetTexture("_UpTex") as Texture2D;
			if (!texture && unityMaterial.HasProperty("_DownTex" )) texture = unityMaterial.GetTexture("_DownTex") as Texture2D;
			return !texture ? (Vector2?)Vector2.one : (Vector2?)new Vector2(texture.width, texture.height);
		}
	}
}