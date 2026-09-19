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
		public static readonly (string generator, int version) GeneratedVersion = ("material", 12);

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

			var cubemapPath = PackagePath.GetOutputPath(PackagePath.GetTextureEntryName(Path.ChangeExtension(outputPath, ".png")));
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

		private const string _litShaderName = "Universal Render Pipeline/Lit";
		private const string _unlitShaderName = "Universal Render Pipeline/Unlit";
		private const string _particleUnlitShaderName = "Universal Render Pipeline/Particles/Unlit";

		private static Shader _litShader;
		private static Shader _unlitShader;
		private static Shader _particleUnlitShader;

		private static Shader FindFallbackShader(string shaderName)
		{
			var shader = Shader.Find(shaderName);
			if (!shader)
				Debug.LogWarning("URP shader not found: " + shaderName);

			return shader;
		}

		private static Shader GetLitShader()
		{
			if (!_litShader)
				_litShader = FindFallbackShader(_litShaderName);

			return _litShader;
		}

		private static Shader GetUnlitShader()
		{
			if (!_unlitShader)
				_unlitShader = FindFallbackShader(_unlitShaderName);

			return _unlitShader;
		}

		private static Shader GetParticleUnlitShader()
		{
			if (!_particleUnlitShader)
				_particleUnlitShader = FindFallbackShader(_particleUnlitShaderName);

			return _particleUnlitShader;
		}
		
		private static void SetFloatIfPresent(Material material, string property, float value)
		{
			if (material && material.HasProperty(property))
				material.SetFloat(property, value);
		}

		private static void SetIntIfPresent(Material material, string property, int value)
		{
			if (material && material.HasProperty(property))
				material.SetInt(property, value);
		}

		private static void ApplyURPFallbackSurfaceState(Material material, VMT sourceMaterial)
		{
			if (!material)
				return;

			var cutout		= sourceMaterial.HaveCutout;
			var transparent = sourceMaterial.HaveTranslucency;
			var additive	= sourceMaterial.HaveAdditiveBlending;

			if (cutout)
			{
				material.SetOverrideTag("RenderType", "TransparentCutout");
				// URP Surface Type:
				// 0 = Opaque
				// 1 = Transparent
				SetIntIfPresent(material, "_Surface", 0);

				// Alpha clipping.
				SetFloatIfPresent(material, "_AlphaClip", 1.0f);

				if (sourceMaterial.AlphaTestReference.HasValue)
					SetFloatIfPresent(material, "_Cutoff", sourceMaterial.AlphaTestReference.Value);

				SetIntIfPresent(material, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				SetIntIfPresent(material, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				SetIntIfPresent(material, "_ZWrite", 1);
				
				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
				
				material.EnableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
			} else			
			if (additive)
			{
				material.SetOverrideTag("RenderType", "Transparent");
				// URP Surface Type:
				// 0 = Opaque
				// 1 = Transparent
				SetIntIfPresent(material, "_Surface", 1);

				// URP Blend mode:
				// 0 = Alpha
				// 1 = Premultiply
				// 2 = Additive
				// 3 = Multiply
				SetIntIfPresent(material, "_Blend", 2);

				// Alpha clipping.
				SetFloatIfPresent(material, "_AlphaClip", 0.0f);

				SetIntIfPresent(material, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				SetIntIfPresent(material, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
				SetIntIfPresent(material, "_ZWrite", 0);

				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
				
				material.DisableKeyword("_ALPHATEST_ON");
				material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			} else
			if (transparent)
			{
				material.SetOverrideTag("RenderType", "Transparent");
				// URP Surface Type:
				// 0 = Opaque
				// 1 = Transparent
				SetIntIfPresent(material, "_Surface", 1);

				// URP:
				// 0 = Alpha
				// 1 = Premultiply
				// 2 = Additive
				// 3 = Multiply
				SetIntIfPresent(material, "_Blend", 0);

				// Alpha clipping.
				SetFloatIfPresent(material, "_AlphaClip", 0.0f);

				SetIntIfPresent(material, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
				SetIntIfPresent(material, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				SetIntIfPresent(material, "_ZWrite", 0);

				material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

				material.DisableKeyword("_ALPHATEST_ON");
				material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			}
			else
			{
				material.SetOverrideTag("RenderType", "Opaque");
				// URP Surface Type:
				// 0 = Opaque
				// 1 = Transparent
				SetIntIfPresent(material, "_Surface", 0);

				SetIntIfPresent(material, "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
				SetIntIfPresent(material, "_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
				SetIntIfPresent(material, "_ZWrite", 1);

				material.renderQueue = -1;

				material.DisableKeyword("_ALPHATEST_ON");
				material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
			}
		}

		private static void ApplyURPParticleColorMode(Material material, VMT sourceMaterial)
		{
			if (!material || !sourceMaterial.HaveAdditiveBlending || !material.HasProperty("_ColorMode"))
				return;

			// URP ParticleGUI.ColorMode.Additive. This controls how the particle vertex color
			// combines with its texture; it is separate from the material's additive framebuffer blend.
			material.SetFloat("_ColorMode", 1.0f);
			if (material.HasProperty("_BaseColorAddSubDiff"))
				material.SetVector("_BaseColorAddSubDiff", new Vector4(1, 0, 0, 0));
			material.DisableKeyword("_COLOROVERLAY_ON");
			material.DisableKeyword("_COLORCOLOR_ON");
			material.EnableKeyword("_COLORADDSUBDIFF_ON");
		}

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
				shader = GetParticleUnlitShader();

				if (!shader)
					return null;
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
						if (!complexShader)
						{
							shader = GetUnlitShader();

							if (shader)
								break;
						}

						shader = GetLitShader();
						if (!shader)
							return null;
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


						shader = GetLitShader();
						if (!shader)
							return null;
						 
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
			if (unityMaterial.HasProperty("_Smoothness"))
				unityMaterial.SetFloat("_Smoothness", 0);

			if (unityMaterial.HasProperty("_Metallic"))
				unityMaterial.SetFloat("_Metallic", 0);

			if (unityMaterial.HasProperty("_SpecularHighlights"))
				unityMaterial.SetFloat("_SpecularHighlights", 0);

			if (unityMaterial.HasProperty("_EnvironmentReflections"))
				unityMaterial.SetFloat("_EnvironmentReflections", 0);


			Texture2D mainTexture = SetMaterialTexture(gameResources, unityMaterial, "_BaseMap", sourceMaterial.BaseTextureName);
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

			if (!setSpecMap)
			{
				if (unityMaterial.HasProperty("_SpecColor"))
					unityMaterial.SetColor("_SpecColor", Color.black);

				if (unityMaterial.HasProperty("_Metallic"))
					unityMaterial.SetFloat("_Metallic", 0);

				if (unityMaterial.HasProperty("_Smoothness"))
					unityMaterial.SetFloat("_Smoothness", 0);
			}

			var setNormalMapOn = normalMap != null;

			var setDetailTextureOn = false;
			if (unityMaterial.HasProperty("_DetailAlbedoMap"))
			{
				setDetailTextureOn |=
					SetMaterialTexture(
						gameResources,
						unityMaterial,
						"_DetailAlbedoMap",
						sourceMaterial.DetailTextureName) != null;

				setDetailTextureOn |=
					SetMaterialTexture(
						gameResources,
						unityMaterial,
						"_DetailAlbedoMap",
						sourceMaterial.BaseTexture2Name) != null;
			}

			if (unityMaterial.HasProperty("_DetailMask"))
			{
				setDetailTextureOn |=
					SetMaterialTexture(
						gameResources,
						unityMaterial,
						"_DetailMask",
						sourceMaterial.BlendModulateTextureName) != null;
			}
			
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
				SetMaterialColor(unityMaterial, sourceMaterial.Color);

			unityMaterial.SetFloat("_AlphaClip", haveCutout ? 1.0f : 0.0f);

			if (haveCutout && sourceMaterial.AlphaTestReference.HasValue)
			{
				unityMaterial.SetFloat("_Cutoff", sourceMaterial.AlphaTestReference.Value);
			}

			if ((haveCutout || translucency) ||
				(sourceMaterial.NoCull.HasValue && sourceMaterial.NoCull.Value))
			{
				unityMaterial.name += "_nocull";
				unityMaterial.SetFloat("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
			}

			// Particle/Unlit uses the same URP surface and blend properties as the other
			// fallbacks. Skipping sprites here left glow materials opaque.
			ApplyURPFallbackSurfaceState(unityMaterial, sourceMaterial);
			if (isSprite)
				ApplyURPParticleColorMode(unityMaterial, sourceMaterial);

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

			var mainTexture = SetMaterialTexture(gameResources, unityMaterial, "_BaseMap", sourceMaterial.BaseTextureName);
			SetMaterialColor(unityMaterial, ShaderColor(ModulationColorOf(sourceMaterial)));

			// UnlitTwoTexture: $texture2 is multiplied over the base texture, with a transform of its own.
			if (unityMaterial.HasProperty("_Texture2"))
				SetMaterialTexture(gameResources, unityMaterial, "_Texture2", sourceMaterial.BaseTexture2Name, warnIfMissing: false);

			// TextureScroll proxies. Source's t axis runs down the image and the importer flips v, so the
			// second component changes sign on the way over.
			if (sourceMaterial.BaseTextureScroll.HasValue && unityMaterial.HasProperty("_BaseTextureScroll"))
				unityMaterial.SetVector("_BaseTextureScroll", new Vector4(sourceMaterial.BaseTextureScroll.Value.x, -sourceMaterial.BaseTextureScroll.Value.y));
			if (sourceMaterial.Texture2Scroll.HasValue && unityMaterial.HasProperty("_Texture2Scroll"))
				unityMaterial.SetVector("_Texture2Scroll", new Vector4(sourceMaterial.Texture2Scroll.Value.x, -sourceMaterial.Texture2Scroll.Value.y));

			// A material that takes its phong mask from the base texture's alpha is drawn by a skin shader that
			// ignores the normal map altogether, so it gets none (IgnoresNormalMap).
			if (!IgnoresNormalMap(sourceMaterial) &&
				SetMaterialTexture(gameResources, unityMaterial, "_BumpMap", BumpMapNameOf(sourceMaterial), isNormalMap: true, warnIfMissing: false,
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
					unityMaterial.SetColor("_SelfIllumTint", ShaderColor(SelfIllumTintOf(sourceMaterial)));
					unityMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.AnyEmissive;
				}
			}

			ApplySourceSpecular(gameResources, unityMaterial, sourceMaterial, mainTexture);
			ApplySourceEnvMap(gameResources, unityMaterial, sourceMaterial, mainTexture);
			ApplySourceVertexColor(gameResources, unityMaterial, sourceMaterial);
			ApplySourceBlendState(unityMaterial, sourceMaterial);

			if (string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "Water",
						  System.StringComparison.OrdinalIgnoreCase))
			{
				Color tint = sourceMaterial.RefractTint ?? sourceMaterial.FogColor ?? (RGBColor)new Color(0.2f, 0.5f, 0.4f);
				if (unityMaterial.HasProperty("_WaterTint"))
					unityMaterial.SetColor("_WaterTint", ShaderColor(tint));
				if (unityMaterial.HasProperty("_Opacity"))
					unityMaterial.SetFloat("_Opacity", Mathf.Clamp01(0.45f + (sourceMaterial.RefractAmount ?? 0.2f) * 0.5f));
				if (unityMaterial.HasProperty("_ReflectionStrength"))
					unityMaterial.SetFloat("_ReflectionStrength", Mathf.Clamp01(sourceMaterial.ReflectAmount ?? 0.2f));
			}

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
			// surfaces into polished chrome.
			unityMaterial.SetColor("_SpecColorTint", Color.black);
			unityMaterial.SetFloat("_SpecularScale", 0);
			unityMaterial.SetFloat("_Smoothness", 0);

			if (!DrawsPhong(sourceMaterial))
				return;

			// $phong is a separate, additive highlight and the shader reproduces it directly: masked
			// per-pixel and shaped by $phongfresnelranges, so it is weak head-on and blooms at
			// grazing angles. That shaping is not optional detail - 1913 of the 1923 materials in
			// this game that set $phong also set $phongfresnelranges, and without it every one of
			// them reads as lit from all directions at once.
			unityMaterial.EnableKeyword("_PHONG");
			unityMaterial.SetColor("_SpecColorTint", ShaderColor(PhongTintOf(sourceMaterial)));
			unityMaterial.SetFloat("_PhongBoost", sourceMaterial.PhongBoost ?? 1.0f);
			unityMaterial.SetFloat("_PhongExponent", PhongExponentOf(sourceMaterial));
			unityMaterial.SetFloat("_PhongExponentFactor", sourceMaterial.PhongExponentFactor ?? 0.0f);

			unityMaterial.SetVector("_PhongFresnelRanges", PhongFresnelRangesOf(sourceMaterial));

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

			// $rimlight: the part of Source's rim light a surface draws by itself - the ambient light along the view
			// ray - with its boost, and the exponent texture's alpha as a mask when $rimmask asks for it.
			var rimLight = DrawsRimLight(sourceMaterial);
			unityMaterial.SetFloat("_RimLight", rimLight ? 1 : 0);
			unityMaterial.SetFloat("_RimLightBoost", sourceMaterial.RimLightBoost ?? 1.0f);
			unityMaterial.SetFloat("_RimMask", (rimLight && sourceMaterial.RimMask == true &&
												unityMaterial.IsKeywordEnabled("_PHONGEXPONENTMAP")) ? 1 : 0);

			// $phongwarptexture: the highlight looked up in a table rather than taken from the lobe
			// directly, which is what makes a surface iridescent. The fresnel is the lookup's second
			// coordinate, so with a warp the shader stops using it as a multiplier.
			unityMaterial.DisableKeyword("_PHONGWARP");
			if (SetMaterialTexture(gameResources, unityMaterial, "_PhongWarp",
								   sourceMaterial.PhongWarpTextureName) != null)
				unityMaterial.EnableKeyword("_PHONGWARP");
		}

		/// <summary>How the game draws a material's $envmap: which cubemap, and the parts of the reflection term its shader uses.</summary>
		internal struct SourceEnvMap
		{
			/// <summary>The material reflects at all</summary>
			public bool Enabled;
			/// <summary>The cubemap the material names, or null for env_cubemap: the reflection probe nearest to the surface</summary>
			public string Cubemap;
			/// <summary>$envmaptint as the shader receives it</summary>
			public Color Tint;
			public Vector3 Contrast;
			public Vector3 Saturation;
			/// <summary>LightmappedGeneric's reflection head-on; 1 is no Fresnel falloff</summary>
			public float FresnelReflection;
			/// <summary>The skin shader's $envmapfresnel: how much $phongfresnelranges shapes the reflection</summary>
			public float PhongFresnel;
			/// <summary>What masks the reflection, one at most: x the normal map's alpha, y the base alpha, z one minus the base
			/// alpha, w the phong mask. None: unmasked.</summary>
			public Vector4 MaskWeights;
			/// <summary>$envmapmask multiplies the reflection</summary>
			public bool MaskTexture;
			/// <summary>The skin shader's $invertphongmask turns the mask around</summary>
			public bool InvertMask;
		}

		static readonly Vector4 kMaskNormalAlpha       = new(1, 0, 0, 0);
		static readonly Vector4 kMaskBaseAlpha         = new(0, 1, 0, 0);
		static readonly Vector4 kMaskInvertedBaseAlpha = new(0, 0, 1, 0);
		static readonly Vector4 kMaskPhong             = new(0, 0, 0, 1);

		/// <summary>
		/// What the game makes of a material's $envmap, shader by shader (SDK 2013):
		/// <list type="bullet">
		/// <item>LightmappedGeneric (lightmappedgeneric_dx9_helper.cpp, lightmappedgeneric_ps2_3_x.h) takes the tint as written,
		/// masks by the normal map's alpha ($normalmapalphaenvmapmask), by $envmapmask when there is no $bumpmap, and by one
		/// minus the base alpha ($basealphaenvmapmask, when the base texture has alpha), and has a Fresnel term
		/// ($fresnelreflection). Its fast path, taken unless contrast is neither 0 nor 1 while saturation isn't 1, or the
		/// Fresnel isn't 1, or a self-illum tint is set, reads contrast as 0 or 1 and saturation and Fresnel as 1.</item>
		/// <item>VertexLitGeneric without the skin shader, and UnlitGeneric (vertexlitgeneric_dx9_helper.cpp,
		/// vertexlit_and_unlit_generic(_bump)_ps2x.fxc), take the tint out of gamma space. With a $bumpmap only the normal
		/// map's alpha masks, and a material asking for $envmapmask or $basealphaenvmapmask instead reflects nothing; without
		/// one, $envmapmask and one minus the base alpha do. No Fresnel.</item>
		/// <item>VertexLitGeneric drawn by the skin shader, where it draws phong (skin_dx9_helper.cpp, skin_ps20b.fxc), takes
		/// the tint as written and masks by the phong mask with $normalmapalphaenvmapmask, by the base alpha without, turned
		/// around by $invertphongmask, and shapes by $phongfresnelranges as much as $envmapfresnel says. No contrast,
		/// saturation or $envmapmask.</item>
		/// </list>
		/// </summary>
		/// <param name="baseTextureHasAlpha">The base texture the material is drawn with has an alpha channel</param>
		internal static SourceEnvMap EnvMapOf(VMT sourceMaterial, bool baseTextureHasAlpha)
		{
			var result = new SourceEnvMap { Tint = Color.white, Contrast = Vector3.zero, Saturation = Vector3.one, FresnelReflection = 1 };
			var envMap = sourceMaterial.EnvMapTextureName;
			if (string.IsNullOrWhiteSpace(envMap))
				return result;

			var shader = SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName);
			var lightmapped = string.Equals(shader, "LightmappedGeneric", System.StringComparison.OrdinalIgnoreCase);
			var vertexLit   = string.Equals(shader, "VertexLitGeneric", System.StringComparison.OrdinalIgnoreCase);
			var unlit       = string.Equals(shader, "UnlitGeneric", System.StringComparison.OrdinalIgnoreCase);
			if (!lightmapped && !vertexLit && !unlit)
				return result;

			result.Enabled = true;
			result.Cubemap = string.Equals(envMap.Trim(), "env_cubemap", System.StringComparison.OrdinalIgnoreCase) ? null : envMap;

			Color tint = (sourceMaterial.EnvMapTint != null) ? (Color)sourceMaterial.EnvMapTint : Color.white;
			tint.a = 1;
			var contrast   = sourceMaterial.EnvMapContrast ?? Vector3.zero;
			var saturation = sourceMaterial.EnvMapSaturation ?? Vector3.one;
			var hasBump    = !string.IsNullOrEmpty(sourceMaterial.BumpMapName);
			var normalAlpha = sourceMaterial.NormalMapAlphaEnvMapMask == true;
			var baseAlpha   = sourceMaterial.BaseAlphaEnvMapMask.HasValue && sourceMaterial.BaseAlphaEnvMapMask.Value != 0;
			var hasMask     = !string.IsNullOrEmpty(sourceMaterial.EnvMapMaskTextureName);
			var hasBase     = !string.IsNullOrEmpty(sourceMaterial.BaseTextureName);

			if (lightmapped)
			{
				result.Tint = tint;
				var fresnel = sourceMaterial.FresnelReflection ?? 1.0f;
				var selfIllumTinted = sourceMaterial.SelfIllumination == true && sourceMaterial.SelfIlluminationColor != null &&
									  ((Color)sourceMaterial.SelfIlluminationColor != Color.white);
				var usingContrast = contrast.x != 0 && contrast.x != 1 && saturation.x != 1;
				if (usingContrast || fresnel != 1 || selfIllumTinted)
				{
					result.Contrast = contrast;
					result.Saturation = saturation;
					result.FresnelReflection = fresnel;
				}
				else
					result.Contrast = (contrast.x == 1) ? Vector3.one : Vector3.zero;

				if (normalAlpha)
					result.MaskWeights = hasBump ? kMaskNormalAlpha : Vector4.zero;
				else if (baseAlpha && hasBase && baseTextureHasAlpha)
					result.MaskWeights = kMaskInvertedBaseAlpha;
				result.MaskTexture = hasMask && !hasBump && string.IsNullOrEmpty(sourceMaterial.BumpMap2Name) && !normalAlpha && !baseAlpha;
				return result;
			}

			if (vertexLit && DrawsPhong(sourceMaterial))
			{
				result.Tint = tint;
				result.PhongFresnel = sourceMaterial.EnvMapFresnel ?? 0.0f;
				result.MaskWeights = normalAlpha ? kMaskPhong : kMaskBaseAlpha;
				result.InvertMask = sourceMaterial.InvertPhongMask == true;
				return result;
			}

			result.Tint = new Color(GammaToLinearConstant(tint.r), GammaToLinearConstant(tint.g), GammaToLinearConstant(tint.b), 1);
			result.Contrast = contrast;
			result.Saturation = saturation;
			if (vertexLit && hasBump)
			{
				if (normalAlpha)
					result.MaskWeights = kMaskNormalAlpha;
				else if (baseAlpha || hasMask)
					result.Enabled = false;
				return result;
			}
			if (hasMask)
				result.MaskTexture = true;
			else if (baseAlpha && hasBase)
				result.MaskWeights = kMaskInvertedBaseAlpha;
			return result;
		}

		/// <summary>Sets up the shader's reflection term for a material's $envmap (<see cref="EnvMapOf"/>).</summary>
		private static void ApplySourceEnvMap(GameResources gameResources, Material unityMaterial, VMT sourceMaterial, Texture2D mainTexture)
		{
			unityMaterial.DisableKeyword("_ENVMAP");
			var baseHasAlpha = mainTexture != null &&
							   UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(mainTexture.graphicsFormat);
			var envMap = EnvMapOf(sourceMaterial, baseHasAlpha);
			if (!envMap.Enabled)
				return;

			if (envMap.Cubemap != null)
			{
				// The game shows its error texture for a cubemap it can't find; nothing is reflected here
				var cubemap = gameResources.ImportCubemap(envMap.Cubemap);
				if (cubemap == null)
					return;
				unityMaterial.SetTexture("_EnvMap", cubemap);
			}
			unityMaterial.SetFloat("_EnvMapFromProbes", (envMap.Cubemap == null) ? 1 : 0);

			unityMaterial.EnableKeyword("_ENVMAP");
			unityMaterial.SetColor("_EnvMapTint", ShaderColor(envMap.Tint));
			unityMaterial.SetVector("_EnvMapContrast", envMap.Contrast);
			unityMaterial.SetVector("_EnvMapSaturation", envMap.Saturation);
			unityMaterial.SetFloat("_FresnelReflection", envMap.FresnelReflection);
			unityMaterial.SetFloat("_EnvMapPhongFresnel", envMap.PhongFresnel);
			unityMaterial.SetFloat("_EnvMapInvertMask", envMap.InvertMask ? 1 : 0);

			var weights = envMap.MaskWeights;
			if (weights.x > 0)
			{
				// The normal map's alpha, written beside it as for the phong mask; without one, alpha is 1 and masks nothing
				var normalAlpha = gameResources.ImportAlphaMask(sourceMaterial.BumpMapName);
				if (normalAlpha != null)
					unityMaterial.SetTexture("_PhongMask", normalAlpha);
				else
					weights = Vector4.zero;
			}
			unityMaterial.SetVector("_EnvMapMaskWeights", weights);

			unityMaterial.SetFloat("_EnvMapMaskTexture", 0);
			if (envMap.MaskTexture &&
				SetMaterialTexture(gameResources, unityMaterial, "_EnvMapMask", sourceMaterial.EnvMapMaskTextureName, warnIfMissing: false) != null)
				unityMaterial.SetFloat("_EnvMapMaskTexture", 1);
		}

		/// <summary>What a Source shader does with a mesh's vertex colour.</summary>
		[System.Flags]
		internal enum SourceVertexColor
		{
			None  = 0,
			/// <summary>The colour multiplies the base colour.</summary>
			Color = 1,
			/// <summary>The alpha multiplies opacity.</summary>
			Alpha = 2,
			/// <summary>The alpha is the weight $basetexture2 / $bumpmap2 are blended in by.</summary>
			Blend = 4,
		}

		/// <summary>
		/// What the Source shader a material asks for does with the mesh's vertex colour - which differs per
		/// shader (SDK 2013):
		/// <list type="bullet">
		/// <item>UnlitGeneric multiplies the base colour by it when the material sets $vertexcolor, and its
		/// opacity by the alpha when it sets $vertexalpha (vertexlit_and_unlit_generic_ps2x.fxc).</item>
		/// <item>LightmappedGeneric multiplies by it when the material sets $vertexcolor; and when the
		/// material has a second layer ($basetexture2 or $bumpmap2) the vertex ALPHA is that layer's blend
		/// weight rather than opacity (lightmappedgeneric_vs20.fxc) - which is how a displacement paints one
		/// texture into another. WorldVertexTransition is the same shader.</item>
		/// <item>VertexLitGeneric ignores both flags: its helper forces them off
		/// ("bHasVertexColor = bVertexLitGeneric ? false : ..."), and the skin shader it draws $phong with
		/// declares the stream but never reads it.</item>
		/// </list>
		/// </summary>
		internal static SourceVertexColor VertexColorOf(VMT sourceMaterial)
		{
			var shader = SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName);
			var vertexColor = sourceMaterial.VertexColor == true;

			if (string.Equals(shader, "UnlitGeneric", System.StringComparison.OrdinalIgnoreCase))
			{
				return (vertexColor ? SourceVertexColor.Color : SourceVertexColor.None) |
					   (sourceMaterial.VertexAlpha == true ? SourceVertexColor.Alpha : SourceVertexColor.None);
			}

			if (string.Equals(shader, "LightmappedGeneric", System.StringComparison.OrdinalIgnoreCase))
			{
				var blend = !string.IsNullOrEmpty(sourceMaterial.BaseTexture2Name) ||
							!string.IsNullOrEmpty(sourceMaterial.BumpMap2Name);
				var result = blend ? SourceVertexColor.Blend : SourceVertexColor.None;
				if (vertexColor)
					result |= SourceVertexColor.Color | (blend ? SourceVertexColor.None : SourceVertexColor.Alpha);
				return result;
			}

			return SourceVertexColor.None;
		}

		// The shaders take these as 0/1 floats rather than keywords (see ChiselSourceGeneric.cginc), with a
		// single keyword for "there is a second layer".
		private static void ApplySourceVertexColor(GameResources gameResources, Material unityMaterial, VMT sourceMaterial)
		{
			var use = VertexColorOf(sourceMaterial);
			unityMaterial.SetFloat("_VertexColor", (use & SourceVertexColor.Color) != 0 ? 1 : 0);
			unityMaterial.SetFloat("_VertexAlpha", (use & SourceVertexColor.Alpha) != 0 ? 1 : 0);

			unityMaterial.DisableKeyword("_VERTEXBLEND");
			unityMaterial.SetFloat("_BlendBase2", 0);
			unityMaterial.SetFloat("_BlendBump2", 0);
			unityMaterial.SetFloat("_BlendModulate", 0);
			unityMaterial.SetFloat("_MaskedBlending", sourceMaterial.MaskedBlending == true ? 1 : 0);
			var blendMask = BlendMaskTransformOf(sourceMaterial);
			unityMaterial.SetVector("_BlendMaskTransformU", blendMask.u);
			unityMaterial.SetVector("_BlendMaskTransformV", blendMask.v);
			if ((use & SourceVertexColor.Blend) == 0)
				return;

			var layers = 0;
			if (SetMaterialTexture(gameResources, unityMaterial, "_BaseTexture2", sourceMaterial.BaseTexture2Name) != null)
			{
				unityMaterial.SetFloat("_BlendBase2", 1);
				layers++;
			}
			if (SetMaterialTexture(gameResources, unityMaterial, "_BumpMap2", sourceMaterial.BumpMap2Name, isNormalMap: true,
								   selfShadowed: sourceMaterial.BumpSelfShadowing == true) != null)
			{
				unityMaterial.SetFloat("_BlendBump2", 1);
				layers++;
			}
			// A second layer the game does not ship blends in nothing here (Source shows its error texture), and
			// the keyword is a shader variant of its own, so it is only worth enabling when a layer did load.
			if (layers == 0)
				return;

			unityMaterial.EnableKeyword("_VERTEXBLEND");
			// $blendmodulatetexture sharpens the blend: green moves the transition, red widens it.
			if (SetMaterialTexture(gameResources, unityMaterial, "_BlendModulateTexture", sourceMaterial.BlendModulateTextureName) != null)
				unityMaterial.SetFloat("_BlendModulate", 1);
		}

		/// <summary>
		/// Source's rule for a colour constant it takes out of gamma space
		/// (SetModulationPixelShaderDynamicState_LinearColorSpace, SetPixelShaderConstantGammaToLinear in
		/// BaseVSShader.cpp): a component above 1 is an overbright and stays as it is, and the rest go
		/// through mathlib's GammaToLinear - a 256-entry pow(x, 2.2) table that returns exactly 1 from 0.95
		/// up and 0 below 0 (color_conversion.cpp).
		/// </summary>
		internal static float GammaToLinearConstant(float value)
		{
			if (value > 1.0f)
				return value;
			if (value < 0.0f)
				return 0.0f;
			if (value >= 0.95f)
				return 1.0f;
			// RoundFloatToInt rounds to nearest, ties to even, as the SSE conversion it is built on does.
			var index = (float)System.Math.Round(value * 255.0f, System.MidpointRounding.ToEven);
			return Mathf.Pow(index / 255.0f, 2.2f);
		}

		/// <summary>
		/// The tint a Source shader multiplies the base colour by: $color times $color2 (BaseShader.h:
		/// GetColorParameter), taken out of gamma space as a product. $alpha is the independent opacity
		/// modulation and is not gamma decoded.
		/// </summary>
		internal static Color ModulationColorOf(VMT sourceMaterial)
		{
			// RGBColor and Color convert into each other both ways, so each branch names its type.
			Color color  = (sourceMaterial.Color  != null) ? (Color)sourceMaterial.Color  : Color.white;
			Color color2 = (sourceMaterial.Color2 != null) ? (Color)sourceMaterial.Color2 : Color.white;
			return new Color(GammaToLinearConstant(color.r * color2.r),
							 GammaToLinearConstant(color.g * color2.g),
							 GammaToLinearConstant(color.b * color2.b),
							 Mathf.Clamp01(sourceMaterial.Alpha ?? 1.0f));
		}

		/// <summary>$selfillumtint, which Source hands to the shader as written (SetPixelShaderConstant),
		/// and which defaults to white.</summary>
		internal static Color SelfIllumTintOf(VMT sourceMaterial)
		{
			if (sourceMaterial.SelfIlluminationColor == null)
				return Color.white;
			Color tint = sourceMaterial.SelfIlluminationColor;
			return new Color(tint.r, tint.g, tint.b, 1);
		}

		/// <summary>
		/// What to give Material.SetColor so the shader receives <paramref name="value"/>. SetColor takes a
		/// colour in gamma space and, in a linear project, converts it before the shader sees it; the Source
		/// values here are already what the shader should see, so they go in pre-encoded - Color.gamma is the
		/// exact inverse of that conversion, overbright components included.
		/// </summary>
		internal static Color ShaderColor(Color value)
		{
			return (QualitySettings.activeColorSpace == ColorSpace.Linear) ? value.gamma : value;
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

		/// <summary>
		/// Whether the game draws this material's $phong at all. VertexLitGeneric draws phong with its skin
		/// shader, and picks that shader only when the material also has a $bumpmap - unless
		/// $basemapalphaphongmask or a $lightwarptexture asks for it anyway (WantsSkinShader,
		/// vertexlitgeneric_dx9_helper.cpp). Otherwise it draws plain VertexLitGeneric, which has no
		/// highlight: 77 of this game's materials set $phong that way. LightmappedGeneric keeps its phong
		/// as before - the SDK's has none, and the game's own shaders are not public - and UnlitGeneric has
		/// no lighting to add a highlight to.
		/// </summary>
		internal static bool DrawsPhong(VMT sourceMaterial)
		{
			if (sourceMaterial.Phong != true)
				return false;
			var shader = SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName);
			if (string.Equals(shader, "LightmappedGeneric", System.StringComparison.OrdinalIgnoreCase))
				return true;
			if (!string.Equals(shader, "VertexLitGeneric", System.StringComparison.OrdinalIgnoreCase))
				return false;
			return !string.IsNullOrEmpty(sourceMaterial.LightWarpTextureName) ||
				   sourceMaterial.BaseMapAlphaPhongMask == true ||
				   !string.IsNullOrEmpty(sourceMaterial.BumpMapName);
		}

		/// <summary>
		/// Whether the game draws this material's rim light: $rimlight is a combo of the skin shader, which exists only
		/// for a VertexLitGeneric material that draws phong (skin_dx9_helper.cpp: bHasRimLight requires bHasPhong).
		/// </summary>
		internal static bool DrawsRimLight(VMT sourceMaterial)
		{
			return sourceMaterial.RimLight == true && DrawsPhong(sourceMaterial) &&
				   string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "VertexLitGeneric",
								 System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// The transform $blendmodulatetexture is sampled with, as the rows the shader takes (u, v; the fourth
		/// component is the translation). LightmappedGeneric applies $blendmasktransform only with $maskedblending
		/// and a $bumptransform that is not identity - lightmappedgeneric_dx9_helper.cpp tests the bump transform
		/// there - and otherwise uses the base texture's coordinates as they are. (A material that leaves that fast
		/// path for another reason reads a constant nothing set; identity is the only defined choice.)
		/// </summary>
		internal static (Vector4 u, Vector4 v) BlendMaskTransformOf(VMT sourceMaterial)
		{
			var matrix = Matrix4x4.identity;
			if (sourceMaterial.MaskedBlending == true && sourceMaterial.BlendMaskTransform.HasValue &&
				sourceMaterial.BumpTransform.HasValue && !TextParser.IsIdentity(sourceMaterial.BumpTransform.Value))
				matrix = sourceMaterial.BlendMaskTransform.Value;
			return (new Vector4(matrix.m00, matrix.m01, 0, matrix.m03), new Vector4(matrix.m10, matrix.m11, 0, matrix.m13));
		}

		/// <summary>
		/// The normal map a material is drawn with: its $bumpmap, or for the Cable shader, whose parameter has a default of
		/// its own, cable/cablenormalmap (cable_dx9.cpp).
		/// </summary>
		internal static string BumpMapNameOf(VMT sourceMaterial)
		{
			if (string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "Water",
						  System.StringComparison.OrdinalIgnoreCase) &&
				!string.IsNullOrEmpty(sourceMaterial.NormalMapName))
				return sourceMaterial.NormalMapName;
			if (!string.IsNullOrEmpty(sourceMaterial.BumpMapName))
				return sourceMaterial.BumpMapName;
			return string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "Cable", System.StringComparison.OrdinalIgnoreCase)
				? "cable/cablenormalmap" : null;
		}

		/// <summary>
		/// Whether the game ignores this material's normal map: the skin shader that draws a
		/// VertexLitGeneric $phong material flattens the normal by $basemapalphaphongmask
		/// (skin_ps20b.fxc), since that material's normal map is not where its phong mask lives.
		/// </summary>
		internal static bool IgnoresNormalMap(VMT sourceMaterial)
		{
			return sourceMaterial.BaseMapAlphaPhongMask == true && DrawsPhong(sourceMaterial) &&
				   string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "VertexLitGeneric",
								 System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// $phongfresnelranges [facing middle grazing], or what the game uses when a material leaves it out:
		/// [0 0.5 1]. skin_dx9_helper.cpp starts from the constant { 1, 0.5, 1 }, which is those three
		/// already in the shader's own encoding ((middle - facing) * 2, middle, (grazing - middle) * 2).
		/// </summary>
		internal static Vector3 PhongFresnelRangesOf(VMT sourceMaterial)
		{
			return sourceMaterial.PhongFresnelRanges ?? new Vector3(0.0f, 0.5f, 1.0f);
		}

		/// <summary>Sets the blend state the shader reads through [_SrcBlend]/[_DstBlend]/[_ZWrite],
		/// the same mechanism the Standard shader uses, so one shader covers every Source blend mode.</summary>
		private static void ApplySourceBlendState(Material unityMaterial, VMT sourceMaterial)
		{
			var blend = UnityEngine.Rendering.BlendMode.One;
			var blendTo = UnityEngine.Rendering.BlendMode.Zero;
			var zWrite = 1;
			var renderQueue = -1;

			unityMaterial.SetFloat("_AlphaClip", sourceMaterial.HaveCutout ? 1.0f : 0.0f);

			if (string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "DecalModulate",
						  System.StringComparison.OrdinalIgnoreCase))
			{
				// Intrinsic Source blend: 2 * source * destination. DecalModulate VMTs do
				// not also spell this out as $translucent.
				blend = UnityEngine.Rendering.BlendMode.DstColor;
				blendTo = UnityEngine.Rendering.BlendMode.SrcColor;
				zWrite = 0;
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			} else
			if (string.Equals(SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName), "Water",
						  System.StringComparison.OrdinalIgnoreCase))
			{
				blend = UnityEngine.Rendering.BlendMode.SrcAlpha;
				blendTo = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
				zWrite = 0;
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			} else
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
				case "_BaseMap":
				case "_DetailAlbedoMap":
				// LightmappedGeneric's second layer is a base texture like the first.
				case "_BaseTexture2":
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

		private static bool SetMaterialColor(Material material, Color color)
		{
			if (!material)
				return false;

			// Stock URP Lit / Unlit / Particle Unlit.
			if (material.HasProperty("_BaseColor"))
			{
				material.SetColor("_BaseColor", color);
				return true;
			}

			// Compatibility with custom/legacy shaders.
			if (material.HasProperty("_Color"))
			{
				material.SetColor("_Color", color);
				return true;
			}

			return false;
		}

		private static string ResolveTextureProperty(Material material, string propertyName)
		{
			if (!material)
				return null;

			if (material.HasProperty(propertyName))
				return propertyName;

			// The Source importer historically uses _MainTex, while stock URP uses _BaseMap.
			if (propertyName == "_MainTex" && material.HasProperty("_BaseMap"))
				return "_BaseMap";
			if (propertyName == "_BaseMap" && material.HasProperty("_MainTex"))
				return "_MainTex";

			return null;
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
		private static Texture2D SetMaterialTexture(GameResources gameResources,
													Material material,
													string materialPropertyName,
													string textureName,
													bool isNormalMap = false,
													bool warnIfMissing = true,
													bool selfShadowed = false)
		{
			if (string.IsNullOrEmpty(textureName) || !material)
				return null;

			var resolvedProperty = ResolveTextureProperty(material, materialPropertyName);

			if (resolvedProperty == null)
			{
				// Not every Source shader equivalent has every slot.
				if (warnIfMissing)
					Debug.LogWarning(
						"Unknown property " + materialPropertyName +
						" on shader " + material.shader.name,
						material);

				return null;
			}

			var images = gameResources.ImportVTF(
				textureName,
				PackagePath.DefaultMaterialPaths);

			if (images == null || images.Length == 0)
				return null;

			var image = images[0];

			if (isNormalMap)
			{
				Texture2DImporter.MarkAsNormalMap(image, selfShadowed);
			}
			else if (IsColorSlot(material, materialPropertyName))
			{
				Texture2DImporter.MarkAsColor(image);
			}

			try
			{
				material.SetTexture(resolvedProperty, image);
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex, material);
				return null;
			}

			return image;
		}

		/*
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
		*/
		internal static Material GenerateEditorColorMaterial(Color color)
		{
			var shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (!shader)
			{
				Debug.LogWarning("Universal Render Pipeline/Unlit shader not found");
				return null;
			}

			var material = new Material(shader)
			{
				name = "Color_" + color,
				hideFlags = HideFlags.None | HideFlags.DontUnloadUnusedAsset
			};
			material.SetColor("_BaseColor", color);
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
		
		private const string _skyboxShaderName = "Skybox/Cubemap Skybox";

		private static Shader _skyboxShader;

		private static Shader GetSkyboxShader()
		{
			if (!_skyboxShader)
			{
				_skyboxShader = Shader.Find(_skyboxShaderName);

				if (!_skyboxShader)
					Debug.LogWarning("URP skybox shader not found: " + _skyboxShaderName);
			}

			return _skyboxShader;
		}

		public static Material CreateSkyboxMaterial(GameResources gameResources, 
												    string materialName,
													Cubemap cubemap)
		{
			var shader = GetSkyboxShader();
			if (!shader)
				return null;

			var unityMaterial = new Material(shader)
			{
				name = materialName,
				hideFlags = HideFlags.DontUnloadUnusedAsset
			};

			try
			{
				if (unityMaterial.HasProperty("_BaseMap"))
					unityMaterial.SetTexture("_BaseMap", cubemap);
				else if (unityMaterial.HasProperty("_Tex"))
					unityMaterial.SetTexture("_Tex", cubemap);
				else if (unityMaterial.HasProperty("_MainTex"))
					unityMaterial.SetTexture("_MainTex", cubemap);

				// ... we use the rotation setting to rotate it back (cubemap has rotated surfaces), so it matches the original game
				if (unityMaterial.HasProperty("_Rotation"))
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
