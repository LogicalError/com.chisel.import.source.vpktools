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
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadText(entry);
		}

		public string LoadText(GameEntry entry)
		{
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
			VTF sourceTexture = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceTexture = VTF.Read(stream);
				return true;
			})) return null;
			return sourceTexture;
		}

		public Texture2D[] ImportVTF(string entryName, string[] searchPaths = null)
		{
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTF);
			if (searchPaths == null)
				searchPaths = PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			if (entry.extension != PackagePath.ExtensionVTF)
				return null;
			var sourceTexture = LoadVTF(entry);
			if (sourceTexture == null)
				return null;			
			var outputPath = PackagePath.GetOutputPath(entry.keyname);
			return Texture2DImporter.Import(sourceTexture, outputPath);
		}


		public VMT LoadVMT(string entryName, string[] searchPaths = null)
		{
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

		public Material ImportVMT(string entryName, string[] searchPaths = null, bool isSprite = false)
		{
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVMT);
			if (searchPaths == null)
				searchPaths = PackagePath.DefaultMaterialPaths;
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			VMT sourceMaterial = LoadVMT(entry);
			if (sourceMaterial == null)
				return null;			
			var outputPath = PackagePath.GetOutputPath(entry.keyname);
			return MaterialImporter.Import(this, sourceMaterial, outputPath, isSprite);
		}

		// any reference to a .spr file, is really a .vmt file
		public VMT LoadSPR(string entryName, string[] searchPaths = null)
		{
			PackagePath.CleanFullPath(ref entryName);
			if (entryName.EndsWith($".{PackagePath.ExtensionSPR}"))
				entryName = Path.ChangeExtension(entryName, string.Empty);
			return LoadVMT(entryName, searchPaths);
		}

		// any reference to a .spr file, is really a .vmt file
		public Material ImportSPR(string entryName, string[] searchPaths = null)
		{
			PackagePath.CleanFullPath(ref entryName);
			if (entryName.EndsWith($".{PackagePath.ExtensionSPR}"))
				entryName = entryName.Remove(entryName.Length - (PackagePath.ExtensionSPR.Length + 1));
			return ImportVMT(entryName, searchPaths, isSprite: true);
		}

		public VTX LoadVTX(string entryName, string[] searchPaths = null)
		{
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVTX);
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadVTX(entry);
		}

		public VTX LoadVTX(GameEntry entry)
		{
			if (entry.extension != PackagePath.ExtensionVTX)
				return null;

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
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionVVD);
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadVVD(entry, mdlHeader);
		}

		public VVD LoadVVD(GameEntry entry, MDL mdlHeader)
		{
			if (entry.extension != PackagePath.ExtensionVVD)
				return null;

			VVD header = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				using var reader = new BinaryReader(stream);
				header = VVD.Load(reader, mdlHeader);
				return true;
			})) return null;
			return header;
		}

		public MDL LoadMDL(string entryName, Lookup _lookup, string[] searchPaths = null)
		{
			PackagePath.EnsureExtension(ref entryName, PackagePath.ExtensionMDL);
			GameEntry entry = GetEntry(entryName, searchPaths);
			if (entry == null)
				return null;
			return LoadMDL(entry, _lookup);
		}

		public MDL LoadMDL(GameEntry entry, Lookup lookup)
		{
			if (entry.extension != PackagePath.ExtensionMDL)
				return null;

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
			return header;
		}

		public SourceModel LoadSourceModel(string modelId, string[] searchPaths = null)
		{
			var lookup = new Lookup();

			var mdlEntry = GetEntry(modelId, searchPaths);
			var mdl = mdlEntry != null ? LoadMDL(mdlEntry, lookup) : null;
			if (mdl == null)
			{
				Debug.LogError($"Failed to load mdlHeader {modelId}");
				return null;
			}

			var vvdHeaderName = Path.ChangeExtension(modelId, PackagePath.ExtensionVVD);
			var vvdEntry = GetEntry(vvdHeaderName);
			var vvd = (vvdEntry != null) ? LoadVVD(vvdEntry, mdl) : null;
			if (vvd == null)
			{
				Debug.LogError($"Failed to load vvdHeader {vvdHeaderName}");
				return null;
			}

			var vtxHeaderName = Path.ChangeExtension(modelId, PackagePath.ExtensionVTX_DX90);
			var vtxEntry = GetEntry(vtxHeaderName);
			var vtx = (vtxEntry != null) ? LoadVTX(vtxEntry) : null;
			if (vtx == null)
			{
				Debug.LogError($"Failed to load vtxHeader {vtxHeaderName}");
				return null;
			}
			
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
				MDLEntry = mdlEntry,
				VVDEntry = vvdEntry,
				VTXEntry = vtxEntry
			};
		}

		public GameObject ImportModel(string entryName, string[] searchPaths = null)
		{
			var sourceModel = LoadSourceModel(entryName, searchPaths);
			if (sourceModel == null)
				return null;
			var outputPath = PackagePath.GetOutputPath(sourceModel.MDLEntry.keyname);
			return ModelImporter.Import(this, sourceModel, outputPath, skin: 0);
		}

		public T Import<T>(GameEntry entry) where T : UnityEngine.Object
		{
			if (entry == null)
				return null;
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

		public void InitializePaths(string[] inputPaths)
		{
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
							sourceFilename	= relativefilename,
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