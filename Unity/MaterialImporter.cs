using System.IO;

using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	public static class MaterialImporter
	{
		/// <summary>
		/// The version of what this builds for a material (<see cref="CreateMaterial"/>,
		/// <see cref="CreateSkyboxMaterial"/>). Bump it whenever that changes: every material then
		/// rebuilds, in place, on its next import. A material also records the texture version it was
		/// built with (<see cref="Texture2DImporter.GeneratedVersion"/>), so a texture change rebuilds
		/// the materials as well. See <see cref="GeneratedAssetVersion"/>.
		/// </summary>
		public static readonly (string generator, int version) GeneratedVersion = ("material", 3);

		public static Material Import(GameResources gameResources, VMT sourceMaterial, string outputPath, bool isSprite = false)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".mat");
			string materialName = Path.GetFileNameWithoutExtension(destinationPath);
			var foundAsset = UnityAssets.Load<Material>(destinationPath);
			if (foundAsset != null)
			{
				if (!IsCurrent(foundAsset))
					Reconvert(gameResources, sourceMaterial, foundAsset, materialName, isSprite);
				return foundAsset;
			}

			foundAsset = CreateMaterial(gameResources, sourceMaterial, materialName, isSprite);
			UnityAssets.Save(foundAsset, destinationPath);
			StampCurrent(foundAsset);
			return foundAsset;
		}

		/// <summary>
		/// Rebuilds an <b>already imported</b> material in place from its VMT, and returns whether it
		/// was rebuilt.
		/// <para>
		/// This is how <see cref="Import"/> brings a material written by older code up to date. It
		/// writes onto the existing Material object rather than deleting and recreating the asset, so
		/// the GUID survives and every scene and prefab referencing it keeps working. Everything is
		/// copied, not just what looks different: a material whose shader, queue and keywords come out
		/// the same can still differ in any texture or value.
		/// </para>
		/// </summary>
		public static bool Reconvert(GameResources gameResources, VMT sourceMaterial, Material existing, string materialName, bool isSprite = false)
		{
			if (existing == null || sourceMaterial == null)
				return false;

			var rebuilt = CreateMaterial(gameResources, sourceMaterial, materialName, isSprite);
			if (rebuilt == null)
				return false;

			Overwrite(existing, rebuilt);
			return true;
		}

		/// <summary>
		/// Writes a freshly built material onto the asset it replaces, saves and stamps that asset, and
		/// destroys the built one.
		/// <para>
		/// The whole serialized material is copied, so the asset ends up exactly as a fresh import would
		/// write it. Copying it field by field did not: the <c>renderQueue</c> getter answers the shader's
		/// queue when the material has none of its own, so assigning it turned every "from shader" (-1)
		/// material into a fixed 2000 - found by rebuilding real materials in place and diffing them.
		/// Only the main object is written, so sub-assets of the .mat, such as ChiselSurfaceMetadata,
		/// survive.
		/// </para>
		/// </summary>
		internal static void Overwrite(Material existing, Material rebuilt)
		{
			try
			{
				// The name is not taken from the build: it is the asset's identity, and CreateMaterial
				// appends "_nocull" to the one it builds.
				var name = existing.name;
				UnityEditor.EditorUtility.CopySerialized(rebuilt, existing);
				existing.name = name;
				UnityEditor.EditorUtility.SetDirty(existing);
				UnityEditor.AssetDatabase.SaveAssetIfDirty(existing);
				StampCurrent(existing);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(rebuilt);
			}
		}

		static bool IsCurrent(Material material)
		{
			return GeneratedAssetVersion.IsCurrent(UnityEditor.AssetDatabase.GetAssetPath(material),
												   GeneratedVersion, Texture2DImporter.GeneratedVersion);
		}

		/// <summary>
		/// Records that the material is what the current code builds. The material has to be saved
		/// already: this reimports it from disk.
		/// </summary>
		static void StampCurrent(Material material)
		{
			if (material == null)
				return;

			var importer = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(material));
			if (importer == null)
				return;

			importer.userData = GeneratedAssetVersion.Stamp(importer.userData, GeneratedVersion, Texture2DImporter.GeneratedVersion);
			importer.SaveAndReimport();
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
			if (foundAsset != null && IsCurrent(foundAsset))
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

			var skybox = CreateSkyboxMaterial(gameResources, skyname, cubemap);
			if (skybox == null)
				return foundAsset;

			// Built by older code: rebuilt onto the same asset, so every scene using it keeps it.
			if (foundAsset != null)
			{
				Overwrite(foundAsset, skybox);
				return foundAsset;
			}

			UnityAssets.Save(skybox, destinationPath);
			StampCurrent(skybox);
			return skybox;
		}

		private const string _standardShaderName				= "Standard (Specular setup)";
		private const string _unlitShaderName					= "Unlit/Texture";
		private const string _unlitTransparentShaderName		= "Unlit/Transparent";
		private const string _unlitTransparentCutoutShaderName	= "Unlit/Transparent Cutout";
		private const string _additiveShaderName				= "Particles/Standard Unlit";

		private static Shader _standardShader;
		private static Shader _additiveShader;
		private static Shader _unlitShader;
		private static Shader _unlitTransparentShader;
		private static Shader _unlitTransparentCutoutShader;
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

			// When we have a Unity shader standing in for the Source shader this VMT asks for, use
			// it and carry the VMT's parameters across as written. Otherwise fall through to the
			// approximation with Unity's Standard shader below, which is unchanged.
			// Sprites are excluded: "sprite" is not a Source shader type, it is our own billboard
			// handling of .spr references.
			if (!isSprite)
			{
				var sourceShader = SourceShaderLookup.Find(sourceMaterial.MaterialTypeName);
				if (sourceShader != null)
					return CreateSourceShaderMaterial(gameResources, sourceMaterial, materialName, sourceShader);
			}

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
							// This used to be Unity's FX/Flare. That is a lens-flare shader and never depth
							// tests, so every material that landed here showed through walls. The unlit
							// Source shader with the VMT's own additive blend state is the real equivalent.
							var unlitSourceShader = SourceShaderLookup.Find("UnlitGeneric");
							if (unlitSourceShader != null)
								return CreateSourceShaderMaterial(gameResources, sourceMaterial, materialName, unlitSourceShader);
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
							// This used to be Unity's FX/Flare. That is a lens-flare shader and never depth
							// tests, so every material that landed here showed through walls. The unlit
							// Source shader with the VMT's own additive blend state is the real equivalent.
							var unlitSourceShader = SourceShaderLookup.Find("UnlitGeneric");
							if (unlitSourceShader != null)
								return CreateSourceShaderMaterial(gameResources, sourceMaterial, materialName, unlitSourceShader);
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
			Texture2D normalMap = SetMaterialTexture(gameResources, unityMaterial, "_BumpMap", sourceMaterial.BumpMapName, isNormalMap: true,
													 selfShadowed: sourceMaterial.BumpSelfShadowing == true);
			Texture2D selfIlluminationMap = SetMaterialTexture(gameResources, unityMaterial, "_EmissionMap", sourceMaterial.SelfIlluminationTexture);
			if (selfIlluminationMap == null)
				selfIlluminationMap = SetMaterialTexture(gameResources, unityMaterial, "_EmissionMap", sourceMaterial.SelfIlluminationMask);


			// The Standard fallback's nearest slot for an exponent map. This path can just as well end
			// on the unlit or additive shader, which has no specular slot at all, so a missing property
			// here is expected rather than a mistake. A material whose Source shader was found never gets
			// this far: CreateSourceShaderMaterial reads the map properly in ApplySourceSpecular.
			var setSpecMap = SetMaterialTexture(gameResources, unityMaterial, "_SpecGlossMap", sourceMaterial.PhongExponentTextureName,
												warnIfMissing: false) != null;
			if (!setSpecMap && unityMaterial.HasProperty("_SpecColor"))
				unityMaterial.SetColor("_SpecColor", UnityEngine.Color.black);

			var setNormalMapOn = normalMap != null;

			var setDetailTextureOn = false;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(gameResources, unityMaterial, "_DetailAlbedoMap", sourceMaterial.DetailTextureName) != null;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(gameResources, unityMaterial, "_DetailAlbedoMap", sourceMaterial.BaseTexture2Name) != null;
			setDetailTextureOn = setDetailTextureOn || SetMaterialTexture(gameResources, unityMaterial, "_DetailMask", sourceMaterial.BlendModulateTextureName) != null;

			if (SetMaterialTexture(gameResources, unityMaterial, "_DetailNormalMap", sourceMaterial.BumpMap2Name, isNormalMap: true,
								   selfShadowed: sourceMaterial.BumpSelfShadowing == true) != null)
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

		/// <summary>
		/// Builds a material against a Unity shader that stands in for the Source shader the VMT
		/// asked for (see <see cref="SourceShaderLookup"/>). Unlike the Standard-shader path below
		/// this does not have to approximate: the shader has a slot for each VMT parameter, so the
		/// values go across as written.
		/// </summary>
		private static Material CreateSourceShaderMaterial(GameResources gameResources, VMT sourceMaterial, string materialName, Shader shader)
		{
			var unityMaterial = new Material(shader)
			{
				name = materialName,
				hideFlags = HideFlags.DontUnloadUnusedAsset
			};

			var mainTexture = SetMaterialTexture(gameResources, unityMaterial, "_MainTex", sourceMaterial.BaseTextureName);
			if (sourceMaterial.Color != null)
				unityMaterial.SetColor("_Color", sourceMaterial.Color);

			// UnlitTwoTexture: $texture2 is multiplied over the base texture, with a transform of its own.
			if (unityMaterial.HasProperty("_Texture2"))
				SetMaterialTexture(gameResources, unityMaterial, "_Texture2", sourceMaterial.BaseTexture2Name, warnIfMissing: false);

			// TextureScroll proxies. Source's t axis runs down the image and the importer flips v, so the
			// second component changes sign on the way over.
			if (sourceMaterial.BaseTextureScroll.HasValue && unityMaterial.HasProperty("_BaseTextureScroll"))
				unityMaterial.SetVector("_BaseTextureScroll", new Vector4(sourceMaterial.BaseTextureScroll.Value.x, -sourceMaterial.BaseTextureScroll.Value.y));
			if (sourceMaterial.Texture2Scroll.HasValue && unityMaterial.HasProperty("_Texture2Scroll"))
				unityMaterial.SetVector("_Texture2Scroll", new Vector4(sourceMaterial.Texture2Scroll.Value.x, -sourceMaterial.Texture2Scroll.Value.y));

			if (SetMaterialTexture(gameResources, unityMaterial, "_BumpMap", sourceMaterial.BumpMapName, isNormalMap: true, warnIfMissing: false,
								   selfShadowed: sourceMaterial.BumpSelfShadowing == true) != null)
				unityMaterial.EnableKeyword("_NORMALMAP");

			// Set ahead of the texture, which the blend mode types as color or data (IsColorSlot).
			if (sourceMaterial.DetailBlendMode.HasValue)
				unityMaterial.SetFloat("_DetailBlendMode", (int)sourceMaterial.DetailBlendMode.Value);
			if (SetMaterialTexture(gameResources, unityMaterial, "_DetailTex", sourceMaterial.DetailTextureName, warnIfMissing: false) != null)
			{
				unityMaterial.EnableKeyword("_DETAIL");
				// $detailscale is a multiplier on the base texture's uvs, so it becomes the detail
				// texture's own tiling. A missing or zero axis means "same as the base".
				if (sourceMaterial.DetailScale.HasValue)
				{
					var scale = sourceMaterial.DetailScale.Value;
					if (scale.x == 0) scale.x = (scale.y == 0) ? 1 : scale.y;
					if (scale.y == 0) scale.y = scale.x;
					unityMaterial.SetTextureScale("_DetailTex", scale);
				}
				if (sourceMaterial.DetailBlendFactor.HasValue)
					unityMaterial.SetFloat("_DetailBlendFactor", sourceMaterial.DetailBlendFactor.Value);
			}

			// $selfillum: the emissive mask is $selfillummask if given, and otherwise the BASE
			// texture's alpha channel. That second case is the one worth being careful about - on
			// such a material the alpha is a mask, not opacity, so it must not also drive blending.
			if (sourceMaterial.SelfIllumination.HasValue && sourceMaterial.SelfIllumination.Value)
			{
				var maskTexture = SetMaterialTexture(gameResources, unityMaterial, "_SelfIllumMask", sourceMaterial.SelfIlluminationMask, warnIfMissing: false);
				if (maskTexture == null)
					maskTexture = SetMaterialTexture(gameResources, unityMaterial, "_SelfIllumMask", sourceMaterial.SelfIlluminationTexture, warnIfMissing: false);

				// With no explicit mask the mask IS the base texture's alpha - so if that texture has
				// no alpha channel there is no mask at all, and emitting albedo * 1 makes the whole
				// surface fully bright. Plenty of Source materials set $selfillum on a base texture
				// compiled as DXT1 (props_office/office_chair is one), and in that state the flag
				// simply has nothing to act on. Ask the texture we actually handed the shader rather
				// than the VTF, because that is what gets sampled.
				var haveMask = (maskTexture != null) ||
							   (mainTexture != null &&
								UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(mainTexture.graphicsFormat));
				if (haveMask)
				{
					unityMaterial.EnableKeyword("_SELFILLUM");
					unityMaterial.SetFloat("_SelfIllumFromBaseAlpha", (maskTexture == null) ? 1 : 0);
					// Set explicitly: the shader defaults this to black so a material NOT using
					// self-illum carries no tint, which means Source's "no $selfillumtint means full
					// strength" has to be spelled out here.
					unityMaterial.SetColor("_SelfIllumTint", (sourceMaterial.SelfIlluminationColor != null)
															  ? sourceMaterial.SelfIlluminationColor : Color.white);
					unityMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.AnyEmissive;
				}
			}

			// $envmap is almost always "env_cubemap", ie "reflect what is around this spot", which in
			// Unity is a reflection probe rather than a texture we sample - so reflection is left to
			// the lighting model. $envmapmask still has a job: it modulates the specular response
			// per-pixel, which matters for a material that also has $phong.
			if (SetMaterialTexture(gameResources, unityMaterial, "_EnvMapMask", sourceMaterial.EnvMapMaskTextureName, warnIfMissing: false) != null)
				unityMaterial.EnableKeyword("_ENVMAPMASK");

			ApplySourceSpecular(gameResources, unityMaterial, sourceMaterial, mainTexture);
			ApplySourceBlendState(unityMaterial, sourceMaterial);

			// $nocull, which the Standard shader cannot express at all. Source uses it for glass,
			// grates and foliage, all of which have to be visible from both sides.
			var noCull = sourceMaterial.NoCull.HasValue && sourceMaterial.NoCull.Value;
			unityMaterial.SetFloat("_Cull", (int)(noCull ? UnityEngine.Rendering.CullMode.Off
													     : UnityEngine.Rendering.CullMode.Back));
			return unityMaterial;
		}

		/// <summary>Maps the $phong* parameters onto the shader's specular/smoothness.</summary>
		private static void ApplySourceSpecular(GameResources gameResources, Material unityMaterial, VMT sourceMaterial, Texture2D mainTexture)
		{
			// PBR specular stays OFF. $envmap is a cubemap reflection modulated by $envmapmask, not a
			// specular lobe - treating its presence as full-strength white specular turned ordinary
			// surfaces into polished chrome. Reflection still happens through Unity's reflection
			// probes, which is the closer equivalent of env_cubemap anyway.
			unityMaterial.SetColor("_SpecColorTint", Color.black);
			unityMaterial.SetFloat("_SpecularScale", 0);
			unityMaterial.SetFloat("_Smoothness", 0);

			if (sourceMaterial.Phong != true)
				return;

			// $phong is a separate, additive highlight and the shader reproduces it directly: masked
			// per-pixel and shaped by $phongfresnelranges, so it is weak head-on and blooms at
			// grazing angles. That shaping is not optional detail - 1913 of the 1923 materials in
			// this game that set $phong also set $phongfresnelranges, and without it every one of
			// them reads as lit from all directions at once.
			unityMaterial.EnableKeyword("_PHONG");
			unityMaterial.SetColor("_SpecColorTint", PhongTintOf(sourceMaterial));
			unityMaterial.SetFloat("_PhongBoost", sourceMaterial.PhongBoost ?? 1.0f);
			unityMaterial.SetFloat("_PhongExponent", PhongExponentOf(sourceMaterial));
			unityMaterial.SetFloat("_PhongExponentFactor", sourceMaterial.PhongExponentFactor ?? 0.0f);

			// Source's default when unspecified is a flat 1 at every angle.
			var fresnel = sourceMaterial.PhongFresnelRanges ?? Vector3.one;
			unityMaterial.SetVector("_PhongFresnelRanges", fresnel);

			// Source masks the highlight with the NORMAL MAP's alpha, unless the material puts the mask in
			// the base texture's alpha ($basemapalphaphongmask, which also flattens the normal) or has no
			// normal map at all: skin_ps20b.fxc reads
			// fSpecMask = lerp(normalTexel.a, baseColor.a, $basemapalphaphongmask).
			unityMaterial.SetFloat("_PhongMaskFromBaseAlpha", 0);
			unityMaterial.DisableKeyword("_PHONGMASK");

			var bumpMapName = sourceMaterial.BumpMapName ?? sourceMaterial.NormalMapName;
			if (sourceMaterial.BaseMapAlphaPhongMask == true || string.IsNullOrEmpty(bumpMapName))
			{
				var baseHasAlpha = mainTexture != null &&
								   UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(mainTexture.graphicsFormat);
				unityMaterial.SetFloat("_PhongMaskFromBaseAlpha", baseHasAlpha ? 1 : 0);
			}
			else
			{
				// Unity re-encodes normal maps, so their alpha is imported as a mask texture of its own. A
				// normal map with no alpha leaves the mask unset, which is what Source samples there: 1.
				var phongMask = gameResources.ImportAlphaMask(bumpMapName);
				if (phongMask != null)
				{
					unityMaterial.SetTexture("_PhongMask", phongMask);
					unityMaterial.EnableKeyword("_PHONGMASK");
				}
			}

			// $phongexponenttexture: the exponent per pixel in its red channel, and how much of the
			// surface's own colour to tint the highlight with in its green. It is data, not colour, so it
			// goes in through the ordinary texture path, which imports everything linear unless a color
			// slot claims it (Texture2DImporter.MarkAsColor) - and this slot never does.
			unityMaterial.DisableKeyword("_PHONGEXPONENTMAP");
			unityMaterial.SetFloat("_PhongAlbedoTint", 0);
			if (SetMaterialTexture(gameResources, unityMaterial, "_PhongExponentMap",
								   sourceMaterial.PhongExponentTextureName) != null)
			{
				unityMaterial.EnableKeyword("_PHONGEXPONENTMAP");
				if (PhongAlbedoTintApplies(sourceMaterial))
					unityMaterial.SetFloat("_PhongAlbedoTint", 1);
			}

			// $phongwarptexture: the highlight looked up in a table rather than taken from the lobe
			// directly, which is what makes a surface iridescent. The fresnel is the lookup's second
			// coordinate, so with a warp the shader stops using it as a multiplier.
			unityMaterial.DisableKeyword("_PHONGWARP");
			if (SetMaterialTexture(gameResources, unityMaterial, "_PhongWarp",
								   sourceMaterial.PhongWarpTextureName) != null)
				unityMaterial.EnableKeyword("_PHONGWARP");
		}

		/// <summary>
		/// The phong tint Source ends up using. An all-zero $phongtint is not a black highlight: the
		/// engine reads all-zero as "the material named no tint" and uses white (skin_dx9_helper.cpp).
		/// </summary>
		internal static Color PhongTintOf(VMT sourceMaterial)
		{
			if (PhongTintIsUnset(sourceMaterial))
				return Color.white;
			Color tint = sourceMaterial.PhongTint;
			return new Color(tint.r, tint.g, tint.b, 1);
		}

		static bool PhongTintIsUnset(VMT sourceMaterial)
		{
			if (sourceMaterial.PhongTint == null)
				return true;
			Color tint = sourceMaterial.PhongTint;
			return tint.r == 0 && tint.g == 0 && tint.b == 0;
		}

		/// <summary>
		/// The phong exponent constant, or -1 meaning "read it from the exponent texture's red channel".
		/// Source only lets the constant win when it is positive, and when a material names no exponent
		/// texture the engine samples a white one - so no exponent at all means 1 + 149 = 150, a tight
		/// hotspot, rather than any mild default (skin_dx9_helper.cpp, skin_ps20b.fxc).
		/// </summary>
		internal static float PhongExponentOf(VMT sourceMaterial)
		{
			return (sourceMaterial.PhongExponentValue > 0) ? sourceMaterial.PhongExponentValue.Value : -1.0f;
		}

		/// <summary>
		/// Whether $phongalbedotint actually colours the highlight. Source honours it only alongside a
		/// $phongexponenttexture - the green channel is the mix - and only while no $phongtint has
		/// already claimed the tint. 578 of this game's materials set it without such a texture, where
		/// it does nothing at all, and tinting those would be a change the game never makes.
		/// </summary>
		internal static bool PhongAlbedoTintApplies(VMT sourceMaterial)
		{
			return sourceMaterial.PhongAlbedoTint == true &&
				   !string.IsNullOrEmpty(sourceMaterial.PhongExponentTextureName) &&
				   PhongTintIsUnset(sourceMaterial);
		}

		/// <summary>Sets the blend state the shader reads through [_SrcBlend]/[_DstBlend]/[_ZWrite],
		/// the same mechanism the Standard shader uses, so one shader covers every Source blend mode.</summary>
		private static void ApplySourceBlendState(Material unityMaterial, VMT sourceMaterial)
		{
			var blend = UnityEngine.Rendering.BlendMode.One;
			var blendTo = UnityEngine.Rendering.BlendMode.Zero;
			var zWrite = 1;
			var renderQueue = -1;

			if (sourceMaterial.HaveCutout)
			{
				// Alpha tested surfaces stay opaque: they write depth and are not sorted, the
				// cutout is done with clip() in the shader.
				unityMaterial.EnableKeyword("_ALPHATEST_ON");
				if (sourceMaterial.AlphaTestReference.HasValue)
					unityMaterial.SetFloat("_Cutoff", sourceMaterial.AlphaTestReference.Value);
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
			} else
			if (sourceMaterial.HaveAdditiveBlending)
			{
				blend = UnityEngine.Rendering.BlendMode.One;
				blendTo = UnityEngine.Rendering.BlendMode.One;
				zWrite = 0;
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			} else
			if (sourceMaterial.HaveTranslucency)
			{
				blend = UnityEngine.Rendering.BlendMode.SrcAlpha;
				blendTo = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
				zWrite = 0;
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			}

			unityMaterial.SetFloat("_SrcBlend", (int)blend);
			unityMaterial.SetFloat("_DstBlend", (int)blendTo);
			unityMaterial.SetFloat("_ZWrite", zWrite);
			unityMaterial.renderQueue = renderQueue;
		}

		/// <summary>
		/// Whether a material slot samples color, which Source reads as sRGB, rather than data.
		/// </summary>
		/// <remarks>
		/// Follows Source's own shaders (EnableSRGBRead in the stdshaders): the base texture is color;
		/// normal maps, $envmapmask, $selfillummask, $blendmodulatetexture and phong exponents are
		/// data. $detail is color only when it is added rather than modulated - VertexLitGeneric reads
		/// it as color for every mode but 0, LightmappedGeneric for mode 1 - and the Source shaders
		/// here add it from mode 0.5 up, so that is where the line is drawn. Standard's detail albedo
		/// is color because its x2 blend assumes an sRGB read.
		/// </remarks>
		public static bool IsColorSlot(Material material, string materialPropertyName)
		{
			switch (materialPropertyName)
			{
				case "_MainTex":
				case "_DetailAlbedoMap":
				// UnlitTwoTexture's second texture is color just like its first - it is multiplied over it.
				case "_Texture2":
					return true;
				case "_DetailTex":
					return material != null && material.HasProperty("_DetailBlendMode") &&
						   material.GetFloat("_DetailBlendMode") >= 0.5f;
				default:
					return false;
			}
		}

		/// <param name="isNormalMap">
		/// True when the VMT named this texture under a bump-map parameter. The caller is the only
		/// place that knows - a VTF's own normal flag is unreliable - so it is passed down here and
		/// the texture asset is re-imported as a normal map.
		/// </param>
		/// <param name="selfShadowed">
		/// The VMT said <c>$ssbump 1</c>, so the named texture is a self-shadowed bump map and needs
		/// decoding rather than merely re-typing. Same reasoning as <paramref name="isNormalMap"/>:
		/// only the VMT knows.
		/// </param>
		private static Texture2D SetMaterialTexture(GameResources gameResources, Material material, string materialPropertyName, string textureName, bool isNormalMap = false, bool warnIfMissing = true, bool selfShadowed = false)
		{
			if (string.IsNullOrEmpty(textureName) || !material)
				return null;

			if (!material.HasProperty(materialPropertyName))
			{
				// Not every Source shader equivalent has every slot - UnlitGeneric has no normal
				// map or specular - so the caller can say that a missing property is expected.
				if (warnIfMissing)
					Debug.LogWarning("Unknown property " + materialPropertyName + " on shader " + material.shader.name, material);
				return null;
			}


			var images = gameResources.ImportVTF(textureName, PackagePath.DefaultMaterialPaths);
			if (images == null || images.Length == 0)
			{
				return null;
			}

			var image = images[0];
			if (isNormalMap)
				Texture2DImporter.MarkAsNormalMap(image, selfShadowed);
			else if (IsColorSlot(material, materialPropertyName))
				Texture2DImporter.MarkAsColor(image);
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