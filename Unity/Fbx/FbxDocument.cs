using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Chisel.Import.Source.VPKTools.Fbx
{
	/// <summary>
	/// The value of a single FBX property. FBX ASCII distinguishes quoted strings from bare
	/// identifiers and from numbers, and it writes large numeric runs in its own array syntax, so a
	/// plain <c>object</c> is not enough to know how to print one. The binary form types every value
	/// exactly - a 32 or 64 bit integer, a float or a double, a bool - so the kinds say that too, and
	/// the ASCII form prints each the way it always did.
	/// </summary>
	internal readonly struct FbxProperty
	{
		public enum Kind { Int, Long, Double, String, Raw, Bool, ObjectName, IntArray, DoubleArray, LongArray, FloatArray }

		public readonly Kind     PropertyKind;
		public readonly long     Integer;
		public readonly double   Number;
		/// <summary>A string's text; an object name's name.</summary>
		public readonly string   Text;
		/// <summary>An object name's class.</summary>
		public readonly string   Class;
		public readonly int[]    Integers;
		/// <summary>A double array's values; a float array's as the ASCII form prints them.</summary>
		public readonly double[] Numbers;
		public readonly long[]   Longs;
		/// <summary>A float array's values as the binary form stores them.</summary>
		public readonly float[]  Floats;

		FbxProperty(Kind kind, long integer = 0, double number = 0, string text = null, string @class = null, int[] integers = null,
					double[] numbers = null, long[] longs = null, float[] floats = null)
		{
			PropertyKind = kind;
			Integer      = integer;
			Number       = number;
			Text         = text;
			Class        = @class;
			Integers     = integers;
			Numbers      = numbers;
			Longs        = longs;
			Floats       = floats;
		}

		public static FbxProperty Int   (int      value) => new(Kind.Int, integer: value);
		public static FbxProperty Long  (long     value) => new(Kind.Long, integer: value);
		public static FbxProperty Double(double   value) => new(Kind.Double, number: value);
		public static FbxProperty String(string   value) => new(Kind.String, text: value);
		/// <summary>An unquoted token in the ASCII form; a string in the binary one.</summary>
		public static FbxProperty Raw   (string   value) => new(Kind.Raw, text: value);
		/// <summary>A bool: the ASCII form's <c>Y</c> or <c>N</c> (as in <c>Shading: Y</c>), the binary form's 'C'.</summary>
		public static FbxProperty Bool  (bool     value) => new(Kind.Bool, integer: value ? 1 : 0);
		/// <summary>
		/// An object's name with its class: <c>"Model::name"</c> in the ASCII form, <c>name\0\x01Model</c> in the
		/// binary one, which is how the FBX SDK writes and reads each.
		/// </summary>
		public static FbxProperty ObjectName(string @class, string name) => new(Kind.ObjectName, text: name ?? "", @class: @class);
		public static FbxProperty Array (int[]    value) => new(Kind.IntArray, integers: value);
		public static FbxProperty Array (double[] value) => new(Kind.DoubleArray, numbers: value);
		/// <summary>64 bit integers, such as an animation curve's key times: printed in the ASCII form as the doubles they
		/// were written as before the binary form (every one a whole number, so the same text).</summary>
		public static FbxProperty Array (long[]   value) => new(Kind.LongArray, longs: value);
		/// <summary>Floats, such as an animation curve's key values: printed in the ASCII form as these doubles, stored in the
		/// binary form as <paramref name="binary"/> (each double as a float when it is null).</summary>
		public static FbxProperty FloatArray(double[] value, float[] binary = null)
		{
			if (binary == null)
			{
				binary = new float[value.Length];
				for (int i = 0; i < value.Length; i++)
					binary[i] = (float)value[i];
			}
			return new(Kind.FloatArray, numbers: value, floats: binary);
		}

		/// <summary>Whether the ASCII form writes it as an array block, which ends its line.</summary>
		public bool IsArray => PropertyKind == Kind.IntArray || PropertyKind == Kind.DoubleArray ||
							   PropertyKind == Kind.LongArray || PropertyKind == Kind.FloatArray;
	}

	/// <summary>
	/// One node of an FBX document tree. An FBX file is nothing but nested nodes, each with a name,
	/// a list of properties and a list of children, so building the document as a tree and printing
	/// it once keeps the format's syntax in exactly one place.
	/// </summary>
	internal sealed class FbxNode
	{
		public readonly string             Name;
		public readonly List<FbxProperty>  Properties = new();
		public readonly List<FbxNode>      Children   = new();

		/// <summary>
		/// Forces <c>{ }</c> to be printed even with no children. A few nodes the FBX SDK reads are
		/// required to be blocks whether or not they have content (an empty <c>Properties70</c>, for
		/// one), and printing them as bare properties makes the file fail to parse.
		/// </summary>
		public bool AlwaysBlock;

		public FbxNode(string name) { Name = name; }

		public FbxNode Add(FbxNode child) { Children.Add(child); return child; }

		public FbxNode Add(string name, params FbxProperty[] properties)
		{
			var child = new FbxNode(name);
			child.Properties.AddRange(properties);
			Children.Add(child);
			return child;
		}

		public FbxNode Prop(params FbxProperty[] properties)
		{
			Properties.AddRange(properties);
			return this;
		}

		/// <summary>
		/// Adds one entry of an FBX "Properties70" block: name, type, subtype, flags, then values.
		/// </summary>
		public FbxNode P(string name, string type, string subType, string flags, params FbxProperty[] values)
		{
			var child = new FbxNode("P");
			child.Properties.Add(FbxProperty.String(name));
			child.Properties.Add(FbxProperty.String(type));
			child.Properties.Add(FbxProperty.String(subType));
			child.Properties.Add(FbxProperty.String(flags));
			child.Properties.AddRange(values);
			Children.Add(child);
			return this;
		}
	}

	/// <summary>
	/// Writes an FBX 7.4 document, in the format's binary form or its ASCII one (<see cref="BinaryEnabled"/>).
	/// <para>
	/// The FBX SDK - which is what Unity's model importer uses - reads both. The binary form is what a map's import
	/// writes: no number is formatted, and Unity reads it without parsing text. The ASCII form can be diffed and
	/// eyeballed, which matters for a format whose failure mode is a silently wrong mesh rather than an error: switch to
	/// it to look at what is written.
	/// </para>
	/// </summary>
	internal static class FbxDocument
	{
		/// <summary>
		/// The binary form. Off, files are written in the ASCII form, which is useful for debugging. Either is read the same;
		/// a file written one way is not written again the other way until its version stamp asks for it (or it is deleted).
		/// </summary>
		public static bool BinaryEnabled = true;
		/// <summary>How many array values to put on one line. Purely cosmetic; the parser does not care.</summary>
		const int kArrayValuesPerLine = 30;

		/// <summary>
		/// The text of each number written so far in the document being written, by its bits. Formatting the numbers is
		/// nearly all of the export, and a mesh's corners, a skin's weights and a take's keys repeat the same values over and
		/// over. A number's text is a function of its bits alone, so what comes out is what formatting each one would write
		/// (FbxDocumentNumberTests). Per document: a map's worth of models is never held. Off, every number is formatted: what
		/// the tests compare with.
		/// </summary>
		internal static bool NumberCacheEnabled = true;

		// One per thread: a document's text can be made on a worker thread (Bytes), several at once
		[ThreadStatic]
		static Dictionary<long, string> s_NumberText;

		public static void Write(string path, FbxNode root)
		{
			byte[] bytes;
			using (ImportSteps.Step("fbx: text"))
				bytes = Bytes(root);
			using (ImportSteps.Step("fbx: file"))
				File.WriteAllBytes(path, bytes);
		}

		/// <summary>
		/// The file <see cref="Write"/> writes, as its bytes. It reads nothing but the document, which nothing changes once it
		/// is made, so it can be made on a worker thread (FbxExporter.Export's unwritten).
		/// </summary>
		internal static byte[] Bytes(FbxNode root) => BinaryEnabled ? BinaryBytes(root) : AsciiBytes(root);

		/// <summary>The file in the ASCII form.</summary>
		internal static byte[] AsciiBytes(FbxNode root)
		{
			var builder = new StringBuilder(1 << 20);
			s_NumberText = NumberCacheEnabled ? new Dictionary<long, string>() : null;
			try
			{
				builder.Append("; FBX 7.4.0 project file\n");
				builder.Append("; Generated by the Chisel Source importer\n");
				builder.Append("; ----------------------------------------------------\n\n");

				foreach (var child in root.Children)
					WriteNode(builder, child, 0);
			}
			finally
			{
				s_NumberText = null;
			}

			// FBX is a Windows-born format and the SDK is happiest with \n; StringBuilder keeps
			// whatever we put in, so the bytes are its text's, with no TextWriter that
			// might translate line endings.
			return new UTF8Encoding(false).GetBytes(builder.ToString());
		}

		#region Binary form

		// What the FBX SDK writes around a binary file's records, as Blender's exporter (io_scene_fbx encode_bin.py) writes
		// it for files the SDK reads: the header, and a footer whose id goes with the fixed FileId and CreationTime below.
		static readonly byte[] kBinaryHead = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");
		static readonly byte[] kFileId = { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 };
		const string kCreationTime = "1970-01-01 10:00:00:000";
		static readonly byte[] kFooterId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
		static readonly byte[] kFooterMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };
		const uint kBinaryVersion = 7400;
		/// <summary>A record of zeros ends a list of nested records (13 bytes: 32 bit offsets, before version 7500)</summary>
		const int kNullRecordLength = 13;

		/// <summary>
		/// The file in the binary form (FBX 7.4: records with 32 bit offsets). Each property is written as the type its kind
		/// says; arrays are stored uncompressed.
		/// </summary>
		internal static byte[] BinaryBytes(FbxNode root)
		{
			using var stream = new MemoryStream(1 << 20);
			using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);
			writer.Write(kBinaryHead);
			writer.Write(kBinaryVersion);
			foreach (var child in root.Children)
			{
				WriteBinaryNode(writer, child);
				// The SDK's own files carry these right after the header extension; the footer id goes with them
				if (child.Name == "FBXHeaderExtension")
				{
					var fileId = new FbxNode("FileId");
					fileId.Properties.Add(FbxProperty.Raw(null));
					WriteBinaryRecord(writer, fileId, kFileId);
					WriteBinaryNode(writer, new FbxNode("CreationTime").Prop(FbxProperty.String(kCreationTime)));
					WriteBinaryNode(writer, new FbxNode("Creator").Prop(FbxProperty.String("Chisel Source importer")));
				}
			}
			writer.Write(new byte[kNullRecordLength]);

			writer.Write(kFooterId);
			writer.Write(new byte[4]);
			var offset = (int)stream.Position;
			var padding = ((offset + 15) & ~15) - offset;
			writer.Write(new byte[padding == 0 ? 16 : padding]);
			writer.Write(kBinaryVersion);
			writer.Write(new byte[120]);
			writer.Write(kFooterMagic);
			writer.Flush();
			return stream.ToArray();
		}

		static void WriteBinaryNode(BinaryWriter writer, FbxNode node) => WriteBinaryRecord(writer, node, null);

		// A record: where it ends, its property count, the properties' length, its name, the properties, then its nested
		// records and a null record - which a node without properties gets even with nothing nested (Blender's
		// encode_bin.py), as does one the ASCII form writes as a block regardless (AlwaysBlock)
		static void WriteBinaryRecord(BinaryWriter writer, FbxNode node, byte[] rawBytes)
		{
			var stream = writer.BaseStream;
			var start = stream.Position;
			writer.Write(0u);                                   // where it ends, written below
			writer.Write((uint)node.Properties.Count);
			writer.Write(0u);                                   // the properties' length, written below
			var name = Encoding.ASCII.GetBytes(node.Name);
			writer.Write((byte)name.Length);
			writer.Write(name);
			var propertiesStart = stream.Position;
			foreach (var property in node.Properties)
				WriteBinaryProperty(writer, property, rawBytes);
			var propertiesLength = stream.Position - propertiesStart;
			if (node.Children.Count > 0 || node.AlwaysBlock || node.Properties.Count == 0)
			{
				foreach (var child in node.Children)
					WriteBinaryNode(writer, child);
				writer.Write(new byte[kNullRecordLength]);
			}
			var end = stream.Position;
			stream.Position = start;
			writer.Write((uint)end);
			stream.Position = start + 8;
			writer.Write((uint)propertiesLength);
			stream.Position = end;
		}

		static void WriteBinaryProperty(BinaryWriter writer, FbxProperty property, byte[] rawBytes)
		{
			switch (property.PropertyKind)
			{
				case FbxProperty.Kind.Int:    writer.Write((byte)'I'); writer.Write((int)property.Integer); break;
				case FbxProperty.Kind.Long:   writer.Write((byte)'L'); writer.Write(property.Integer); break;
				case FbxProperty.Kind.Double: writer.Write((byte)'D'); writer.Write(Finite(property.Number)); break;
				case FbxProperty.Kind.Bool:   writer.Write((byte)'C'); writer.Write((byte)(property.Integer != 0 ? 1 : 0)); break;
				case FbxProperty.Kind.String:
				case FbxProperty.Kind.Raw when rawBytes == null:
					WriteBinaryString(writer, property.Text ?? "");
					break;
				case FbxProperty.Kind.Raw:
					writer.Write((byte)'R');
					writer.Write((uint)rawBytes.Length);
					writer.Write(rawBytes);
					break;
				case FbxProperty.Kind.ObjectName:
					// The binary form names an object the other way round: its name, then \0\x01, then its class
					WriteBinaryString(writer, (property.Text ?? "") + "\0\x01" + property.Class);
					break;
				case FbxProperty.Kind.IntArray:
					WriteBinaryArray(writer, 'i', property.Integers.Length, MemoryMarshal.AsBytes(property.Integers.AsSpan()));
					break;
				case FbxProperty.Kind.LongArray:
					WriteBinaryArray(writer, 'l', property.Longs.Length, MemoryMarshal.AsBytes(property.Longs.AsSpan()));
					break;
				case FbxProperty.Kind.DoubleArray:
				{
					var values = property.Numbers;
					double[] finite = null;
					for (int i = 0; i < values.Length; i++)
					{
						if (double.IsFinite(values[i]))
							continue;
						finite ??= (double[])values.Clone();
						finite[i] = Finite(values[i]);
					}
					WriteBinaryArray(writer, 'd', values.Length, MemoryMarshal.AsBytes((finite ?? values).AsSpan()));
					break;
				}
				case FbxProperty.Kind.FloatArray:
				{
					var values = property.Floats;
					float[] finite = null;
					for (int i = 0; i < values.Length; i++)
					{
						if (float.IsFinite(values[i]))
							continue;
						finite ??= (float[])values.Clone();
						finite[i] = float.IsNaN(values[i]) ? 0 : (values[i] > 0 ? float.MaxValue : float.MinValue);
					}
					WriteBinaryArray(writer, 'f', values.Length, MemoryMarshal.AsBytes((finite ?? values).AsSpan()));
					break;
				}
			}
		}

		static void WriteBinaryString(BinaryWriter writer, string text)
		{
			var bytes = Encoding.UTF8.GetBytes(text);
			writer.Write((byte)'S');
			writer.Write((uint)bytes.Length);
			writer.Write(bytes);
		}

		// An array: its type, its count, its encoding (0: stored as it is) and its length in bytes, then its values
		static void WriteBinaryArray(BinaryWriter writer, char type, int count, ReadOnlySpan<byte> bytes)
		{
			writer.Write((byte)type);
			writer.Write((uint)count);
			writer.Write(0u);
			writer.Write((uint)bytes.Length);
			writer.Write(bytes);
		}

		// As AppendNumber clamps them: a non-finite value makes the SDK give up on the whole file
		static double Finite(double value)
		{
			if (double.IsNaN(value))              return 0;
			if (double.IsPositiveInfinity(value)) return double.MaxValue;
			if (double.IsNegativeInfinity(value)) return double.MinValue;
			return value;
		}

		#endregion

		static void WriteNode(StringBuilder builder, FbxNode node, int depth)
		{
			Indent(builder, depth);
			builder.Append(node.Name);
			builder.Append(':');

			var arrayProperty = -1;
			for (int i = 0; i < node.Properties.Count; i++)
			{
				var property = node.Properties[i];
				if (property.IsArray)
				{
					// An array is a block of its own, so it has to be the last thing on the line.
					arrayProperty = i;
					break;
				}

				builder.Append(i == 0 ? " " : ", ");
				WriteScalar(builder, property);
			}

			if (arrayProperty >= 0)
			{
				WriteArray(builder, node.Properties[arrayProperty], depth);
				return;
			}

			if (node.Children.Count == 0 && !node.AlwaysBlock)
			{
				builder.Append('\n');
				return;
			}

			builder.Append(node.Properties.Count > 0 ? " {\n" : "  {\n");
			foreach (var child in node.Children)
				WriteNode(builder, child, depth + 1);
			Indent(builder, depth);
			builder.Append("}\n");
		}

		static void WriteArray(StringBuilder builder, FbxProperty property, int depth)
		{
			var count = property.PropertyKind switch
			{
				FbxProperty.Kind.IntArray  => property.Integers.Length,
				FbxProperty.Kind.LongArray => property.Longs.Length,
				_                          => property.Numbers.Length,
			};

			builder.Append(" *");
			AppendInteger(builder, count);
			builder.Append(" {\n");
			Indent(builder, depth + 1);
			builder.Append("a: ");

			for (int i = 0; i < count; i++)
			{
				if (i > 0)
				{
					builder.Append(',');
					if ((i % kArrayValuesPerLine) == 0)
					{
						builder.Append('\n');
						Indent(builder, depth + 2);
					}
				}
				if (property.PropertyKind == FbxProperty.Kind.IntArray)
					AppendInteger(builder, property.Integers[i]);
				else if (property.PropertyKind == FbxProperty.Kind.LongArray)
					AppendNumber(builder, property.Longs[i]);       // as the doubles they were written as
				else
					AppendNumber(builder, property.Numbers[i]);
			}

			builder.Append('\n');
			Indent(builder, depth);
			builder.Append("}\n");
		}

		static void WriteScalar(StringBuilder builder, FbxProperty property)
		{
			switch (property.PropertyKind)
			{
				case FbxProperty.Kind.Int:    AppendInteger(builder, property.Integer); break;
				case FbxProperty.Kind.Long:   AppendInteger(builder, property.Integer); break;
				case FbxProperty.Kind.Double: AppendNumber(builder, property.Number); break;
				case FbxProperty.Kind.Raw:    builder.Append(property.Text); break;
				case FbxProperty.Kind.Bool:   builder.Append(property.Integer != 0 ? 'Y' : 'N'); break;
				case FbxProperty.Kind.ObjectName:
					builder.Append('"');
					AppendEscaped(builder, property.Class);
					builder.Append("::");
					AppendEscaped(builder, property.Text);
					builder.Append('"');
					break;
				case FbxProperty.Kind.String:
					builder.Append('"');
					AppendEscaped(builder, property.Text);
					builder.Append('"');
					break;
			}
		}

		/// <summary>
		/// An integer's digits, as <c>ToString(CultureInfo.InvariantCulture)</c> writes them - a minus sign, then the digits -
		/// without a string for each (FbxDocumentNumberTests).
		/// </summary>
		internal static void AppendInteger(StringBuilder builder, long value)
		{
			if (value == long.MinValue)
			{
				builder.Append(value.ToString(CultureInfo.InvariantCulture));
				return;
			}
			if (value < 0)
			{
				builder.Append('-');
				value = -value;
			}
			Span<char> digits = stackalloc char[20];
			int count = 0;
			do
			{
				digits[count++] = (char)('0' + (int)(value % 10));
				value /= 10;
			}
			while (value != 0);
			while (count > 0)
				builder.Append(digits[--count]);
		}

		internal static void AppendNumber(StringBuilder builder, double value)
		{
			// Non-finite values are not representable in FBX and make the SDK give up on the whole
			// file, so they are clamped here rather than left to produce an unreadable model.
			if (double.IsNaN(value))              value = 0;
			else if (double.IsPositiveInfinity(value)) value = double.MaxValue;
			else if (double.IsNegativeInfinity(value)) value = double.MinValue;

			if (s_NumberText == null)
			{
				builder.Append(NumberText(value));
				return;
			}
			var bits = BitConverter.DoubleToInt64Bits(value);
			if (!s_NumberText.TryGetValue(bits, out var text))
				s_NumberText.Add(bits, text = NumberText(value));
			builder.Append(text);
		}

		// A finite number's text, as AppendNumber writes it
		static string NumberText(double value)
		{

			// "R" round-trips exactly, which keeps an exported-then-imported mesh bit-identical to
			// the one we built, so a round-trip test can compare for equality rather than guess at a
			// tolerance.
			//
			// Everything here originates as a float - Unity has no other kind of coordinate - so it
			// is written back as one whenever that is exact. Otherwise the widening shows through:
			// 0.3f becomes "0.30000001192092896", which is nineteen characters to say what six would,
			// on every number in the file. Animation curves are hundreds of thousands of numbers.
			var asFloat = (float)value;
			var text = (asFloat == value)
					   ? asFloat.ToString("R", CultureInfo.InvariantCulture)
					   : value.ToString("R", CultureInfo.InvariantCulture);

			// The FBX SDK reads "1" as an integer property; where a double is expected that is still
			// accepted, but exponent-free whole numbers read back more predictably with a decimal
			// point, and it costs nothing. A trailing fraction digit rather than a bare "." because
			// not every parser accepts the latter.
			if (text.IndexOf('.') < 0 && text.IndexOf('e') < 0 && text.IndexOf('E') < 0 &&
				text.IndexOf("Infinity", StringComparison.Ordinal) < 0 &&
				text.IndexOf("NaN", StringComparison.Ordinal) < 0)
				text += ".0";
			return text;
		}

		static void AppendEscaped(StringBuilder builder, string text)
		{
			if (string.IsNullOrEmpty(text))
				return;

			foreach (var character in text)
			{
				// FBX ASCII has no escape sequence for a double quote inside a string: the SDK's own
				// writer replaces it with &quot;. Newlines would break the line-oriented syntax
				// outright, so they go too.
				switch (character)
				{
					case '"':  builder.Append("&quot;"); break;
					case '\r': break;
					case '\n': builder.Append(' '); break;
					default:   builder.Append(character); break;
				}
			}
		}

		static void Indent(StringBuilder builder, int depth)
		{
			for (int i = 0; i < depth; i++)
				builder.Append('\t');
		}
	}
}
