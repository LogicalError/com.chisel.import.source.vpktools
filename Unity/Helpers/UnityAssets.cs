using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEditor;
using System.Text;
using UnityEngine.Profiling;
using System.Buffers;
using UnityEngine.Pool;

namespace Chisel.Import.Source.VPKTools
{
	public static class UnityAssets
	{
		public static string GetAssetPath(string destinationPath)
		{
			return Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, destinationPath));
		}

		public static T Load<T>(string destinationPath) where T : UnityEngine.Object
		{
			if (!File.Exists(destinationPath))
				return null;

			var assetPath = GetAssetPath(destinationPath);
			try
			{
				return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
				return null;
			}
		}

		public static void Save<T>(T asset, string destinationPath) where T : UnityEngine.Object
		{
			PackagePath.EnsureDirectoriesExist(destinationPath);
			
			var assetPath = GetAssetPath(destinationPath);
			//Debug.Log($"Creating asset at: {assetPath}");
			UnityEditor.AssetDatabase.CreateAsset(asset, assetPath);
			UnityEditor.AssetDatabase.ImportAsset(assetPath);
		}
	}
}