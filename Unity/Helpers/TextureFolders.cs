using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Keeps the imported textures in a tree of their own (<see cref="PackagePath.TextureSubPath"/>), apart from the
	/// materials, so the materials can all be deleted and imported again without extracting every texture again.
	/// </summary>
	public static class TextureFolders
	{
		/// <summary>
		/// Moves the textures an older import wrote next to the materials into the textures tree. They keep their
		/// GUIDs and import settings, so the materials that use them keep them. A texture already in the textures tree
		/// is left where it is, and so is its old copy.
		/// </summary>
		/// <returns>What was moved, or null when there was nothing to move.</returns>
		public static string MoveTexturesOutOfMaterials()
		{
			var materialsFolder = ToAssetPath(PackagePath.GetOutputPath("materials"));
			if (!AssetDatabase.IsValidFolder(materialsFolder))
				return null;
			var texturesFolder = ToAssetPath(PackagePath.GetOutputPath(PackagePath.TextureSubPath));

			var moves = AssetDatabase.FindAssets("t:Texture", new[] { materialsFolder })
									 .Select(AssetDatabase.GUIDToAssetPath)
									 .Distinct()
									 .Where(path => path.StartsWith(materialsFolder + "/", StringComparison.OrdinalIgnoreCase))
									 .Select(path => (source: path, destination: texturesFolder + path.Substring(materialsFolder.Length)))
									 .ToList();
			if (moves.Count == 0)
				return null;

			// MoveAsset needs the folder it moves into to exist already
			foreach (var folder in moves.Select(move => Path.GetDirectoryName(move.destination).Replace('\\', '/')).Distinct())
				CreateFolder(folder);

			int moved = 0, kept = 0;
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (var (source, destination) in moves)
				{
					if (File.Exists(destination))
					{
						kept++;
						continue;
					}
					var error = AssetDatabase.MoveAsset(source, destination);
					if (string.IsNullOrEmpty(error))
						moved++;
					else
						Debug.LogWarning($"Couldn't move texture {source} to {destination}: {error}");
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			return $"moved {moved} textures from {materialsFolder} to {texturesFolder}" +
				   (kept > 0 ? $", and left {kept} that were in both" : "");
		}

		static void CreateFolder(string folder)
		{
			if (AssetDatabase.IsValidFolder(folder))
				return;
			var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			CreateFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}

		static string ToAssetPath(string fullPath)
		{
			return ("Assets/" + Path.GetRelativePath(Application.dataPath, fullPath)).Replace('\\', '/');
		}
	}
}
