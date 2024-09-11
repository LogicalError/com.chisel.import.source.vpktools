using System.IO;

namespace Chisel.Import.Source.VPKTools
{
	public static class PackagePath
	{
		public const string VpkExtension = ".vpk";
		public const string VpkDirExtension = "_dir.vpk";
		public const string VtfExtension = ".vtf";
		public const string VmtExtension = ".vmt";
		public const string MdlExtension = ".mdl";
		public const string VvdExtension = ".vvd";
		public const string VtxExtension = ".vtx";
		public const string VtxDX90Extension = ".dx90.vtx";
		public const string SprExtension = ".spr";

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

		public static void DecomposePathNotCleaned(string filePath, out string directory, out string filename, out string extension)
		{
			directory = Path.GetDirectoryName(filePath);
			filename  = Path.GetFileNameWithoutExtension(filePath);
			extension = Path.GetExtension(filePath);
		}

		public static void DecomposePath(string filePath, out string directory, out string filename, out string extension)
		{
			directory = GetDirectory(filePath);
			filename  = GetFilename(filePath);
			extension = GetExtension(filePath);
		}

		public static string Combine(params string[] paths)
		{
			var combinedPath = Path.Combine(paths).ToLower().Replace('\\', '/');
			while (combinedPath.Contains("//"))
				combinedPath = combinedPath.Replace("//", "/");
			return combinedPath;
		}

		public static void CleanPath(ref string directory, ref string filename, ref string extension)
		{
			directory = CleanDirectory(directory);
			filename = CleanFilename(filename);
			extension = CleanExtension(extension);
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

		/*
		public static string FixLocation(VPKParser vpkParser, string rawPath)
		{
			string fixedLocation = rawPath.Replace("\\", "/").ToLower();

			if (!Path.GetExtension(fixedLocation).Equals(VtfExtension) && (vpkParser == null || !vpkParser.FileExists(fixedLocation)))
				fixedLocation += VtfExtension;
			if ((vpkParser == null || !vpkParser.FileExists(fixedLocation)))
				fixedLocation = Path.Combine("materials", fixedLocation).Replace("\\", "/");

			return fixedLocation;
		}
		*/

		public static void EnsureDirectoriesExist(string path)
		{
			var outputDirectory = Path.GetDirectoryName(path);
			if (!Directory.Exists(outputDirectory))
				Directory.CreateDirectory(outputDirectory);
		}
	}
}