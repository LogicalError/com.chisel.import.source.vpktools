using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>Contents bits as vbsp uses them (public/bspflags.h), the ones that decide what a brush is made of.</summary>
	[Flags]
	public enum SourceContents
	{
		None        = 0,
		Solid       = 0x1,
		Window      = 0x2,
		Aux         = 0x4,
		Grate       = 0x8,
		Slime       = 0x10,
		Water       = 0x20,
		BlockLOS    = 0x40,
		Opaque      = 0x80,
		PlayerClip  = 0x10000,
		MonsterClip = 0x20000,
		Origin      = 0x1000000,
		Detail      = 0x8000000,
		Translucent = 0x10000000,
		Ladder      = 0x20000000,

		/// <summary>The contents a player can see through: what makes a brush something other than solid.</summary>
		Transparent = Window | Grate | Slime | Water,
		/// <summary>LAST_VISIBLE_CONTENTS | (LAST_VISIBLE_CONTENTS - 1)</summary>
		AllVisible  = 0xFF,
	}

	/// <summary>Surface flags as vbsp uses them (public/bspflags.h), the ones FindMiptex sets.</summary>
	[Flags]
	public enum SourceSurfaceFlags
	{
		None    = 0,
		Sky2D   = 0x2,
		Sky     = 0x4,
		Trigger = 0x40,
		NoDraw  = 0x80,
		Hint    = 0x100,
		Skip    = 0x200,
		NoLight = 0x400,
		NoChop  = 0x4000,
	}

	/// <summary>
	/// What vbsp makes of a brush side with this material, read from the VMT the way vbsp reads it: FindMiptex
	/// (utils/vbsp/textures.cpp) for the material, LoadSideCallback and BrushContents (utils/vbsp/map.cpp) for the
	/// side and the brush. Kept apart from <see cref="VMT"/>: the material importer has no use for these keys, and
	/// "patch" materials are followed here only for them, so reading contents never changes an imported material.
	/// </summary>
	/// <remarks>
	/// vbsp asks the material system whether a material is translucent or alpha tested. The material system isn't
	/// available here, so that is read from the keys that make it so ($translucent, $additive, $vertexalpha, $alpha
	/// below 1, the Water shader, $alphatest), which is how the offline Black Mesa scan did it too.
	/// </remarks>
	public sealed class VMTContents
	{
		/// <summary>The shader, after following a "patch" material to the material it includes</summary>
		public string Shader { get; private set; } = string.Empty;
		/// <summary>The texture's contents, as FindMiptex gives them</summary>
		public SourceContents Contents { get; private set; }
		/// <summary>The texture's surface flags, as FindMiptex gives them</summary>
		public SourceSurfaceFlags SurfaceFlags { get; private set; }
		/// <summary>False when the material, or a material it includes, could not be read</summary>
		public bool Found { get; private set; } = true;

		/// <summary>A material that could not be found: vbsp builds a side with it as solid</summary>
		public static readonly VMTContents Missing = new() { Found = false };

		/// <summary>What a side with this material contributes to its brush (LoadSideCallback)</summary>
		public SourceContents SideContents => SideContentsOf(Contents, SurfaceFlags);

		const int kMaxIncludeDepth = 8;

		static readonly string[] kChainKeys =
		{
			"%compilesky", "%compile2dsky", "%compilehint", "%compileskip", "%compileorigin", "%compileclip",
			"%playerclip", "%compilenpcclip", "%compilenochop", "%compiletrigger", "%compilenolight",
		};
		static readonly (SourceContents contents, SourceSurfaceFlags surface)[] kChainEffects =
		{
			(SourceContents.None, SourceSurfaceFlags.Sky | SourceSurfaceFlags.NoLight),
			(SourceContents.None, SourceSurfaceFlags.Sky | SourceSurfaceFlags.Sky2D | SourceSurfaceFlags.NoLight),
			(SourceContents.None, SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight | SourceSurfaceFlags.Hint),
			(SourceContents.None, SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight | SourceSurfaceFlags.Skip),
			(SourceContents.Origin | SourceContents.Detail, SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight),
			(SourceContents.PlayerClip | SourceContents.MonsterClip, SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight),
			(SourceContents.PlayerClip, SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight),
			(SourceContents.MonsterClip, SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight),
			(SourceContents.None, SourceSurfaceFlags.NoChop),
			(SourceContents.None, SourceSurfaceFlags.NoLight | SourceSurfaceFlags.Trigger),
			(SourceContents.None, SourceSurfaceFlags.NoLight),
		};

		/// <summary>
		/// Reads a VMT's text. <paramref name="loadInclude"/> returns the text of a material a "patch" includes, or
		/// null when it can't be found.
		/// </summary>
		public static VMTContents FromText(string text, Func<string, string> loadInclude = null)
		{
			return FromText(text, loadInclude, 0);
		}

		static VMTContents FromText(string text, Func<string, string> loadInclude, int depth)
		{
			var resolved = ResolvePatch(text, loadInclude, depth);
			if (resolved == null)
				return Missing;
			var (shader, keys) = resolved.Value;
			var (contents, surface) = FindMiptex(shader, keys);
			return new VMTContents { Shader = shader, Contents = contents, SurfaceFlags = surface };
		}

		// The shader and keys of a material; for a "patch" material, those of the material it includes with the
		// patch's insert (adds or overwrites) and replace (overwrites only) applied. Null when something is missing.
		// Those semantics are the Valve Developer Community's "Patch" shader page's; the material system that applies
		// them isn't public. The offline Black Mesa scan read patches the same way.
		static (string shader, Dictionary<string, string> keys)? ResolvePatch(string text, Func<string, string> loadInclude, int depth)
		{
			var shader = ReadKeys(text, out var keys, out var insert, out var replace);
			if (shader == null)
				return null;
			if (!string.Equals(shader, "patch", StringComparison.OrdinalIgnoreCase))
				return (shader, keys);
			if (loadInclude == null || !keys.TryGetValue("include", out var include) || depth >= kMaxIncludeDepth)
				return null;
			var includedText = loadInclude(include);
			if (includedText == null)
				return null;
			var inner = ResolvePatch(includedText, loadInclude, depth + 1);
			if (inner == null)
				return null;
			var (baseShader, baseKeys) = inner.Value;
			foreach (var (key, value) in insert)
				baseKeys[key] = value;
			foreach (var (key, value) in replace)
			{
				if (baseKeys.ContainsKey(key))
					baseKeys[key] = value;
			}
			return (baseShader, baseKeys);
		}

		/// <summary>FindMiptex: the contents and surface flags vbsp gives a texture, from its shader and keys.</summary>
		public static (SourceContents contents, SourceSurfaceFlags surface) FindMiptex(string shader, IReadOnlyDictionary<string, string> keys)
		{
			bool IsTrue(string key) => keys.TryGetValue(key, out var value) && StringIsTrue(value);

			var contents = SourceContents.None;
			var surface  = SourceSurfaceFlags.None;

			// The tool materials come first, as an if/else chain; the first one that applies ends it
			for (int i = 0; i < kChainKeys.Length; i++)
			{
				var key = kChainKeys[i];
				if (!IsTrue(key))
					continue;
				if (key == "%compilenolight" && IsTrue("%compilewater"))
					continue;
				contents |= kChainEffects[i].contents;
				surface  |= kChainEffects[i].surface;
				return (contents, surface);
			}

			if (IsTrue("%compileladder"))
				contents |= SourceContents.Ladder;
			if (IsTrue("%compilepassbullets"))
				contents = (contents & ~SourceContents.Solid) | SourceContents.Grate;
			if (IsTrue("%compilenodraw"))
				surface |= SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight;
			if (IsTrue("%compileinvisible"))
			{
				contents = (contents & ~SourceContents.Solid) | SourceContents.Grate;
				surface |= SourceSurfaceFlags.NoDraw | SourceSurfaceFlags.NoLight;
			}

			var checkWindow = true;
			if (IsTrue("%compilenonsolid"))
			{
				contents = SourceContents.Opaque;
				checkWindow = false;
			}
			if (IsTrue("%compileblocklos"))
			{
				contents = SourceContents.BlockLOS;
				checkWindow = false;
			}
			if (IsTrue("%compiledetail"))
				contents |= SourceContents.Detail;
			if (IsTrue("%compilewater"))
				contents = (contents & ~(SourceContents.Solid | SourceContents.Detail)) | SourceContents.Water;
			if (IsTrue("%compileslime"))
				contents = (contents & ~(SourceContents.Solid | SourceContents.Detail)) | SourceContents.Slime;

			// What you can see through is a window, unless it already is grate or water
			if (checkWindow && !IsOpaque(shader, keys))
			{
				if ((contents & (SourceContents.Grate | SourceContents.Water)) == 0)
					contents |= SourceContents.Window;
				contents &= ~SourceContents.Solid;
			}
			return (contents, surface);
		}

		// IMaterial::IsTranslucent() || IMaterial::IsAlphaTested(), from the keys that make a material so
		static bool IsOpaque(string shader, IReadOnlyDictionary<string, string> keys)
		{
			bool FlagOn(string key) => keys.TryGetValue(key, out var value) && TryParseNumber(value, out var number) && (int)number != 0;

			if (FlagOn("$translucent") || FlagOn("$additive") || FlagOn("$vertexalpha") || FlagOn("$alphatest"))
				return false;
			if (keys.TryGetValue("$alpha", out var alpha) && TryParseNumber(alpha, out var alphaValue) && alphaValue < 1.0)
				return false;
			if (shader != null && shader.StartsWith("water", StringComparison.OrdinalIgnoreCase))
				return false;
			return true;
		}

		/// <summary>LoadSideCallback: a side with no visible contents of its own is solid, and hint and skip sides are nothing.</summary>
		public static SourceContents SideContentsOf(SourceContents textureContents, SourceSurfaceFlags surface)
		{
			var contents = textureContents;
			if ((contents & (SourceContents.AllVisible | SourceContents.PlayerClip | SourceContents.MonsterClip)) == 0)
				contents |= SourceContents.Solid;
			if ((surface & (SourceSurfaceFlags.Hint | SourceSurfaceFlags.Skip)) != 0)
				contents = SourceContents.None;
			return contents;
		}

		/// <summary>BrushContents: the first side's contents, and any see-through contents of any side, which then clear solid.</summary>
		public static SourceContents BrushContentsOf(IReadOnlyList<SourceContents> sides)
		{
			if (sides == null || sides.Count == 0)
				return SourceContents.None;
			var contents = sides[0];
			var union = SourceContents.None;
			foreach (var side in sides)
				union |= side;
			var transparent = union & SourceContents.Transparent;
			if (transparent != 0)
			{
				contents |= transparent | SourceContents.Translucent;
				contents &= ~SourceContents.Solid;
			}
			return contents;
		}

		/// <summary>
		/// The one see-through type a brush gets when it has several, by vbsp's VisibleContents order (the lowest bit
		/// wins: window, then grate, slime, water), or None for a brush that isn't see-through.
		/// </summary>
		public static SourceContents VisibleType(SourceContents brushContents)
		{
			foreach (var type in new[] { SourceContents.Window, SourceContents.Grate, SourceContents.Slime, SourceContents.Water })
			{
				if ((brushContents & type) != 0)
					return type;
			}
			return SourceContents.None;
		}

		// textures.cpp StringIsTrue
		static bool StringIsTrue(string value)
		{
			return value != null &&
				   (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));
		}

		static bool TryParseNumber(string value, out double number)
		{
			return double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}

		#region KeyValues
		// Tokens as KeyValues::ReadToken reads them: quoted strings (no escapes), braces, and bare tokens that end at
		// whitespace, a quote or a brace; // starts a comment. An unquoted [$CONDITION] after a key or value decides,
		// for a Windows PC, whether that pair is read at all.
		static readonly Regex kToken = new(@"//[^\n]*|""([^""]*)""?|([{}])|([^\s""{}]+)", RegexOptions.Compiled);

		enum TokenKind { Quoted, Brace, Bare }

		sealed class KeyValue
		{
			public string         Key;
			public string         Value;    // null for a block
			public List<KeyValue> Children; // null for a value
		}

		// The shader and the top-level keys of a material, lower case, the first occurrence of each. Keys with a
		// "?" condition in their name, nested blocks and Proxies are left out; a patch's insert and replace blocks
		// come back separately. Null when the text has no shader block.
		static string ReadKeys(string text, out Dictionary<string, string> keys,
							   out List<(string key, string value)> insert, out List<(string key, string value)> replace)
		{
			keys    = new Dictionary<string, string>();
			insert  = new List<(string, string)>();
			replace = new List<(string, string)>();
			if (string.IsNullOrEmpty(text))
				return null;
			text = text.Replace("\0", string.Empty).TrimStart('\ufeff');

			var tokens = new List<(TokenKind kind, string value)>();
			foreach (Match match in kToken.Matches(text))
			{
				if (match.Groups[1].Success)      tokens.Add((TokenKind.Quoted, match.Groups[1].Value));
				else if (match.Groups[2].Success) tokens.Add((TokenKind.Brace,  match.Groups[2].Value));
				else if (match.Groups[3].Success) tokens.Add((TokenKind.Bare,   match.Groups[3].Value));
			}

			var position = 0;
			foreach (var block in ReadBlock(tokens, ref position))
			{
				if (block.Children == null)
					continue;
				foreach (var item in block.Children)
				{
					var lower = item.Key.ToLowerInvariant();
					if (item.Children != null)
					{
						if (lower == "insert" || lower == "replace")
						{
							var target = lower == "insert" ? insert : replace;
							foreach (var child in item.Children)
							{
								if (child.Children == null)
									target.Add((child.Key.ToLowerInvariant(), child.Value));
							}
						}
						continue;
					}
					if (lower.Contains("?"))
						continue;
					if (!keys.ContainsKey(lower))
						keys[lower] = item.Value;
				}
				return block.Key;
			}
			return null;
		}

		static List<KeyValue> ReadBlock(List<(TokenKind kind, string value)> tokens, ref int position)
		{
			var items = new List<KeyValue>();
			while (position < tokens.Count)
			{
				var (kind, value) = tokens[position];
				if (kind == TokenKind.Brace && value == "}")
				{
					position++;
					return items;
				}
				if (kind == TokenKind.Brace)
				{
					// An anonymous block
					position++;
					items.Add(new KeyValue { Key = string.Empty, Children = ReadBlock(tokens, ref position) });
					continue;
				}

				var key = value;
				position++;
				string condition = null;
				if (IsCondition(tokens, position))
				{
					condition = tokens[position].value;
					position++;
				}
				if (position >= tokens.Count)
				{
					items.Add(new KeyValue { Key = key, Value = string.Empty });
					break;
				}

				KeyValue item;
				var (valueKind, valueText) = tokens[position];
				if (valueKind == TokenKind.Brace && valueText == "{")
				{
					position++;
					item = new KeyValue { Key = key, Children = ReadBlock(tokens, ref position) };
				}
				else if (valueKind == TokenKind.Brace)
				{
					// A key right before the closing brace
					item = new KeyValue { Key = key, Value = string.Empty };
				}
				else
				{
					position++;
					item = new KeyValue { Key = key, Value = valueText };
				}

				if (IsCondition(tokens, position))
				{
					condition = tokens[position].value;
					position++;
				}
				if (condition != null && !ConditionHolds(condition))
					continue;
				items.Add(item);
			}
			return items;
		}

		// Only an unquoted [...] token is a condition
		static bool IsCondition(List<(TokenKind kind, string value)> tokens, int position)
		{
			return position < tokens.Count &&
				   tokens[position].kind == TokenKind.Bare &&
				   tokens[position].value.StartsWith("[") &&
				   tokens[position].value.EndsWith("]");
		}

		// [$WIN32], [!$X360], [$WIN32 || $OSX] and the like, evaluated for a Windows PC
		static bool ConditionHolds(string condition)
		{
			var expression = condition.Substring(1, condition.Length - 2).Trim();
			foreach (var alternative in expression.Split(new[] { "||" }, StringSplitOptions.None))
			{
				var holds = true;
				foreach (var termText in alternative.Split(new[] { "&&" }, StringSplitOptions.None))
				{
					var term = termText.Trim();
					var negated = term.StartsWith("!");
					if (negated)
						term = term.Substring(1).Trim();
					var value = term.Equals("$WIN32", StringComparison.OrdinalIgnoreCase) ||
								term.Equals("$WINDOWS", StringComparison.OrdinalIgnoreCase);
					if (negated == value)
					{
						holds = false;
						break;
					}
				}
				if (holds)
					return true;
			}
			return false;
		}
		#endregion
	}
}
