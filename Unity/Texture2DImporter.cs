using System.IO;

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// What a material asks of a texture it names: how the slot it goes into reads it. Handed to the texture's import, so a
	/// texture written from its VTF is imported once, already as that, instead of imported plain and then re-imported for
	/// every claim (<see cref="Texture2DImporter.MarkAsColor"/>, <see cref="Texture2DImporter.MarkAsNormalMap"/>,
	/// <see cref="Texture2DImporter.MarkAsAlphaTested"/>, which still apply a claim to a texture that exists).
	/// </summary>
	public readonly struct TextureClaim
	{
		/// <summary>The slot samples colour, which Source reads as sRGB (MaterialImporter.IsColorSlot).</summary>
		public readonly bool Color;
		/// <summary>The VMT names it under a bump-map parameter.</summary>
		public readonly bool NormalMap;
		/// <summary>... and says $ssbump 1.</summary>
		public readonly bool SelfShadowed;
		/// <summary>The value its alpha is tested at, as a cut-out silhouette; 0 for none.</summary>
		public readonly float AlphaTestReference;

		TextureClaim(bool color, bool normalMap, bool selfShadowed, float alphaTestReference)
		{
			Color = color;
			NormalMap = normalMap;
			SelfShadowed = selfShadowed;
			AlphaTestReference = alphaTestReference > 0.0f ? alphaTestReference : 0.0f;
		}

		public static TextureClaim AsColor => new(true, false, false, 0);
		public static TextureClaim AsNormalMap(bool selfShadowed) => new(false, true, selfShadowed, 0);
		public TextureClaim WithAlphaTest(float reference) => new(Color, NormalMap, SelfShadowed, reference);

		/// <summary>
		/// The claim on the other frames of an animated texture, of which a material names frame 0: colour reaches every
		/// frame (MarkAsColor claims the whole set), a normal map and an alpha test only the frame named.
		/// </summary>
		public TextureClaim OfOtherFrame() => new(Color, false, false, 0);
	}

	public static class Texture2DImporter
	{
		/// <summary>
		/// The version of what this writes for a texture: the pixels, the importer settings, and the
		/// conversions a material's claim applies to them (<see cref="MarkAsColor"/>,
		/// <see cref="MarkAsNormalMap"/>). Bump it whenever any of that changes. Every texture then
		/// rebuilds on its next import, and so does every material: a material records the texture
		/// version it was built with, because rebuilding a texture resets what its materials claimed it
		/// as, and a material claims its textures again as it rebuilds. See
		/// <see cref="GeneratedAssetVersion"/>.
		/// </summary>
		// 2: I8, A8 and IA88 textures read as Direct3D 9 samples them (they were (I, 1, 1) and (I, A, 1))
		// 6: DXT3 decoded as it is stored, sixteen bytes a block (it read eight, as DXT1: 122 of Black Mesa's textures)
		public static readonly (string generator, int version) GeneratedVersion = ("texture", 6);

		/// <summary>
		/// What every texture's anisotropy is set to, matching Black Mesa's own <c>mat_forceaniso "16"</c>.
		/// </summary>
		const int kForcedAnisoLevel = 16;

		static bool IsCurrent(Texture texture)
		{
			return GeneratedAssetVersion.IsCurrent(AssetDatabase.GetAssetPath(texture), GeneratedVersion);
		}

		/// <summary>
		/// The texture, or its frames, written earlier from this VTF, when it is current and still imported as written; null
		/// when it has to be written (again), by <see cref="Import"/>. All this takes of the VTF is its header
		/// (<see cref="VTF.ReadHeader"/>), so a caller can ask before reading the pixels: a material rebuilt over textures
		/// that are current reads none of them.
		/// </summary>
		public static Texture2D[] Reusable(VTF sourceTexture, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".png");
			var foundAsset = UnityAssets.Load<Texture2D>(destinationPath);
			if (foundAsset != null && IsCurrent(foundAsset) && ImportedAsWritten(foundAsset, sourceTexture))
				return new Texture2D[] { foundAsset };

			var filePath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + "[0].png");
			foundAsset = UnityAssets.Load<Texture2D>(filePath);
			if (foundAsset != null && IsCurrent(foundAsset))
			{
				var frameTextureList = new List<Texture2D>();
				bool asWritten = true;
				int index = 0;
				while (foundAsset != null)
				{
					asWritten &= ImportedAsWritten(foundAsset, sourceTexture);
					frameTextureList.Add(foundAsset);
					index++;
					filePath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + $"[{index}].png");
					foundAsset = UnityAssets.Load<Texture2D>(filePath);
				}
				if (asWritten)
					return frameTextureList.ToArray();
			}
			return null;
		}

		/// <param name="claim">How the material asking for it reads it; see <see cref="TextureClaim"/>.</param>
		public static Texture2D[] Import(VTF sourceTexture, string outputPath, TextureClaim claim = default)
		{
			var reusable = Reusable(sourceTexture, outputPath);
			if (reusable != null)
				return reusable;
			var destinationPath = Path.ChangeExtension(outputPath, ".png");

			// Write texture as a png file - over the outdated one, if there is one, which keeps its .meta
			// and so its GUID and every reference to it
			var frameNames = SaveFramesAsPNG(sourceTexture, destinationPath);
			if (frameNames == null || frameNames.Length == 0)
				return null;

			var frameTextures = new Texture2D[frameNames.Length];
			var assetPaths = new string[frameNames.Length];
			for (int i = 0; i < frameNames.Length; i++)
				assetPaths[i] = UnityAssets.GetAssetPath(frameNames[i]);

			// Imported once, as what they end up as (ImportOnce); the way below only where that cannot be done
			if (ImportOnce(sourceTexture, assetPaths, claim))
			{
				for (int i = 0; i < assetPaths.Length; i++)
					frameTextures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPaths[i]);
				return frameTextures;
			}

			for (int i = 0; i < frameNames.Length; i++)
			{
				// Import png file as an asset
				var assetPath = assetPaths[i];
				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);

				// Hand the asset over to our own importer, which rebuilds the mip chain the way Source
				// does (see SourceMipmaps) instead of letting Unity box-filter it. The asset keeps its
				// path, so its GUID and every reference to it survive; only the importer changes.
				// Only convert an asset that is not already ours. Unity rewrites the .meta from a
				// TextureImporter into a ScriptedImporter to do it, and complains about the version of the
				// block it replaces while doing so - once per texture, which is tolerable, but re-asserting
				// an override that is already set repeats that complaint and forces another import for
				// nothing.
				if (AssetImporter.GetAtPath(assetPath) is not SourceTextureImporter)
					AssetDatabase.SetImporterOverride<SourceTextureImporter>(assetPath);

				// Configure the importer (can only be done after importing once) and re-import
				ConfigureTextureImporter(sourceTexture, assetPath);

				// Reload the asset
				frameTextures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
			}
			return frameTextures;
		}

		/// <summary>
		/// Whether a texture this importer wrote from a VTF still has the importer, and the settings, it was written with.
		/// <para>
		/// A stamp says which code wrote an asset, not that nothing changed it since. An importer setting changed by hand,
		/// or by a script fixing textures up afterwards, stays wrong under a current stamp for good: nothing ever looks at it
		/// again. So each import that reuses a texture checks it, and one that does not hold is written again right here,
		/// in place (the GUID, and what claims left on it, survive). What is checked is what the importer decides: the
		/// importer itself, and the settings the VTF decides (<see cref="SourceSettings"/>); a normal map's type and
		/// shape. What materials claim and the switches meant to be set by hand are the asset's own and are left alone.
		/// </para>
		/// </summary>
		internal static bool ImportedAsWritten(Texture2D texture, VTF sourceTexture)
		{
			switch (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)))
			{
				case SourceTextureImporter source:
				{
					var written = SourceSettings(sourceTexture, default, source);
					return source.generateMipmaps == written.generateMipmaps && source.filterMode == written.filterMode &&
						   source.anisoLevel == written.anisoLevel && source.wrapU == written.wrapU && source.wrapV == written.wrapV;
				}
				case TextureImporter textureImporter:
				{
					// Only a normal map leaves SourceTextureImporter (MarkAsNormalMap): a 2D normal map, sampled as its VTF says
					var sampling = NormalMapSamplingOf(sourceTexture);
					return textureImporter.textureType == TextureImporterType.NormalMap &&
						   textureImporter.textureShape == TextureImporterShape.Texture2D &&
						   textureImporter.filterMode == sampling.filterMode && textureImporter.anisoLevel == sampling.anisoLevel &&
						   textureImporter.wrapModeU == sampling.wrapU && textureImporter.wrapModeV == sampling.wrapV;
				}
				default:
					return false;
			}
		}

		/// <summary>
		/// Off takes every texture the slow way again - imported plain, handed to <see cref="SourceTextureImporter"/>,
		/// configured, then claimed, one import each - which is what <see cref="ImportOnce"/> is compared against.
		/// </summary>
		public static bool ImportOnceEnabled = true;

		// Said once per editor session, whatever the number of textures it happens to
		static bool s_SaidRepaired;

		/// <summary>
		/// Imports a texture that was just written from its VTF ONCE, as what it ends up as, and returns whether it did.
		/// <para>
		/// The slow way takes three to six imports, each its own refresh: plain, handed to SourceTextureImporter, configured
		/// from the VTF (<see cref="ConfigureSourceImporter"/>), then once for each claim of the material asking for it
		/// (<see cref="MarkAsColor"/>, <see cref="MarkAsAlphaTested"/>, or <see cref="MarkAsNormalMap"/>'s conversion). Here
		/// the .meta is written first, with the importer and every one of those settings (see <see cref="GeneratedMeta"/>),
		/// so the one import is the last one. What the asset ends up as is the same: the same importer and settings, and a
		/// normal map's pixels converted by the same code, exactly once. The claims the caller applies afterwards then find
		/// nothing left to do.
		/// </para>
		/// <para>
		/// False, with nothing changed, when it cannot be done - there is no template to write the .meta from yet, or a
		/// normal map does not convert - and the slow way does it as before. Once the .meta is written there is no going
		/// back to the slow way: its configure step would strip the normal-map stamp and convert the pixels a second time.
		/// So an import that did not come out as written is put right through the importer instead, and said once.
		/// </para>
		/// </summary>
		/// <param name="assetPaths">The texture's frames, as just written by <see cref="SaveFramesAsPNG"/>; a material
		/// names frame 0, and the others take the claim as <see cref="TextureClaim.OfOtherFrame"/> gives it.</param>
		static bool ImportOnce(VTF sourceTexture, string[] assetPaths, TextureClaim claim)
		{
			var written = WriteMetas(sourceTexture, assetPaths, claim);
			if (written == null)
				return false;
			for (int i = 0; i < assetPaths.Length; i++)
			{
				AssetDatabase.ImportAsset(assetPaths[i], ImportAssetOptions.ForceSynchronousImport);
				written.Repair(i);
			}
			return true;
		}

		// Frame 0 is a normal map when claimed as one, every other frame a Source texture: whether there are templates for
		// what a texture with this many frames needs
		static bool HaveTemplates(TextureClaim claim, int frames)
		{
			return (!claim.NormalMap || GeneratedMeta.Template(GeneratedMeta.Kind.NormalMap) != null) &&
				   ((claim.NormalMap && frames <= 1) || GeneratedMeta.Template(GeneratedMeta.Kind.SourceTexture) != null);
		}

		/// <summary>
		/// A texture's frames written with their .meta, before their import (<see cref="WriteMetas"/>): what each import has
		/// to come out as, which it is put right to if it did not (<see cref="Repair"/>).
		/// </summary>
		internal sealed class Written
		{
			internal string[] AssetPaths;
			internal TextureClaim Claim;
			/// <summary>A mask written from another texture's alpha (<see cref="WriteAlphaMaskMeta"/>): one frame, data.</summary>
			internal bool IsAlphaMask;
			internal string AlphaMaskUserData;
			internal string NormalUserData;
			internal GeneratedMeta.NormalMapSampling NormalSampling;
			internal GeneratedMeta.SourceTextureSettings[] SourceSettings;
			internal string[] SourceUserData;
			/// <summary>What each frame's importer held before: null for a new one.</summary>
			internal string[] PreviousUserData;

			internal bool IsNormalMap(int frame) => frame == 0 && Claim.NormalMap;

			/// <summary>The userData frame <paramref name="frame"/>'s .meta was written with, which its import reads.</summary>
			internal string UserData(int frame) => IsAlphaMask ? AlphaMaskUserData : IsNormalMap(frame) ? NormalUserData : SourceUserData[frame];

			/// <summary>
			/// Frame <paramref name="frame"/> has been imported since it was written, as written: its importer holds the
			/// userData written for it, and held another before (a texture written again with the same userData cannot tell).
			/// </summary>
			internal bool ImportedSince(int frame)
			{
				var userData = UserData(frame);
				return userData != PreviousUserData[frame] &&
					   AssetImporter.GetAtPath(AssetPaths[frame]) is AssetImporter importer && importer.userData == userData;
			}

			/// <summary>Frame <paramref name="frame"/>'s import came out as its .meta said, or is made to.</summary>
			internal void Repair(int frame)
			{
				if (IsAlphaMask)
					RepairAlphaMask(AssetPaths[frame], AlphaMaskUserData);
				else if (IsNormalMap(frame))
					RepairNormalMap(AssetPaths[frame], NormalUserData, NormalSampling);
				else
					RepairSourceTexture(AssetPaths[frame], SourceSettings[frame], SourceUserData[frame]);
			}
		}

		/// <summary>
		/// ImportOnce's writing: the .meta of each of a texture's frames, just written as .png, with the importer and every
		/// setting it ends up with; a normal map's pixels converted. Null when it cannot be done - no template yet, or a normal
		/// map that does not convert - with nothing written; the slow way then takes the texture. With
		/// <paramref name="unwritten"/>, what it writes is left to it (<see cref="WriteAhead"/>).
		/// </summary>
		static Written WriteMetas(VTF sourceTexture, string[] assetPaths, TextureClaim claim, Unwritten unwritten = null)
		{
			if (!ImportOnceEnabled || assetPaths == null || assetPaths.Length == 0 || !HaveTemplates(claim, assetPaths.Length))
				return null;
			var normalTemplate = claim.NormalMap ? GeneratedMeta.Template(GeneratedMeta.Kind.NormalMap) : null;
			var sourceTemplate = (!claim.NormalMap || assetPaths.Length > 1) ? GeneratedMeta.Template(GeneratedMeta.Kind.SourceTexture) : null;

			// Every frame's .meta goes down before any of them is imported: an import may notice the other new files, and
			// one noticed without its .meta would be imported with Unity's defaults
			var written = new Written
			{
				AssetPaths     = assetPaths,
				Claim          = claim,
				NormalSampling = NormalMapSamplingOf(sourceTexture),
				SourceSettings = new GeneratedMeta.SourceTextureSettings[assetPaths.Length],
				SourceUserData = new string[assetPaths.Length],
				PreviousUserData = new string[assetPaths.Length],
			};
			for (int i = 0; i < assetPaths.Length; i++)
			{
				var assetPath = assetPaths[i];
				var frameClaim = i == 0 ? claim : claim.OfOtherFrame();
				var existing = AssetImporter.GetAtPath(assetPath);
				written.PreviousUserData[i] = existing?.userData;
				var guid = GeneratedMeta.GuidFor(assetPath);
				if (frameClaim.NormalMap)                  // frame 0 only, so nothing is written yet
				{
					// As MarkAsNormalMap stamps it: pixels written from the VTF, converted; sampled as the VTF says
					written.NormalUserData = UserDataForRewrittenPixels(existing?.userData) + ";" + kNormalMapStamp;
					var normalMeta = GeneratedMeta.FillNormalMap(normalTemplate, guid, written.NormalUserData, written.NormalSampling);
					// The .meta is made before the pixels are converted: the slow way would convert them again. They are
					// converted as written, from the pixels (WriteSourceNormalMap: the file the .png converted made)
					if (normalMeta == null ||
						!WriteSourceNormalMap(assetPath, sourceTexture.Frames[i].Pixels, sourceTexture.Width, sourceTexture.Height, frameClaim.SelfShadowed,
											  unwritten))
						return null;                        // the slow way takes it, as before
					WriteMeta(assetPath, normalMeta, unwritten);
				}
				else
				{
					written.SourceSettings[i] = SourceSettings(sourceTexture, frameClaim, existing as SourceTextureImporter);
					written.SourceUserData[i] = UserDataForRewrittenPixels(existing?.userData);
					WriteMeta(assetPath, GeneratedMeta.Fill(sourceTemplate, guid, written.SourceUserData[i], written.SourceSettings[i]), unwritten);
				}
			}
			return written;
		}

		/// <summary>
		/// While a texture census runs (GameResources.BeginTextureCensus), what a build claims of a texture (MarkAsColor,
		/// MarkAsNormalMap, MarkAsAlphaTested) is left for the build that is made for real afterwards: the builds then meet
		/// each texture claimed in the order they would have without a census, and so are built the same.
		/// </summary>
		internal static bool ClaimsDeferred;

		/// <summary>
		/// A texture's claim applied at once, census or not (<see cref="ClaimsDeferred"/>), as a build applies it to the
		/// texture it is handed (MaterialImporter.SetMaterialTexture, and MarkAsAlphaTested on the base texture): for a
		/// texture a census imports the slow way, because there is no template yet for what it is to be. Without a census
		/// the first build to ask applies its claim at once too, so the builds still meet it claimed in the same order.
		/// </summary>
		internal static Texture2D[] ClaimNow(Texture2D[] textures, TextureClaim claim)
		{
			if (textures == null || textures.Length == 0 || textures[0] == null)
				return textures;
			var deferred = ClaimsDeferred;
			ClaimsDeferred = false;
			try
			{
				if (claim.NormalMap)
					textures[0] = MarkAsNormalMap(textures[0], claim.SelfShadowed);
				else if (claim.Color)
					MarkAsColor(textures[0]);
				if (claim.AlphaTestReference > 0)
					MarkAsAlphaTested(textures[0], claim.AlphaTestReference);
			}
			finally
			{
				ClaimsDeferred = deferred;
			}
			return textures;
		}

		/// <summary>
		/// A texture written ahead of its import for a texture census (<see cref="WriteAhead"/>), and stand-ins for its frames:
		/// textures that are no asset, in the format the import is going to give each frame (SourceTextureImporter.FormatOf),
		/// which is all a material build reads of a texture it is handed.
		/// </summary>
		internal sealed class Ahead
		{
			internal Written Written;
			internal Texture2D[] StandIns;
			/// <summary>What it has still to write, which <see cref="ImportAhead"/> writes first; null for nothing.</summary>
			internal Unwritten Unwritten;
		}

		/// <summary>
		/// What a texture written ahead has still to write (<see cref="WriteAhead"/>): its .pngs, each encoded meanwhile on a
		/// worker thread into a file of the project's Temp folder, and its .metas, all put in place when it is imported
		/// (<see cref="ImportAhead"/>). Encoding them one after another, a normal map's conversion with it, was about 7 of the
		/// 57 s of a map's first import. Until then
		/// neither is where a refresh in between could come across it: one that found a .png without its .meta would import it
		/// with Unity's defaults, and one that found a .meta without its .png would delete it.
		/// </summary>
		internal sealed class Unwritten
		{
			readonly List<(string path, string temporary, System.Func<byte[]> encode, Task encoding)> pngs = new();
			readonly List<(string assetPath, string text)> metas = new();

			/// <summary>The .png at <paramref name="path"/> (a full path), as <paramref name="encode"/> makes it, which a worker
			/// thread starts on now (ImageConversion's EncodeArrayTo* are thread safe).</summary>
			internal void Png(string path, System.Func<byte[]> encode) => File(path, encode);

			/// <summary>Any file at <paramref name="path"/> (a full path) as <paramref name="make"/> makes it, on a worker thread
			/// from now: a model's FBX (FbxExporter.Export).</summary>
			internal void File(string path, System.Func<byte[]> make)
			{
				var temporary = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject());
				pngs.Add((path, temporary, make, Task.Run(() => System.IO.File.WriteAllBytes(temporary, make()))));
			}

			/// <summary>The .meta of the asset at <paramref name="assetPath"/>, as <see cref="GeneratedMeta.Write"/> writes it.</summary>
			internal void Meta(string assetPath, string text) => metas.Add((assetPath, text));

			/// <summary>Whether every .png has been encoded by now, or has failed to be.</summary>
			internal bool Encoded => pngs.TrueForAll(png => png.encoding.IsCompleted);

			/// <summary>
			/// Puts every .png in place, over the one written before (which keeps its .meta, and so its GUID), then every .meta.
			/// A .png a worker thread did not encode is encoded here, as it was before, and said.
			/// </summary>
			internal void WriteAll()
			{
				foreach (var (path, temporary, encode, encoding) in pngs)
				{
					PackagePath.EnsureDirectoriesExist(path);
					try
					{
						encoding.Wait();
					}
					catch (System.AggregateException exception)
					{
						Debug.LogWarning($"{Path.GetFileName(path)} was not encoded on a worker thread, so it is encoded now: " +
										 exception.InnerException?.Message);
						System.IO.File.WriteAllBytes(path, encode());
						continue;
					}
					if (System.IO.File.Exists(path))
						System.IO.File.Delete(path);
					System.IO.File.Move(temporary, path);
				}
				pngs.Clear();
				foreach (var (assetPath, text) in metas)
					GeneratedMeta.Write(assetPath, text);
				metas.Clear();
			}
		}

		// A .meta written now, or left to what is left unwritten
		static void WriteMeta(string assetPath, string text, Unwritten unwritten)
		{
			if (unwritten != null)
				unwritten.Meta(assetPath, text);
			else
				GeneratedMeta.Write(assetPath, text);
		}

		// A texture that may hold a NaN has its .pngs written at once, as before: what a .png makes of a NaN is only known from
		// the .png (FormatOfPng, ConvertSourceNormalMapFile). Only an HDR texture can: its texels are the one format decoded
		// as floats (RGBA16161616F); every other is decoded from integers. Looking at every texel for one instead took 2.1 s
		// of a map's first import.
		static bool WrittenAtOnce(VTF sourceTexture)
		{
			if (sourceTexture.IsHDR)
				return true;
			foreach (var frame in sourceTexture.Frames)
			{
				if (frame.Pixels == null)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Writes a texture as <see cref="Import"/> would - its frames as .png, each with its .meta - without importing it, for
		/// <see cref="ImportAhead"/> to import together with every other one written so: one refresh between them all, where
		/// Import takes one each. What it writes is written when it is imported, its .pngs encoded meanwhile on worker threads
		/// (<see cref="Unwritten"/>). Null when it cannot be written ahead - there is no template yet for its .meta, or a normal
		/// map does not convert - with nothing written; Import then takes it, now.
		/// </summary>
		internal static Ahead WriteAhead(VTF sourceTexture, string outputPath, TextureClaim claim)
		{
			if (!ImportOnceEnabled || sourceTexture?.Frames == null || sourceTexture.Frames.Length == 0 ||
				!HaveTemplates(claim, sourceTexture.Frames.Length))
				return null;
			var unwritten = WrittenAtOnce(sourceTexture) ? null : new Unwritten();
			var pngPath = Path.ChangeExtension(outputPath, ".png");
			string[] frameNames;
			using (ImportSteps.Step("pngs written"))
			{
				// A normal map of one frame is written by WriteMetas, converted (WriteSourceNormalMap), once
				if (claim.NormalMap && sourceTexture.Frames.Length == 1)
				{
					if (unwritten == null)
						PackagePath.EnsureDirectoriesExist(pngPath);
					frameNames = new[] { pngPath };
				}
				else if (unwritten != null)
					frameNames = FramePaths(pngPath, sourceTexture.Frames.Length);
				else
					frameNames = SaveFramesAsPNG(sourceTexture, pngPath);
			}
			if (frameNames == null || frameNames.Length == 0)
				return null;
			var assetPaths = new string[frameNames.Length];
			for (int i = 0; i < frameNames.Length; i++)
				assetPaths[i] = UnityAssets.GetAssetPath(frameNames[i]).Replace('\\', '/');
			Written written;
			using (ImportSteps.Step("metas written ahead"))
				written = WriteMetas(sourceTexture, assetPaths, claim, unwritten);
			if (written == null)
				return null;
			if (unwritten != null)
			{
				// Every frame as SaveFramesAsPNG writes it, but a normal map's, which WriteMetas has converted
				using (ImportSteps.Step("pngs written"))
				{
					for (int i = claim.NormalMap ? 1 : 0; i < frameNames.Length; i++)
					{
						var pixels = sourceTexture.Frames[i].Pixels;
						int width = sourceTexture.Width, height = sourceTexture.Height;
						unwritten.Png(frameNames[i], () => EncodeFrame(pixels, width, height));
					}
				}
			}

			var standIns = new Texture2D[assetPaths.Length];
			using (ImportSteps.Step("stand-ins"))
			{
				for (int i = 0; i < assetPaths.Length; i++)
					standIns[i] = StandInFor(written, i, sourceTexture);
			}
			return new Ahead { Written = written, StandIns = standIns, Unwritten = unwritten };
		}

		// A texture in the format frame's import is going to have: what SourceTextureImporter decides from the .png, taken from
		// the pixels it was written from (FormatOfWritten); a normal map's is Unity's own importer's, which no build asks about
		static Texture2D StandInFor(Written written, int frame, VTF sourceTexture)
		{
			var format = TextureFormat.RGBA32;
			var linear = true;
			if (!written.IsNormalMap(frame))
			{
				var settings = written.SourceSettings[frame];
				linear = !settings.sRGB;
				format = FormatOfWritten(sourceTexture.Frames[frame].Pixels, sourceTexture.Width, sourceTexture.Height, settings.compress,
										 written.AssetPaths[frame], sourceTexture.AlphaIsOne);
			}
			return new Texture2D(4, 4, format, mipChain: false, linear: linear)
			{
				name      = Path.GetFileNameWithoutExtension(written.AssetPaths[frame]),
				hideFlags = HideFlags.HideAndDontSave,
			};
		}

		/// <summary>
		/// Imports the textures written ahead (<see cref="WriteAhead"/>, <see cref="WriteAlphaMaskAhead"/>) in ONE asset-editing
		/// block, what they have still to write written first (<see cref="Unwritten"/>), puts right what did not come out as
		/// written (as ImportOnce does), lets the stand-ins go, and returns each one's imported frames.
		/// <para>
		/// A refresh in between - a build importing a cubemap or an alpha mask, say - imports whatever new files it comes
		/// across, a texture written at once among them (one with a NaN, see <see cref="WrittenAtOnce"/>), as written, its
		/// .meta being there: one that has been imported since (<see cref="Written.ImportedSince"/>) is not imported twice.
		/// </para>
		/// </summary>
		internal static Texture2D[][] ImportAhead(IReadOnlyList<Ahead> aheads)
		{
			var imported = new Texture2D[aheads.Count][];
			if (aheads.Count == 0)
				return imported;
			using (ImportSteps.Step("pngs put in place"))
			{
				foreach (var ahead in aheads)
				{
					ahead.Unwritten?.WriteAll();
					ahead.Unwritten = null;
				}
			}
			AssetDatabase.StartAssetEditing();
			try
			{
				foreach (var ahead in aheads)
				{
					for (int i = 0; i < ahead.Written.AssetPaths.Length; i++)
					{
						if (!ahead.Written.ImportedSince(i))
							AssetDatabase.ImportAsset(ahead.Written.AssetPaths[i], ImportAssetOptions.ForceSynchronousImport);
					}
				}
			}
			finally
			{
				AssetDatabase.StopAssetEditing();
			}
			for (int a = 0; a < aheads.Count; a++)
			{
				var ahead = aheads[a];
				imported[a] = new Texture2D[ahead.Written.AssetPaths.Length];
				for (int i = 0; i < ahead.Written.AssetPaths.Length; i++)
				{
					ahead.Written.Repair(i);
					imported[a][i] = AssetDatabase.LoadAssetAtPath<Texture2D>(ahead.Written.AssetPaths[i]);
				}
				foreach (var standIn in ahead.StandIns)
				{
					if (standIn != null)
						Object.DestroyImmediate(standIn);
				}
				ahead.StandIns = null;
			}
			return imported;
		}

		/// <summary>
		/// How a normal map is sampled: as its VTF says, like every other texture (<see cref="SourceSettings"/>) - the forced
		/// anisotropy (Black Mesa's mat_forceaniso 16, point-sampled textures aside), and the VTF's filter and wrap. Unity's
		/// own importer, which a normal map goes to, would leave its defaults: anisotropy 1, bilinear, repeat.
		/// </summary>
		internal static GeneratedMeta.NormalMapSampling NormalMapSamplingOf(VTF sourceTexture)
		{
			var settings = SourceSettings(sourceTexture, default, null);
			return new GeneratedMeta.NormalMapSampling
			{
				filterMode = settings.filterMode,
				anisoLevel = settings.anisoLevel,
				wrapU      = settings.wrapU,
				wrapV      = settings.wrapV,
			};
		}

		/// <summary>
		/// What a Source texture's importer ends up set to: <see cref="ConfigureSourceImporter"/>'s settings from the VTF, the
		/// claim, and what the asset being rewritten already carried, which ConfigureSourceImporter leaves alone - a texture
		/// any material samples as colour stays colour, the strictest alpha test stays, and the switches set by hand stay.
		/// </summary>
		internal static GeneratedMeta.SourceTextureSettings SourceSettings(VTF sourceTexture, TextureClaim claim, SourceTextureImporter existing)
		{
			var flags = sourceTexture.Flags;
			var pointSampled = (flags & VTFImageFlag.TEXTUREFLAGS_POINTSAMPLE) != (VTFImageFlag)0;
			var filterMode = FilterMode.Bilinear;
			if (pointSampled) filterMode = FilterMode.Point;
			if ((flags & VTFImageFlag.TEXTUREFLAGS_TRILINEAR) != (VTFImageFlag)0) filterMode = FilterMode.Trilinear;
			if ((flags & VTFImageFlag.TEXTUREFLAGS_ANISOTROPIC) != (VTFImageFlag)0) filterMode = FilterMode.Trilinear;

			var settings = new GeneratedMeta.SourceTextureSettings
			{
				sRGB               = claim.Color,
				generateMipmaps    = sourceTexture.MipmapCount > 1,
				alphaTestReference = claim.AlphaTestReference,
				// What a new SourceTextureImporter starts as; set by hand, per texture, if ever
				preserveAlphaCoverage = false,
				compress           = true,
				filterMode         = filterMode,
				anisoLevel         = pointSampled ? 1 : kForcedAnisoLevel,
				wrapU = ((flags & VTFImageFlag.TEXTUREFLAGS_CLAMPS) != (VTFImageFlag)0) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
				wrapV = ((flags & VTFImageFlag.TEXTUREFLAGS_CLAMPT) != (VTFImageFlag)0) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
			};
			if (existing != null)
			{
				settings.sRGB                  |= existing.sRGB;
				settings.alphaTestReference     = Mathf.Max(settings.alphaTestReference, existing.alphaTestReference);
				settings.preserveAlphaCoverage  = existing.preserveAlphaCoverage;
				settings.compress               = existing.compress;
			}
			return settings;
		}

		// The import came out as the .meta said, or is made to, through the importer
		static void RepairSourceTexture(string assetPath, GeneratedMeta.SourceTextureSettings settings, string userData)
		{
			if (AssetImporter.GetAtPath(assetPath) is not SourceTextureImporter)
			{
				SayRepaired(assetPath, "not imported by SourceTextureImporter");
				AssetDatabase.SetImporterOverride<SourceTextureImporter>(assetPath);
			}
			if (AssetImporter.GetAtPath(assetPath) is not SourceTextureImporter importer)
				return;

			var serialized = new SerializedObject(importer);
			bool changed = false;
			foreach (var field in GeneratedMeta.SourceTextureFields)
			{
				var property = serialized.FindProperty(field);
				var want = settings.Value(field);
				var have = property == null ? null
						 : property.propertyType == SerializedPropertyType.Boolean ? (property.boolValue ? "1" : "0")
						 : property.propertyType == SerializedPropertyType.Float ? property.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
						 : property.propertyType == SerializedPropertyType.Enum ? property.enumValueIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
						 : property.intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
				if (property == null || have == want)
					continue;
				SayRepaired(assetPath, $"{field} is {have}, the .meta said {want}");
				switch (property.propertyType)
				{
					case SerializedPropertyType.Boolean: property.boolValue = want == "1"; break;
					case SerializedPropertyType.Float:   property.floatValue = float.Parse(want, System.Globalization.CultureInfo.InvariantCulture); break;
					case SerializedPropertyType.Enum:    property.enumValueIndex = int.Parse(want, System.Globalization.CultureInfo.InvariantCulture); break;
					default:                             property.intValue = int.Parse(want, System.Globalization.CultureInfo.InvariantCulture); break;
				}
				changed = true;
			}
			if (changed)
				serialized.ApplyModifiedPropertiesWithoutUndo();
			if (importer.userData != userData)
			{
				SayRepaired(assetPath, "userData");
				importer.userData = userData;
				changed = true;
			}
			if (changed)
				importer.SaveAndReimport();
		}

		// As MarkAsNormalMap leaves it
		static void RepairNormalMap(string assetPath, string userData, GeneratedMeta.NormalMapSampling sampling)
		{
			if (AssetImporter.GetAtPath(assetPath) is SourceTextureImporter)
			{
				SayRepaired(assetPath, "imported by SourceTextureImporter, not as a normal map");
				AssetDatabase.ClearImporterOverride(assetPath);
			}
			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
				return;
			if (importer.textureType == TextureImporterType.NormalMap && importer.textureShape == TextureImporterShape.Texture2D &&
				importer.userData == userData && SampledAs(importer, sampling))
				return;

			SayRepaired(assetPath, $"type {importer.textureType}, shape {importer.textureShape}, aniso {importer.anisoLevel}, " +
								   $"{importer.filterMode}, {importer.wrapModeU}/{importer.wrapModeV}");
			importer.userData = userData;
			importer.textureType = TextureImporterType.NormalMap;
			TextureImporterSettings settings = new();
			importer.ReadTextureSettings(settings);
			settings.textureShape = TextureImporterShape.Texture2D;
			importer.SetTextureSettings(settings);
			SetSampling(importer, sampling);
			importer.SaveAndReimport();
		}

		static bool SampledAs(TextureImporter importer, GeneratedMeta.NormalMapSampling sampling)
		{
			return importer.filterMode == sampling.filterMode && importer.anisoLevel == sampling.anisoLevel &&
				   importer.wrapModeU == sampling.wrapU && importer.wrapModeV == sampling.wrapV;
		}

		static void SetSampling(TextureImporter importer, GeneratedMeta.NormalMapSampling sampling)
		{
			importer.filterMode  = sampling.filterMode;
			importer.anisoLevel  = sampling.anisoLevel;
			importer.wrapModeU   = sampling.wrapU;
			importer.wrapModeV   = sampling.wrapV;
		}

		static void SayRepaired(string assetPath, string what)
		{
			if (s_SaidRepaired)
				return;
			s_SaidRepaired = true;
			Debug.LogWarning($"[Chisel] {assetPath} did not import as its .meta was written ({what}); set through the importer " +
							 "instead. Said once: textures after it may have the same problem.");
		}

		/// <summary>
		/// The version of the mask textures written from another texture's alpha channel (see
		/// <see cref="ImportAlphaMask"/>). Bump it when what one of them holds changes.
		/// </summary>
		public static readonly (string generator, int version) AlphaMaskVersion = ("alphamask", 1);

		// What a mask written from a texture's alpha is called, beside the texture it came from.
		internal const string kAlphaMaskSuffix = "_alphamask";

		/// <summary>
		/// Writes a texture's alpha channel as a mask texture of its own and returns it; null when there is
		/// no alpha to take.
		/// <para>
		/// Source keeps masks in alpha channels - the phong mask is the normal map's alpha - and Unity
		/// re-encodes normal maps (DXT5nm keeps X in the alpha), so that channel cannot be sampled from the
		/// imported normal map. Rather than import the normal map as something it is not, the channel is
		/// written beside it as data, and the material points at that.
		/// </para>
		/// </summary>
		public static Texture2D ImportAlphaMask(VTF sourceTexture, string outputPath)
		{
			if (!HasAlphaMask(sourceTexture))
				return null;

			return WriteAlphaMask(AlphaAsMask(sourceTexture.Frames[0].Pixels),
								  sourceTexture.Width, sourceTexture.Height, outputPath);
		}

		/// <summary>Whether the texture has an alpha channel to write a mask from (<see cref="ImportAlphaMask"/>).</summary>
		internal static bool HasAlphaMask(VTF sourceTexture)
		{
			return sourceTexture != null && sourceTexture.HasAlpha &&
				   sourceTexture.Frames != null && sourceTexture.Frames.Length > 0 && sourceTexture.Frames[0].Pixels != null;
		}

		/// <summary>
		/// Writes a texture's alpha as a mask as <see cref="ImportAlphaMask"/> would - its .png and its .meta - without importing
		/// it, for <see cref="ImportAhead"/> to import with the textures written ahead: in a texture census, a build that asks
		/// for a mask (a phong or environment-map mask in a normal map's alpha) took two refreshes of its own for it
		///. Its stand-in is a texture that is no asset: a build only asks
		/// whether there is a mask. Null when it cannot be written ahead - no alpha, or no template yet for its .meta - with
		/// nothing written; ImportAlphaMask then takes it, now.
		/// </summary>
		internal static Ahead WriteAlphaMaskAhead(VTF sourceTexture, string outputPath)
		{
			if (!ImportOnceEnabled || !HasAlphaMask(sourceTexture) || GeneratedMeta.Template(GeneratedMeta.Kind.AlphaMask) == null)
				return null;
			// Written when it is imported, encoded meanwhile on a worker thread (Unwritten); nothing reads it back
			var unwritten = new Unwritten();
			var destinationPath = AlphaMaskFilePath(outputPath);
			var assetPath = UnityAssets.GetAssetPath(destinationPath).Replace('\\', '/');
			var written = WriteAlphaMaskMeta(assetPath, unwritten);
			if (written == null)
				return null;
			var pixels = sourceTexture.Frames[0].Pixels;
			int width = sourceTexture.Width, height = sourceTexture.Height;
			unwritten.Png(destinationPath, () => EncodeFrame(AlphaAsMask(pixels), width, height));
			var standIn = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false, linear: true)
			{
				name      = Path.GetFileNameWithoutExtension(assetPath),
				hideFlags = HideFlags.HideAndDontSave,
			};
			return new Ahead { Written = written, StandIns = new[] { standIn }, Unwritten = unwritten };
		}

		/// <summary>
		/// Whether a mask this importer wrote is still imported as it was written: as data (see <see cref="WriteAlphaMask"/>).
		/// One changed since - power_up_transformer_n's was typed as a normal map on 09-22, three hours after it was
		/// written, and read as a normal map it masks nothing - is written again at its next import, rather than kept
		/// because its stamp is current (see <see cref="ImportedAsWritten"/>).
		/// </summary>
		internal static bool AlphaMaskImportedAsWritten(string assetPath)
		{
			return AssetImporter.GetAtPath(assetPath) is TextureImporter importer &&
				   importer.textureType == TextureImporterType.Default && !importer.sRGBTexture &&
				   importer.alphaSource == TextureImporterAlphaSource.None && !importer.alphaIsTransparency;
		}

		/// <summary>A texture's alpha channel as an opaque grey image.</summary>
		internal static Color[] AlphaAsMask(Color[] pixels)
		{
			var mask = new Color[pixels.Length];
			for (int i = 0; i < pixels.Length; i++)
				mask[i] = new Color(pixels[i].a, pixels[i].a, pixels[i].a, 1);
			return mask;
		}

		/// <summary>
		/// The mask written earlier from the alpha of the texture at <paramref name="outputPath"/>, when it is current and
		/// still imported as written; null when it has to be written (again), or there is none because the texture has no
		/// alpha - which only its pixels tell. Takes nothing of the texture itself.
		/// </summary>
		public static Texture2D ReusableAlphaMask(string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, null) + kAlphaMaskSuffix + ".png";
			var foundAsset = UnityAssets.Load<Texture2D>(destinationPath);
			if (foundAsset != null &&
				GeneratedAssetVersion.IsCurrent(AssetDatabase.GetAssetPath(foundAsset), GeneratedVersion, AlphaMaskVersion) &&
				AlphaMaskImportedAsWritten(AssetDatabase.GetAssetPath(foundAsset)))
				return foundAsset;
			return null;
		}

		/// <summary>
		/// Writes mask pixels beside the texture they came from, and imports them as data: ONCE, its .meta written first
		/// (<see cref="WriteAlphaMaskMeta"/>), where the slow way takes two imports, each its own refresh - Unity's defaults,
		/// then <see cref="ConfigureAlphaMask"/>'s - which it still does while there is no mask yet to write the .meta from.
		/// </summary>
		internal static Texture2D WriteAlphaMask(Color[] pixels, int width, int height, string outputPath)
		{
			var reusable = ReusableAlphaMask(outputPath);
			if (reusable != null)
				return reusable;

			var assetPath = WriteAlphaMaskFile(pixels, width, height, outputPath);
			var written = WriteAlphaMaskMeta(assetPath);
			if (written != null)
			{
				AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
				written.Repair(0);
				return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
			}

			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);
			ConfigureAlphaMask(assetPath);
			return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
		}

		// The mask's pixels as a .png beside the texture they came from - over the one written before, which keeps its .meta
		// and so its GUID; its asset path
		static string WriteAlphaMaskFile(Color[] pixels, int width, int height, string outputPath)
		{
			var destinationPath = AlphaMaskFilePath(outputPath);
			PackagePath.EnsureDirectoriesExist(destinationPath);
			File.WriteAllBytes(destinationPath, EncodeFrame(pixels, width, height));
			return UnityAssets.GetAssetPath(destinationPath).Replace('\\', '/');
		}

		// Where the mask of the texture at outputPath is written
		static string AlphaMaskFilePath(string outputPath) => Path.ChangeExtension(outputPath, null) + kAlphaMaskSuffix + ".png";

		/// <summary>
		/// The .meta of a mask just written, before its import: a template Unity wrote for another mask
		/// (<see cref="GeneratedMeta.Kind.AlphaMask"/>), with this one's GUID and stamp, so the one import is the last one.
		/// Null, with nothing written, when there is no template yet. With <paramref name="unwritten"/>, it is left to it.
		/// </summary>
		static Written WriteAlphaMaskMeta(string assetPath, Unwritten unwritten = null)
		{
			if (!ImportOnceEnabled)
				return null;
			var template = GeneratedMeta.Template(GeneratedMeta.Kind.AlphaMask);
			if (template == null)
				return null;
			var existing = AssetImporter.GetAtPath(assetPath);
			var userData = GeneratedAssetVersion.Stamp(existing?.userData, GeneratedVersion, AlphaMaskVersion);
			WriteMeta(assetPath, GeneratedMeta.Fill(template, GeneratedMeta.GuidFor(assetPath), userData), unwritten);
			return new Written
			{
				AssetPaths        = new[] { assetPath },
				IsAlphaMask       = true,
				AlphaMaskUserData = userData,
				PreviousUserData  = new[] { existing?.userData },
			};
		}

		// A mask's import came out as its .meta said - data, stamped - or is made to, through the importer
		static void RepairAlphaMask(string assetPath, string userData)
		{
			if (AlphaMaskImportedAsWritten(assetPath) && AssetImporter.GetAtPath(assetPath).userData == userData)
				return;
			SayRepaired(assetPath, "an alpha mask's settings");
			ConfigureAlphaMask(assetPath);
		}

		// What makes a mask imported with Unity's defaults data, and its stamp; imported again
		static void ConfigureAlphaMask(string assetPath)
		{
			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
				return;
			// A mask is data, not colour: read as sRGB, every value but 0 and 1 comes out wrong. It carries no alpha of its
			// own - the mask is in the colour channels - and it is never a normal map, whichever slot ends up naming it.
			textureImporter.textureType         = TextureImporterType.Default;
			textureImporter.sRGBTexture         = false;
			textureImporter.alphaSource         = TextureImporterAlphaSource.None;
			textureImporter.alphaIsTransparency = false;
			textureImporter.userData = GeneratedAssetVersion.Stamp(textureImporter.userData, GeneratedVersion, AlphaMaskVersion);
			textureImporter.SaveAndReimport();
		}

		public static Cubemap ImportSkybox(VTF frontVTF, VTF backVTF, VTF upVTF, VTF downVTF, VTF leftVTF, VTF rightVTF, string outputPath)
		{
			var destinationPath = Path.ChangeExtension(outputPath, ".png");
			var foundAsset = UnityAssets.Load<Cubemap>(destinationPath);
			if (foundAsset != null && IsCurrent(foundAsset))
				return foundAsset;

			// Write texture as a png file
			SaveCubemapAsPNG(frontVTF, backVTF, upVTF, downVTF, leftVTF, rightVTF, destinationPath);

			// Import png file as an asset
			var assetPath = UnityAssets.GetAssetPath(destinationPath);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);

			// Configure the importer (can only be done after importing once) and re-import
			ConfigureCubemapTextureImporter(assetPath);

			// Reload the asset
			return AssetDatabase.LoadAssetAtPath<Cubemap>(assetPath);
		}

		/// <summary>
		/// Writes a VTF environment map as a Unity cubemap asset and returns it, rebuilt in place when out of date.
		/// <para>
		/// Source's faces are +x, -x, +y, -y, +z, -z of Source's world, each laid out as Direct3D lays a cube face out; so
		/// are Unity's, in Unity's world, whose y and z are Source's z and y (Cubemap.SetPixel, checked in the editor). Each
		/// Unity texel is filled from the Source texel its direction lands on. The texels stay linear: the game reads an
		/// environment map without an sRGB conversion when it renders in HDR (LoadCubeMap), and HDR ones are floats.
		/// </para>
		/// </summary>
		/// <summary>
		/// The cubemap written earlier from this environment map, when it is current; null when it has to be written (again),
		/// by <see cref="ImportCubemap"/>, or the VTF is not a square environment map. Takes only the VTF's header
		/// (<see cref="VTF.ReadHeader"/>): the pixels are six faces.
		/// </summary>
		public static Cubemap ReusableCubemap(VTF sourceTexture, string outputPath)
		{
			if (sourceTexture == null || (sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_ENVMAP) == 0 ||
				sourceTexture.Width != sourceTexture.Height)
				return null;
			var foundAsset = UnityAssets.Load<Cubemap>(Path.ChangeExtension(outputPath, ".cubemap"));
			return (foundAsset != null && IsCurrent(foundAsset)) ? foundAsset : null;
		}

		public static Cubemap ImportCubemap(VTF sourceTexture, string outputPath)
		{
			var faces = (sourceTexture?.Frames != null && sourceTexture.Frames.Length > 0) ? sourceTexture.Frames[0].Faces : null;
			if (faces == null || faces.Length < 6 || sourceTexture.Width != sourceTexture.Height)
				return null;

			var destinationPath = Path.ChangeExtension(outputPath, ".cubemap");
			var foundAsset = UnityAssets.Load<Cubemap>(destinationPath);
			if (foundAsset != null && IsCurrent(foundAsset))
				return foundAsset;

			var size = sourceTexture.Width;
			var format = sourceTexture.IsHDR ? UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat
											 : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
			var cubemap = new Cubemap(size, format, UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain)
			{
				name = Path.GetFileNameWithoutExtension(destinationPath)
			};
			for (int face = 0; face < 6; face++)
			{
				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						var unityDirection = CubeDirection(face, (2 * x + 1) / (float)size - 1, (2 * y + 1) / (float)size - 1);
						var sourceDirection = new Vector3(unityDirection.x, unityDirection.z, unityDirection.y);
						var sourceFace = CubeTexel(sourceDirection, out var u, out var v);
						var sourceX = Mathf.Clamp(Mathf.FloorToInt((u + 1) * 0.5f * size), 0, size - 1);
						var sourceY = Mathf.Clamp(Mathf.FloorToInt((v + 1) * 0.5f * size), 0, size - 1);
						// The VTF reader keeps the bottom row first
						cubemap.SetPixel((CubemapFace)face, x, y, faces[sourceFace][(size - 1 - sourceY) * size + sourceX]);
					}
				}
			}
			cubemap.Apply(updateMipmaps: true);

			if (foundAsset != null)
			{
				// Over the outdated one, which keeps its GUID and every material using it
				EditorUtility.CopySerialized(cubemap, foundAsset);
				foundAsset.name = cubemap.name;
				Object.DestroyImmediate(cubemap);
				EditorUtility.SetDirty(foundAsset);
				AssetDatabase.SaveAssetIfDirty(foundAsset);
				cubemap = foundAsset;
			}
			else
				UnityAssets.Save(cubemap, destinationPath);

			if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(cubemap)) is AssetImporter importer)
			{
				importer.userData = GeneratedAssetVersion.Stamp(importer.userData, GeneratedVersion);
				importer.SaveAndReimport();
			}
			return AssetDatabase.LoadAssetAtPath<Cubemap>(AssetDatabase.GetAssetPath(cubemap));
		}

		/// <summary>The direction a cube face's texel looks along, Direct3D's layout: u to the right, v down, both -1 to 1.</summary>
		internal static Vector3 CubeDirection(int face, float u, float v)
		{
			switch (face)
			{
				case 0:  return new Vector3( 1, -v, -u);
				case 1:  return new Vector3(-1, -v,  u);
				case 2:  return new Vector3( u,  1,  v);
				case 3:  return new Vector3( u, -1, -v);
				case 4:  return new Vector3( u, -v,  1);
				default: return new Vector3(-u, -v, -1);
			}
		}

		/// <summary>The face and position (u to the right, v down, -1 to 1) a direction lands on, Direct3D's layout.</summary>
		internal static int CubeTexel(Vector3 direction, out float u, out float v)
		{
			var ax = Mathf.Abs(direction.x);
			var ay = Mathf.Abs(direction.y);
			var az = Mathf.Abs(direction.z);
			if (ax >= ay && ax >= az)
			{
				u = -Mathf.Sign(direction.x) * direction.z / ax;
				v = -direction.y / ax;
				return direction.x >= 0 ? 0 : 1;
			}
			if (ay >= az)
			{
				u = direction.x / ay;
				v = Mathf.Sign(direction.y) * direction.z / ay;
				return direction.y >= 0 ? 2 : 3;
			}
			u = Mathf.Sign(direction.z) * direction.x / az;
			v = -direction.y / az;
			return direction.z >= 0 ? 4 : 5;
		}

		static readonly Vector2Int[] placementRects = new Vector2Int[] { new (2, 1), new (0, 1), new (1, 2), new (1, 0), new (1, 1), new (3, 1) };

		public static void SaveCubemapAsPNG(VTF frontVTF, VTF backVTF, VTF upVTF, VTF downVTF, VTF leftVTF, VTF rightVTF, string destinationPath)
		{
			var vtffiles = new VTF[6] { frontVTF, backVTF, upVTF, downVTF, rightVTF, leftVTF };

			var size = 0;
			for (int i = 0; i < vtffiles.Length; i++)
			{
				var vtfFile = vtffiles[i];
				if (vtfFile == null)
					continue;

				size = Mathf.Max(size, vtfFile.Width);
				size = Mathf.Max(size, vtfFile.Height);
			}

			var sidePixels = new Color[6][];
			for (int i = 0; i < vtffiles.Length; i++)
			{
				var vtfFile = vtffiles[i];
				var pixels  = (vtfFile?.Frames != null && vtfFile.Frames.Length > 0) ? vtfFile.Frames[0].Pixels : null;
				// Front, back, right and left are the sides; up and down are not
				var isSide  = i != 2 && i != 3;
				sidePixels[i] = (pixels == null) ? new Color[size * size]
												 : SkyFaceAsSquare(pixels, vtfFile.Width, vtfFile.Height, size, isSide);
			}

			bool isHDR = false;
			TextureFormat format = isHDR ? TextureFormat.RGBAFloat : TextureFormat.RGBA32;

			var cubeTexture = new Texture2D(size * 4, size * 3, format, false);
			for (int i = 0; i < 6; i++)
			{
				cubeTexture.SetPixels(placementRects[i].x * size, placementRects[i].y * size, size, size, sidePixels[i]);
			}

			// Save the texture to the specified path, and destroy the temporary object
			var bytes = isHDR ? cubeTexture.EncodeToEXR() : cubeTexture.EncodeToPNG();
			PackagePath.EnsureDirectoriesExist(destinationPath);
			File.WriteAllBytes(destinationPath, bytes);
			Object.DestroyImmediate(cubeTexture);
		}

		/// <summary>
		/// One sky face as the engine draws it onto its side of the sky cube, resampled to a size x size square. Source
		/// stretches each face's texture over its face whatever its resolution - Half-Life 2's skies ship a 4x4 bottom -
		/// with one exception: a SIDE half as tall as it is wide (512x256, most of the HL2 skies) covers only the UPPER
		/// half of its face, and the engine stretches its bottom row down over the lower half. Pixels are bottom row
		/// first, as the VTF reader keeps them and SetPixels takes them; the texture is sampled bilinearly and clamped,
		/// as its CLAMPS/CLAMPT flags have the game do.
		/// <para>
		/// A face that already is size x size comes back unchanged, so a sky that imported before still writes the
		/// same file. Every other shape used to throw from SetPixels, which took the whole map import down with it.
		/// </para>
		/// </summary>
		internal static Color[] SkyFaceAsSquare(Color[] pixels, int width, int height, int size, bool isSide)
		{
			var halfHeight = isSide && height * 2 == width;
			if (width == size && height == size && pixels.Length >= size * size)
				return pixels;
			var square = new Color[size * size];
			if (width <= 0 || height <= 0 || pixels.Length < width * height)
				return square;

			// Row 'fromTop' of the texture, as the reader stores it: bottom row first
			Color Texel(int fromTop, int column) => pixels[(height - 1 - fromTop) * width + column];
			for (int y = 0; y < size; y++)
			{
				// Down the face from its top edge, 0 to 1, at this row's centre
				var down = 1.0f - (y + 0.5f) / size;
				if (halfHeight)
					down = Mathf.Min(down * 2.0f, 1.0f);
				var sy = Mathf.Clamp(down * height - 0.5f, 0, height - 1);
				var y0 = (int)sy;
				var y1 = Mathf.Min(y0 + 1, height - 1);
				for (int x = 0; x < size; x++)
				{
					var sx = Mathf.Clamp((x + 0.5f) / size * width - 0.5f, 0, width - 1);
					var x0 = (int)sx;
					var x1 = Mathf.Min(x0 + 1, width - 1);
					var upper = Color.Lerp(Texel(y0, x0), Texel(y0, x1), sx - x0);
					var lower = Color.Lerp(Texel(y1, x0), Texel(y1, x1), sx - x0);
					square[y * size + x] = Color.Lerp(upper, lower, sy - y0);
				}
			}
			return square;
		}

		// Where an animated texture's frames are written as one flipbook, beside the PNGs of its frames
		static string FramesPath(string outputPath)
		{
			return Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + " (frames).png");
		}

		/// <summary>The texture array <see cref="ImportFrames"/> wrote for a texture, when it is current</summary>
		public static Texture2DArray LoadFrames(string outputPath)
		{
			var found = UnityAssets.Load<Texture2DArray>(FramesPath(outputPath));
			return (found != null && IsCurrent(found)) ? found : null;
		}

		/// <summary>
		/// All the frames of an animated texture as one texture array, for a shader that picks the frame itself, as Source's
		/// AnimatedTexture proxy does. The frames go into one PNG beside the texture's own, frame 0 on top, which Unity imports
		/// as a flipbook of one slice per frame. A normal map is converted as a single one is (<see cref="MarkAsNormalMap"/>).
		/// Null for a texture of one frame.
		/// </summary>
		public static Texture2DArray ImportFrames(VTF sourceTexture, string outputPath, bool isNormalMap)
		{
			var found = LoadFrames(outputPath);
			if (found != null)
				return found;
			if (sourceTexture?.Frames == null || sourceTexture.Frames.Length < 2)
				return null;

			var destinationPath = FramesPath(outputPath);
			var bytes = ImageConversion.EncodeArrayToPNG(StackFrames(sourceTexture.Frames, sourceTexture.Width, sourceTexture.Height),
				UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
				(uint)sourceTexture.Width, (uint)(sourceTexture.Height * sourceTexture.Frames.Length));
			PackagePath.EnsureDirectoriesExist(destinationPath);
			File.WriteAllBytes(destinationPath, bytes);

			var assetPath = UnityAssets.GetAssetPath(destinationPath);
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);
			ConfigureTextureImporter(sourceTexture, assetPath);
			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
				return null;

			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);
			if (isNormalMap && ConvertSourceNormalMapFile(assetPath, selfShadowed: false))
			{
				textureImporter.userData += ";" + kNormalMapStamp;
				settings.textureType = TextureImporterType.NormalMap;
			}
			settings.textureShape = TextureImporterShape.Texture2DArray;
			settings.flipbookRows = sourceTexture.Frames.Length;
			settings.flipbookColumns = 1;
			textureImporter.SetTextureSettings(settings);
			textureImporter.SaveAndReimport();
			return AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);
		}

		/// <summary>
		/// The frames in one image, frame 0 on top. Pixels run bottom row first, in each frame and in the result, as Unity keeps
		/// them, so the last frame's rows come first.
		/// </summary>
		internal static Color[] StackFrames(VTF.Frame[] frames, int width, int height)
		{
			var size = width * height;
			var stacked = new Color[size * frames.Length];
			for (int i = 0; i < frames.Length; i++)
				System.Array.Copy(frames[i].Pixels, 0, stacked, (frames.Length - 1 - i) * size, size);
			return stacked;
		}

		public static string[] SaveFramesAsPNG(VTF texture, string destinationPath)
		{
			if (texture.Frames == null || texture.Frames.Length == 0)
				return null;

			PackagePath.EnsureDirectoriesExist(destinationPath);

			var frameNames = FramePaths(destinationPath, texture.Frames.Length);
			for (int i = 0; i < frameNames.Length; i++)
				File.WriteAllBytes(frameNames[i], EncodeFrame(texture.Frames[i].Pixels, texture.Width, texture.Height));
			return frameNames;
		}

		/// <summary>Where <see cref="SaveFramesAsPNG"/> writes a texture's frames: one at the path, more each with its number.</summary>
		internal static string[] FramePaths(string destinationPath, int frames)
		{
			if (frames == 1)
				return new[] { destinationPath };
			var frameNames = new string[frames];
			for (int i = 0; i < frames; i++)
				frameNames[i] = Path.Combine(Path.GetDirectoryName(destinationPath), Path.GetFileNameWithoutExtension(destinationPath) + $"[{i}].png");
			return frameNames;
		}

		/// <summary>
		/// A frame's .png, as <see cref="SaveFramesAsPNG"/> writes it: an 8-bit RGBA .png, each float (v * 255 + 0.5), clamped
		/// (<see cref="PngByteOf"/>). Thread safe, as ImageConversion.EncodeArrayToPNG is.
		/// </summary>
		// TODO: support both rgba8 and rgba float textures
		internal static byte[] EncodeFrame(Color[] pixels, int width, int height) =>
			ImageConversion.EncodeArrayToPNG(pixels, UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
											 (uint)width, (uint)height);
		
		public static void ConfigureCubemapTextureImporter(string destinationPath)
		{
			var assetPath = Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, destinationPath));
			AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUncompressedImport);

			TextureImporter textureImporter = TextureImporter.GetAtPath(assetPath) as TextureImporter;
			if (textureImporter == null)
				return;

			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);

			settings.textureShape = TextureImporterShape.TextureCube;
			// The faces are written exactly as the VTFs store them - gamma-encoded, like every color
			// texture (see MarkAsColor).
			settings.sRGBTexture = true;
			settings.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;

			// The faces were just written, so the cubemap is what the current code produces.
			textureImporter.userData = GeneratedAssetVersion.Stamp(textureImporter.userData, GeneratedVersion);

			textureImporter.SetTextureSettings(settings);
			textureImporter.SaveAndReimport();
		}

		// Stamped into the texture importer's userData once the pixel conversion below has been done,
		// so it happens exactly once per texture no matter how many materials claim it. The stamp
		// rather than "is it already typed NormalMap?" is the guard on purpose: textures imported
		// before this conversion existed ARE already typed NormalMap and still need converting, and
		// this is the only thing standing between them and a second, ruinous inversion.
		// It records work done to the pixels on disk; it is not a version. Never change the string:
		// every texture converted under it would be converted again. Changing the conversion changes
		// what a texture is, so it bumps GeneratedVersion instead, which rewrites the pixels from the
		// VTF and drops this stamp in the same write (see UserDataForRewrittenPixels).
		internal const string kNormalMapStamp = "chisel-sourcenormal-v1";

		/// <summary>
		/// The importer userData for pixels just written from the VTF: stamped current, and without the
		/// normal-map conversion stamp, which described the pixels these replaced. A material claiming
		/// the texture as a normal map then converts these exactly once, as it did those.
		/// </summary>
		internal static string UserDataForRewrittenPixels(string userData)
		{
			return GeneratedAssetVersion.Stamp(GeneratedAssetVersion.Remove(userData, kNormalMapStamp), GeneratedVersion);
		}

		// Valve's bumpvects.h: the three tangent-space directions an $ssbump's R/G/B channels carry
		// the lighting for. Source tangent space, so the result needs the same green flip below.
		static readonly Vector3[] kSourceBumpBasis =
		{
			new (-0.40824829f, -0.70710678f, 0.57735027f), // (-1/sqrt6, -1/sqrt2, 1/sqrt3)
			new (-0.40824829f,  0.70710678f, 0.57735027f), // (-1/sqrt6,  1/sqrt2, 1/sqrt3)
			new ( 0.81649658f,  0.00000000f, 0.57735027f), // ( 2/sqrt6,  0,       1/sqrt3)
		};

		/// <summary>
		/// Re-imports a texture as sRGB once a material has claimed it for a color slot (see
		/// MaterialImporter.IsColorSlot).
		/// </summary>
		/// <remarks>
		/// Decided by use, like normal maps, because nothing in the file can say. The .png holds the
		/// VTF's texels exactly as stored - compared against the decoded VTF they match to within DXT
		/// rounding - and a color texture stores them gamma-encoded; Source's own shaders then choose
		/// per sampler whether to decode (EnableSRGBRead). Imported linear in this Linear-space
		/// project, a mid-grey texel reads 2.3 times too bright and darker ones worse, which is the
		/// washed-out look. Only ever promotes: a texture any material samples as color is color,
		/// whatever else also uses it.
		/// </remarks>
		public static void MarkAsColor(Texture texture)
		{
			if (texture == null || ClaimsDeferred)
				return;

			var assetPath = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(assetPath))
				return;

			// A frame SET is one texture, so the whole set is claimed, not the frame a material happened
			// to name. An animated VTF is written out as name[0].png, name[1].png ... and a material binds
			// frame 0 only, so promoting by reference left the rest linear - one texture living in two
			// colour spaces. Measured on this project: of the 87 imported textures no material references,
			// 59 are monitor_screensaver frames 1..N whose frame 0 IS claimed as colour.
			//
			// It is invisible today only because the sole animated path implemented is the normal-map one,
			// where linear is correct. The moment $basetexture takes an AnimatedTexture proxy - what Source
			// uses for monitor screens - those frames render far too bright, and it would present as a
			// flicker as the animation cycles rather than as a colour-space fault, so it would be hunted in
			// the wrong place entirely.
			MarkFrameSiblingsAsColor(assetPath);

			// Textures written out of a VTF are owned by SourceTextureImporter, not by Unity's, so the
			// claim has to be set on whichever one has the asset. Reaching only TextureImporter here would
			// not fail - it would return quietly, and every Source texture would stay linear.
			if (AssetImporter.GetAtPath(assetPath) is SourceTextureImporter)
			{
				SetSourceImporterSRGB(assetPath);
				return;
			}

			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
				return;

			// Idempotent, and a normal map is never color, whichever slot names it.
			if (textureImporter.sRGBTexture || textureImporter.textureType == TextureImporterType.NormalMap)
				return;

			textureImporter.sRGBTexture = true;
			textureImporter.SaveAndReimport();
		}

		/// <summary>
		/// Re-imports a texture as a normal map, converting the pixels out of Source's tangent-space
		/// conventions first. Called once a material has claimed the texture for a normal-map slot -
		/// which is the only reliable statement that it IS one, since a VTF's own normal flag is set
		/// on many textures that are not (see ConfigureTextureImporter).
		/// Unity's NormalMap type also forces the texture linear, which a normal map needs.
		/// </summary>
		/// <param name="selfShadowed">
		/// The VMT said <c>$ssbump 1</c>. An ssbump is NOT a normal map - it stores how much light
		/// reaches the texel from each of three fixed basis directions - so feeding it to a normal
		/// map slot unconverted produces nonsense. Only the VMT key can say: the filename convention
		/// disagrees with it on 62 of 253 materials, and the VTF's own SSBUMP flag is never set.
		/// </param>
		/// <returns>
		/// The texture as it is AFTER any re-import this did, which is not the object that was passed in:
		/// clearing the override and re-writing the .png both destroy it, and assigning a destroyed object to
		/// a material serialises as a null reference. Callers that go on to use the texture must use this.
		/// </returns>
		public static Texture2D MarkAsNormalMap(Texture2D texture, bool selfShadowed = false)
		{
			if (texture == null)
				return null;
			if (ClaimsDeferred)
				return texture;

			var assetPath = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(assetPath))
				return texture;

			// A normal map goes back to Unity's own importer. SourceTextureImporter deliberately does not
			// handle one: Unity's NormalMap type re-encodes the texture (DXT5nm keeps X in alpha) and every
			// shader in the project unpacks that convention, so reproducing it would be a second encoder to
			// keep in step for no gain - the mip filtering this is all for is an alpha-test problem, and a
			// normal map has no alpha test. How the VTF says it is sampled goes with it: Unity's importer
			// starts from its defaults (anisotropy 1, bilinear, repeat), which every normal map had until
			// the rest of the textures had the forced anisotropy and the VTF's filter and wrap.
			GeneratedMeta.NormalMapSampling? sampling = null;
			if (AssetImporter.GetAtPath(assetPath) is SourceTextureImporter source)
			{
				sampling = new GeneratedMeta.NormalMapSampling
				{
					filterMode = source.filterMode, anisoLevel = source.anisoLevel, wrapU = source.wrapU, wrapV = source.wrapV,
				};
				AssetDatabase.ClearImporterOverride(assetPath);
			}

			if (AssetImporter.GetAtPath(assetPath) is not TextureImporter textureImporter)
				return texture;

			// Idempotent: the same texture is claimed by every material that uses it, and a re-import
			// is expensive.
			if (textureImporter.userData != null && textureImporter.userData.Contains(kNormalMapStamp))
				return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

			if (ConvertSourceNormalMapFile(assetPath, selfShadowed))
				textureImporter.userData = GeneratedAssetVersion.Stamp(textureImporter.userData, GeneratedVersion)
										 + ";" + kNormalMapStamp;

			textureImporter.textureType = TextureImporterType.NormalMap;

			// Say what the shape IS rather than inheriting it. Clearing the override above replaces the
			// ScriptedImporter's block with a fresh TextureImporter one, and what that block carries for
			// the settings nobody has written is not Texture2D: 217 of this project's 416 normal maps came
			// out as CUBEMAPS, which is 217 of the 219 cubemaps in the whole project. A cube asset's main
			// object is a Cubemap, so LoadAssetAtPath<Texture2D> on it returns null and the material's
			// $bumpmap slot serialises as a null reference - 329 of the 624 materials whose VMT names a
			// $bumpmap had no normal map because of this, with the converted .png sitting right there on
			// disk. Nothing else in this importer writes a shape, so an inherited wrong one survives every
			// re-import forever.
			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);
			settings.textureShape = TextureImporterShape.Texture2D;
			textureImporter.SetTextureSettings(settings);
			if (sampling.HasValue)
				SetSampling(textureImporter, sampling.Value);

			textureImporter.SaveAndReimport();
			return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
		}

		// What ConvertSourceNormalMapFile does to each pixel of a Source normal map, in place
		static void ConvertSourceNormalMapPixels(Color32[] pixels, bool selfShadowed)
		{
			for (int i = 0; i < pixels.Length; i++)
			{
				var p = pixels[i];
				if (selfShadowed)
				{
					// Reconstruct a normal from the three basis intensities. Approximate by
					// nature - the self-shadowing term is baked in and cannot be separated
					// again - but far closer than treating the channels as an xyz normal.
					var n = kSourceBumpBasis[0] * (p.r / 255.0f)
						  + kSourceBumpBasis[1] * (p.g / 255.0f)
						  + kSourceBumpBasis[2] * (p.b / 255.0f);
					n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.forward;
					p.r = (byte)Mathf.Clamp(Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255.0f), 0, 255);
					p.g = (byte)Mathf.Clamp(Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255.0f), 0, 255);
					p.b = (byte)Mathf.Clamp(Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255.0f), 0, 255);
					p.a = 255;
				}
				// DirectX -> OpenGL. Exact in 8 bits: v encodes 2v/255-1, and 255-v encodes
				// its negation.
				p.g = (byte)(255 - p.g);
				pixels[i] = p;
			}
		}

		/// <summary>
		/// The byte a .png SaveFramesAsPNG writes holds for a float, as LoadImage reads it back: EncodeArrayToPNG makes each
		/// float (v * 255 + 0.5), clamped, an 8-bit RGBA .png
		/// . Not for a NaN, which the probe did not try.
		/// </summary>
		static byte PngByteOf(float value) => (byte)Mathf.Clamp(value * 255f + 0.5f, 0f, 255f);

		/// <summary>The pixels a .png SaveFramesAsPNG writes from these holds (<see cref="PngByteOf"/>); null with a NaN.</summary>
		internal static Color32[] PngPixelsOf(Color[] pixels)
		{
			var result = new Color32[pixels.Length];
			for (int i = 0; i < pixels.Length; i++)
			{
				var p = pixels[i];
				if (float.IsNaN(p.r) || float.IsNaN(p.g) || float.IsNaN(p.b) || float.IsNaN(p.a))
					return null;
				result[i] = new Color32(PngByteOf(p.r), PngByteOf(p.g), PngByteOf(p.b), PngByteOf(p.a));
			}
			return result;
		}

		/// <summary>
		/// A normal map written ahead, as SaveFramesAsPNG and then ConvertSourceNormalMapFile write it - the same file - from the
		/// pixels it is written from, as the .png would hold them (<see cref="PngPixelsOf"/>): without the unconverted .png
		/// written first and read back. With a NaN among them, the way it was. With <paramref name="unwritten"/>, it is left to
		/// it, its pixels taken as the .png holds them, converted and encoded on a worker thread (<see cref="EncodeNormalMap"/>):
		/// a texture written ahead holds no NaN (<see cref="WrittenAtOnce"/>), and one that did would not be written, but said.
		/// Taking them on the main thread was most of the 1.2 s the metas written ahead took of a map's first
		/// import.
		/// </summary>
		internal static bool WriteSourceNormalMap(string assetPath, Color[] pixels, int width, int height, bool selfShadowed,
												  Unwritten unwritten = null)
		{
			var fullPath = Path.GetFullPath(assetPath);
			if (unwritten != null)
			{
				unwritten.Png(fullPath, () => EncodeNormalMap(PngPixelsOf(pixels) ?? throw new System.InvalidOperationException(
					$"{assetPath} holds a NaN, so what its .png makes of it is only known from the .png: it is written at once, not ahead"),
															  width, height, selfShadowed));
				return true;
			}
			var converted = PngPixelsOf(pixels);
			if (converted == null)
			{
				File.WriteAllBytes(fullPath, ImageConversion.EncodeArrayToPNG(pixels, UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
																			 (uint)width, (uint)height));
				return ConvertSourceNormalMapFile(assetPath, selfShadowed);
			}
			ConvertSourceNormalMapPixels(converted, selfShadowed);
			Texture2D scratch = null;
			try
			{
				scratch = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true);
				scratch.SetPixels32(converted);
				scratch.Apply(updateMipmaps: false);
				File.WriteAllBytes(fullPath, scratch.EncodeToPNG());
				return true;
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex);
				return false;
			}
			finally
			{
				if (scratch != null)
					Object.DestroyImmediate(scratch);
			}
		}

		/// <summary>
		/// A normal map's .png, converted from the bytes a .png of its pixels holds (<see cref="PngPixelsOf"/>), as
		/// <see cref="WriteSourceNormalMap"/> writes it through a texture of its own, which only the main thread can make:
		/// encoded from the converted bytes as they are, which any thread can (ImageConversion.EncodeArrayToPNG).
		/// </summary>
		internal static byte[] EncodeNormalMap(Color32[] converted, int width, int height, bool selfShadowed)
		{
			ConvertSourceNormalMapPixels(converted, selfShadowed);
			return ImageConversion.EncodeArrayToPNG(converted, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
													(uint)width, (uint)height);
		}

		/// <summary>
		/// What SourceTextureImporter.FormatOf makes of a .png SaveFramesAsPNG wrote, from the pixels it was written from, as
		/// the .png holds them (<see cref="PngByteOf"/>): only alpha decides it. Reading the .png back for it took 2.4 s of
		/// a map's first import. A NaN alpha before any other below 255 has the .png read back.
		/// With <paramref name="alphaIsOne"/> (<see cref="VTF.AlphaIsOne"/>: every alpha is 1, which the .png holds as 255) the
		/// pixels are not looked at again: that took 1.1 s of the same import.
		/// </summary>
		internal static TextureFormat FormatOfWritten(Color[] pixels, int width, int height, bool compress, string assetPath,
													  bool alphaIsOne = false)
		{
			if (!compress || width % 4 != 0 || height % 4 != 0)
				return TextureFormat.RGBA32;
			if (alphaIsOne)
				return TextureFormat.DXT1;
			foreach (var pixel in pixels)
			{
				if (float.IsNaN(pixel.a))
					return FormatOfPng(assetPath, compress);
				if (PngByteOf(pixel.a) != 255)
					return TextureFormat.DXT5;
			}
			return TextureFormat.DXT1;
		}

		// What SourceTextureImporter.FormatOf makes of the .png, read back the way it reads it; RGBA32 when it does not read
		static TextureFormat FormatOfPng(string assetPath, bool compress)
		{
			var format = TextureFormat.RGBA32;
			var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
			try
			{
				if (source.LoadImage(File.ReadAllBytes(Path.GetFullPath(assetPath)), markNonReadable: false))
					format = SourceTextureImporter.FormatOf(source.GetPixels32(), source.width, source.height, compress);
			}
			finally
			{
				Object.DestroyImmediate(source);
			}
			return format;
		}

		/// <summary>
		/// Rewrites the .png in place so it holds a Unity-convention tangent normal.
		/// <para>
		/// Source stores normal maps in the DirectX convention, where green measures -V while Unity
		/// measures +V. That was measured over this project's own imported set rather than assumed:
		/// correlating each base texture's luminance gradient (crevices read dark, from baked AO)
		/// against its normal map, using the red channel - which both conventions agree on - as a
		/// control, the agreement with Unity's convention falls monotonically as the control gets
		/// cleaner, reaching 0 of the 17 strongest pairs. So: invert green.
		/// </para>
		/// <para>
		/// Rewriting the FILE rather than compensating in the shader is deliberate. It keeps the
		/// asset correct for everything that reads it, including the materials that fall back to
		/// Unity's Standard shader, and writing the same path leaves the .meta - and therefore the
		/// GUID and every reference to it - untouched.
		/// </para>
		/// </summary>
		internal static bool ConvertSourceNormalMapFile(string assetPath, bool selfShadowed)
		{
			var fullPath = Path.GetFullPath(assetPath);
			if (!File.Exists(fullPath))
				return false;

			Texture2D scratch = null;
			try
			{
				// Loaded from the bytes rather than taken from the AssetDatabase so it is readable
				// regardless of the importer's settings, and unaffected by compression.
				scratch = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
				if (!scratch.LoadImage(File.ReadAllBytes(fullPath), markNonReadable: false))
					return false;

				var pixels = scratch.GetPixels32();
				ConvertSourceNormalMapPixels(pixels, selfShadowed);

				scratch.SetPixels32(pixels);
				scratch.Apply(updateMipmaps: false);
				File.WriteAllBytes(fullPath, scratch.EncodeToPNG());
				return true;
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex);
				return false;
			}
			finally
			{
				if (scratch != null)
					Object.DestroyImmediate(scratch);
			}
		}

		/// <summary>
		/// The same flags <see cref="ConfigureTextureImporter"/> reads, applied to our own importer.
		/// </summary>
		/// <remarks>
		/// Written through SerializedObject rather than by assigning the fields. A ScriptedImporter instance
		/// is recreated around an import, so a plain field assignment can be dropped without saying so;
		/// SerializedObject writes the .meta, which is what the next import actually reads.
		/// </remarks>
		static void ConfigureSourceImporter(VTF sourceTexture, string assetPath)
		{
			if (AssetImporter.GetAtPath(assetPath) is not SourceTextureImporter importer)
				return;

			var filterMode = FilterMode.Bilinear;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_POINTSAMPLE) != (VTFImageFlag)0) filterMode = FilterMode.Point;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_TRILINEAR) != (VTFImageFlag)0) filterMode = FilterMode.Trilinear;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_ANISOTROPIC) != (VTFImageFlag)0) filterMode = FilterMode.Trilinear;

			// Anisotropy is not a per-texture decision in Source: the game forces it globally, and Black
			// Mesa's config ships mat_forceaniso "16". The VTF's own ANISOTROPIC flag is a hint from the
			// era when it was, and following it left 574 of 691 textures at 1 - which is no anisotropy at
			// all. Anisotropy is what stops a surface seen at a grazing angle from over-blurring along one
			// axis while aliasing along the other, which is exactly how a fence or a road reads wrong.
			// A point-sampled texture is excluded: it asked not to be filtered.
			int aniso = ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_POINTSAMPLE) != (VTFImageFlag)0)
					  ? 1 : kForcedAnisoLevel;

			var serialized = new SerializedObject(importer);
			// Whether the file HAS mips, not whether it carries TEXTUREFLAGS_NOMIP - see VTF.MipmapCount.
			serialized.FindProperty("generateMipmaps").boolValue = sourceTexture.MipmapCount > 1;
			serialized.FindProperty("filterMode").enumValueIndex = (int)filterMode;
			serialized.FindProperty("anisoLevel").intValue = aniso;
			serialized.FindProperty("wrapU").enumValueIndex =
				(int)(((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPS) != (VTFImageFlag)0)
					  ? TextureWrapMode.Clamp : TextureWrapMode.Repeat);
			serialized.FindProperty("wrapV").enumValueIndex =
				(int)(((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPT) != (VTFImageFlag)0)
					  ? TextureWrapMode.Clamp : TextureWrapMode.Repeat);
			serialized.ApplyModifiedPropertiesWithoutUndo();

			// Only ever called for pixels Import has just written from the VTF.
			importer.userData = UserDataForRewrittenPixels(importer.userData);
			importer.SaveAndReimport();
		}

		/// <summary>
		/// Re-imports a texture as a cut-out, so its mip chain keeps the alpha test's coverage.
		/// </summary>
		/// <remarks>
		/// Claimed by USE, like colour and normal maps, and for the same reason: nothing in a texture file
		/// says whether its alpha is a silhouette or one of the masks Source also keeps there. Only the VMT
		/// knows, by setting $alphatest - and 2673 opaque materials in this game put a $selfillum,
		/// $basealphaenvmapmask, $basemapalphaphongmask or $detail mask in that same channel, every one of
		/// which would be corrupted by scaling it. $allowalphatocoverage would be the wrong gate: not one
		/// of the four fence materials sets it.
		/// </remarks>
		public static void MarkAsAlphaTested(Texture texture, float reference)
		{
			if (texture == null || !(reference > 0.0f) || ClaimsDeferred)
				return;

			var assetPath = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(assetPath))
				return;
			if (AssetImporter.GetAtPath(assetPath) is not SourceTextureImporter importer)
				return;                     // a normal map, or a texture Unity's own importer still owns

			var serialized = new SerializedObject(importer);
			var property = serialized.FindProperty("alphaTestReference");
			// The STRICTEST claim wins, not the last one. One texture is shared by several materials that
			// can test it at different values - the four fence materials use 0.5 and 0.1 - and whichever
			// imported last would otherwise decide. The higher cutoff is the one where thinning actually
			// costs something, and a lower one only ever asks for less work.
			if (property == null || reference <= property.floatValue)
				return;                     // idempotent, and never weakened by a later claim

			property.floatValue = reference;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			importer.SaveAndReimport();
		}

		/// <summary>Claims every OTHER frame of an animated texture's set as colour; see <see cref="MarkAsColor"/>.</summary>
		static void MarkFrameSiblingsAsColor(string assetPath)
		{
			// name[3].png -> name, 3. Anything else is not a frame set and has no siblings.
			var fileName = Path.GetFileNameWithoutExtension(assetPath);
			int open = fileName.LastIndexOf('[');
			if (open <= 0 || !fileName.EndsWith("]"))
				return;
			var stem = fileName.Substring(0, open);
			var directory = Path.GetDirectoryName(assetPath);
			if (string.IsNullOrEmpty(directory))
				return;

			// Walk outward until a frame is missing, which is how Import itself finds the set.
			for (int frame = 0; ; frame++)
			{
				var sibling = Path.Combine(directory, stem + "[" + frame + "].png").Replace(Path.DirectorySeparatorChar, '/');
				if (sibling == assetPath)
					continue;                       // the caller is already doing this one
				if (AssetDatabase.LoadAssetAtPath<Texture2D>(sibling) == null)
					break;
				if (AssetImporter.GetAtPath(sibling) is SourceTextureImporter)
					SetSourceImporterSRGB(sibling);
				else if (AssetImporter.GetAtPath(sibling) is TextureImporter siblingImporter &&
						 !siblingImporter.sRGBTexture &&
						 siblingImporter.textureType != TextureImporterType.NormalMap)
				{
					siblingImporter.sRGBTexture = true;
					siblingImporter.SaveAndReimport();
				}
			}
		}

		/// <summary>Sets the colour claim on our importer; see <see cref="MarkAsColor"/>.</summary>
		static void SetSourceImporterSRGB(string assetPath)
		{
			if (AssetImporter.GetAtPath(assetPath) is not SourceTextureImporter importer)
				return;

			var serialized = new SerializedObject(importer);
			var sRGB = serialized.FindProperty("sRGB");
			if (sRGB == null || sRGB.boolValue)
				return;                                     // idempotent: every material using it claims it

			sRGB.boolValue = true;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			importer.SaveAndReimport();
		}

		public static void ConfigureTextureImporter(VTF sourceTexture, string destinationPath)
		{
			var assetPath = Path.Combine("Assets", Path.GetRelativePath(Application.dataPath, destinationPath));

			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_HINT_DXT5) != (VTFImageFlag)0) stringBuilder.Append("HINT_DXT5 ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_NOLOD) != (VTFImageFlag)0) stringBuilder.Append("NOLOD ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_MINMIP) != (VTFImageFlag)0) stringBuilder.Append("MINMIP ");


			//Debug.Log($"assetpath: {assetPath}");
			if (AssetImporter.GetAtPath(assetPath) is SourceTextureImporter)
			{
				ConfigureSourceImporter(sourceTexture, assetPath);
				return;
			}

			TextureImporter textureImporter = TextureImporter.GetAtPath(assetPath) as TextureImporter;
			if (textureImporter == null)
				return;

			// A VTF's TEXTUREFLAGS_NORMAL is set on plenty of textures that are not normal maps,
			// so it cannot decide this. What a texture IS gets decided by how a material uses it:
			// the VMT names it under $bumpmap/$bumpmap2, and MarkAsNormalMap below is called from
			// there. Start every texture off as Default; a normal map is promoted when a material
			// claims it.
			textureImporter.textureType = TextureImporterType.Default;

			// TODO: handle these somehow
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_PROCEDURAL) != (VTFImageFlag)0) stringBuilder.Append("PROCEDURAL ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_VERTEXTEXTURE) != (VTFImageFlag)0) stringBuilder.Append("VERTEXTEXTURE ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_ENVMAP) != (VTFImageFlag)0) stringBuilder.Append("ENVMAP ");
			//if ((sourceTexture.flags & VTFImageFlag.TEXTUREFLAGS_SSBUMP) != (VTFImageFlag)0) stringBuilder.Append("SSBUMP ");


			TextureImporterSettings settings = new();
			textureImporter.ReadTextureSettings(settings);

			// alphaSource decides whether the alpha channel is imported at all, which is safe to
			// decide from the VTF: it either has one or it does not.
			settings.alphaSource = sourceTexture.HasAlpha ? TextureImporterAlphaSource.FromInput
														  : TextureImporterAlphaSource.None;

			// alphaIsTransparency is a different question, and nothing in the VTF can answer it. It
			// makes Unity dilate the colour outward into transparent texels, rewriting the RGB it
			// ships - correct when the alpha really is opacity, destructive whenever it is not, and
			// in Source it very often is not: the base texture's alpha carries $selfillum masks,
			// $basealphaenvmapmask, $basemapalphaphongmask and detail blend factors. Only the VMT
			// knows, and by the time one is read the texture is already imported. So: never.
			settings.alphaIsTransparency = false;

			// The file's own level count, not TEXTUREFLAGS_NOMIP, which vtex sets while still writing a
			// full chain (see VTF.MipmapCount).
			settings.mipmapEnabled = sourceTexture.MipmapCount > 1;

			settings.borderMipmap = false;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_BORDER) != (VTFImageFlag)0) settings.borderMipmap = true;

			// NOTE: TEXTUREFLAGS_ONEBITALPHA/EIGHTBITALPHA say the texture HAS an alpha channel, not
			//       that the channel is opacity, so they must not drive alphaIsTransparency - that is
			//       what alphaSource above is for.

			// Linear until a material claims the texture for a color slot (MarkAsColor). The VTF's
			// TEXTUREFLAGS_SRGB cannot decide it: it used to mean NOCOMPRESS, and in this game it is set
			// on only 191 of the 1468 textures materials sample as color, and on a few that hold masks.
			settings.sRGBTexture = false;

			settings.filterMode = FilterMode.Bilinear;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_POINTSAMPLE) != (VTFImageFlag)0) settings.filterMode = FilterMode.Point;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_TRILINEAR) != (VTFImageFlag)0) settings.filterMode = FilterMode.Trilinear;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_ANISOTROPIC) != (VTFImageFlag)0) settings.filterMode = FilterMode.Trilinear;

			// Forced globally, as Source forces it; see ConfigureSourceImporter.
			settings.aniso = ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_POINTSAMPLE) != (VTFImageFlag)0)
						   ? 1 : kForcedAnisoLevel;

			settings.wrapMode = TextureWrapMode.Repeat;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPS) != (VTFImageFlag)0) settings.wrapModeU = TextureWrapMode.Clamp;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPT) != (VTFImageFlag)0) settings.wrapModeV = TextureWrapMode.Clamp;
			if ((sourceTexture.Flags & VTFImageFlag.TEXTUREFLAGS_CLAMPU) != (VTFImageFlag)0) settings.wrapModeW = TextureWrapMode.Clamp;

			// Only ever called for pixels Import has just written from the VTF.
			textureImporter.userData = UserDataForRewrittenPixels(textureImporter.userData);

			textureImporter.SetTextureSettings(settings);
			textureImporter.SaveAndReimport();
		}
	}

}