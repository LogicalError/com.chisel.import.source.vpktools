using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Debug = UnityEngine.Debug;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;
using Matrix4x4 = UnityEngine.Matrix4x4;

namespace Chisel.Import.Source.VPKTools
{
	[System.Diagnostics.DebuggerDisplay("Name = {Name}, Value = {Value}")]
	public class EntityProperty
	{
		public string Name;
		public string Value;
	}

	[System.Diagnostics.DebuggerDisplay("Name = {Name}, Value = {Value}, Children = {Children?.Length??0}, LineNumber: {LineNumber}")]
	public class EntityHierarchyProperty : EntityProperty
	{
		public int						 LineNumber;
		public EntityHierarchyProperty[] Children;
	}

	public class TextParser
	{
		public EntityHierarchyProperty[] Properties;

		public static string LoadStreamAsString(Stream stream)
		{
			var bytes = new byte[stream.Length];
			stream.Read(bytes, 0, (int)stream.Length);
			return Encoding.ASCII.GetString(bytes);
		}

		/// <summary>
		/// A colour parameter of a VMT, read the way the material system reads a vector parameter: "[r g b]"
		/// as written, "{r g b}" divided down from 0..255 (<see cref="ParseDoubles"/>), and a single number
		/// as a grey. Nothing else is done to it - whether Source then takes the value out of gamma space
		/// differs from one parameter to the next, so that is left to whoever applies it.
		/// </summary>
		/// <remarks>
		/// This used to be a VMF light parser - every component divided by 255 and raised to 2.2, a fourth
		/// read as a brightness - which turned "[1 1 1]" into 0.000005, and with it the $phongtint, $color
		/// and $selfillumtint of hundreds of materials into black.
		/// </remarks>
		public static RGBColor ParseColor(string stringValue)
		{
			var values = ParseDoubles(stringValue);
			if (values == null || values.Length == 0)
				return null;

			// A single number is a scalar parameter, and the material system hands a scalar to a colour as
			// a grey; a vector shorter than three leaves the rest at zero.
			if (values.Length == 1)
				return new RGBColor { r = (float)values[0], g = (float)values[0], b = (float)values[0] };
			return new RGBColor
			{
				r = (float)values[0],
				g = (float)values[1],
				b = (values.Length > 2) ? (float)values[2] : 0
			};
		}

		public static bool SafeParse(string input, out float output)
		{
			output = 0;
			input = input.Trim();
			if (string.IsNullOrWhiteSpace(input))
				return false;
			input = input.Replace("..", ".").Replace(',', '.');
			if (input.Length > 1 && input[0] == '-' && input[1] == '.')
			{
				input = "-0" + input.Substring(1);
			} else
			{
				if (input[0] == '.')
				{
					input = "0" + input;
				}
			}
			return Single.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out output);
		}

