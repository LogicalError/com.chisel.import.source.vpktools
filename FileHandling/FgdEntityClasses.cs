using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>The kinds of entity class an FGD declares, named after the keyword that declares them.</summary>
	public enum FgdClassKind
	{
		/// <summary><c>@BaseClass</c>: declared only to be the base of other classes.</summary>
		Base,

		/// <summary><c>@PointClass</c>.</summary>
		Point,

		/// <summary><c>@SolidClass</c>: made of brushes.</summary>
		Solid,

		/// <summary><c>@NPCClass</c>.</summary>
		NPC,

		/// <summary><c>@FilterClass</c>.</summary>
		Filter,

		/// <summary><c>@MoveClass</c>.</summary>
		Move,

		/// <summary><c>@KeyFrameClass</c>.</summary>
		KeyFrame
	}

	/// <summary>
	/// The entity classes a game's FGD files declare: the kind of each, the classes it is based on and the model it
	/// names, read the way Hammer reads them (Source SDK 2013: fgdlib's GameData::Load and GDclass::InitFromTokens, over
	/// tier1's TokenReader).
	/// <para>
	/// The FGD is the game's own description of its entities, and the only one outside game code: the model an NPC or
	/// an item uses, which classes are NPCs, which are built on <c>prop_dynamic_base</c>. A map was made with what Hammer
	/// made of the files, so Hammer's rules are followed:
	/// </para>
	/// <list type="bullet">
	/// <item>An included file is read where its <c>@include</c> stands, and a class declared again replaces its earlier
	/// declaration - which is how a mod's FGD changes the classes of the FGDs it includes.</item>
	/// <item>A class is based on its bases as they were declared at the time, and a class naming a base that has not
	/// been declared is not declared either.</item>
	/// <item>A <c>//</c> comment runs to the end of its line, so a declaration that has been commented out is none.</item>
	/// </list>
	/// </summary>
	public sealed class FgdEntityClasses
	{
		sealed class Declaration
		{
			public string        Name;
			public FgdClassKind  Kind;
			public Declaration[] Bases;
			public string        Model;
		}

		enum TokenType { Operator, String, Integer, Identifier, Error }

		readonly struct Token
		{
			public readonly TokenType Type;
			public readonly string    Text;

			public Token(TokenType type, string text)
			{
				Type = type;
				Text = text;
			}

			public bool IsOperator(char op)
			{
				return Type == TokenType.Operator && Text[0] == op;
			}
		}

		// Hammer looks a declaration up by its exact name (GameData::ClassForName compares with strcmp), and writes the
		// class names of a map's entities as they are declared...
		readonly Dictionary<string, Declaration> declarations = new(StringComparer.Ordinal);

		// ...while the game finds an entity's class whatever its case.
		readonly Dictionary<string, Declaration> entityClasses = new(StringComparer.OrdinalIgnoreCase);

		// TokenReader::NextToken's operators. '+' is one as well, but TokenReader::SkipWhiteSpace always takes it first.
		const string kOperators = "@,!&*$.=:[](){}\\";

		/// <summary>How many classes are declared.</summary>
		public int Count => declarations.Count;

		/// <summary>
		/// Loads the FGDs in a directory that no other FGD in it includes, with everything they include: in the directory
		/// a game keeps its FGDs in, the game's own FGD and the FGDs it builds on. Several such files load in the order of
		/// their names.
		/// </summary>
		public void LoadDirectory(string directory)
		{
			var files = Directory.GetFiles(directory, "*.fgd", SearchOption.TopDirectoryOnly);
			Array.Sort(files, StringComparer.OrdinalIgnoreCase);

			var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var file in files)
			{
				if (!TryRead(file, File.ReadAllText, out var text))
					continue;

				var tokens = Tokenize(text);
				for (int i = 0; i + 2 < tokens.Count; i++)
				{
					if (tokens[i].IsOperator('@') && tokens[i + 1].Type == TokenType.Identifier &&
						tokens[i + 1].Text.Equals("include", StringComparison.OrdinalIgnoreCase) &&
						tokens[i + 2].Type == TokenType.String)
						included.Add(Path.GetFullPath(Path.Combine(directory, tokens[i + 2].Text)));
				}
			}

			foreach (var file in files)
			{
				if (!included.Contains(Path.GetFullPath(file)))
					Load(file);
			}
		}

		/// <summary>Loads an FGD, with everything it includes.</summary>
		public void Load(string path)
		{
			Load(path, File.ReadAllText);
		}

		/// <summary>Loads an FGD, with everything it includes, reading each file with <paramref name="readFile"/>.</summary>
		internal void Load(string path, Func<string, string> readFile)
		{
			Load(path, readFile, new List<string>());
		}

		void Load(string path, Func<string, string> readFile, List<string> including)
		{
			// Hammer would go on including a file that includes itself until it ran out of stack.
			if (including.Exists(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase)) ||
				!TryRead(path, readFile, out var text))
				return;

			including.Add(path);
			var directory = Path.GetDirectoryName(path) ?? string.Empty;
			var tokens    = Tokenize(text);
			var i = 0;
			while (i < tokens.Count)
			{
				// A section is '@' and its name. After one that could not be read, Hammer skips ahead to the next '@'.
				if (!tokens[i++].IsOperator('@') || i >= tokens.Count || tokens[i].Type != TokenType.Identifier)
					continue;

				var section = tokens[i++].Text;
				if (TryGetKind(section, out var kind))
				{
					var declaration = ReadClass(tokens, ref i, kind);
					if (declaration != null)
					{
						declarations[declaration.Name]  = declaration;
						entityClasses[declaration.Name] = declaration;
					}
				}
				else if (section.Equals("include", StringComparison.OrdinalIgnoreCase) &&
						 i < tokens.Count && tokens[i].Type == TokenType.String)
				{
					// Looked for next to the file that includes it.
					Load(Path.Combine(directory, tokens[i++].Text), readFile, including);
				}
			}
			including.RemoveAt(including.Count - 1);
		}

		/// <summary>The model the class names with <c>studio("...")</c>, or null.</summary>
		public string GetModel(string className)
		{
			return Find(className)?.Model;
		}

		/// <summary>The kind of class the class is declared as, or null when it is not declared.</summary>
		public FgdClassKind? GetKind(string className)
		{
			return Find(className)?.Kind;
		}

		/// <summary>Whether the class is the named class, or is based on it through any chain of bases.</summary>
		public bool DerivesFrom(string className, string baseName)
		{
			var pending     = new Stack<Declaration>();
			var declaration = Find(className);
			if (declaration != null)
				pending.Push(declaration);

			while (pending.Count > 0)
			{
				var current = pending.Pop();
				if (string.Equals(current.Name, baseName, StringComparison.OrdinalIgnoreCase))
					return true;
				foreach (var baseClass in current.Bases)
					pending.Push(baseClass);
			}
			return false;
		}

		// By the exact name first, since names can differ in case alone: base.fgd declares the base class "Light" and the
		// entity "light".
		Declaration Find(string className)
		{
			if (className == null)
				return null;
			return (declarations.TryGetValue(className, out var declaration) || entityClasses.TryGetValue(className, out declaration))
				? declaration : null;
		}

		static bool TryRead(string path, Func<string, string> readFile, out string text)
		{
			try
			{
				text = readFile(path);
				return text != null;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
			{
				text = null;
				return false;
			}
		}

		static bool TryGetKind(string section, out FgdClassKind kind)
		{
			switch (section.ToLowerInvariant())
			{
				case "baseclass":     kind = FgdClassKind.Base;     return true;
				case "pointclass":    kind = FgdClassKind.Point;    return true;
				case "solidclass":    kind = FgdClassKind.Solid;    return true;
				case "npcclass":      kind = FgdClassKind.NPC;      return true;
				case "filterclass":   kind = FgdClassKind.Filter;   return true;
				case "moveclass":     kind = FgdClassKind.Move;     return true;
				case "keyframeclass": kind = FgdClassKind.KeyFrame; return true;
				default:              kind = default;               return false;
			}
		}

		// GDclass::InitFromTokens: the specifiers, '=', the class name, a description after ':' if there is one, and the
		// body in brackets. Null when any of it is not what Hammer expects, which leaves the class undeclared.
		Declaration ReadClass(List<Token> tokens, ref int i, FgdClassKind kind)
		{
			var    bases = new List<Declaration>();
			string model = null;
			while (i < tokens.Count && tokens[i].Type == TokenType.Identifier)
			{
				var specifier = tokens[i++].Text;
				if (specifier.Equals("halfgridsnap", StringComparison.OrdinalIgnoreCase))
					continue;
				if (!Skip(tokens, ref i, '('))
					return null;

				if (specifier.Equals("base", StringComparison.OrdinalIgnoreCase))
				{
					// GDclass::ParseBase: names separated by commas, each of a class declared before.
					do
					{
						if (i >= tokens.Count || tokens[i].Type != TokenType.Identifier ||
							!declarations.TryGetValue(tokens[i].Text, out var baseClass))
							return null;
						bases.Add(baseClass);
						i++;
					}
					while (Skip(tokens, ref i, ','));

					if (!Skip(tokens, ref i, ')'))
						return null;
				}
				else
				{
					// GDclass::ParseHelper: everything up to the closing parenthesis that is not an operator is a parameter.
					var parameters = new List<string>();
					while (true)
					{
						if (i >= tokens.Count || tokens[i].Type == TokenType.Error)
							return null;

						var token = tokens[i++];
						if (token.IsOperator(')'))
							break;
						if (token.IsOperator('='))
							return null;
						if (token.Type != TokenType.Operator)
							parameters.Add(token.Text);
					}

					if (model == null && specifier.Equals("studio", StringComparison.OrdinalIgnoreCase) &&
						parameters.Count > 0 && parameters[0].Length > 0)
						model = parameters[0];
				}
			}

			if (!Skip(tokens, ref i, '=') || i >= tokens.Count || tokens[i].Type != TokenType.Identifier)
				return null;
			var name = tokens[i++].Text;

			if (Skip(tokens, ref i, ':'))
			{
				if (i >= tokens.Count || tokens[i].Type != TokenType.String)
					return null;
				i++;
			}

			if (!Skip(tokens, ref i, '['))
				return null;

			// The keyvalues, inputs and outputs, whose choices and flags nest brackets of their own.
			for (var depth = 1; depth > 0; i++)
			{
				if (i >= tokens.Count)
					return null;
				if (tokens[i].IsOperator('['))
					depth++;
				else if (tokens[i].IsOperator(']'))
					depth--;
			}

			return new Declaration { Name = name, Kind = kind, Bases = bases.ToArray(), Model = model };
		}

		static bool Skip(List<Token> tokens, ref int i, char op)
		{
			if (i >= tokens.Count || !tokens[i].IsOperator(op))
				return false;
			i++;
			return true;
		}

		// TokenReader::NextToken, over the whole text.
		static List<Token> Tokenize(string text)
		{
			var tokens = new List<Token>();
			var i = 0;
			while (true)
			{
				SkipWhiteSpace(text, ref i);
				if (i >= text.Length)
					return tokens;

				var c = text[i];
				if (kOperators.IndexOf(c) >= 0)
				{
					tokens.Add(new Token(TokenType.Operator, c.ToString()));
					i++;
				}
				else if (c == '"')
				{
					tokens.Add(ReadString(text, ref i));
				}
				else if (IsDigit(c) || c == '-')
				{
					var start = i++;
					while (i < text.Length && IsDigit(text[i]))
						i++;

					// A number may not run into another '-', a letter or an underscore.
					if (i < text.Length && (text[i] == '-' || IsLetter(text[i]) || text[i] == '_'))
					{
						i++;
						tokens.Add(new Token(TokenType.Error, text.Substring(start, i - start)));
					}
					else
						tokens.Add(new Token(TokenType.Integer, text.Substring(start, i - start)));
				}
				else if (IsLetter(c) || c == '_')
				{
					var start = i;
					while (i < text.Length && (IsLetter(text[i]) || IsDigit(text[i]) || text[i] == '_'))
						i++;
					tokens.Add(new Token(TokenType.Identifier, text.Substring(start, i - start)));
				}
				else
				{
					// A character that starts no token, which TokenReader never gets past.
					tokens.Add(new Token(TokenType.Error, c.ToString()));
					i++;
				}
			}
		}

		// TokenReader::SkipWhiteSpace. Returns whether a '+' was among it, which joins two strings into one.
		static bool SkipWhiteSpace(string text, ref int i)
		{
			var combine = false;
			while (i < text.Length)
			{
				var c = text[i];
				if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\0')
				{
					i++;
				}
				else if (c == '+')
				{
					combine = true;
					i++;
				}
				else if (c == '/')
				{
					// "//" comments out the rest of the line; a '/' on its own is skipped.
					if (i + 1 < text.Length && text[i + 1] == '/')
					{
						var end = text.IndexOf('\n', i);
						i = (end < 0) ? text.Length : end + 1;
					}
					else
						i++;
				}
				else
					break;
			}
			return combine;
		}

		// TokenReader::GetString: everything up to the next quote, in which "\n" stands for a line break and a carriage
		// return is an error. A string followed by '+' and another string continues in it.
		static Token ReadString(string text, ref int i)
		{
			var value = new StringBuilder();
			while (true)
			{
				var close = text.IndexOf('"', i + 1);
				if (close < 0)
				{
					i = text.Length;
					return new Token(TokenType.Error, value.ToString());
				}

				for (var c = i + 1; c < close; c++)
				{
					if (text[c] == '\r')
					{
						i = close + 1;
						return new Token(TokenType.Error, value.ToString());
					}

					if (text[c] != '\\')
						value.Append(text[c]);
					else
					{
						if (c + 1 < close && text[c + 1] == 'n')
							value.Append('\n');
						c++;
					}
				}

				i = close + 1;
				if (!SkipWhiteSpace(text, ref i) || i >= text.Length || text[i] != '"')
					return new Token(TokenType.String, value.ToString());
			}
		}

		// C's isdigit and isalpha, which TokenReader uses: ASCII only.
		static bool IsDigit(char c)
		{
			return c >= '0' && c <= '9';
		}

		static bool IsLetter(char c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
		}
	}
}
