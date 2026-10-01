using System.Collections.Generic;
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
		// 15: $rimlightexponent reaches the shader. Every material built before this carries the shader's default of
		// 4 however wide its VMT asked to be, so they all have to be rebuilt rather than only the ones that set it.
		// 16: _AlphaToMask, so an alpha-tested edge resolves against the multisample coverage mask
		// (DrawsWithAlphaToCoverage). The property defaults to 0, which is the state every material built before this
		// one has, so they rebuild rather than silently keeping the hard edge.
		// 17: _AlphaToMask and _NoFog reach the SOURCE shaders. Both were written in ApplyURPFallbackSurfaceState,
		// which CreateMaterial only reaches when there is NO Source shader for the VMT - it returns at the
		// CreateSourceShaderMaterial call well before it. So on the 1262 materials that use our own shaders, the two
		// shaders that declare these properties, neither was ever set: $nofog did nothing at all, and v16 rebuilt
		// every material without writing the mask it was bumped for. They are set in ApplySourceBlendState now,
		// which is that path's surface state. A v16 stamp therefore means "rebuilt, but still wrong", which is why
		// this is a bump and not a correction in place.
		// 18: _OpaqueAlpha, so a plain opaque surface writes 1 into the frame's alpha instead of whatever mask its
		// base texture carries. Every material built before this exports that mask, and ~2200 of them hold values
		// low enough to read as "do not fog me".
		// 19: EyeRefract exists, and the VMT parser no longer loses a key. Neither is a change to what a
		// material is BUILT from, which is why this was missed: Chisel/Source/EyeRefract is found by name at
		// build time, so a material built before the shader existed keeps the URP/Lit fallback it resolved to
		// then and draws every eye white; and a material whose VMT put two key/value pairs on one line lost
		// the second one to TextParser until 0d291af, so it kept the empty slot it was built with. A reimport
		// changed neither, because both were stamped current at 18 - the generator changed underneath a stamp
		// that did not. Any new SHADER is a material version bump for exactly this reason.
		// 20: $bumpmap slots are filled again. 329 of the 624 materials whose VMT names one had _BumpMap bound
		// to nothing, with the converted .png sitting on disk - see Texture2DImporter.MarkAsNormalMap for the
		// cubemap shape behind it. Every one of those materials has to be rebuilt to pick the texture up, and
		// to enable _NORMALMAP with it: the keyword is set from the texture, so a null one left the whole bump
		// path compiled out of the variant, and assigning the texture by hand afterwards did nothing.
		// 21: eye materials gain their cornea cubemap (_EnvMap + _ENVMAP, which EnvMapOf had turned away) and
		// $halflambert, and _EyeballRadius is written in metres. Also clears the dead _ENVMAPFROMPROBES_ON that
		// [Toggle] left in every material's m_InvalidKeywords before 7e598b6, since a rebuild copies a fresh
		// material over the asset, keywords and all.
		// 22: no material is written with DontUnloadUnusedAsset any more. Every builder set it on the material it made,
		// and it is a serialized flag: 6,844 .mat files carried "m_ObjectHideFlags: 32", so every material ever loaded,
		// in any session, stayed loaded with its textures (3.8 GB of them, unheld, in one census). URP fallback
		// materials are written as URP's own validation leaves them (ValidateAsTheShaderInspectorDoes), which it
		// otherwise did to them after the import, unsaved, for the next SaveAssets to write. And each material carries
		// a hash of itself as written (WrittenHash), so one changed after it was written is put right at its next import.
		public static readonly (string generator, int version) GeneratedVersion = ("material", 22);

		public static Material Import(GameResources gameResources, VMT sourceMaterial, string outputPath, bool isSprite = false)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".mat");
			string materialName = Path.GetFileNameWithoutExtension(destinationPath);
			var foundAsset = UnityAssets.Load<Material>(destinationPath);
			if (foundAsset != null)
			{
				if (!IsCurrent(foundAsset))
					Reconvert(gameResources, sourceMaterial, foundAsset, materialName, isSprite);
				else if (!IsAsWritten(foundAsset))
				{
					Debug.Log($"{UnityEditor.AssetDatabase.GetAssetPath(foundAsset)} was changed after it was written, so it is written again.");
					Reconvert(gameResources, sourceMaterial, foundAsset, materialName, isSprite);
				}
				return foundAsset;
			}

			foundAsset = CreateMaterial(gameResources, sourceMaterial, materialName, isSprite);
			if (foundAsset == null)
				return null;
			PackagePath.EnsureDirectoriesExist(destinationPath);
			CreateStamped(foundAsset, PackagePath.GetAssetPath(destinationPath));
			return foundAsset;
		}

		// Said once per editor session, whatever the number of materials it happens to
		static bool s_SaidStampedTheSlowWay;

		/// <summary>
		/// Writes a material this importer has just built as a new asset, stamped current, with ONE import.
		/// <para>
		/// It used to take three, each its own refresh: CreateAsset imports the file it writes, UnityAssets.Save imported
		/// it again, and the stamp - importer userData - took a third (SaveAndReimport). A .meta written before
		/// CreateAsset does not help: CreateAsset writes its own. What does: CreateAsset inside an asset-editing block,
		/// which writes the .mat and its .meta at once and leaves the import to the end of the block, and the stamp put
		/// into that .meta on disk before the block ends (<see cref="GeneratedMeta.SetUserData"/>), where the one import
		/// reads it. The file is byte for byte what CreateAsset writes on its own.
		/// </para>
		/// <para>
		/// Should the import not have read the stamp, it is stamped the slow way, through the importer, and said once.
		/// </para>
		/// </summary>
		internal static void CreateStamped(Material built, string assetPath)
		{
			assetPath = assetPath.Replace('\\', '/');
			UnityEditor.AssetDatabase.StartAssetEditing();
			try
			{
				WriteNewInBlock(built, assetPath);
			}
			finally
			{
				UnityEditor.AssetDatabase.StopAssetEditing();
			}
			StampedByItsImport(built, assetPath);
		}

		// CreateStamped's writing, inside an asset-editing block, whose end imports it once: the file, and its .meta with the
		// stamp and the hash of what was written in it
		static void WriteNewInBlock(Material built, string assetPath)
		{
			UnityEditor.AssetDatabase.CreateAsset(built, assetPath);
			// The file is written by now, and the import at the end of the block leaves it as it is
			var userData = GeneratedAssetVersion.WithWrittenHash(
				GeneratedAssetVersion.Stamp(null, GeneratedVersion, Texture2DImporter.GeneratedVersion), WrittenHash(assetPath));
			GeneratedMeta.SetUserData(assetPath, userData);
		}

		// After the block: should the import not have read the stamp, it is stamped the slow way, and said once
		static void StampedByItsImport(Material built, string assetPath)
		{
			var importer = UnityEditor.AssetImporter.GetAtPath(assetPath);
			if (importer != null && !GeneratedAssetVersion.IsCurrentUserData(importer.userData, GeneratedVersion, Texture2DImporter.GeneratedVersion))
			{
				if (!s_SaidStampedTheSlowWay)
				{
					s_SaidStampedTheSlowWay = true;
					Debug.LogWarning($"{assetPath}: the import did not read the stamp put into its .meta, so it was stamped with an import of its own. " +
									 "Every material written this way takes two imports instead of one until this is looked into.");
				}
				StampCurrent(built);
			}
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
		/// <para>
		/// The save and the stamp go in with one import: both are written inside an asset-editing block, whose end
		/// imports the asset once (saving it, then stamping it with SaveAndReimport, took two).
		/// </para>
		/// </summary>
		internal static void Overwrite(Material existing, Material rebuilt)
		{
			try
			{
				UnityEditor.AssetDatabase.StartAssetEditing();
				try
				{
					WriteOverInBlock(existing, rebuilt);
				}
				finally
				{
					UnityEditor.AssetDatabase.StopAssetEditing();
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(rebuilt);
			}
		}

		// Overwrite's writing, inside an asset-editing block, whose end imports the asset once: saved, then stamped with
		// the hash of what was written
		static void WriteOverInBlock(Material existing, Material rebuilt)
		{
			var assetPath = UnityEditor.AssetDatabase.GetAssetPath(existing);
			// The name is not taken from the build: it is the asset's identity, and CreateMaterial
			// appends "_nocull" to the one it builds.
			var name = existing.name;
			UnityEditor.EditorUtility.CopySerialized(rebuilt, existing);
			existing.name = name;
			// However older code pinned it: an asset is unloaded when nothing uses it, and reloaded when used
			existing.hideFlags &= ~HideFlags.DontUnloadUnusedAsset;
			UnityEditor.EditorUtility.SetDirty(existing);
			UnityEditor.AssetDatabase.SaveAssetIfDirty(existing);

			var importer = UnityEditor.AssetImporter.GetAtPath(assetPath);
			if (importer != null)
			{
				importer.userData = GeneratedAssetVersion.WithWrittenHash(
					GeneratedAssetVersion.Stamp(importer.userData, GeneratedVersion, Texture2DImporter.GeneratedVersion),
					WrittenHash(assetPath));
				UnityEditor.AssetDatabase.WriteImportSettingsIfDirty(assetPath);
			}
		}

		/// <summary>A material <see cref="ImportAll"/> is asked for: what <see cref="Import"/> takes.</summary>
		public readonly struct Request
		{
			public readonly VMT    SourceMaterial;
			public readonly string OutputPath;
			public readonly bool   IsSprite;

			public Request(VMT sourceMaterial, string outputPath, bool isSprite = false)
			{
				SourceMaterial = sourceMaterial;
				OutputPath     = outputPath;
				IsSprite       = isSprite;
			}
		}

		/// <summary>
		/// Imports a material for each request, each as <see cref="Import"/> would, but writes all the ones that have to be
		/// written in ONE asset-editing block: one refresh between them, where Import takes one each. In this project a
		/// refresh costs about 0.12 s besides the import itself
		/// , and a material version bump rebuilds every material a map uses at its next import,
		/// over a thousand for the larger maps.
		/// <para>
		/// The textures they are built with that have to be written are written first, and imported together too: the
		/// materials are built once under a texture census (<see cref="GameResources.BeginTextureCensus"/>), which writes each
		/// such texture ahead of its import and hands the build stand-ins for it, then the census imports them in one refresh,
		/// and the materials are built again, for real, from the textures as imported. A map's first import wrote ~475
		/// textures one refresh each.
		/// </para>
		/// <para>
		/// The first request for a file decides it, as the first of two Import calls would. A material that cannot be
		/// written comes back null and is said, so whoever asks for it next tries again on its own.
		/// </para>
		/// </summary>
		/// <param name="progress">Told the index of each request it builds and the count, for a progress bar.</param>
		public static Material[] ImportAll(GameResources gameResources, IReadOnlyList<Request> requests, System.Action<int, int> progress = null)
		{
			var results = new Material[requests.Count];
			var first = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
			var sameFile = new List<(int index, int firstIndex)>();
			// What each request comes to is decided first, so the census builds exactly what is built for real
			var builds = new List<(int index, Material existing, string materialName, string destinationPath)>();
			for (int i = 0; i < requests.Count; i++)
			{
				var request = requests[i];
				if (request.SourceMaterial == null || string.IsNullOrEmpty(request.OutputPath))
					continue;
				var destinationPath = Path.ChangeExtension(request.OutputPath, ".mat");
				var file = Path.GetFullPath(destinationPath);
				if (first.TryGetValue(file, out var firstIndex))
				{
					sameFile.Add((i, firstIndex));
					continue;
				}
				first[file] = i;

				var foundAsset = UnityAssets.Load<Material>(destinationPath);
				if (foundAsset != null)
				{
					results[i] = foundAsset;
					var current = IsCurrent(foundAsset);
					if (current && IsAsWritten(foundAsset))
						continue;
					if (current)
						Debug.Log($"{UnityEditor.AssetDatabase.GetAssetPath(foundAsset)} was changed after it was written, so it is written again.");
				}
				builds.Add((i, foundAsset, Path.GetFileNameWithoutExtension(destinationPath), destinationPath));
			}

			if (TextureCensusEnabled && builds.Count > 0)
				CensusTextures(gameResources, requests, builds, progress);

			var created = new List<(int index, Material built, string assetPath)>();
			var rebuilt = new List<(int index, Material existing, Material rebuilt)>();
			foreach (var (i, existing, materialName, destinationPath) in builds)
			{
				if (!TextureCensusEnabled)
					progress?.Invoke(i, requests.Count);
				var request = requests[i];
				var build = CreateMaterial(gameResources, request.SourceMaterial, materialName, request.IsSprite);
				if (build == null)
					continue;
				if (existing != null)
				{
					rebuilt.Add((i, existing, build));
					continue;
				}
				PackagePath.EnsureDirectoriesExist(destinationPath);
				results[i] = build;
				created.Add((i, build, PackagePath.GetAssetPath(destinationPath).Replace('\\', '/')));
			}

			if (created.Count > 0 || rebuilt.Count > 0)
			{
				var failed = new HashSet<int>();
				UnityEditor.AssetDatabase.StartAssetEditing();
				try
				{
					foreach (var (index, built, assetPath) in created)
					{
						try { WriteNewInBlock(built, assetPath); }
						catch (System.Exception exception) { failed.Add(index); Debug.LogException(exception); }
					}
					foreach (var (index, existing, build) in rebuilt)
					{
						try { WriteOverInBlock(existing, build); }
						catch (System.Exception exception) { failed.Add(index); Debug.LogException(exception); }
					}
				}
				finally
				{
					UnityEditor.AssetDatabase.StopAssetEditing();
					foreach (var (_, _, build) in rebuilt)
						UnityEngine.Object.DestroyImmediate(build);
				}
				foreach (var (index, built, assetPath) in created)
				{
					if (!failed.Contains(index) && UnityEditor.EditorUtility.IsPersistent(built))
						StampedByItsImport(built, assetPath);
					else
					{
						// Not an asset: a scene saved with it would carry a copy of its own
						results[index] = null;
						UnityEngine.Object.DestroyImmediate(built);
					}
				}
				foreach (var (index, _, _) in rebuilt)
				{
					if (failed.Contains(index))
						results[index] = null;
				}
			}
			foreach (var (index, firstIndex) in sameFile)
				results[index] = results[firstIndex];
			return results;
		}

		/// <summary>
		/// Off, <see cref="ImportAll"/> imports the textures as the builds ask for them, one refresh each: what the texture
		/// census is compared against.
		/// </summary>
		public static bool TextureCensusEnabled = true;

		// The builds ImportAll is about to make, made once under a texture census for the textures they ask for, and thrown
		// away; the census then imports the textures it wrote, all in one refresh
		static void CensusTextures(GameResources gameResources, IReadOnlyList<Request> requests,
								   List<(int index, Material existing, string materialName, string destinationPath)> builds,
								   System.Action<int, int> progress)
		{
			using var census = gameResources.BeginTextureCensus();
			// What a build has to say it says again when it is built for real: once is enough
			var logging = Debug.unityLogger.logEnabled;
			Debug.unityLogger.logEnabled = false;
			try
			{
				foreach (var (index, _, materialName, _) in builds)
				{
					progress?.Invoke(index, requests.Count);
					var request = requests[index];
					Material build = null;
					try
					{
						build = CreateMaterial(gameResources, request.SourceMaterial, materialName, request.IsSprite);
					}
					catch (System.Exception)
					{
						// built for real, it throws again, and that is said
					}
					finally
					{
						if (build != null)
							UnityEngine.Object.DestroyImmediate(build);
					}
				}
			}
			finally
			{
				Debug.unityLogger.logEnabled = logging;
			}
			census.ImportWritten();
		}

		static bool IsCurrent(Material material)
		{
			return GeneratedAssetVersion.IsCurrent(UnityEditor.AssetDatabase.GetAssetPath(material),
												   GeneratedVersion, Texture2DImporter.GeneratedVersion);
		}

		/// <summary>
		/// A hash of the material as its file holds it: the Material's own YAML document, not what else the file may come
		/// to hold - ChiselSurfaceMetadata, URP's version marker - which is not the importer's to write. Null when there
		/// is no file.
		/// </summary>
		internal static string WrittenHash(string assetPath)
		{
			var path = Path.GetFullPath(assetPath);
			if (!File.Exists(path))
				return null;
			var text = File.ReadAllText(path).Replace("\r\n", "\n");
			// The main Material is "--- !u!21 &2100000", up to the next object or the end. Unity writes a file's objects in
			// fileID order, so a sub-asset may come after it: taken without the line end before the next object, and
			// without the file's last one, it reads the same whichever follows
			var start = text.IndexOf("--- !u!21 &2100000", System.StringComparison.Ordinal);
			if (start >= 0)
			{
				var end = text.IndexOf("\n--- !u!", start, System.StringComparison.Ordinal);
				text = ((end >= 0) ? text.Substring(start, end - start) : text.Substring(start)).TrimEnd('\n');
			}
			using var sha = System.Security.Cryptography.SHA1.Create();
			var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
			return System.BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
		}

		/// <summary>
		/// Whether the material is still what this importer wrote: its file holds what <see cref="WrittenHash"/> recorded
		/// when it was written.
		/// <para>
		/// A version stamp says which code wrote a material, not that nothing changed it since: a value set by hand, or by
		/// a script fixing materials up, would stay under a current stamp for good, and so would what URP's
		/// postprocessor did to a new material for the next SaveAssets to write. So an import that reuses a material
		/// writes it again, in place, when it was changed (as Texture2DImporter.ImportedAsWritten does for a texture).
		/// One written before the hash was recorded has none, and is written again once.
		/// </para>
		/// </summary>
		static bool IsAsWritten(Material material)
		{
			var assetPath = UnityEditor.AssetDatabase.GetAssetPath(material);
			var importer = UnityEditor.AssetImporter.GetAtPath(assetPath);
			var written = (importer != null) ? GeneratedAssetVersion.WrittenHashOf(importer.userData) : null;
			return written != null && written == WrittenHash(assetPath);
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

			var assetPath = UnityEditor.AssetDatabase.GetAssetPath(material);
			importer.userData = GeneratedAssetVersion.WithWrittenHash(
				GeneratedAssetVersion.Stamp(importer.userData, GeneratedVersion, Texture2DImporter.GeneratedVersion),
				WrittenHash(assetPath));
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
			if (foundAsset != null && IsCurrent(foundAsset) && IsAsWritten(foundAsset))
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

			CreateStamped(skybox, PackagePath.GetAssetPath(destinationPath));
			return skybox;
		}

		private const string _litShaderName = "Universal Render Pipeline/Lit";
		private const string _unlitShaderName = "Universal Render Pipeline/Unlit";
		/// <summary>Chisel's billboard shader for sprites (Shaders/Particle AddSmooth Aligned.shader)</summary>
		private const string _spriteShaderName = "Particles/Additive (Soft) Camera Aligned";

		private static Shader _litShader;
		private static Shader _unlitShader;
		private static Shader _spriteShader;

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

		private static Shader GetSpriteShader()
		{
			if (!_spriteShader)
				_spriteShader = FindFallbackShader(_spriteShaderName);

			return _spriteShader;
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

			// This is the FALLBACK path - a VMT whose Source shader we have not ported, drawn by URP's own Lit or
			// Unlit, which declare _AlphaToMask themselves. The materials that use our shaders never come through
			// here: CreateMaterial returns at CreateSourceShaderMaterial, and ApplySourceBlendState is where their
			// copy of this lives.
			SetFloatIfPresent(material, "_AlphaToMask", DrawsWithAlphaToCoverage(sourceMaterial) ? 1.0f : 0.0f);
		}

		/// <summary>
		/// A sprite's material (env_sprite and whatever else names a .spr or sprite .vmt): Chisel's billboard shader with the
		/// sprite's texture. A sprite entity tints and dims it with its rendercolor and renderamt, through _TintColor on a
		/// variant of it (VmfEntityConverter.GetTintedSpriteMaterial). URP's Particles/Unlit, which sprites used to get,
		/// neither faces the camera nor has a _TintColor.
		/// </summary>
		internal static Material CreateSpriteMaterial(GameResources gameResources, VMT sourceMaterial, string materialName)
		{
			var shader = GetSpriteShader();
			if (!shader)
				return null;

			var unityMaterial = new Material(shader) { name = materialName };
			SetMaterialTexture(gameResources, unityMaterial, "_BaseMap", sourceMaterial.BaseTextureName);
			return unityMaterial;
		}

		internal static Material CreateMaterial(GameResources gameResources, VMT sourceMaterial, string materialName, bool isSprite)
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

			// "sprite" is not a Source shader type: it is our own billboard handling of .spr references
			if (isSprite)
				return CreateSpriteMaterial(gameResources, sourceMaterial, materialName);

			// When we have a Unity shader standing in for the Source shader this VMT asks for, use
			// it and carry the VMT's parameters across as written. Otherwise fall through to the
			// approximation with Unity's Standard shader below, which is unchanged.
			var sourceShader = SourceShaderLookup.Find(sourceMaterial.MaterialTypeName);
			if (sourceShader != null)
				return CreateSourceShaderMaterial(gameResources, sourceMaterial, materialName, sourceShader);

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

			ApplyURPFallbackSurfaceState(unityMaterial, sourceMaterial);

			// $nofog. Our fog is a screen-space pass, so a surface excludes itself from it the way sky faces already
			// do: by leaving 0 in the camera colour's alpha, which SourceFogPass reads and then restores to 1.
			if (unityMaterial.HasProperty("_NoFog"))
				unityMaterial.SetFloat("_NoFog", DrawsWithoutFog(sourceMaterial) ? 1 : 0);

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

			// What URP itself makes of these properties: the keywords, blend state, queue and passes they call for. Left to
			// URP, its postprocessor did it to a new material after the import, in memory, for the next SaveAssets to
			// write (37 materials found rewritten after their stamp), and a rebuilt one, which it leaves alone, kept
			// what is written here: the same VMT came out two ways.
			ValidateAsTheShaderInspectorDoes(unityMaterial);
			return unityMaterial;
		}

		/// <summary>
		/// Runs the shader's own inspector's ValidateMaterial on the material, as the Material inspector does. For URP's
		/// Lit and Unlit that is BaseShaderGUI.SetMaterialKeywords - what URP's material postprocessor also does to a new
		/// material (ShaderUtils.UpdateMaterial). A shader without an inspector of its own, which is all of ours, is left
		/// as it is.
		/// </summary>
		static void ValidateAsTheShaderInspectorDoes(Material material)
		{
			var editor = UnityEditor.Editor.CreateEditor(material) as UnityEditor.MaterialEditor;
			try
			{
				editor?.customShaderGUI?.ValidateMaterial(material);
			}
			finally
			{
				if (editor != null)
					Object.DestroyImmediate(editor);
			}
		}

		/// <summary>
		/// Builds a material against a Unity shader that stands in for the Source shader the VMT
		/// asked for (see <see cref="SourceShaderLookup"/>). Unlike the Standard-shader path below
		/// this does not have to approximate: the shader has a slot for each VMT parameter, so the
		/// values go across as written.
		/// </summary>
		private static Material CreateSourceShaderMaterial(GameResources gameResources, VMT sourceMaterial, string materialName, Shader shader)
		{
			var unityMaterial = new Material(shader) { name = materialName };

			// A cut-out's base alpha IS its silhouette, so its mip chain has to keep the alpha test's
			// coverage or the shape thins away with distance (Texture2DImporter.MarkAsAlphaTested).
			var alphaTestReference = sourceMaterial.HaveCutout ? (sourceMaterial.AlphaTestReference ?? 0.5f) : 0.0f;
			var mainTexture = SetMaterialTexture(gameResources, unityMaterial, "_BaseMap", sourceMaterial.BaseTextureName,
												 alphaTestReference: alphaTestReference);
			if (mainTexture != null && sourceMaterial.HaveCutout)
				Texture2DImporter.MarkAsAlphaTested(mainTexture, alphaTestReference);

			// EyeRefract. An eye material names no $basetexture at all - $iris carries the iris, the pupil
			// AND the sclera - so the line above found nothing and, before Chisel/Source/EyeRefract
			// existed, every eye in the game fell through to a base-texture shader and drew pure white.
			if (unityMaterial.HasProperty("_Iris"))
			{
				SetMaterialTexture(gameResources, unityMaterial, "_Iris", sourceMaterial.IrisTextureName);
				SetMaterialTexture(gameResources, unityMaterial, "_CorneaTexture", sourceMaterial.CorneaTextureName, warnIfMissing: false);
				SetMaterialTexture(gameResources, unityMaterial, "_AmbientOcclTexture", sourceMaterial.AmbientOcclusionTextureName, warnIfMissing: false);
				// $ambientocclcolor tints the occluded side of the eye; Source's own default is 0.33 grey.
				if (sourceMaterial.AmbientOcclusionColor != null)
					unityMaterial.SetColor("_AmbientOcclColor", ShaderColor(new Color(
						sourceMaterial.AmbientOcclusionColor.r,
						sourceMaterial.AmbientOcclusionColor.g,
						sourceMaterial.AmbientOcclusionColor.b, 1)));
				// In Source this is a WORLD-space radius, used for one thing only: ray-tracing the eye as a sphere
				// (IntersectRaySphere, eye_refract_ps2x.fxc:187) when $raytracesphere is on. It plays no part in the
				// iris projection, which comes from the model's eyeball (ModelImporter.EyeProjection). So it goes
				// over in world units - metres here - to be right for whatever reads it. Every eye in Black Mesa
				// sets $raytracesphere 0, and the ray trace is not ported.
				if (sourceMaterial.EyeballRadius.HasValue)
					SetFloatIfPresent(unityMaterial, "_EyeballRadius", sourceMaterial.EyeballRadius.Value * SourceEngineUnits.VmfInvMeters);
				// The eye's diffuse light wraps round its dark side (CosineTermInternal); all 8 of the game's eye
				// materials set it.
				SetFloatIfPresent(unityMaterial, "_HalfLambert", sourceMaterial.HalfLambert == true ? 1.0f : 0.0f);
				// The cornea's $envmap is ApplySourceEnvMap's, below - it owns _ENVMAP and would switch it off again.
				if (sourceMaterial.Dilation.HasValue)
					SetFloatIfPresent(unityMaterial, "_Dilation", sourceMaterial.Dilation.Value);
				if (sourceMaterial.CorneaBumpStrength.HasValue)
					SetFloatIfPresent(unityMaterial, "_CorneaBumpStrength", sourceMaterial.CorneaBumpStrength.Value);
				// $glossiness scales the cornea's cubemap reflection (eye_refract_ps2x.fxc:396).
				if (sourceMaterial.Glossiness.HasValue)
					SetFloatIfPresent(unityMaterial, "_Glossiness", sourceMaterial.Glossiness.Value);
				if (sourceMaterial.ParallaxStrength.HasValue)
					SetFloatIfPresent(unityMaterial, "_ParallaxStrength", sourceMaterial.ParallaxStrength.Value);
			}
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
				ApplySourceWater(gameResources, unityMaterial, sourceMaterial);

			// $nocull, which the Standard shader cannot express at all. Source uses it for glass,
			// grates and foliage, all of which have to be visible from both sides.
			var noCull = sourceMaterial.NoCull.HasValue && sourceMaterial.NoCull.Value;
			unityMaterial.SetFloat("_Cull", (int)(noCull ? UnityEngine.Rendering.CullMode.Off
													     : UnityEngine.Rendering.CullMode.Back));
			return unityMaterial;
		}

		/// <summary>
		/// Sets up Source's Water shader (water.cpp). Water refracts when it names a $refracttexture, and reflects when it
		/// names a $reflecttexture and is expensive, which water is on a PC unless it asks for $forcecheap ($forceexpensive
		/// defaults to 1 there). Water that doesn't reflect also draws the cheap version: its $envmap (env_cubemap unless it
		/// names one) faded in by fresnel and distance. The tints and the fog colour come out of gamma space as water.cpp sets
		/// them, and distances go across in units. $reflectamount offsets the planar reflection (SourceWaterReflection), as it
		/// offsets the game's.
		/// </summary>
		internal static void ApplySourceWater(GameResources gameResources, Material unityMaterial, VMT sourceMaterial)
		{
			var forceCheap = sourceMaterial.ForceCheap == true;
			var refract = !forceCheap && !string.IsNullOrEmpty(sourceMaterial.RefractTextureName);
			var reflect = !forceCheap && (sourceMaterial.ForceExpensive ?? true) && !string.IsNullOrEmpty(sourceMaterial.ReflectTextureName);
			unityMaterial.SetFloat("_Refract", refract ? 1 : 0);
			unityMaterial.SetFloat("_Reflect", reflect ? 1 : 0);
			unityMaterial.SetFloat("_Cheap", reflect ? 0 : 1);
			unityMaterial.SetFloat("_CheapBlend", forceCheap ? 0 : 1);
			unityMaterial.SetFloat("_CheapFresnel", (sourceMaterial.NoFresnel == true) ? 0 : 1);
			unityMaterial.SetFloat("_ReflectBlendFactor", sourceMaterial.ReflectBlendFactor ?? 1.0f);

			unityMaterial.SetColor("_RefractTint", WaterColor(sourceMaterial.RefractTint, Color.white));
			unityMaterial.SetColor("_ReflectTint", WaterColor(sourceMaterial.ReflectTint, Color.white));
			// water.cpp's fog colour defaults to red, which shows a material that has none
			unityMaterial.SetColor("_WaterFogColor", WaterColor(sourceMaterial.FogColor, Color.red));
			unityMaterial.SetFloat("_RefractAmount", sourceMaterial.RefractAmount ?? 0.0f);
			unityMaterial.SetFloat("_ReflectAmount", sourceMaterial.ReflectAmount ?? 0.8f);

			// The fog under the surface reaches its colour after $fogend - $fogstart of water in front of the scene
			var fogRange = Mathf.Max((sourceMaterial.FogEnd ?? 0.0f) - (sourceMaterial.FogStart ?? 0.0f), 1.0f);
			unityMaterial.SetFloat("_WaterFogRange", (sourceMaterial.FogEnable == false) ? kNoWaterFog : fogRange * SourceEngineUnits.VmfInvMeters);
			// water.cpp's defaults; in the game a WaterLOD proxy puts the map's water_lod_control distances here
			var cheapStart = sourceMaterial.CheapWaterStartDistance ?? 500.0f;
			var cheapEnd   = sourceMaterial.CheapWaterEndDistance ?? 1000.0f;
			unityMaterial.SetVector("_CheapWaterDistances", new Vector4(cheapStart, cheapEnd, 0, 0) * SourceEngineUnits.VmfInvMeters);

			// A TextureScroll proxy on $bumptransform replaces it by an offset that grows with time (CTextureScrollMaterialProxy).
			// Source's t axis runs down the image and the importer flips v, so second components change sign.
			var bumpTransform = (sourceMaterial.BumpTextureScroll.HasValue || !sourceMaterial.BumpTransform.HasValue)
							  ? Matrix4x4.identity : sourceMaterial.BumpTransform.Value;
			unityMaterial.SetVector("_BumpTransformU", new Vector4(bumpTransform.m00, bumpTransform.m01, 0, bumpTransform.m03));
			unityMaterial.SetVector("_BumpTransformV", new Vector4(bumpTransform.m10, bumpTransform.m11, 0, bumpTransform.m13));
			var bumpScroll = sourceMaterial.BumpTextureScroll ?? Vector2.zero;
			unityMaterial.SetVector("_BumpScroll", new Vector4(bumpScroll.x, -bumpScroll.y));
			// An AnimatedTexture proxy on $normalmap steps it through its frames (CAnimatedTextureProxy): the shader picks the
			// frame from a texture array of them
			var frames = (sourceMaterial.NormalMapFrameRate > 0 && gameResources != null)
					   ? gameResources.ImportVTFFrames(BumpMapNameOf(sourceMaterial), isNormalMap: true) : null;
			if (frames != null)
				unityMaterial.SetTexture("_BumpFrames", frames);
			unityMaterial.SetFloat("_BumpFrameRate", (frames != null) ? sourceMaterial.NormalMapFrameRate.Value : 0);
			unityMaterial.SetFloat("_BumpFrameCount", (frames != null) ? frames.depth : 0);
			var scroll1 = sourceMaterial.Scroll1 ?? Vector3.zero;
			var scroll2 = sourceMaterial.Scroll2 ?? Vector3.zero;
			unityMaterial.SetVector("_Scroll1", new Vector4(scroll1.x, -scroll1.y));
			unityMaterial.SetVector("_Scroll2", new Vector4(scroll2.x, -scroll2.y));

			// $envmap is env_cubemap unless the material names a cubemap: the reflection probe nearest to the pixel
			var envMap = string.IsNullOrWhiteSpace(sourceMaterial.EnvMapTextureName) ? "env_cubemap" : sourceMaterial.EnvMapTextureName.Trim();
			Cubemap cubemap = null;
			if (!string.Equals(envMap, "env_cubemap", System.StringComparison.OrdinalIgnoreCase))
				cubemap = gameResources.ImportCubemap(envMap);
			if (cubemap != null)
				unityMaterial.SetTexture("_EnvMap", cubemap);
			unityMaterial.SetFloat("_EnvMapFromProbes", (cubemap == null) ? 1 : 0);
		}

		/// <summary>What the shader's water fog range is without $fogenable: further than any view</summary>
		const float kNoWaterFog = 1.0e9f;

		/// <summary>A water colour as water.cpp sets it: out of gamma space (SetPixelShaderConstantGammaToLinear)</summary>
		static Color WaterColor(RGBColor value, Color fallback)
		{
			Color color = (value != null) ? (Color)value : fallback;
			return ShaderColor(new Color(GammaToLinearConstant(color.r), GammaToLinearConstant(color.g), GammaToLinearConstant(color.b), 1));
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
			unityMaterial.SetFloat("_RimLightExponent", RimLightExponentOf(sourceMaterial));
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

			// EyeRefract's cornea reflection. EnvMapOf knows only the *Generic shaders and turned every eye's $envmap
			// away, so no eye had ever reflected anything. Source's EyeRefract takes a named cubemap straight, scaled
			// by $glossiness (eye_refract_ps2x.fxc:396), with none of the Generic rules. All 8 eye materials in the
			// game name one ("models/<character>/eye-reflection-cubemap-"). env_cubemap is not handled: no eye uses
			// it, and this shader has no probe lookup.
			if (unityMaterial.HasProperty("_Iris"))
			{
				var eyeEnvMap = sourceMaterial.EnvMapTextureName?.Trim();
				if (string.IsNullOrEmpty(eyeEnvMap) ||
					string.Equals(eyeEnvMap, "env_cubemap", System.StringComparison.OrdinalIgnoreCase))
					return;
				var eyeCubemap = gameResources.ImportCubemap(eyeEnvMap);
				if (eyeCubemap == null)
					return;
				unityMaterial.SetTexture("_EnvMap", eyeCubemap);
				unityMaterial.EnableKeyword("_ENVMAP");
				return;
			}
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
		/// $nofog: whether this material draws unfogged. MATERIAL_VAR_NOFOG (imaterial.h, 1 &lt;&lt; 14).
		/// </summary>
		/// <remarks>
		/// Our fog is a full-screen pass rather than something each shader mixes in, so a surface opts out by leaving
		/// 0 in the camera colour's alpha - the mark sky faces already use, which SourceFogPass tests and then
		/// restores to 1. That mark can only be carried by a material whose alpha is not also its blend weight, so a
		/// translucent or additive material cannot ask for it: writing 0 there would not exclude it from the fog, it
		/// would make it invisible. Source's own uses are opaque - the sky and debug wireframe (sky_dx9,
		/// sky_hdr_dx9, wireframe_dx8/dx9 are the only things the public SDK sets the flag on) - so this covers what
		/// the engine actually does with it, and refuses the case it cannot honour rather than half-doing it.
		/// An alpha-tested material is refused for the same reason: with alpha to coverage
		/// (<see cref="DrawsWithAlphaToCoverage"/>) its exported alpha is the coverage the hardware masks samples
		/// with, so zeroing it would cut the surface away instead of leaving it unfogged.
		/// </remarks>
		internal static bool DrawsWithoutFog(VMT sourceMaterial)
		{
			if (sourceMaterial == null || sourceMaterial.NoFog != true)
				return false;
			return !sourceMaterial.HaveTranslucency && !sourceMaterial.HaveAdditiveBlending && !sourceMaterial.HaveCutout;
		}

		/// <summary>
		/// $alphatest: whether this material's cutout edge is resolved against the multisample coverage mask rather
		/// than kept as the hard per-pixel test Source does.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A DELIBERATE DEVIATION from Source, recorded here rather than left to be
		/// discovered. Source gates alpha to coverage on its own key, $allowalphatocoverage, and only 142 of the 480
		/// $alphatest materials in Black Mesa set it - not the chain-link fences whose stair-stepped edges are what
		/// prompted the work. Following the key faithfully would therefore leave the complaint unanswered on the
		/// materials it was made about, so every alpha-tested material gets it. Valve's key exists because the feature
		/// cost something on 2004 hardware and was not always available; it is free and universal on ours.
		/// </para>
		/// <para>
		/// It is a uniform rule and not a classification: nothing here decides what KIND of surface a material is, so
		/// there is no judgement to be wrong about on another map. It also does nothing at all unless the camera is
		/// multisampling - URP only turns the render state on through _AlphaToMaskAvailable when it is, and this
		/// project multisamples at 4x since the move to Forward+.
		/// </para>
		/// <para>
		/// URP gates it on the surface being OPAQUE as well as clipped, because a translucent surface's alpha is its
		/// blend weight and handing that to the coverage mask would count it twice. Which surfaces those are is
		/// decided by <see cref="ApplySourceBlendState"/>, and this is the condition of the branch that makes a
		/// material an opaque cutout there: $alphatest beats $translucent and $additive, and loses to the two shaders
		/// whose blend is intrinsic. DecalModulate multiplies the frame by the surface and Water blends by its own
		/// alpha, so neither writes opacity in that channel and neither can hand it to the coverage mask.
		/// </para>
		/// </remarks>
		internal static bool DrawsWithAlphaToCoverage(VMT sourceMaterial)
		{
			if (sourceMaterial == null || !sourceMaterial.HaveCutout)
				return false;
			var shader = SourceShaderLookup.Canonical(sourceMaterial.MaterialTypeName);
			return !string.Equals(shader, "DecalModulate", System.StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(shader, "Water", System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// $rimlightexponent: how wide the rim is, 4 when the key is absent as Source has it.
		/// </summary>
		/// <remarks>
		/// The key was parsed all along and never reached the shader, which squared the edge term twice for a fixed
		/// exponent of 4 - so a material asking for anything narrower got a rim twice as broad as it wanted. The term
		/// is ADDITIVE emission of the ambient probe rather than a modulation of the surface, so too broad a rim does
		/// not merely brighten an edge, it washes the albedo out across whole faces: a map's agave asks for 8 and
		/// its upward-facing inner leaves came out flat pale grey while the outward-facing ones were correct.
		/// Clamped at 1 because the exponent is a lobe width and a value below 1 broadens it past the whole surface.
		/// </remarks>
		internal static float RimLightExponentOf(VMT sourceMaterial)
		{
			return Mathf.Max(sourceMaterial?.RimLightExponent ?? 4.0f, 1.0f);
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

			// Off unless the branches below turn them on, so every other surface state clears them rather than
			// inheriting whatever the last build left
			SetFloatIfPresent(unityMaterial, "_AlphaToMask", 0.0f);
			SetFloatIfPresent(unityMaterial, "_OpaqueAlpha", 0.0f);

			// $nofog belongs here, with the rest of what the surface writes: these shaders opt out of the fog by
			// leaving 0 in the camera colour's alpha, which is a property of the blend state and not of the albedo.
			SetFloatIfPresent(unityMaterial, "_NoFog", DrawsWithoutFog(sourceMaterial) ? 1.0f : 0.0f);

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
			// The predicate rather than HaveCutout, so that what decides the render state IS what
			// DrawsWithAlphaToCoverage answers, and the two cannot drift apart
			if (DrawsWithAlphaToCoverage(sourceMaterial))
			{
				// Alpha tested surfaces stay opaque: they write depth and are not sorted, the
				// cutout is done with clip() in the shader.
				unityMaterial.EnableKeyword("_ALPHATEST_ON");
				if (sourceMaterial.AlphaTestReference.HasValue)
					unityMaterial.SetFloat("_Cutoff", sourceMaterial.AlphaTestReference.Value);
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
				SetFloatIfPresent(unityMaterial, "_AlphaToMask", 1.0f);
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
			else
			{
				// A PLAIN OPAQUE SURFACE WRITES ALPHA 1, whatever its base texture's alpha channel holds.
				//
				// In Source that channel is never opacity on a material that is neither $translucent nor $alphatest -
				// it is a mask for whatever feature claims it ($selfillum, $basealphaenvmapmask,
				// $basemapalphaphongmask, $detail) or it is unused. Source never noticed, because its fog is mixed in
				// per pixel by each shader. Ours is a screen-space pass that READS the camera colour's alpha, so
				// exporting a mask there quietly excluded those surfaces from the fog.
				//
				// A census of both VPKs: of 11070 opaque VMTs, 2673 have a base texture with an alpha
				// channel and NOT ONE of them claims that alpha as opacity. Sampling 60 of them, 85% have texels
				// below 0.5. The worst are worldspawn walls - concrete_wall_c2a2_deeptnl_01a has a mean alpha of
				// 25/255, with 98.5% of its texels below 0.5 and 34.7% below even the fog's 0.05 threshold, so
				// tightening that threshold could never have closed this.
				SetFloatIfPresent(unityMaterial, "_OpaqueAlpha", 1.0f);
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
		/// <param name="alphaTestReference">
		/// Above zero, the value this slot's alpha is tested at as a cut-out (Texture2DImporter.MarkAsAlphaTested), which a
		/// texture written now is imported with.
		/// </param>
		private static Texture2D SetMaterialTexture(GameResources gameResources,
													Material material,
													string materialPropertyName,
													string textureName,
													bool isNormalMap = false,
													bool warnIfMissing = true,
													bool selfShadowed = false,
													float alphaTestReference = 0.0f)
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

			// What this slot makes of the texture, so one written now is imported once, already as that; one that exists
			// is claimed below, as before (both leave it the same)
			var claim = isNormalMap ? TextureClaim.AsNormalMap(selfShadowed)
					  : IsColorSlot(material, materialPropertyName) ? TextureClaim.AsColor
					  : default;
			var images = gameResources.ImportVTF(
				textureName,
				PackagePath.DefaultMaterialPaths,
				claim.WithAlphaTest(alphaTestReference));

			if (images == null || images.Length == 0)
			{
				// A texture the VMT names but that cannot be imported used to return null in silence, and
				// Unity binds WHITE for an unset texture - so the material came out as a white surface with
				// nothing anywhere saying why. It looks like a shader bug and it is not one. Ten materials
				// in this game are in that state; five name textures that are in no VPK at all, and the
				// rest were losing the name to a parse bug. Saying so makes the difference visible.
				if (warnIfMissing)
					Debug.LogWarning($"Material '{material.name}' names texture '{textureName}' for " +
									 $"{materialPropertyName}, which could not be imported - the slot will " +
									 "sample white.", material);
				return null;
			}

			var image = images[0];

			if (isNormalMap)
			{
				// Take the texture back: marking it re-imports the asset, which destroys the object loaded
				// above, and a destroyed object assigned below serialises as a null reference rather than
				// failing - a $bumpmap slot that silently reads as empty.
				image = Texture2DImporter.MarkAsNormalMap(image, selfShadowed);
				if (image == null)
					return null;
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

			var material = new Material(shader) { name = "Color_" + color };
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
		private const string _skyFaceShaderName = "Chisel/Source/Sky Face";

		// The last sky whose face material was asked for, and that material: a map asks once per sky face
		private static Material _skyFaceSource, _skyFaceMaterial;

		/// <summary>
		/// The material the level's sky faces are drawn with (toolsskybox): the 2D sky's cubemap, tint, exposure and rotation on
		/// Chisel/Source/Sky Face, which shows the 3D skybox's view there when the map has one, and writes the faces' depth, so
		/// nothing behind them shows, as nothing does in the game. It is kept beside the sky's material ("&lt;sky&gt; (faces).mat")
		/// and rebuilt in place. The sky's material itself without that shader.
		/// </summary>
		public static Material SkyFaceMaterialOf(Material skybox)
		{
			if (skybox == null)
				return null;
			if (skybox == _skyFaceSource && _skyFaceMaterial != null)
				return _skyFaceMaterial;

			var shader = Shader.Find(_skyFaceShaderName);
			if (!shader)
			{
				Debug.LogWarning("Sky face shader not found: " + _skyFaceShaderName);
				return skybox;
			}
			var built = new Material(shader) { name = skybox.name + " (faces)" };
			built.CopyMatchingPropertiesFromMaterial(skybox);

			var skyboxPath = UnityEditor.AssetDatabase.GetAssetPath(skybox);
			if (!string.IsNullOrEmpty(skyboxPath))
			{
				var path = Path.Combine(Path.GetDirectoryName(skyboxPath), built.name + ".mat").Replace('\\', '/');
				var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
				if (existing != null)
				{
					Overwrite(existing, built);
					built = existing;
				}
				else
					CreateStamped(built, path);
			}
			_skyFaceSource = skybox;
			_skyFaceMaterial = built;
			return built;
		}

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

			var unityMaterial = new Material(shader) { name = materialName };

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
