using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEditor;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Records which version of this importer generated an asset, so an asset written by older code is
	/// rebuilt instead of being reused forever.
	/// <para>
	/// Everything the importer writes - textures, materials, models, shared animation - is created once
	/// and then reused by every later import, which is what keeps re-importing a map cheap. It also meant
	/// that no change to how an asset is generated ever reached one already on disk: whether a file
	/// reflected the current code could only be settled by deleting it, and a file nobody thought to
	/// delete stayed silently out of date. So each generator declares a version, bumped whenever what it
	/// writes changes, and stamps that version into the importer userData of every asset it writes. An
	/// asset whose stamp is missing or different is rebuilt on its next import - in place, so its GUID
	/// and every reference to it survive.
	/// </para>
	/// <para>
	/// A stamp is a token such as <c>chisel-texture-v3</c>. Tokens are separated by ';' and share the
	/// userData with tokens other code keeps there, which are left untouched.
	/// </para>
	/// </summary>
	public static class GeneratedAssetVersion
	{
		const string kTokenPrefix      = "chisel-";
		const string kVersionSeparator = "-v";
		const char   kTokenSeparator   = ';';

		public static string Token(string generator, int version)
		{
			return kTokenPrefix + generator + kVersionSeparator + version.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// True when the asset has been imported and its importer records exactly these versions of these
		/// generators.
		/// </summary>
		public static bool IsCurrent(string assetPath, params (string generator, int version)[] versions)
		{
			if (string.IsNullOrEmpty(assetPath))
				return false;

			var importer = AssetImporter.GetAtPath(assetPath);
			return importer != null && HasVersions(importer.userData, versions);
		}

		/// <summary>
		/// The userData with these generators set to these versions. Any other version of the same
		/// generators is replaced; every other token is kept as it is.
		/// </summary>
		public static string Stamp(string userData, params (string generator, int version)[] versions)
		{
			var tokens = Split(userData);
			foreach (var (generator, version) in versions)
			{
				tokens.RemoveAll(token => IsTokenOf(token, generator));
				tokens.Add(Token(generator, version));
			}
			return string.Join(kTokenSeparator.ToString(), tokens);
		}

		/// <summary>
		/// The userData without this exact token.
		/// </summary>
		public static string Remove(string userData, string token)
		{
			var tokens = Split(userData);
			tokens.RemoveAll(existing => existing == token);
			return string.Join(kTokenSeparator.ToString(), tokens);
		}

		internal static bool HasVersions(string userData, params (string generator, int version)[] versions)
		{
			var tokens = Split(userData);
			foreach (var (generator, version) in versions)
			{
				if (!tokens.Contains(Token(generator, version)))
					return false;
			}
			return true;
		}

		// Any version of the generator's token: chisel-texture-v1 and chisel-texture-v12 both belong to
		// "texture"; chisel-texturex-v1 does not, and nor does a token whose version is not a plain number.
		static bool IsTokenOf(string token, string generator)
		{
			var prefix = kTokenPrefix + generator + kVersionSeparator;
			return token.StartsWith(prefix, StringComparison.Ordinal) &&
				   int.TryParse(token.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);
		}

		static List<string> Split(string userData)
		{
			var tokens = new List<string>();
			if (string.IsNullOrEmpty(userData))
				return tokens;

			foreach (var token in userData.Split(kTokenSeparator))
			{
				var trimmed = token.Trim();
				if (trimmed.Length > 0)
					tokens.Add(trimmed);
			}
			return tokens;
		}
	}
}