		public static bool SafeParse(string input, out double output)
		{
			output = 0;
			input = input.Trim();
			if (string.IsNullOrWhiteSpace(input))
				return false;
			input = input.Replace("..", ".").Replace(',', '.');
			if (input.Length > 1 && input[0] == '-' && input[1] == '.')
			{
				input = "-0" + input.Substring(1);
			} else
			{
				if (input[0] == '.')
				{
					input = "0" + input;
				}
			}
			return Double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out output);
		}

		public static Vector3 ParseVector3(string input)
		{
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return Vector3.zero;
			}
			input = input.Trim();
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return Vector3.zero;
			}

			if (input.StartsWith("["))
			{
				if (input.EndsWith("]"))
					input = input.Substring(1, input.Length - 2);
				else
					input = input.Substring(1);
			}
			var tokens = input.Split(new [] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var values = new List<string>(tokens.Length);
			for (var i = 0; i < tokens.Length; i++)
			{
				var token = tokens[i];
				// A small number of shipped Black Mesa materials contain a decimal point separated from its
				// digits (for example creosotebush.vmt's "[.125 -.5 . 5]"). Treat it as the obvious .5;
				// rejecting the whole vector otherwise floods a map import with warnings for valid game data.
				if (token == "." && i + 1 < tokens.Length &&
					Double.TryParse(tokens[i + 1].TrimEnd(']'), NumberStyles.Integer,
									CultureInfo.InvariantCulture, out _))
				{
					token += tokens[++i];
				}
				values.Add(token);
			}
			var result = Vector3.zero;
			if (values.Count != 3)
			{
				Debug.LogWarning($"Unexpected value found {input}");
				if (values.Count == 0)
					return result;
				if (values.Count == 1)
				{
					if (!SafeParse(values[0], out result.x))
						return result;
					return result;
				}
				if (values.Count == 2)
				{
					if (!SafeParse(values[0], out result.x) ||
						!SafeParse(values[1], out result.y))
						return result;
					return result;
				}
			}

			if (!SafeParse(values[0], out result.x) ||
				!SafeParse(values[1], out result.y) ||
				!SafeParse(values[2], out result.z))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return result;
			}

			return result;
		}

		/// <summary>
		/// A texture transform parameter ($basetexturetransform and its kin), in the two forms the material system
		/// accepts: the sixteen numbers of the matrix in brackets, row by row, or "center x y scale x y rotate
		/// degrees translate x y". The second form is composed as the TextureTransform proxy composes it: move the
		/// center to the origin, scale, rotate counter-clockwise, move the center back, translate. A key left out
		/// keeps its default (center .5 .5, scale 1 1, rotate 0, translate 0 0), and a token that is not a number
		/// (a proxy variable such as $angle) leaves that value at its default too.
		/// </summary>
		/// <remarks>
		/// The Valve Developer Wiki documents the keys, their defaults and that the rotation is counter-clockwise
		/// in degrees; the order the four are combined in is the proxy's and is not spelled out in public engine
		/// source for the string form.
		/// </remarks>
		public static Matrix4x4? ParseTextureTransform(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
				return null;
			input = input.Trim();
			if (input[0] == '[')
			{
				var values = ParseDoubles(input);
				if (values == null || values.Length < 16)
					return null;
				var matrix = new Matrix4x4();
				for (int row = 0; row < 4; row++)
					for (int column = 0; column < 4; column++)
						matrix[row, column] = (float)values[row * 4 + column];
				return matrix;
			}

			var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var center = new Vector2(0.5f, 0.5f);
			var scale = Vector2.one;
			var translate = Vector2.zero;
			var rotate = 0.0f;
			var any = false;
			for (int i = 0; i < tokens.Length; i++)
			{
				switch (tokens[i].ToLowerInvariant())
				{
					case "center":    any = true; ReadPair(tokens, ref i, ref center); break;
					case "scale":     any = true; ReadPair(tokens, ref i, ref scale); break;
					case "translate": any = true; ReadPair(tokens, ref i, ref translate); break;
					case "rotate":
					{
						any = true;
						if (i + 1 < tokens.Length && SafeParse(tokens[i + 1], out float angle))
						{
							rotate = angle;
							i++;
						}
						break;
					}
				}
			}
			if (!any)
				return null;

			var toOrigin = Matrix4x4.Translate(new Vector3(-center.x, -center.y, 0));
			var scaled   = Matrix4x4.Scale(new Vector3(scale.x, scale.y, 1));
			// MatrixBuildRotateZ: x' = cos x - sin y, y' = sin x + cos y.
			var radians  = rotate * (float)(Math.PI / 180.0);
			var rotated  = Matrix4x4.identity;
			rotated.m00 = (float)Math.Cos(radians); rotated.m01 = -(float)Math.Sin(radians);
			rotated.m10 = (float)Math.Sin(radians); rotated.m11 = (float)Math.Cos(radians);
			var back     = Matrix4x4.Translate(new Vector3(center.x, center.y, 0));
			var moved    = Matrix4x4.Translate(new Vector3(translate.x, translate.y, 0));
			return moved * back * rotated * scaled * toOrigin;
		}

		/// <summary>Exactly the identity, as the material system's matrix test asks (VMatrix::IsIdentity).</summary>
		public static bool IsIdentity(Matrix4x4 matrix)
		{
			for (int row = 0; row < 4; row++)
				for (int column = 0; column < 4; column++)
					if (matrix[row, column] != (row == column ? 1.0f : 0.0f))
						return false;
			return true;
		}

		static void ReadPair(string[] tokens, ref int index, ref Vector2 value)
		{
			if (index + 2 < tokens.Length && SafeParse(tokens[index + 1], out float x) && SafeParse(tokens[index + 2], out float y))
			{
				value = new Vector2(x, y);
				index += 2;
			}
		}

		public static Vector4 ParseVector4(string input)
		{
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return Vector4.zero;
			}
			input = input.Trim();
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return Vector4.zero;
			}

			if (input.StartsWith("[") &&
				input.EndsWith("]"))
			{
				input = input.Substring(1, input.Length - 2);
			}
			var values = input.Split(new [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (values.Length != 4)
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return Vector4.zero;
			}

			Vector4 result;
			if (!SafeParse(values[0], out result.x) ||
				!SafeParse(values[1], out result.y) ||
				!SafeParse(values[2], out result.z) ||
				!SafeParse(values[3], out result.w))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return Vector4.zero;
			}

			return result;
		}

		public static int[] ParseInts(string input)
		{
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			input = input.Trim();
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			if (input.StartsWith("[") &&
				input.EndsWith("]"))
			{
				input = input.Substring(1, input.Length - 2);
			}
			var values = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			var result = new int[values.Length];
			for (var i = 0; i < values.Length; i++)
			{
				if (int.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
					continue;

				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			return result;
		}

		public static float[] ParseFloats(string input)
		{
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			input = input.Trim();
			if (string.IsNullOrEmpty(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			if (input.StartsWith("[") &&
				input.EndsWith("]"))
			{
				input = input.Substring(1, input.Length - 2);
			}
			var values = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			var result = new float[values.Length];
			for (var i = 0; i < values.Length; i++)
			{
				if (SafeParse(values[i], out result[i]))
					continue;

				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			return result;
		}

		/// <summary>
		/// The numbers of a VMT vector: "[x y z]" taken as written, "{r g b}" in 0..255 and divided down, and
		/// a bare list as written. The closing bracket is not required, and reading stops at the first token
		/// that is not a number - the files hold values like "{68.0 53.0 59.0 $model 1", where a missing quote
		/// swallowed the next key, and the numbers in front of it still count.
		/// </summary>
		public static double[] ParseDoubles(string input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				Debug.LogWarning($"Unexpected value found {input}");
				return null;
			}
			input = input.Trim();

			var isBytes = input[0] == '{';
			if (input[0] == '[' || input[0] == '{')
				input = input.Substring(1);

			var result = new List<double>();
			foreach (var token in input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var number = token.TrimEnd(']', '}');
				if (number.Length == 0 || !SafeParse(number, out double value))
					break;
				result.Add(isBytes ? value / 255.0 : value);
				if (number.Length != token.Length)
					break;
			}

			if (result.Count == 0)
			{
				Debug.LogWarning($"Unexpected value found '{input}' for doubles");
				return null;
			}
			return result.ToArray();
		}

		public static Vector3[] ParsePoints(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return null;
			}
			value = value.Trim();
			if (string.IsNullOrEmpty(value))
			{
				return null;
			}
			if (!value.StartsWith("("))
			{
				return null;
			}
			value = value.Substring(1);

			var results = new List<Vector3>();
			var points = value.Split('(');
			for (var i = 0; i < points.Length; i++)
			{
				var point = points[i].Trim();
				if (!point.EndsWith(")"))
				{
					return null;
				}
				point = point.Substring(0, point.Length - 1);
				var pointValues = point.Split(new [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (pointValues.Length != 3)
				{
					return null;
				}
				Vector3 v;
				if (!SafeParse(pointValues[0], out v.x) ||
					!SafeParse(pointValues[1], out v.y) ||
					!SafeParse(pointValues[2], out v.z))
				{
					return null;
				}
				results.Add(v);
			}
			return results.ToArray();
		}

		public static TextParser ParseString(string text)
		{
			if (string.IsNullOrEmpty(text))
				return null;

			text = text.Replace('\r', ' ');
			var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			bool inComment = false;
			int inCommentStart = 0;
			for (var i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				bool inString = false;
				if (inComment)
				{
					inCommentStart = 0;
				}
				for (int j = 0; j < line.Length - 1; j++)
				{
					if (inComment)
					{
						if (line[j] == '*' && line[j + 1] == '/')
						{
							inComment = false;
							line = line.Remove(inCommentStart, ((j+2) - inCommentStart));
						}
					} else
					{
						if (line[j] == '"')
						{
							inString = !inString;
							continue;
						}
						if (inString)
							continue;
						if ((line[j] == '/' && line[j+1] == '/') ||
							(line[j] == '\\' && line[j + 1] == '\\'))
						{
							line = line.Remove(j);
							break;
						}
						if (line[j] == '/' && line[j + 1] == '*')
						{
							inComment = true;
							inCommentStart = j;
						}
					}
				}

				if (inComment)
				{
					line = line.Remove(inCommentStart, (line.Length - inCommentStart));
				}
				line = line.Trim();
				lines[i] = line;
			}
			
			return Parse(lines);
		}

		public static TextParser LoadTextFile(string path)
		{
			try
			{
				return ParseString(File.ReadAllText(path));
			}
			catch
			{
				Debug.Log("Exception while parsing the file: " + path);
				throw;
			}
		}

		private static int SkipWhitespaceLines(string[] lines, int index)
		{
			while (index < lines.Length &&
					string.IsNullOrEmpty(lines[index]))
			{
				index++;
			}
			return index;
		}

		public static string AddLineNumbers(string text)
		{
			var lines = text.Split('\n');
			var newString = new StringBuilder();
			for (int i = 0; i < lines.Length; i++)
			{
				newString.AppendLine($"{i}: {lines[i].Replace("\r", "")}");
			}
			return newString.ToString();
		}

		private List<EntityHierarchyProperty> ParseChildren(string[] lines, ref int index)
		{
			while (index < lines.Length && lines[index].Length == 0)
				index++;
			var foundChildren = new List<EntityHierarchyProperty>();
			if (index >= lines.Length)
				return foundChildren;

			if (lines[index][0] != '{')
			{
				throw new Exception($"Unexpected syntax found at line {index}.\n'{lines[index]}'");
			}

			lines[index] = lines[index].Substring(1);
			while (index < lines.Length && lines[index].Length == 0)
				index++;
			if (index >= lines.Length)
				return foundChildren;

			index = SkipWhitespaceLines(lines, index);
				
			while (index < lines.Length)
			{
				if (string.IsNullOrEmpty(lines[index]))
				{
					index++;
					continue;
				}
				if (lines[index] == "}")
				{
					index++;
					return foundChildren;
				}

				var property = ParseProperty(lines, ref index);
				if (property != null)
					foundChildren.Add(property);
			}
			return foundChildren;
        }

		private static string[] SplitValues(string line)
		{
			var items = new List<string>();

			var offset = 0;
			while (offset < line.Length)
			{
				if (char.IsWhiteSpace(line[offset]))
				{
					offset++;
				} else
				if (line[offset] == '"')
				{
					var final = offset + 1;
					while (final < line.Length && line[final] != '"') final++;
					items.Add(line.Substring(offset + 1, final - offset - 1));
                    offset = final + 1;
				} else
				{
					int final = offset + 1;
					while (final < line.Length && !char.IsWhiteSpace(line[final]) && line[final] != '"') final++;
					items.Add(line.Substring(offset, final - offset));
					offset = final + 1;
				}
			}

			for (var i = items.Count - 1; i >= 0; i--)
			{
				items[i] = items[i].Trim();
				if (string.IsNullOrEmpty(items[i]) || items[i] == "\"")
					items.RemoveAt(i);
			}

			return items.ToArray();
		}

		private EntityHierarchyProperty ParseProperty(string[] lines, ref int index)
		{
			if (index >= lines.Length)
				return null;
			var property = new EntityHierarchyProperty();
			var lineText = lines[index];
			var items = SplitValues(lineText);

			property.LineNumber = index + 1;
				
			switch (items.Length)
			{
				case 1:
				{
					property.Name = items[0].Trim();
					index = SkipWhitespaceLines(lines, index + 1);
					break;
				}
				case 2:
				{
					property.Name = items[0].Trim();
					property.Value = items[1];
					index = SkipWhitespaceLines(lines, index + 1);
					break;
				}
				default:
				{
					property.Name = items[0].Trim();

					var value = items[1];
					for (var i = 2; i < items.Length; i++)
						value += " " + items[i];
					property.Value = value;
					index = SkipWhitespaceLines(lines, index + 1);
					break;
				}
				case 0:
                {
					throw new Exception($"Unexpected syntax found at line {index}.\n'{lineText}'");
				}
			}
			if (property.Name.Length == 0)
			{
				throw new Exception($"Unexpected empty token at line {index}.\n'{lineText}'");
			}
			if (property.Name[0] == '}')
			{
				throw new Exception($"Unexpected syntax found at line {index}.\n'{lineText}'");
			}
			if (index >= lines.Length || lines[index][0] != '{')
				return property;

			var children = ParseChildren(lines, ref index);
			if (children != null && children.Count >= 1)
				property.Children = children.ToArray();
			return property;
		}

		public static TextParser Parse(string[] lines)
		{
			var map		   = new TextParser();
			var index	   = SkipWhitespaceLines(lines, 0);
			var properties = new List<EntityHierarchyProperty>();

			while (index < lines.Length)
			{
                if (string.IsNullOrEmpty(lines[index]))
				{
					index++;
					continue;
				}
					
				var property = map.ParseProperty(lines, ref index);
				if (property != null)
					properties.Add(property);
            }
			map.Properties = properties.ToArray();
            return map;
		}
	}
}
