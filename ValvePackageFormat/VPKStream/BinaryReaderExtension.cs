using System.Collections.Generic;
using System.IO;

using Quaternion = UnityEngine.Quaternion;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;
using Bounds = UnityEngine.Bounds;

public static class BinaryReaderExtension
{
	public static Vector2 ReadVector2(this BinaryReader reader)
	{
		return new Vector2(reader.ReadSingle(),
						   reader.ReadSingle());
	}

	public static Vector3 ReadVector3(this BinaryReader reader)
	{
		return new Vector3(reader.ReadSingle(),
						   reader.ReadSingle(),
						   reader.ReadSingle());
	}

	public static Vector4 ReadVector4(this BinaryReader reader)
	{
		return new Vector4(reader.ReadSingle(),
						   reader.ReadSingle(),
						   reader.ReadSingle(),
						   reader.ReadSingle());
	}

	public static Quaternion ReadQuaternion(this BinaryReader reader)
	{
		return new Quaternion(reader.ReadSingle(),
							  reader.ReadSingle(),
							  reader.ReadSingle(),
							  reader.ReadSingle());
	}

	public static Matrix4x4 ReadMatrix3x4(this BinaryReader reader)
	{
		var result = Matrix4x4.identity;
		result.SetColumn(0, ReadVector4(reader));
		result.SetColumn(1, ReadVector4(reader));
		result.SetColumn(2, ReadVector4(reader));
		return result;
	}

	public static Bounds ReadBounds(this BinaryReader reader)
	{
		var min = ReadVector3(reader);
		var max = ReadVector3(reader);
		return new Bounds((max + min) * 0.5f, (max - min) * 0.5f);
	}

	public static string ReadNullString(this BinaryReader reader)
	{
		var bytes = new List<byte>();
		byte readByte;
		do
		{
			if (!reader.BaseStream.CanRead)
				break;
			readByte = reader.ReadByte();
			if (readByte == 0)
				break;
			bytes.Add(readByte);
		} while (readByte != 0);
		return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
	}
	
	public static string ReadNullString(this BinaryReader reader, long offset)
	{
		if (offset <= 0 || offset > reader.BaseStream.Length)
			return string.Empty;

		var startPosition = reader.BaseStream.Position;
		reader.BaseStream.Seek(offset, SeekOrigin.Begin);
		var bytes = new List<byte>();
		byte readByte;
		do
		{
			if (!reader.BaseStream.CanRead)
				break;
			readByte = reader.ReadByte();
			if (readByte == 0)
				break;
			bytes.Add(readByte);
		} while (readByte != 0);
		var result = System.Text.Encoding.ASCII.GetString(bytes.ToArray());

		reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		return result;
	}

	public static string ReadStringWithLength(this BinaryReader reader, int count)
	{
		if (count <= 0)
			return string.Empty;

		var bytes = reader.ReadBytes(count);
		var length = 0;
		while (length < bytes.Length && bytes[length] != 0) length++;
		return System.Text.Encoding.ASCII.GetString(bytes, 0, length);
	}

	public static string ReadStringWithLength(this BinaryReader reader, long offset, int count)
	{
		if (count <= 0 || offset <= 0 || offset > reader.BaseStream.Length)
			return string.Empty;

		var startPosition = reader.BaseStream.Position;
		reader.BaseStream.Seek(offset, SeekOrigin.Begin);
		var bytes = reader.ReadBytes(count);
		var length = 0;
		while (length < bytes.Length && bytes[length] != 0) length++;
		var result = System.Text.Encoding.ASCII.GetString(bytes, 0, length);

		reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		return result;
	}

	public static ushort[] ReadUShorts(this BinaryReader reader, int count, long offset = 0)
	{
		var startPosition = (offset == 0) ? 0 : reader.BaseStream.Position;
		if (startPosition > 0)
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

		var result = new ushort[count];
		for (var i = 0; i < count; i++)
			result[i] = reader.ReadUInt16();

		if (startPosition > 0)
			reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		return result;
	}

	public static int[] ReadInts(this BinaryReader reader, int count, long offset = 0)
	{
		var startPosition = (offset == 0) ? 0 : reader.BaseStream.Position;
		if (startPosition > 0)
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

		var result = new int[count];
		for (var i = 0; i < count; i++)
			result[i] = reader.ReadInt32();

		if (startPosition > 0)
			reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		return result;
	}

	public static float[] ReadFloats(this BinaryReader reader, int count, long offset = 0)
	{
		var startPosition = (offset == 0) ? 0 : reader.BaseStream.Position;
		if (startPosition > 0)
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

		var result = new float[count];
		for (var i = 0; i < count; i++)
			result[i] = reader.ReadSingle();

		if (startPosition > 0)
			reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		return result;
	}

	public static short[][] ReadInt16Table(this BinaryReader reader, int count1, int count2, int offset)
	{
		if (offset == 0)
			return null;

		var startPosition = reader.BaseStream.Position;

		reader.BaseStream.Seek(offset, SeekOrigin.Begin);
		var table = new short[count1][];
		for (int i = 0; i < count1; i++)
		{
			var subtable = new short[count2];
			for (int j = 0; j < count2; j++)
			{
				subtable[j] = reader.ReadInt16();
			}
			table[i] = subtable;
		}

		reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);

		return table;
	}

	public static Vector4[] ReadVector4Array(this BinaryReader reader, int count, int offset)
	{
		if (offset == 0)
			return null;

		var startPosition = reader.BaseStream.Position;

		reader.BaseStream.Seek(offset, SeekOrigin.Begin);

		var vectors = new Vector4[count];
		for (var i = 0; i < count; i++)
			vectors[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

		reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		return vectors;
	}
}