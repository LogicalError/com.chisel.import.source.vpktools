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
		public int		extensionStart;
		public string	sourceFilename;
		public VPKEntry entry;

		public string Extension
		{ 
			get
			{
				if (extensionStart == keyname.Length)
					return string.Empty;
				return keyname.Substring(extensionStart);
			}
		}

		public string Name
		{
			get
			{
				if (extensionStart == 0)
					return string.Empty;
				return keyname.Remove(extensionStart);
			}
		}
	}

	public class GameResources
	{
		public const string OutputPath = "import";

		// TODO: make this work well with resource lookups in packages (lets avoid duplication of functionality)
		readonly Dictionary<string, GameEntry> lookup = new();

		public IEnumerable<string> GetEntryNames() { return lookup.Keys; }


		public GameEntry GetEntry(string entryName)
		{
			if (entryName == null)
				return null;
			// TODO: use PackagePath instead
			var rentedCharList = ArrayPool<char>.Shared.Rent(255);
			entryName = CleanedKeyname(entryName, 0, rentedCharList, out int extensionstart);
			ArrayPool<char>.Shared.Return(rentedCharList);
			if (entryName == null)
				return null;
			if (lookup.TryGetValue(entryName, out var entry))
				return entry;
			return null;
		}

		public bool LoadFileAsStream(GameEntry entry, VPKParser.FileLoadDelegate streamLoadDelegate)
		{
			if (entry == null)
				return false;
			if (streamLoadDelegate == null) throw new NullReferenceException(nameof(streamLoadDelegate));
			if (entry.sourceFilename.EndsWith(PackagePath.VpkExtension))
			{
				VPKResource resource = LoadResource(entry.sourceFilename);
				if (resource == null)
					return false;
				return resource.LoadFileAsStream(entry.keyname, streamLoadDelegate);
			}

			using FileStream file = new(entry.sourceFilename, FileMode.Open);
			return streamLoadDelegate.Invoke(file);
		}

		// TODO: Generalize ImportGetEntry to support multiple searchdirs
		public Texture2D ImportTexture(string entryName)
		{
			entryName = entryName.Replace('\\', '/').ToLower();
			string fullname = Path.ChangeExtension($"materials/{entryName}", PackagePath.VtfExtension);
			return ImportGetEntry<Texture2D>(fullname);
		}

		public T ImportGetEntry<T>(string entryName) where T : UnityEngine.Object
		{
			if (entryName == null)
				return null;
			// TODO: use PackagePath instead
			var rentedCharList = ArrayPool<char>.Shared.Rent(255);
			entryName = CleanedKeyname(entryName, 0, rentedCharList, out int extensionstart);
			ArrayPool<char>.Shared.Return(rentedCharList);
			if (entryName == null || !lookup.TryGetValue(entryName, out var entry))
				return null;
			if (!Import<T>(entry, out var asset))
				return null;
			return asset;
		}


		public static string GetImportPath(string fullname) { return Path.Combine(OutputPath, fullname); }
		public static string GetOutputPath(string fullname) { return Path.Combine(Application.dataPath, GetImportPath(fullname)); }

		public string LoadSourceText(GameEntry entry)
		{
			string sourceText = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceText = TextParser.LoadStreamAsString(stream);
				return true;
			})) return null;
			return sourceText;
		}

		public VTF ImportVTF(GameEntry entry)
		{
			VTF sourceTexture = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceTexture = VTF.Read(stream);
				return true;
			})) return null;
			return sourceTexture;
		}

		public VmfMaterial ImportVmf(GameEntry entry)
		{
			VmfMaterial sourceMaterial = null; 
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				sourceMaterial = VmfMaterial.Read(stream);
				return true;
			}))
			{
				return null;
			}
			return sourceMaterial;
		}

		public MdlHeader ImportMdl(GameEntry entry, Lookup lookup)
		{
			if (entry.Extension != PackagePath.MdlExtension)
				return null;

			MdlHeader header = null;
			try
			{
				if (!LoadFileAsStream(entry, delegate (Stream stream)
				{
					var outputPath = GetOutputPath(entry.keyname);
					PackagePath.EnsureDirectoriesExist(outputPath);
					using var reader = new BinaryReader(stream);
					header = MdlHeader.Read(reader, this, lookup);
					return true;
				})) return null;
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			return header;
		}

		public VtxHeader ImportVtx(GameEntry entry)
		{
			if (entry.Extension != PackagePath.VtxExtension)
				return null;

			VtxHeader header = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				var outputPath = GetOutputPath(entry.keyname);
				PackagePath.EnsureDirectoriesExist(outputPath);
				using var reader = new BinaryReader(stream);
				header = VtxHeader.Read(reader);
				return true;
			})) return null;
			return header;
		}

		public VvdHeader ImportVvd(GameEntry entry, MdlHeader mdlHeader)
		{
			if (entry.Extension != PackagePath.VvdExtension)
				return null;

			VvdHeader header = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				var outputPath = GetOutputPath(entry.keyname);
				PackagePath.EnsureDirectoriesExist(outputPath);
				using var reader = new BinaryReader(stream);
				header = VvdHeader.Load(reader, mdlHeader);
				return true;
			})) return null;
			return header;
		}

		public MdlModel ImportMdlModel(string modelId)
		{
			var lookup = new Lookup();

			var mdlEntry = GetEntry(modelId);
			var mdlHeader = mdlEntry != null ? ImportMdl(mdlEntry, lookup) : null;
			if (mdlHeader == null)
			{
				Debug.LogError($"Failed to load mdlHeader {modelId}");
				return null;
			}

			var vvdHeaderName = Path.ChangeExtension(modelId, PackagePath.VvdExtension);
			var vvdEntry = GetEntry(vvdHeaderName);
			var vvdHeader = (vvdEntry != null) ? ImportVvd(vvdEntry, mdlHeader) : null;
			if (vvdHeader == null)
			{
				Debug.LogError($"Failed to load vvdHeader {vvdHeaderName}");
				return null;
			}

			var vtxHeaderName = Path.ChangeExtension(modelId, PackagePath.VtxDX90Extension);
			var vtxEntry = GetEntry(vtxHeaderName);
			var vtxHeader = (vtxEntry != null) ? ImportVtx(vtxEntry) : null;
			if (vtxHeader == null)
			{
				Debug.LogError($"Failed to load vtxHeader {vtxHeaderName}");
				return null;
			}
			
			for (var i = 0; i < mdlHeader.TextureDirs.Length; i++)
			{
				mdlHeader.TextureDirs[i] = 
					PackagePath.CleanDirectory(mdlHeader.TextureDirs[i]);
			}

			return new MdlModel
			{
				MdlHeader = mdlHeader,
				VvdHeader = vvdHeader,
				VtxHeader = vtxHeader
			};
		}


		public bool Import<T>(GameEntry entry, out T asset) where T : UnityEngine.Object
		{
			asset = null;
			if (entry == null)
				return false;

			//Debug.Log($"Importing {entry.name}{entry.extension} from {entry.sourceFilename}");
			UnityEngine.Object foundAsset = null;
			if (!LoadFileAsStream(entry, delegate (Stream stream)
			{
				var outputPath = GetOutputPath(entry.keyname);
				PackagePath.EnsureDirectoriesExist(outputPath);
				switch (entry.Extension.ToString()) // TODO: move this outside of LoadFileAsStream
				{
					case PackagePath.VmtExtension:
					{
						VmfMaterial sourceMaterial = VmfMaterial.Read(stream);
						foundAsset = MaterialImporter.Import(this, sourceMaterial, outputPath);
						return foundAsset != null;
					}
					case PackagePath.VtfExtension:
					{
						VTF sourceTexture = VTF.Read(stream);
						var foundAssets = Texture2DImporter.Import(sourceTexture, outputPath);
						if (foundAssets != null)
							foundAsset = foundAssets[0]; // TODO: handle frames better
						return foundAsset != null;
					}
					default:
					{
						var assetPath = Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, outputPath));
						if (!File.Exists(outputPath))
						{
							var bytes = new byte[stream.Length];
							stream.Read(bytes, 0, (int)stream.Length);
							File.WriteAllBytes(outputPath, bytes);
							AssetDatabase.ImportAsset(assetPath);
						}
						foundAsset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
						return true;
					}
				}
			})) return false;

			asset = foundAsset as T;
			return true;
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

		// TODO: get rid of this
		static string CleanedKeyname(string input, int skip, char[] rentedCharList, out int extensionstart)
		{
			extensionstart = -1;
			if (string.IsNullOrEmpty(input))
				return string.Empty;

			Profiler.BeginSample("cleaning");
			input = input.Replace("//", "/");
			int keynameLength = input.Length;
			if (keynameLength > rentedCharList.Length)
			{
				ArrayPool<char>.Shared.Return(rentedCharList);
				rentedCharList = ArrayPool<char>.Shared.Rent(keynameLength);
			}
			int extensionLength = -1;
			for (int i = skip, o = 0; i < keynameLength; i++, o++)
			{
				var ch = input[i];
				if (ch == '.') extensionLength = i;
				if (ch == '\\') ch = '/';
				else if (char.IsUpper(ch)) ch = char.ToLowerInvariant(ch);
				rentedCharList[o] = ch;
			}
			extensionstart = ((extensionLength != -1) ? extensionLength : keynameLength) - skip;
			Profiler.EndSample();

			Profiler.BeginSample("new string");
			var result = new string(rentedCharList, 0, keynameLength - skip);
			Profiler.EndSample();
			return result;
		}

		public void LoadPaths(string[] inputPaths)
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
						if (filename.EndsWith(PackagePath.VpkExtension))
						{
							Profiler.BeginSample("vpkFiles.Add");
							if (filename.EndsWith(PackagePath.VpkDirExtension))
								vpkFiles.Add(filename);
							Profiler.EndSample();
							continue;
						}

						// TODO: use PackagePath instead
						var keyname = CleanedKeyname(filename, path.Length, rentedCharList, out int extensionstart);
						if (keyname == null)
							continue;

						if (lookup.ContainsKey(keyname))
							continue;

						Profiler.BeginSample("lookup");						
						var entry = new GameEntry()
						{
							keyname			= keyname,
							extensionStart	= extensionstart,
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
								// TODO: use PackagePath instead
								var keyname = CleanedKeyname(item.Key, 0, rentedCharList, out int extensionstart);
								if (keyname == null)
									continue;
								if (lookup.ContainsKey(keyname))
									continue;

								Profiler.BeginSample("lookup");
								var entry = new GameEntry()
								{
									keyname			= keyname,
									extensionStart	= extensionstart,
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