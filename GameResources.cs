using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEditor;
using UnityEngine.Profiling;
using System.Buffers;
using UnityEngine.Pool;

namespace Chisel.Import.Source.VPKTools
{
	public class GameEntry
	{
		public string	keyname;
		public string	extension;
		public string	name;
		public string	sourceFilename;
		public VPKEntry entry;
	}

	public class GameResources
	{
		// TODO: make this work well with resource lookups in packages (lets avoid duplication of functionality)
		readonly Dictionary<string, GameEntry> lookup = new();

		public IEnumerable<string> GetEntryNames() { return lookup.Keys; }

		
		public GameEntry GetEntry(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.CleanFullPath(ref entryName);
			if (string.IsNullOrEmpty(entryName)) return null;
			if (lookup.TryGetValue(entryName, out var entry))
				return entry;
			if (searchPaths == null)
				return null;
			foreach(var searchPath in searchPaths)
			{
				if (lookup.TryGetValue(PackagePath.Combine(searchPath, entryName), out entry))
					return entry;
			}
			return null;
		}

		/// <summary>
		/// The entire contents of an entry as bytes.
		/// <para>
		/// Needed by the animation decoder, which walks run-length encoded data by offset rather than
		/// reading fields in order, and follows offsets that point backwards as often as forwards.
		/// Handing it the bytes keeps that out of the structured readers, and keeps the buffer alive
		/// only for as long as the caller holds it rather than for the lifetime of the model.
		/// </para>
		/// </summary>
		public byte[] LoadFileAsBytes(GameEntry entry)
		{
			if (entry == null)
				return null;

			byte[] result = null;
			LoadFileAsStream(entry, delegate (Stream stream)
			{
				using var memory = new MemoryStream();
				stream.CopyTo(memory);
				result = memory.ToArray();
				return true;
			});
			return result;
		}

