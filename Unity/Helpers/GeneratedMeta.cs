using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Writes the .meta of an asset this importer generates BEFORE the asset's first import, so Unity imports it once,
	/// with the importer and the settings it ends up with, instead of importing it with defaults and again for every
	/// setting applied afterwards.
	/// <para>
	/// The .meta is never written from scratch: it is a template Unity itself wrote for the same importer - the .meta of
	/// an asset this importer made before, found on disk - with this asset's GUID, settings and userData put in. So the
	/// layout, the field set and the serialized versions are whatever the running Unity writes. Where there is no asset of
	/// that kind yet to learn it from, there is no template, and the caller takes the slow path, which makes the first one.
	/// </para>
	/// </summary>
	internal static class GeneratedMeta
	{
		public enum Kind
		{
			/// <summary>A .png owned by <see cref="SourceTextureImporter"/> (the importerOverride block).</summary>
			SourceTexture,
			/// <summary>A .png converted to Unity's normal-map convention, owned by Unity's TextureImporter.</summary>
			NormalMap,
			/// <summary>A model file (.fbx) owned by Unity's ModelImporter: a model or a shared animation.</summary>
			Model,
			/// <summary>
			/// A mask written from a texture's alpha (Texture2DImporter.WriteAlphaMask), a .png owned by Unity's TextureImporter:
			/// its defaults but for what makes it data. Every one has the same .meta but for its GUID
			/// .
			/// </summary>
			AlphaMask,
		}

		// Where the templates are looked for: the assets of that kind this importer wrote before, in the folder it writes to
		static string FolderOf(Kind kind) => TemplateFolderOverride ??
			Path.Combine(Application.dataPath, PackagePath.OutputRoot, kind == Kind.Model ? kModelSubPath : PackagePath.TextureSubPath);

		static string PatternOf(Kind kind) => kind switch
		{
			Kind.Model     => "*.fbx.meta",
			Kind.AlphaMask => "*" + Texture2DImporter.kAlphaMaskSuffix + ".png.meta",
			_              => "*.png.meta",
		};

		// Where Source's models land: their entry names all begin with "models/"
		const string kModelSubPath = "models";

		/// <summary>For a test: look for templates here instead, among assets it wrote the slow way itself.</summary>
		internal static string TemplateFolderOverride;

		/// <summary>For a test: forget the templates found, so the next asset looks again.</summary>
		internal static void ForgetTemplates() => s_Templates.Clear();

		// How many .meta files of a kind are read to find the shape most of them share
		const int kTemplateSample = 24;

		static readonly Dictionary<Kind, string> s_Templates = new();

		/// <summary>
		/// The .meta text Unity wrote for an asset of this kind, with its GUID and userData lines as found: the shape most of
		/// a sample of them share (a few, animated textures' frames among them, carry other settings). Null when this
		/// project has none yet.
		/// </summary>
		public static string Template(Kind kind)
		{
			if (s_Templates.TryGetValue(kind, out var cached))
				return cached;

			string template = null;
			var folder = FolderOf(kind);
			if (Directory.Exists(folder))
			{
				var shapes = new Dictionary<string, (int count, string text)>();
				int sampled = 0;
				foreach (var path in Directory.EnumerateFiles(folder, PatternOf(kind), SearchOption.AllDirectories))
				{
					string text;
					try { text = File.ReadAllText(path); }
					catch (IOException) { continue; }
					if (!IsKind(kind, text))
						continue;
					var shape = Shape(kind, text);
					shapes[shape] = shapes.TryGetValue(shape, out var seen) ? (seen.count + 1, seen.text) : (1, text);
					if (++sampled >= kTemplateSample)
						break;
				}
				int best = 0;
				foreach (var (count, text) in shapes.Values)
				{
					if (count > best)
					{
						best = count;
						template = text;
					}
				}
			}
			// Only a found template is remembered: a project without one gets its first asset the slow way, and can have a
			// template as soon as that exists.
			if (template != null)
				s_Templates[kind] = template;
			return template;
		}

		internal static bool IsKind(Kind kind, string metaText)
		{
			switch (kind)
			{
				case Kind.SourceTexture:
					return metaText.Contains("importerOverride:") && metaText.Contains("\nScriptedImporter:") &&
						   metaText.Contains("\n  sRGB: ") && metaText.Contains("\n  alphaTestReference: ");
				case Kind.NormalMap:
					return metaText.Contains("\nTextureImporter:") &&
						   metaText.Contains(Texture2DImporter.kNormalMapStamp) &&
						   Regex.IsMatch(metaText, @"(?m)^  textureType: 1\r?$") && Regex.IsMatch(metaText, @"(?m)^  textureShape: 1\r?$");
				case Kind.Model:
					return metaText.Contains("\nModelImporter:") &&
						   (metaText.Contains(GeneratedAssetVersion.Token(ModelImporter.ModelVersion.generator, ModelImporter.ModelVersion.version)) ||
							metaText.Contains(GeneratedAssetVersion.Token(ModelImporter.AnimationVersion.generator, ModelImporter.AnimationVersion.version)));
				case Kind.AlphaMask:
					// As Texture2DImporter.AlphaMaskImportedAsWritten holds a mask to: data, never claimed as anything else
					return metaText.Contains("\nTextureImporter:") &&
						   metaText.Contains(GeneratedAssetVersion.Token(Texture2DImporter.AlphaMaskVersion.generator, Texture2DImporter.AlphaMaskVersion.version)) &&
						   Regex.IsMatch(metaText, @"(?m)^  textureType: 0\r?$") && Regex.IsMatch(metaText, @"(?m)^    sRGBTexture: 0\r?$") &&
						   Regex.IsMatch(metaText, @"(?m)^  alphaUsage: 0\r?$") && Regex.IsMatch(metaText, @"(?m)^  alphaIsTransparency: 0\r?$");
				default:
					return false;
			}
		}

		// What a .meta of this kind shares with all the others: everything but its GUID, its userData and, for a Source
		// texture, the fields set per texture
		internal static string Shape(Kind kind, string metaText)
		{
			var shape = Regex.Replace(metaText.Replace("\r\n", "\n"), @"(?m)^guid: \w+$", "guid: _");
			shape = Regex.Replace(shape, @"(?m)^(  userData:).*$", "$1 _");
			if (kind == Kind.SourceTexture)
			{
				foreach (var field in SourceTextureFields)
					shape = Regex.Replace(shape, @"(?m)^(  " + field + @":).*$", "$1 _");
			}
			if (kind == Kind.NormalMap)
			{
				foreach (var field in NormalMapSamplingFields)
					shape = Regex.Replace(shape, @"(?m)^(    " + field + @":).*$", "$1 _");
			}
			if (kind == Kind.Model)
			{
				shape = s_ModelExternalObjects.Replace(shape, "  externalObjects: _\n");
				shape = s_ModelLodScreenPercentages.Replace(shape, "    lODScreenPercentages: _\n");
				foreach (var field in ModelFields)
					shape = Regex.Replace(shape, @"(?m)^(  " + field + @":).*$", "$1 _");
			}
			return shape;
		}

		// A model's material remaps, up to the block that follows them, and its LOD transitions, likewise
		static readonly Regex s_ModelExternalObjects     = new(@"(?ms)^  externalObjects:.*?(?=^  materials:)");
		static readonly Regex s_ModelLodScreenPercentages = new(@"(?ms)^    lODScreenPercentages:.*?(?=^    globalScale:)");

		/// <summary>
		/// The single-line fields of a generated model's .meta set per model (ModelImporter.ConfigureImportedModel): beside
		/// them only its GUID, userData, material remaps and LOD transitions differ from one to the next
		/// .
		/// </summary>
		internal static readonly string[] ModelFields = { "importAnimation", "animationType", "avatarSetup" };

		/// <summary>What a generated model's importer is set to per model, as ModelImporter.ConfigureImportedModel sets it.</summary>
		internal struct ModelSettings
		{
			/// <summary>Each material name in the file and the GUID of the material asset it is remapped onto.</summary>
			public IReadOnlyCollection<(string name, string guid)> materials;
			/// <summary>How many LODs the file's _LOD names make; Unity's default transition for each goes in.</summary>
			public int lodCount;
			/// <summary>Bones or takes: animationType Generic and an avatar of its own.</summary>
			public bool rigged;
			/// <summary>Takes: importAnimation.</summary>
			public bool importAnimation;
		}

		/// <summary>
		/// The transition Unity's model importer gives LOD <paramref name="index"/> of <paramref name="count"/> when it
		/// builds a file's LODGroup: a quarter of the screen, halved for each level after, and 1% for the last. Every one of
		/// the 10,442 generated model .metas that Unity wrote holds exactly these.
		/// </summary>
		internal static float DefaultLodTransition(int index, int count)
		{
			return (index == count - 1) ? 0.01f : 0.25f * Mathf.Pow(0.5f, index);
		}

		// A name the remap block can hold as it is, unquoted - Source's material names are; anything else takes the slow way
		static readonly Regex s_PlainName = new(@"^[A-Za-z0-9_][A-Za-z0-9_.\-/ ]*$");

		/// <summary>
		/// The template with this GUID, userData and these per-model settings, or null when the template does not have the
		/// blocks to put them in or a material name is not one this writes (the caller takes the slow way).
		/// </summary>
		internal static string FillModel(string template, string guid, string userData, ModelSettings settings)
		{
			var crlf = template.Contains("\r\n");
			var text = Fill(template, guid, userData).Replace("\r\n", "\n");

			// The remaps in the order Unity keeps them: by name
			var remaps = new StringBuilder();
			var materials = settings.materials ?? Array.Empty<(string name, string guid)>();
			if (materials.Count == 0)
				remaps.Append("  externalObjects: {}\n");
			else
			{
				remaps.Append("  externalObjects:\n");
				foreach (var (name, materialGuid) in materials.OrderBy(material => material.name, StringComparer.Ordinal))
				{
					if (name == null || name.EndsWith(" ") || !s_PlainName.IsMatch(name) || !Regex.IsMatch(materialGuid ?? "", "^[0-9a-f]{32}$"))
						return null;
					remaps.Append("  - first:\n      type: UnityEngine:Material\n      assembly: UnityEngine.CoreModule\n      name: ").Append(name)
						  .Append("\n    second: {fileID: 2100000, guid: ").Append(materialGuid).Append(", type: 2}\n");
				}
			}
			if (s_ModelExternalObjects.Matches(text).Count != 1)
				return null;
			text = s_ModelExternalObjects.Replace(text, remaps.ToString().Replace("$", "$$"));

			var lods = new StringBuilder();
			if (settings.lodCount <= 1)
				lods.Append("    lODScreenPercentages: []\n");
			else
			{
				lods.Append("    lODScreenPercentages:\n");
				for (int i = 0; i < settings.lodCount; i++)
					lods.Append("    - ").Append(DefaultLodTransition(i, settings.lodCount).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
			}
			if (s_ModelLodScreenPercentages.Matches(text).Count != 1)
				return null;
			text = s_ModelLodScreenPercentages.Replace(text, lods.ToString());

			foreach (var (field, value) in new[] { ("importAnimation", settings.importAnimation ? "1" : "0"),
												   ("animationType", settings.rigged ? "2" : "0"),
												   ("avatarSetup", settings.rigged ? "1" : "0") })
			{
				var line = new Regex(@"(?m)^(  " + field + @":).*$");
				if (line.Matches(text).Count != 1)
					return null;
				text = line.Replace(text, "$1 " + value);
			}
			return crlf ? text.Replace("\n", "\r\n") : text;
		}

		/// <summary>
		/// The fields of <see cref="SourceTextureImporter"/> a Source texture's .meta carries per texture, in the order the
		/// importer declares them. <see cref="SourceTextureMeta"/> writes every one; GeneratedMetaTests holds this list to
		/// the importer's serialized fields, so a field added there fails a test until it is written here too.
		/// </summary>
		internal static readonly string[] SourceTextureFields =
		{
			"sRGB", "generateMipmaps", "alphaTestReference", "preserveAlphaCoverage", "compress", "filterMode", "anisoLevel", "wrapU", "wrapV",
		};

		/// <summary>What a Source texture's importer is set to: the fields of <see cref="SourceTextureFields"/>.</summary>
		internal struct SourceTextureSettings
		{
			public bool        sRGB;
			public bool        generateMipmaps;
			public float       alphaTestReference;
			public bool        preserveAlphaCoverage;
			public bool        compress;
			public FilterMode  filterMode;
			public int         anisoLevel;
			public TextureWrapMode wrapU;
			public TextureWrapMode wrapV;

			public string Value(string field)
			{
				switch (field)
				{
					case "sRGB":                  return Bool(sRGB);
					case "generateMipmaps":       return Bool(generateMipmaps);
					case "alphaTestReference":    return alphaTestReference.ToString("R", CultureInfo.InvariantCulture);
					case "preserveAlphaCoverage": return Bool(preserveAlphaCoverage);
					case "compress":              return Bool(compress);
					case "filterMode":            return ((int)filterMode).ToString(CultureInfo.InvariantCulture);
					case "anisoLevel":            return anisoLevel.ToString(CultureInfo.InvariantCulture);
					case "wrapU":                 return ((int)wrapU).ToString(CultureInfo.InvariantCulture);
					case "wrapV":                 return ((int)wrapV).ToString(CultureInfo.InvariantCulture);
					default: throw new ArgumentException("not a field of SourceTextureImporter: " + field);
				}
			}

			static string Bool(bool value) => value ? "1" : "0";
		}

		/// <summary>
		/// The fields of a normal map's TextureImporter set per texture from its VTF, in its textureSettings block: how it is
		/// sampled, as every other texture the VTF decides is (Texture2DImporter.SourceSettings).
		/// </summary>
		internal static readonly string[] NormalMapSamplingFields = { "filterMode", "aniso", "wrapU", "wrapV" };

		/// <summary>How a normal map is sampled: the fields of <see cref="NormalMapSamplingFields"/>.</summary>
		internal struct NormalMapSampling
		{
			public FilterMode      filterMode;
			public int             anisoLevel;
			public TextureWrapMode wrapU;
			public TextureWrapMode wrapV;

			public string Value(string field)
			{
				switch (field)
				{
					case "filterMode": return ((int)filterMode).ToString(CultureInfo.InvariantCulture);
					case "aniso":      return anisoLevel.ToString(CultureInfo.InvariantCulture);
					case "wrapU":      return ((int)wrapU).ToString(CultureInfo.InvariantCulture);
					case "wrapV":      return ((int)wrapV).ToString(CultureInfo.InvariantCulture);
					default: throw new ArgumentException("not a sampling field of a normal map: " + field);
				}
			}
		}

		/// <summary>
		/// The normal-map template with this GUID, userData and sampling, or null when the template does not hold each
		/// sampling field exactly once (the caller takes the slow way).
		/// </summary>
		internal static string FillNormalMap(string template, string guid, string userData, NormalMapSampling sampling)
		{
			var text = Fill(template, guid, userData);
			foreach (var field in NormalMapSamplingFields)
			{
				var line = new Regex(@"(?m)^(    " + field + @":).*?(\r?)$");
				if (line.Matches(text).Count != 1)
					return null;
				var value = sampling.Value(field);
				text = line.Replace(text, m => m.Groups[1].Value + " " + value + m.Groups[2].Value);
			}
			return text;
		}

		/// <summary>The template with this GUID and userData, and for a Source texture these settings.</summary>
		internal static string Fill(string template, string guid, string userData, SourceTextureSettings? settings = null)
		{
			var text = Regex.Replace(template, @"(?m)^guid: \w+(\r?)$", "guid: " + guid + "$1");
			text = Regex.Replace(text, @"(?m)^(  userData:).*?(\r?)$", m => m.Groups[1].Value + " " + userData + m.Groups[2].Value);
			if (settings.HasValue)
			{
				foreach (var field in SourceTextureFields)
				{
					var value = settings.Value.Value(field);
					text = Regex.Replace(text, @"(?m)^(  " + field + @":).*?(\r?)$", m => m.Groups[1].Value + " " + value + m.Groups[2].Value);
				}
			}
			return text;
		}

		/// <summary>
		/// The GUID the asset at this path has, or a new one: rewriting an asset keeps its GUID, and with it every
		/// reference to it.
		/// </summary>
		internal static string GuidFor(string assetPath)
		{
			var existing = AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets);
			return string.IsNullOrEmpty(existing) ? GUID.Generate().ToString() : existing;
		}

		/// <summary>Writes the .meta beside the asset, which must not be imported until it is there.</summary>
		internal static void Write(string assetPath, string metaText)
		{
			File.WriteAllText(Path.GetFullPath(assetPath) + ".meta", metaText);
		}

		/// <summary>
		/// Puts this userData into the .meta beside the asset, as the .meta stands on disk, and returns whether it had a
		/// userData line to put it in.
		/// <para>
		/// For an asset Unity has written but not imported yet: CreateAsset inside an asset-editing block writes the file
		/// and a .meta of its own at once, and leaves the import to the end of the block. The asset has no importer to set
		/// the userData on until then (a .meta written before CreateAsset is ignored), so the one import there is has to
		/// find it in the file.
		/// </para>
		/// </summary>
		internal static bool SetUserData(string assetPath, string userData)
		{
			var metaPath = Path.GetFullPath(assetPath) + ".meta";
			if (!File.Exists(metaPath))
				return false;
			var text = File.ReadAllText(metaPath);
			var line = new Regex(@"(?m)^(  userData:).*?(\r?)$");
			if (!line.IsMatch(text))
				return false;
			File.WriteAllText(metaPath, line.Replace(text, m => m.Groups[1].Value + " " + userData + m.Groups[2].Value, 1));
			return true;
		}
	}
}
