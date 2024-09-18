using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Debug = UnityEngine.Debug;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

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

		public static RGBColor ParseColor(string stringValue)
		{
			var values = ParseDoubles(stringValue);
			if (values == null || values.Length == 0)
				return null;

			if (values.Length == 8)
			{
				values[0] = values[4];
				values[1] = values[5];
				values[2] = values[6];
				values[3] = values[7];
				Array.Resize(ref values, 4);
			}

			for (int i = 0; i < values.Length; i++)
			{
				if (values[i] < 0.0f)
				{
					Debug.LogWarning($"Invalid light color '{stringValue}'");
					return null;
				}
			}

			
			switch (values.Length)
			{
				case 1:
				{
					values[0] = Math.Pow( values[0] / 255.0, 2.2);
					values = new[] { values[0], values[0], values[0] };
					break;
				}
				case 3:
				{
					values[0] = Math.Pow(values[0] / 255.0, 2.2);
					values[1] = Math.Pow(values[1] / 255.0, 2.2);
					values[2] = Math.Pow(values[2] / 255.0, 2.2);
					break;
				}
				case 4:
				{
					values[0] = Math.Pow(values[0] / 255.0, 2.2);
					values[1] = Math.Pow(values[1] / 255.0, 2.2);
					values[2] = Math.Pow(values[2] / 255.0, 2.2);

					var scale = values[3] / 255.0;
					values[0] *= scale;
					values[1] *= scale;
					values[2] *= scale;
					break;
				}

				default:
				{
					Debug.LogWarning($"Invalid light color '{stringValue}'");
					return null;
				}
			}

			var color = new RGBColor
			{
				r = (float)values[0],
				g = (float)values[1],
				b = (float)values[2]
			};
			return color;
		}

		public static bool SafeParse(string input, out float output)
		{
			output = 0;
			input = input.Trim();
			if (string.IsNullOrWhiteSpace(input))
				return false;
			input = input.Replace("..", ".").Replace(',', '.');
			if (input[0] == '-' && input[1] == '.')
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
			if (input[0] == '-' && input[1] == '.')
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
			var values = input.Split(new [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			var result = Vector3.zero;
			if (values.Length != 3)
			{
				Debug.LogWarning($"Unexpected value found {input}");
				if (values.Length == 0)
					return result;
				if (values.Length == 1)
				{
					if (!SafeParse(values[0], out result.x))
						return result;
					return result;
				}
				if (values.Length == 2)
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

		public static double[] ParseDoubles(string input)
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
			bool isBytes = false;
			if (input.StartsWith("[") &&
				(input.EndsWith("]") || input.EndsWith("}")))
			{
				input = input.Substring(1, input.Length - 2);
			} else
			if (input.StartsWith("{") &&
				(input.EndsWith("]") || input.EndsWith("}")))
			{
				input = input.Substring(1, input.Length - 2);
				isBytes = true;
			}
			var values = input.Split(new [] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			var result = new double[values.Length];
			for (var i = 0; i < values.Length; i++)
			{
				if (SafeParse(values[i], out result[i]))
				{
					if (isBytes)
					{
						result[i] /= 255.0;
					}
					continue;
				}

				Debug.LogWarning($"Unexpected value found '{input}' for doubles");
				return null;
			}
			return result;
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