		/// <inheritdoc cref="LoadFileAsBytes(GameEntry)"/>
		public byte[] LoadFileAsBytes(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName))
				return null;
			return LoadFileAsBytes(GetEntry(entryName, searchPaths));
		}

		public bool LoadFileAsStream(GameEntry entry, VPKParser.FileLoadDelegate streamLoadDelegate)
		{
			if (entry == null)
				return false;
			if (streamLoadDelegate == null) throw new NullReferenceException(nameof(streamLoadDelegate));
			if (entry.sourceFilename.EndsWith($".{PackagePath.ExtensionVPK}"))
			{
				VPKResource resource = LoadResource(entry.sourceFilename);
				if (resource == null)
					return false;
				return resource.LoadFileAsStream(entry.keyname, streamLoadDelegate);
			}

			using FileStream file = new(entry.sourceFilename, FileMode.Open);
			return streamLoadDelegate.Invoke(file);
		}

		public string LoadText(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadText(entry);
		}

		public string LoadText(GameEntry entry)
		{
			if (entry == null) return null;
			string sourceText = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceText = TextParser.LoadStreamAsString(stream);
				return true;
			})) return null;
			return sourceText;
		}

		public string ImportFile(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			string outputPath = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				outputPath = PackagePath.GetOutputPath(entry.keyname);
				if (!File.Exists(outputPath))
				{
					PackagePath.EnsureDirectoriesExist(outputPath);
					var bytes = new byte[stream.Length];
					stream.Read(bytes, 0, (int)stream.Length);
					File.WriteAllBytes(outputPath, bytes);
					var assetPath = PackagePath.GetAssetPath(entry.keyname);
					AssetDatabase.ImportAsset(assetPath);
				}
				return true;
			})) return null;
			return outputPath;
		}

		public VTF LoadVTF(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTF);
			if (searchPaths == null)
				searchPaths = PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadVTF(entry);
		}

		public VTF LoadVTF(GameEntry entry)
		{
			if (entry == null) return null;
			VTF sourceTexture = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceTexture = VTF.Read(stream);
				return true;
			})) return null;
			return sourceTexture;
		}

		/// <summary>The texture's header alone (<see cref="VTF.ReadHeader"/>): what telling whether it is current takes.</summary>
		public VTF LoadVTFHeader(GameEntry entry)
		{
			if (entry == null) return null;
			VTF header = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				header = VTF.ReadHeader(stream);
				return true;
			})) return null;
			return header;
		}

		/// <param name="claim">How the material asking for it reads it, so a texture written now is imported once, already
		/// as that (<see cref="TextureClaim"/>).</param>
		public Texture2D[] ImportVTF(string entryName, string[] searchPaths = null, TextureClaim claim = default)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTF);
			if (searchPaths == null)
				searchPaths = PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			if (entry.extension != PackagePath.ExtensionVTF)
				return null;
			var outputPath = PackagePath.GetOutputPath(PackagePath.GetTextureEntryName(entry.keyname));
			// Written ahead earlier in a texture census: its stand-ins, or for another claim the texture itself
			if (textureCensus != null && textureCensus.TryTake(entry, claim, out var taken))
				return taken;
			// A texture imported earlier is taken on its header alone; the pixels are only read to write it. Every material
			// that is built reads its textures, and most of them are current: a version bump rebuilds every material.
			Texture2D[] reusable;
			using (ImportSteps.Step("texture lookups"))
			{
				var header = LoadVTFHeader(entry);
				if (header == null)
					return null;
				reusable = Texture2DImporter.Reusable(header, outputPath);
			}
			if (reusable != null)
				return reusable;
			VTF sourceTexture;
			using (ImportSteps.Step("texture reads"))
				sourceTexture = LoadVTF(entry);
			if (sourceTexture == null)
				return null;
			// In a texture census it is written now and imported with every other one when the census ends
			if (textureCensus != null)
			{
				Texture2D[] standIns;
				using (ImportSteps.Step("textures written ahead"))
					standIns = textureCensus.WriteAhead(entry, sourceTexture, outputPath, claim);
				if (standIns != null)
					return standIns;
				// Not written ahead: there is no template yet for what it is to be, so it is imported now, the slow way, and
				// claimed at once, as without a census. The first texture of a kind is what the next ones are written ahead
				// from, and a normal map is one only once it is converted
				using (ImportSteps.Step("textures imported one by one"))
					return Texture2DImporter.ClaimNow(Texture2DImporter.Import(sourceTexture, outputPath, claim), claim);
			}
			using (ImportSteps.Step("textures imported one by one"))
				return Texture2DImporter.Import(sourceTexture, outputPath, claim);
		}

		// While a texture census runs (BeginTextureCensus), what ImportVTF has to write it writes ahead of the import
		TextureCensus textureCensus;

		/// <summary>
		/// Starts a texture census, which ends when it is disposed. Until then, a texture ImportVTF is asked for that has to be
		/// written is written ahead of its import (Texture2DImporter.WriteAhead) and the asker is handed stand-ins for it, and
		/// so is a mask ImportAlphaMask has to write (Texture2DImporter.WriteAlphaMaskAhead); ending the census imports every
		/// one of them in ONE refresh, where each took one of its own as it was asked for (about 0.12 s apiece in this
		/// project). MaterialImporter.ImportAll builds its materials once under a census, for the textures they ask for, and
		/// then again for real.
		/// </summary>
		internal TextureCensus BeginTextureCensus()
		{
			if (textureCensus != null)
				throw new InvalidOperationException("a texture census is running already");
			// What a build claims of a texture is left for the build made for real afterwards (Texture2DImporter.ClaimsDeferred)
			Texture2DImporter.ClaimsDeferred = true;
			return textureCensus = new TextureCensus(this);
		}

		/// <summary>See <see cref="BeginTextureCensus"/>.</summary>
		internal sealed class TextureCensus : IDisposable
		{
			readonly GameResources owner;
			readonly List<Texture2DImporter.Ahead> written = new();
			readonly Dictionary<GameEntry, (TextureClaim claim, Texture2DImporter.Ahead ahead)> byEntry = new();
			// The masks written ahead, by the texture whose alpha they are
			readonly Dictionary<GameEntry, Texture2DImporter.Ahead> alphaMasks = new();

			internal TextureCensus(GameResources owner) => this.owner = owner;

			/// <summary>How many textures it has written ahead that are not imported yet.</summary>
			internal int Pending => written.Count;

			/// <summary>
			/// A texture written ahead earlier in this census, as a build asking for it now would meet it: as written, its
			/// claim comes after (Texture2DImporter.ClaimsDeferred) - its stand-ins, which answer what the import of what was
			/// written is going to answer. Except one written as a normal map, whose import is Unity's own importer's and whose
			/// stand-in answers nothing: asked for with another claim, it is imported now, as written.
			/// </summary>
			internal bool TryTake(GameEntry entry, TextureClaim claim, out Texture2D[] textures)
			{
				textures = null;
				if (!byEntry.TryGetValue(entry, out var found))
					return false;
				if (SameClaim(found.claim, claim) || !found.claim.NormalMap)
				{
					textures = found.ahead.StandIns;
					return true;
				}
				byEntry.Remove(entry);
				written.Remove(found.ahead);
				textures = Texture2DImporter.ImportAhead(new[] { found.ahead })[0];
				return true;
			}

			/// <summary>Writes the texture ahead, and returns its stand-ins; null when it cannot be (the asker imports it now).</summary>
			internal Texture2D[] WriteAhead(GameEntry entry, VTF sourceTexture, string outputPath, TextureClaim claim)
			{
				var ahead = Texture2DImporter.WriteAhead(sourceTexture, outputPath, claim);
				if (ahead == null)
					return null;
				written.Add(ahead);
				byEntry[entry] = (claim, ahead);
				return ahead.StandIns;
			}

			/// <summary>A mask written ahead earlier in this census, from this texture's alpha: its stand-in.</summary>
			internal bool TryTakeAlphaMask(GameEntry entry, out Texture2D standIn)
			{
				standIn = alphaMasks.TryGetValue(entry, out var ahead) ? ahead.StandIns[0] : null;
				return standIn != null;
			}

			/// <summary>Writes a mask of the texture's alpha ahead, and returns its stand-in; null when it cannot be (the asker
			/// imports it now, or there is no alpha to take).</summary>
			internal Texture2D WriteAlphaMaskAhead(GameEntry entry, VTF sourceTexture, string outputPath)
			{
				var ahead = Texture2DImporter.WriteAlphaMaskAhead(sourceTexture, outputPath);
				if (ahead == null)
					return null;
				written.Add(ahead);
				alphaMasks[entry] = ahead;
				return ahead.StandIns[0];
			}

			/// <summary>Imports every texture and mask written ahead so far, in one refresh.</summary>
			internal void ImportWritten()
			{
				var aheads = written.ToArray();
				written.Clear();
				byEntry.Clear();
				alphaMasks.Clear();
				Texture2DImporter.ImportAhead(aheads);
			}

			// What was written is imported however the census ends: a .png left behind would be imported by whichever
			// refresh came across it next
			public void Dispose()
			{
				try
				{
					if (written.Count > 0)
						ImportWritten();
				}
				finally
				{
					if (owner.textureCensus == this)
					{
						owner.textureCensus = null;
						Texture2DImporter.ClaimsDeferred = false;
					}
				}
			}

			static bool SameClaim(TextureClaim a, TextureClaim b) =>
				a.Color == b.Color && a.NormalMap == b.NormalMap && a.SelfShadowed == b.SelfShadowed &&
				a.AlphaTestReference == b.AlphaTestReference;
		}


		/// <summary>
		/// All the frames of an animated texture as one texture array (<see cref="Texture2DImporter.ImportFrames"/>), or null
		/// when it has one frame. A current array is taken as it is, without reading the texture.
		/// </summary>
		public Texture2DArray ImportVTFFrames(string entryName, bool isNormalMap, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTF);
			searchPaths ??= PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null || entry.extension != PackagePath.ExtensionVTF)
				return null;
			var outputPath = PackagePath.GetOutputPath(PackagePath.GetTextureEntryName(entry.keyname));
			var found = Texture2DImporter.LoadFrames(outputPath);
			if (found != null)
				return found;
			var sourceTexture = LoadVTF(entry);
			if (sourceTexture == null)
				return null;
			return Texture2DImporter.ImportFrames(sourceTexture, outputPath, isNormalMap);
		}

		/// <summary>
		/// A cubemap a material names ($envmap), as a Unity cubemap asset, or null when there is none or the texture isn't an
		/// environment map. The game renders in HDR - its maps carry only HDR cubemaps - and an environment map's HDR version
		/// sits beside it as "&lt;name&gt;.hdr.vtf", so that one is taken first.
		/// </summary>
		public Cubemap ImportCubemap(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			searchPaths ??= PackagePath.DefaultMaterialPaths;
			var name = entryName.EndsWith("." + PackagePath.ExtensionVTF, StringComparison.OrdinalIgnoreCase)
					 ? entryName.Substring(0, entryName.Length - PackagePath.ExtensionVTF.Length - 1) : entryName;
			var entry = GetEntry(name + ".hdr." + PackagePath.ExtensionVTF, searchPaths) ??
						GetEntry(name + "." + PackagePath.ExtensionVTF, searchPaths);
			if (entry == null)
				return null;
			var outputPath = PackagePath.GetOutputPath(PackagePath.GetTextureEntryName(entry.keyname));
			// Taken on its header alone when it is current: the pixels are six faces
			var header = LoadVTFHeader(entry);
			if (header == null || (header.Flags & VTFImageFlag.TEXTUREFLAGS_ENVMAP) == 0)
				return null;
			var reusable = Texture2DImporter.ReusableCubemap(header, outputPath);
			if (reusable != null)
				return reusable;
			var sourceTexture = LoadVTF(entry);
			if (sourceTexture == null || (sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_ENVMAP) == 0)
				return null;
			return Texture2DImporter.ImportCubemap(sourceTexture, outputPath);
		}

		/// <summary>
		/// The alpha channel of a texture, as a mask texture of its own (see
		/// <see cref="Texture2DImporter.ImportAlphaMask"/>), or null when it has none.
		/// </summary>
		public Texture2D ImportAlphaMask(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTF);
			searchPaths ??= PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null || entry.extension != PackagePath.ExtensionVTF)
				return null;
			var outputPath = PackagePath.GetOutputPath(PackagePath.GetTextureEntryName(entry.keyname));
			// A mask written earlier is taken without reading the texture at all
			Texture2D reusable;
			using (ImportSteps.Step("texture lookups"))
				reusable = Texture2DImporter.ReusableAlphaMask(outputPath);
			if (reusable != null)
				return reusable;
			// Written ahead earlier in a texture census: its stand-in
			if (textureCensus != null && textureCensus.TryTakeAlphaMask(entry, out var standIn))
				return standIn;
			VTF sourceTexture;
			using (ImportSteps.Step("texture reads"))
				sourceTexture = LoadVTF(entry);
			// In a texture census it is written now and imported with the textures when the census ends; without a template
			// yet, it is imported now, and is the template for the next
			if (textureCensus != null)
			{
				using (ImportSteps.Step("alpha masks written ahead"))
					standIn = textureCensus.WriteAlphaMaskAhead(entry, sourceTexture, outputPath);
				if (standIn != null)
					return standIn;
			}
			using (ImportSteps.Step("alpha masks imported one by one"))
				return Texture2DImporter.ImportAlphaMask(sourceTexture, outputPath);
		}


		public VMT LoadVMT(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVMT);
			if (searchPaths == null)
				searchPaths = PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadVMT(entry);
		}

		public VMT LoadVMT(GameEntry entry)
		{
			if (entry == null) return null;
			VMT sourceMaterial = null; 
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceMaterial = VMT.Read(stream);
				return true;
			}))
			{
				return null;
			}
			return sourceMaterial;
		}

		// A map references the same handful of materials from tens of thousands of brush sides -
		// a map resolves 34040 sides down to 1393 distinct materials. Without this, every one
		// of those sides re-reads the VMT out of the VPK, re-parses it, and does an AssetDatabase
		// lookup, only to arrive at the Material we already had. Keyed on the GameEntry instance
		// because entries are interned in `lookup`, so the same name always yields the same object.
		readonly Dictionary<(GameEntry entry, bool isSprite, bool brushFace), Material> importedMaterials = new();

		/// <summary>
		/// Lets go of the materials imported so far. They are one import's: kept, they stayed loaded for as long as this
		/// object lived - the importer's lives until the domain reloads - every import adding its own.
		/// </summary>
		public void ReleaseImportedMaterials() => importedMaterials.Clear();

		/// <param name="brushFace">
		/// The material goes on a brush face that is not a displacement. A material that blends a second layer
		/// by vertex alpha then comes back as vbsp would give it to such a face, without that layer
		/// (<see cref="VMT.WithoutSecondLayer"/>), saved beside the original as "&lt;name&gt;_wvt_patch".
		/// </param>
		public Material ImportVMT(string entryName, string[] searchPaths = null, bool isSprite = false, bool brushFace = false)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVMT);
			if (searchPaths == null)
				searchPaths = PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;

			if (importedMaterials.TryGetValue((entry, isSprite, brushFace), out var cachedMaterial) &&
				cachedMaterial)
				return cachedMaterial;

			Material material = null;
			var (sourceMaterial, outputPath) = MaterialRequestOf(entry, brushFace);
			if (sourceMaterial != null)
				material = MaterialImporter.Import(this, sourceMaterial, outputPath, isSprite);

			// Cached even when null: a VMT that failed to load will fail identically next time,
			// and a miss is exactly as expensive as a hit.
			importedMaterials[(entry, isSprite, brushFace)] = material;
			return material;
		}

		// The VMT a material is built from and where it is written: for a brush face, a material that blends a second
		// layer as vbsp patches it (see ImportVMT)
		(VMT sourceMaterial, string outputPath) MaterialRequestOf(GameEntry entry, bool brushFace)
		{
			var sourceMaterial = LoadVMT(entry);
			if (sourceMaterial == null)
				return (null, null);
			var outputPath = PackagePath.GetOutputPath(entry.keyname);
			if (brushFace && (MaterialImporter.VertexColorOf(sourceMaterial) & MaterialImporter.SourceVertexColor.Blend) != 0)
			{
				sourceMaterial = sourceMaterial.WithoutSecondLayer();
				outputPath = Path.ChangeExtension(outputPath, null) + "_wvt_patch";
			}
			return (sourceMaterial, outputPath);
		}

		/// <summary>
		/// Imports the materials these requests name as <see cref="ImportVMT"/> would, one at a time - the same entries, the
		/// same patch for a brush face, into the same cache - but writes them all at once (MaterialImporter.ImportAll): one
		/// refresh between them, where one at a time takes one each. Asked first, with what a map's import is going to ask
		/// for, ImportVMT then finds each of them here.
		/// </summary>
		/// <param name="progress">Told the index of each material it builds and the count, for a progress bar.</param>
		public void PrepareMaterials(IEnumerable<(string entryName, bool isSprite, bool brushFace)> requests, Action<int, int> progress = null)
		{
			var keys = new List<(GameEntry entry, bool isSprite, bool brushFace)>();
			var batch = new List<MaterialImporter.Request>();
			var asked = new HashSet<(GameEntry entry, bool isSprite, bool brushFace)>();
			foreach (var (entryName, isSprite, brushFace) in requests)
			{
				if (string.IsNullOrEmpty(entryName))
					continue;
				var name = entryName;
				PackagePath.EnsureExtension(ref name, PackagePath.ExtensionVMT);
				var entry = GetEntry(name, PackagePath.DefaultMaterialPaths);
				if (entry == null)
					continue;
				var key = (entry, isSprite, brushFace);
				if (importedMaterials.ContainsKey(key) || !asked.Add(key))
					continue;
				var (sourceMaterial, outputPath) = MaterialRequestOf(entry, brushFace);
				// One that does not load is left to ImportVMT, which says so in its own way
				if (sourceMaterial == null)
					continue;
				keys.Add(key);
				batch.Add(new MaterialImporter.Request(sourceMaterial, outputPath, isSprite));
			}
			if (batch.Count == 0)
				return;

			var materials = MaterialImporter.ImportAll(this, batch, progress);
			for (int i = 0; i < keys.Count; i++)
			{
				if (materials[i] != null)
					importedMaterials[keys[i]] = materials[i];
			}
		}

		// What vbsp makes of a brush side with each material (see VMTContents), read once per material
		readonly Dictionary<GameEntry, VMTContents> materialContents = new();

		/// <summary>
		/// What vbsp makes of a brush side with this material: its contents and surface flags, as FindMiptex gives
		/// them. A material that can't be found comes back as <see cref="VMTContents.Missing"/>, which vbsp builds as
		/// solid.
		/// </summary>
		public VMTContents GetMaterialContents(string materialName)
		{
			if (string.IsNullOrEmpty(materialName)) return VMTContents.Missing;
			var entryName = materialName.ToLowerInvariant();
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVMT);
			GameEntry entry = GetEntry(entryName, PackagePath.DefaultMaterialPaths);
			if (entry == null)
				return VMTContents.Missing;
			if (materialContents.TryGetValue(entry, out var cached))
				return cached;
			var contents = VMTContents.FromText(LoadText(entry), LoadIncludedMaterialText);
			materialContents[entry] = contents;
			return contents;
		}

		// A "patch" material names the material it includes by its path in the game, usually with the materials/
		// folder and the extension
		string LoadIncludedMaterialText(string include)
		{
			if (string.IsNullOrEmpty(include)) return null;
			var entryName = include.ToLowerInvariant();
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVMT);
			return LoadText(GetEntry(entryName) ?? GetEntry(entryName, PackagePath.DefaultMaterialPaths));
		}

		// any reference to a .spr file, is really a .vmt file
		public VMT LoadSPR(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.CleanFullPath(ref entryName);
			if (entryName.EndsWith($".{PackagePath.ExtensionSPR}"))
				entryName = Path.ChangeExtension(entryName, string.Empty);
			return LoadVMT(entryName, searchPaths);
		}

		// any reference to a .spr file, is really a .vmt file
		public Material ImportSPR(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			return ImportVMT(SpriteMaterialName(entryName), searchPaths, isSprite: true);
		}

		/// <summary>
		/// The material a sprite names, as <see cref="ImportSPR"/> imports it: the name cleaned, a .spr reference as the .vmt of
		/// the same name. Asked for with isSprite in <see cref="PrepareMaterials"/>, ImportSPR then finds it there.
		/// </summary>
		public static string SpriteMaterialName(string entryName)
		{
			if (string.IsNullOrEmpty(entryName)) return entryName;
			PackagePath.CleanFullPath(ref entryName);
			if (entryName.EndsWith($".{PackagePath.ExtensionSPR}"))
				entryName = entryName.Remove(entryName.Length - (PackagePath.ExtensionSPR.Length + 1));
			return entryName;
		}

		public VTX LoadVTX(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTX);
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadVTX(entry);
		}

		public VTX LoadVTX(GameEntry entry)
		{
			if (entry == null) return null;
			if (entry.extension != PackagePath.ExtensionVTX) return null;
			VTX header = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				using var reader = new BinaryReader(stream);
				header = VTX.Read(reader);
				return true;
			})) return null;
			return header;
		}

		public VVD LoadVVD(string entryName, MDL mdlHeader, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVVD);
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadVVD(entry, mdlHeader);
		}

		public VVD LoadVVD(GameEntry entry, MDL mdlHeader)
		{
			if (entry == null) return null;
			if (entry.extension != PackagePath.ExtensionVVD) return null;
			VVD header = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				using var reader = new BinaryReader(stream);
				header = VVD.Load(reader, mdlHeader);
				return true;
			})) return null;
			return header;
		}

		public MDL LoadMDL(string entryName, Lookup lookup = null, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionMDL);
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadMDL(entry, lookup);
		}

		/// <summary>
		/// The models currently being read, so <c>$includemodel</c> cannot recurse into one of them.
		/// <para>
		/// Reading a model reads the models it shares animation with, and nothing in the format says
		/// those cannot point back. Sharing one interning table across files used to break such a
		/// cycle by accident - the second visit found the first one's structures at the same offsets
		/// and stopped - which is no longer true now that each file interns separately.
		/// </para>
		/// </summary>
		readonly HashSet<string> mdlLoadsInProgress = new();

		public MDL LoadMDL(GameEntry entry, Lookup lookup = null)
		{
			if (entry == null) return null;
			if (entry.extension != PackagePath.ExtensionMDL) return null;

			if (!mdlLoadsInProgress.Add(entry.keyname))
				return null;

			// A Lookup resolves offsets against one file, so it has to belong to THIS one. Done here
			// rather than at the call sites because a caller that gets it wrong produces structures
			// read out of the wrong file, which looks like corrupt model data rather than a mistake.
			lookup = Lookup.ForFile(lookup, entry.keyname);

			MDL header = null;
			try
			{
				if (!LoadFileAsStream(entry, delegate (Stream stream)
				{
					using var reader = new BinaryReader(stream);
					header = MDL.Read(reader, this, lookup);
					return true;
				})) return null;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			finally
			{
				mdlLoadsInProgress.Remove(entry.keyname);
			}
			return header;
		}

		/// <summary>
		/// Reads a model's collision. Returns null when it cannot be read, which leaves the model without collision
		/// rather than failing its import.
		/// </summary>
		public PhyFile LoadPHY(GameEntry entry)
		{
			if (entry == null) return null;
			if (entry.extension != PackagePath.ExtensionPHY) return null;
			var bytes = LoadFileAsBytes(entry);
			if (bytes == null) return null;
			try
			{
				return PhyFile.Read(bytes);
			}
			catch (Exception exception)
			{
				Debug.LogError($"[Chisel] could not read the collision model {entry.keyname}: {exception.Message}");
				return null;
			}
		}

		public SourceModel LoadSourceModel(string entryName, string[] searchPaths = null)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			var mdlEntry = GetEntry(entryName, searchPaths);
			// LoadMDL scopes its own interning to the file it reads; nothing here needs to see it.
			var mdl = mdlEntry != null ? LoadMDL(mdlEntry) : null;
			if (mdl == null)
			{
				Debug.LogError($"Failed to load mdlHeader {entryName}");
				return null;
			}

			var vvdHeaderName = Path.ChangeExtension(entryName, PackagePath.ExtensionVVD);
			var vvdEntry = GetEntry(vvdHeaderName, searchPaths);
			var vvd = (vvdEntry != null) ? LoadVVD(vvdEntry, mdl) : null;
			if (vvd == null)
			{
				Debug.LogError($"Failed to load vvdHeader {vvdHeaderName}");
				return null;
			}

			if (vvd.Checksum != mdl.Checksum)
			{
				Debug.LogError($"Model sidecar checksum mismatch for {entryName}: " +
							   $"MDL {mdl.Checksum} ({mdlEntry.sourceFilename}), " +
							   $"VVD {vvd.Checksum} ({vvdEntry.sourceFilename})");
				return null;
			}

			// Black Mesa ships a few models whose dx90 VTX is stale while the software VTX belongs to the
			// current MDL (c2a5a_lower_dam_foliage_07 is one). Source validates these checksums before use;
			// do the same and try the other compiled VTX flavours instead of combining incompatible files.
			GameEntry vtxEntry = null;
			VTX vtx = null;
			var vtxExtensions = new[] { PackagePath.ExtensionVTX_DX90, "sw.vtx", "dx80.vtx" };
			var rejectedVtx = new List<string>();
			foreach (var extension in vtxExtensions)
			{
				var candidateName = Path.ChangeExtension(entryName, extension);
				var candidateEntry = GetEntry(candidateName, searchPaths);
				if (candidateEntry == null)
					continue;

				var candidate = LoadVTX(candidateEntry);
				if (candidate == null)
					continue;
				if (candidate.CheckSum == mdl.Checksum)
				{
					vtxEntry = candidateEntry;
					vtx = candidate;
					break;
				}
				rejectedVtx.Add($"{candidateName}={candidate.CheckSum}");
			}
			if (vtx == null)
			{
				Debug.LogError($"Failed to load a VTX matching {entryName} checksum {mdl.Checksum}. " +
							   (rejectedVtx.Count == 0 ? "No VTX sidecars were found." :
								$"Rejected: {string.Join(", ", rejectedVtx)}"));
				return null;
			}

			// Collision is optional: a model without any has no .phy at all.
			var phyEntry = GetEntry(Path.ChangeExtension(entryName, PackagePath.ExtensionPHY), searchPaths);
			var phy = (phyEntry != null) ? LoadPHY(phyEntry) : null;
			
			for (var i = 0; i < mdl.TextureDirs.Length; i++)
			{
				mdl.TextureDirs[i] = 
					PackagePath.CleanDirectory(mdl.TextureDirs[i]);
			}

			return new SourceModel
			{
				MDL = mdl,
				VVD = vvd,
				VTX = vtx,
				PHY = phy,
				MDLEntry = mdlEntry,
				VVDEntry = vvdEntry,
				VTXEntry = vtxEntry,
				PHYEntry = phyEntry
			};
		}

		/// <param name="skin">The entity's skin: which of the model's material sets it uses. One the model does not
		/// have gives the default skin, as in the game.</param>
		public GameObject ImportModel(string entryName, string[] searchPaths = null, int skin = 0)
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			// A map places the same model again and again - a map with 170 entities over 64 models - and each one
			// read the model's files out of the VPKs again, and asked for its materials and files again, only to arrive at
			// the prefab it already had
			var entry = GetEntry(entryName, searchPaths);
			if (entry != null && importedModels.TryGetValue((entry, skin), out var cached))
				return cached;
			GameObject prefab;
			if (entry != null && importedModelFiles.TryGetValue(entry, out var skinned))
				prefab = ModelImporter.SkinOf(skinned, skin);
			else
			{
				// One PrepareModels found does not read is not read again, nor said again
				var sourceModel = (entry != null && unreadableModels.Contains(entry)) ? null : LoadSourceModel(entryName, searchPaths);
				prefab = (sourceModel == null) ? null
					   : ModelImporter.Import(this, sourceModel, PackagePath.GetOutputPath(sourceModel.MDLEntry.keyname), skin);
			}
			// Cached even when null, as importedMaterials is: what failed fails identically next time
			if (entry != null)
				importedModels[(entry, skin)] = prefab;
			return prefab;
		}

		// The models imported so far, by file and skin: one import's, like importedMaterials
		readonly Dictionary<(GameEntry entry, int skin), GameObject> importedModels = new();

		// The model files PrepareModels imported, with the prefabs of all their skins (ModelImporter.ImportAll)
		readonly Dictionary<GameEntry, GameObject[]> importedModelFiles = new();

		// The model files that could not be read
		readonly HashSet<GameEntry> unreadableModels = new();

		/// <summary>
		/// Lets go of the models imported so far. They are one import's, as the materials are
		/// (<see cref="ReleaseImportedMaterials"/>).
		/// </summary>
		public void ReleaseImportedModels()
		{
			importedModels.Clear();
			importedModelFiles.Clear();
			unreadableModels.Clear();
		}

		/// <summary>
		/// Imports the models these requests name, each file once, together (ModelImporter.ImportAll): their materials in one
		/// refresh for their textures and one for themselves, and the model files that have to be written in one more, where
		/// each model took its own of all three as an entity asked for it. Asked first, with what a map's entities are going
		/// to ask for (VmfEntityConverter.ModelRequests), ImportModel then finds every one of them imported.
		/// </summary>
		/// <param name="progress">Told the index of each model it writes and the count, for a progress bar.</param>
		public void PrepareModels(IEnumerable<(string entryName, int skin)> requests, Action<int, int> progress = null)
		{
			// Each file once: one imported already, under any skin, is not asked for again
			var asked = new HashSet<GameEntry>(importedModelFiles.Keys);
			foreach (var key in importedModels.Keys)
				asked.Add(key.entry);
			var files = new List<(GameEntry entry, string entryName)>();
			foreach (var (entryName, _) in requests)
			{
				if (string.IsNullOrEmpty(entryName))
					continue;
				var entry = GetEntry(entryName, null);
				if (entry == null || !asked.Add(entry) || unreadableModels.Contains(entry))
					continue;
				files.Add((entry, entryName));
			}
			if (files.Count == 0)
				return;

			var batch = new List<ModelImporter.Request>(files.Count);
			foreach (var (entry, entryName) in files)
				batch.Add(new ModelImporter.Request(() => ReadModel(entry, entryName), PackagePath.GetOutputPath(entry.keyname)));
			var imported = ModelImporter.ImportAll(this, batch, progress);
			for (int i = 0; i < files.Count; i++)
				importedModelFiles[files[i].entry] = imported[i];
		}

		// A model's files, as ImportModel reads them, each time they are needed (ModelImporter.ImportAll lets a model go once
		// it is done with it). One that does not read is said once, and not read again.
		SourceModel ReadModel(GameEntry entry, string entryName)
		{
			if (unreadableModels.Contains(entry))
				return null;
			var model = LoadSourceModel(entryName);
			if (model == null)
				unreadableModels.Add(entry);
			return model;
		}

		public T Import<T>(GameEntry entry) where T : UnityEngine.Object
		{
			if (entry == null) return null;
			return Import<T>(entry.keyname);
		}

		public T Import<T>(string entryName, string[] searchPaths = null) where T : UnityEngine.Object
		{
			if (string.IsNullOrEmpty(entryName)) return null;
			var extension = PackagePath.GetExtension(entryName);
			switch (extension)
			{
				case PackagePath.ExtensionVMT: return ImportVMT(entryName, searchPaths) as T; 
				case PackagePath.ExtensionVTF: var textures = ImportVTF(entryName, searchPaths); return (textures == null || textures.Length == 0) ? null : textures[0] as T; 
				case PackagePath.ExtensionMDL: return ImportModel(entryName, searchPaths) as T; 
				default:
				{
					var outputPath = ImportFile(entryName, searchPaths);
					if (outputPath == null) return null;
					var assetPath = PackagePath.GetAssetPath(entryName);
					return AssetDatabase.LoadAssetAtPath<T>(assetPath);
				}
			}
		}

		readonly Dictionary<string, VPKResource> resourceLookup = new();
		public VPKResource LoadResource(string resourceFilename)
		{
			if (!resourceLookup.TryGetValue(resourceFilename, out var resource))
			{
				if (File.Exists(resourceFilename))
					resource = new VPKResource(resourceFilename);
				else
					resource = null;
				resourceLookup[resourceFilename] = resource;
			}
			if (resource == null) { Debug.LogError($"Could not find {resourceFilename}"); }
			return resource;
		}

		/// <summary>
		/// Closes the game's archives that no read is using. A read keeps its archive open for the next one (VPKParser), so a
		/// map's thousands of reads open each archive about once; a map import closes them when it ends, so the game's files
		/// are not held open between imports. The next read opens its archive again.
		/// </summary>
		public void CloseArchives()
		{
			foreach (var resource in resourceLookup.Values)
				resource?.CloseArchives();
		}

		/// <summary>
		/// The entity classes the configured game's FGDs declare. Entities such as NPCs and items store no model path in
		/// the .vmf - the model comes from the entity class - and the kind of class an entity is decides how the game
		/// gives it collision, so this is the only place either exists outside game code. Hammer reads the same files
		/// for the same reasons.
		/// </summary>
		public FgdEntityClasses EntityClasses { get; } = new();

		/// <summary>
		/// Loads entity definitions from whatever FGDs the configured game ships. The game's mod
		/// directory sits next to its "bin" directory, which is where Source games keep them, so
		/// this is derived from the paths already being mounted rather than hardcoded per title.
		/// </summary>
		void LoadEntityDefinitions(string[] inputPaths)
		{
			var searched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var path in inputPaths)
			{
				if (string.IsNullOrEmpty(path))
					continue;

				// <game>/<mod>/ -> <game>/bin/
				var binPath = Path.GetFullPath(Path.Combine(path, @"..", "bin"));
				if (!searched.Add(binPath) || !Directory.Exists(binPath))
					continue;

				EntityClasses.LoadDirectory(binPath);
			}

			if (EntityClasses.Count > 0)
				Debug.Log($"Loaded {EntityClasses.Count} entity class definitions from the game's FGD files.");
		}

		public void InitializePaths(string[] inputPaths)
		{
			LoadEntityDefinitions(inputPaths);

			var rentedCharList = ArrayPool<char>.Shared.Rent(255);
			var vpkFiles = HashSetPool<string>.Get();
			try
			{
				foreach (var path in inputPaths)
				{
					Profiler.BeginSample("Directory.GetFiles");
					var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
					Profiler.EndSample();

					if (files.Length == 0)
						continue;

					Profiler.BeginSample("EnsureCapacity");
					lookup.EnsureCapacity(files.Length);
					vpkFiles.Clear();
					vpkFiles.EnsureCapacity(files.Length);
					Profiler.EndSample();

					foreach (var filename in files)
					{
						if (string.IsNullOrEmpty(filename))
							continue;

						if (filename.EndsWith($".{PackagePath.ExtensionVPK}"))
						{
							Profiler.BeginSample("vpkFiles.Add");
							if (filename.EndsWith(PackagePath.ExtensionVPK_Dir))
								vpkFiles.Add(filename);
							Profiler.EndSample();
							continue;
						}

						var relativefilename = filename.Substring(path.Length);
						var keyname = relativefilename;
						PackagePath.CleanFullPath(ref keyname);
						if (lookup.ContainsKey(keyname))
							continue; 

						PackagePath.DecomposePath(relativefilename, out string directory, out string name, out string extension);
						Profiler.BeginSample("lookup");						
						var entry = new GameEntry()
						{
							keyname			= keyname,
							extension		= extension,
							name			= name,
							sourceFilename	= filename,
							entry			= new VPKEntry { }
						};
						lookup[keyname] = entry;
						Profiler.EndSample();
					}

					Profiler.BeginSample("LoadVPKFiles");
					foreach (var vpkFile in vpkFiles)
					{
						try
						{
							// TODO: do we really need this log?
							Profiler.BeginSample("new VPKArchive");
							var logFileName = $"{Application.dataPath}\\{Path.GetFileNameWithoutExtension(vpkFile)}_log.txt";
							var archive = new VPKArchive(vpkFile, logFileName, 2);
							Profiler.EndSample();

							Profiler.BeginSample("archive.GetEntries");
							var archiveEntries = archive.GetEntries();
							Profiler.EndSample();

							if (archiveEntries.Count == 0)
								continue;
							
							Profiler.BeginSample("EnsureCapacity");
							lookup.EnsureCapacity(archiveEntries.Count);
							Profiler.EndSample();

							Profiler.BeginSample("archiveEntries Iteration");
							foreach (var item in archiveEntries)
							{
								var keyname = item.Key;
								PackagePath.CleanFullPath(ref keyname);
								if (lookup.ContainsKey(keyname))
									continue;

								if (lookup.ContainsKey(keyname))
									continue;

								PackagePath.DecomposePath(keyname, out string directory, out string name, out string extension);
								Profiler.BeginSample("lookup");
								var entry = new GameEntry()
								{
									keyname			= keyname,
									extension		= extension,
									name			= name,
									sourceFilename	= vpkFile,
									entry			= item.Value
								};
								lookup[keyname] = entry;
								Profiler.EndSample();
							}
							Profiler.EndSample();
						}
						catch (System.Exception ex)
						{
							Debug.LogException(ex);
						}
					}
					Profiler.EndSample();
				}
			}
			finally
			{
				ArrayPool<char>.Shared.Return(rentedCharList);
				HashSetPool<string>.Release(vpkFiles);
			}
		}
	}
}
