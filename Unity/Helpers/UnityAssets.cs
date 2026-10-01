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

			var assetPath = PackagePath.GetAssetPath(destinationPath);
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

			var assetPath = PackagePath.GetAssetPath(destinationPath);;
			UnityEditor.AssetDatabase.CreateAsset(asset, assetPath);
			UnityEditor.AssetDatabase.ImportAsset(assetPath);
		}

		// Said once per editor session, whatever the number of assets it happens to
		static bool s_SaidStampedTheSlowWay;

		/// <summary>
		/// Writes a new asset stamped with these generators' versions (<see cref="GeneratedAssetVersion"/>) with ONE import,
		/// as <see cref="MaterialImporter"/> writes a material: CreateAsset inside an asset-editing block writes the file and
		/// a .meta of its own at once and leaves the import to the end of the block, and the stamp is put into that .meta on
		/// disk before then (<see cref="GeneratedMeta.SetUserData"/>), where the one import reads it. CreateAsset and then the
		/// stamp through the importer (SaveAndReimport) took two, each a refresh of its own. The asset is the object given,
		/// as after CreateAsset. Should the import not have read the stamp, it is stamped the slow way, and that is said once.
		/// </summary>
		public static void CreateStamped(UnityEngine.Object asset, string assetPath, params (string generator, int version)[] versions)
		{
			assetPath = assetPath.Replace('\\', '/');
			var userData = GeneratedAssetVersion.Stamp(null, versions);
			UnityEditor.AssetDatabase.StartAssetEditing();
			try
			{
				UnityEditor.AssetDatabase.CreateAsset(asset, assetPath);
				GeneratedMeta.SetUserData(assetPath, userData);
			}
			finally
			{
				UnityEditor.AssetDatabase.StopAssetEditing();
			}

			var importer = UnityEditor.AssetImporter.GetAtPath(assetPath);
			if (importer != null && !GeneratedAssetVersion.IsCurrentUserData(importer.userData, versions))
			{
				if (!s_SaidStampedTheSlowWay)
				{
					s_SaidStampedTheSlowWay = true;
					Debug.LogWarning($"{assetPath}: the import did not read the stamp put into its .meta, so it was stamped with an import of its own. " +
									 "Every asset written this way takes two imports instead of one until this is looked into.");
				}
				importer.userData = GeneratedAssetVersion.Stamp(importer.userData, versions);
				importer.SaveAndReimport();
			}
		}

		/// <summary>
		/// Saves an asset changed in memory, stamped with these generators' versions (<see cref="GeneratedAssetVersion"/>),
		/// with ONE import, as <see cref="MaterialImporter"/> rebuilds a material in place: the asset and its importer's
		/// userData are both written inside an asset-editing block, whose end imports it once (saving it, then stamping it
		/// with SaveAndReimport, took two). The importer's other tokens are kept.
		/// </summary>
		public static void SaveStamped(UnityEngine.Object asset, params (string generator, int version)[] versions)
		{
			var assetPath = UnityEditor.AssetDatabase.GetAssetPath(asset);
			if (string.IsNullOrEmpty(assetPath))
				throw new ArgumentException($"{asset.name} is not an asset, so it can't be saved as one", nameof(asset));
			UnityEditor.AssetDatabase.StartAssetEditing();
			try
			{
				UnityEditor.EditorUtility.SetDirty(asset);
				UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
				var importer = UnityEditor.AssetImporter.GetAtPath(assetPath);
				if (importer != null)
				{
					importer.userData = GeneratedAssetVersion.Stamp(importer.userData, versions);
					UnityEditor.AssetDatabase.WriteImportSettingsIfDirty(assetPath);
				}
			}
			finally
			{
				UnityEditor.AssetDatabase.StopAssetEditing();
			}
		}
	}
}