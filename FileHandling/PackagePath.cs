using System;
using System.IO;

using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	public static class PackagePath
	{
		public const string OutputPath = "import"; // TODO: make this configurable

		public const string ExtensionVPK = "vpk";
		public const string ExtensionVPK_Dir = "_dir.vpk";
		public const string ExtensionVTF = "vtf";
		public const string ExtensionVMT = "vmt";
		public const string ExtensionMDL = "mdl";
		public const string ExtensionVVD = "vvd";
		public const string ExtensionVTX = "vtx";
		public const string ExtensionVTX_DX90 = "dx90.vtx";
		public const string ExtensionSPR = "spr";

		public static readonly string[] DefaultSkyBoxMaterialPaths = new string[] { "materials/", "materials/skybox/" };
		public static readonly string[] DefaultMaterialPaths = new string[] { "materials/" };

		public static string GetImportPath(string fullname) { return Path.Combine(OutputPath, fullname); }
		public static string GetOutputPath(string fullname) { return Path.GetFullPath(Path.Combine(Application.dataPath, GetImportPath(fullname))); }
		public static string GetAssetPath(string fullname) { return Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, GetOutputPath(fullname))); }

		public static string GetDirectory(string filePath)
		{
			return CleanDirectory(Path.GetDirectoryName(filePath));
		}

		public static string GetFilename(string filePath)
		{
			return CleanFilename(Path.GetFileNameWithoutExtension(filePath));
		}
		public static string GetExtension(string filePath)
		{
			return CleanExtension(Path.GetExtension(filePath));
		}

		public static void DecomposePath(string filePath, out string directory, out string filename, out string extension)
		{
			directory = GetDirectory(filePath);
			filename  = GetFilename(filePath);
			extension = GetExtension(filePath);
		}

		public static string Combine(params string[] paths)
		{
			var combinedPath = Path.Combine(paths);
			CleanFullPath(ref combinedPath);
			return combinedPath;
		}

		public static void CleanPath(ref string directory, ref string filename, ref string extension)
		{
			directory = CleanDirectory(directory);
			filename = CleanFilename(filename);
			extension = CleanExtension(extension);
		}

		public static void CleanFullPath(ref string fullName)
		{
			fullName = fullName.ToLower().Replace('\\', '/');
			while (fullName.Contains("//"))
				fullName = fullName.Replace("//", "/");
		}

		public static string CleanExtension(string extension)
		{
			extension = extension.ToLower();
			if (extension.Length > 0 && extension[0] == '.')
				extension = extension.Substring(1);
			return extension;
		}

		public static string CleanDirectory(string directory)
		{
			var dirFixed = directory.ToLower().Replace('\\', '/');
			while (dirFixed.Contains("//"))
				dirFixed = dirFixed.Replace("//", "/");
			if (dirFixed.Length > 0 && dirFixed[0] == '/')
				dirFixed = dirFixed.Substring(1);
			if (dirFixed.Length > 0 && dirFixed.LastIndexOf('/') == dirFixed.Length - 1)
			{
				if (dirFixed.Length == 1)
					dirFixed = string.Empty;
				else
					dirFixed = dirFixed.Remove(dirFixed.Length - 1);
			}
			return dirFixed;
		}

		public static string CleanFilename(string filename)
		{
			return filename.ToLower();
		}

		public static void EnsureDirectoriesExist(string path)
		{
			var outputDirectory = Path.GetDirectoryName(path);
			if (!Directory.Exists(outputDirectory))
				Directory.CreateDirectory(outputDirectory);
		}

		internal static void EnsureExtension(ref string entryName, string extension)
		{
			if (string.IsNullOrEmpty(entryName))
				return;
			if (entryName.EndsWith($".{extension}"))
				return;
			entryName += $".{extension}";
		}

		internal static void EnsurePathStart(ref string entryName, string path)
		{
			if (string.IsNullOrEmpty(entryName))
				return;
			CleanFullPath(ref entryName);
			path = CleanDirectory(path);
			if (entryName.StartsWith(path))
				return;

			var pathPieces = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			for (int i = pathPieces.Length - 1; i >= 0; i--)
			{
				if (entryName.StartsWith(pathPieces[i]))
					continue;
				entryName = Combine(pathPieces[i], entryName);
			}
		}
	}
}