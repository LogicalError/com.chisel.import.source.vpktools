/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.Helpers.StreamHelpers.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using Profiler = UnityEngine.Profiling.Profiler;
using Debug = UnityEngine.Debug;

namespace Chisel.Import.Source.VPKTools.Helpers
{
    public static class StreamHelpers
	{
		public static byte ReadValueByte(this Stream stream)
		{
			return (byte)stream.ReadByte();
		}

		public static void WriteValueByte(this Stream stream, byte value)
        {
            stream.WriteByte(value);
        }

        public static bool ReadValueBool(this Stream stream)
        {
            return stream.ReadByte() > 0;
        }

        public static void WriteValueBool(this Stream stream, bool value)
        {
            stream.WriteValueByte((byte)(value ? 1 : 0));
        }

        internal static bool ShouldSwap(bool littleEndian)
        {
            if (littleEndian && !BitConverter.IsLittleEndian)
            {
                return true;
            }
            if (!littleEndian && BitConverter.IsLittleEndian)
            {
                return true;
            }
            return false;
        }

        public static MemoryStream ReadToMemoryStream(this Stream stream, long size)
        {
            MemoryStream memoryStream = new MemoryStream();
            long num = size;
            byte[] buffer = new byte[4096];
            while (num > 0)
            {
                int num2 = (int)Math.Min(num, 4096L);
                stream.Read(buffer, 0, num2);
                memoryStream.Write(buffer, 0, num2);
                num -= num2;
            }
            memoryStream.Seek(0L, SeekOrigin.Begin);
            return memoryStream;
        }

        public static void WriteFromStream(this Stream stream, Stream input, long size)
        {
            long num = size;
            byte[] buffer = new byte[4096];
            while (num > 0)
            {
                int num2 = (int)Math.Min(num, 4096L);
                input.Read(buffer, 0, num2);
                stream.Write(buffer, 0, num2);
                num -= num2;
            }
        }

        public static string ReadUTF16String(this Stream stream, uint size)
        {
            return stream.ReadUTF16String(size, littleEndian: true);
        }

        public static string ReadUTF16String(this Stream stream, uint size, bool littleEndian)
        {
            byte[] array = new byte[size];
            stream.Read(array, 0, array.Length);
            if (littleEndian)
            {
                return Encoding.Unicode.GetString(array);
            }
            return Encoding.BigEndianUnicode.GetString(array);
        }

        public static string ReadUTF16NullString(this Stream stream)
        {
            return stream.ReadUTF16NullString(littleEndian: true);
        }

        public static string ReadUTF16NullString(this Stream stream, bool littleEndian)
        {
            int num = 0;
            byte[] array = new byte[128];
            while (true)
            {
                stream.Read(array, num, 2);
                if (BitConverter.ToUInt16(array, num) == 0)
                {
                    break;
                }
                if (num >= array.Length)
                {
                    if (array.Length >= 8192)
                    {
                        throw new InvalidOperationException();
                    }
                    Array.Resize(ref array, array.Length + 128);
                }
                num += 2;
            }
            if (num == 0)
            {
                return "";
            }
            if (littleEndian)
            {
                return Encoding.Unicode.GetString(array, 0, num);
            }
            return Encoding.BigEndianUnicode.GetString(array, 0, num);
        }

        public static void WriteUTF16String(this Stream stream, string value)
        {
            stream.WriteUTF16String(value, littleEndian: true);
        }

        public static void WriteUTF16String(this Stream stream, string value, bool littleEndian)
        {
            byte[] array = (!littleEndian) ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.Unicode.GetBytes(value);
            stream.Write(array, 0, array.Length);
        }

        public static void WriteUTF16NullString(this Stream stream, string value)
        {
            stream.WriteUTF16NullString(value, littleEndian: true);
        }

        public static void WriteUTF16NullString(this Stream stream, string value, bool littleEndian)
        {
            byte[] array = (!littleEndian) ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.Unicode.GetBytes(value);
            stream.Write(array, 0, array.Length);
            stream.WriteValueUInt16(0);
        }

        public static ushort ReadValueUInt16(this Stream stream)
        {
            return stream.ReadValueUInt16(littleEndian: true);
        }

        public static ushort ReadValueUInt16(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[2];
            Debug.Assert(stream.Read(array, 0, 2) == 2);
            ushort num = BitConverter.ToUInt16(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap();
            }
            return num;
        }

        public static void WriteValueUInt16(this Stream stream, ushort value)
        {
            stream.WriteValueUInt16(value, littleEndian: true);
        }

        public static void WriteValueUInt16(this Stream stream, ushort value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap();
            }
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 2);
            stream.Write(bytes, 0, 2);
        }

        public static short ReadValueInt16(this Stream stream)
        {
            return stream.ReadValueInt16(littleEndian: true);
        }

        public static short ReadValueInt16(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[2];
            Debug.Assert(stream.Read(array, 0, 2) == 2);
            short num = BitConverter.ToInt16(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap();
            }
            return num;
        }

        public static void WriteValueInt16(this Stream stream, short value)
        {
            stream.WriteValueInt16(value, littleEndian: true);
        }

        public static void WriteValueInt16(this Stream stream, short value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap();
            }
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 2);
            stream.Write(bytes, 0, 2);
        }

        public static int ReadAligned(this Stream stream, byte[] buffer, int offset, int size, int align)
        {
            if (size == 0)
            {
                return 0;
            }
            int result = stream.Read(buffer, offset, size);
            int num = size % align;
            if (num > 0)
            {
                stream.Seek(align - num, SeekOrigin.Current);
            }
            return result;
        }

        public static void WriteAligned(this Stream stream, byte[] buffer, int offset, int size, int align)
        {
            if (size != 0)
            {
                stream.Write(buffer, offset, size);
                int num = size % align;
                if (num > 0)
                {
                    byte[] buffer2 = new byte[align - num];
                    stream.Write(buffer2, 0, align - num);
                }
            }
        }

        public static uint ReadValueUInt24(this Stream stream)
        {
            return stream.ReadValueUInt24(littleEndian: true);
        }

        public static uint ReadValueUInt24(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[4];
            Debug.Assert(stream.Read(array, 0, 3) == 3);
            uint num = BitConverter.ToUInt32(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap24();
            }
            return num & 0xFFFFFF;
        }

        public static void WriteValueUInt24(this Stream stream, uint value)
        {
            stream.WriteValueUInt24(value, littleEndian: true);
        }

        public static void WriteValueUInt24(this Stream stream, uint value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap24();
            }
            value &= 0xFFFFFF;
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 4);
            stream.Write(bytes, 0, 3);
        }

        public static string ReadASCIIString(this Stream stream, uint size, bool trailingNull)
        {
            byte[] array = new byte[size];
            stream.Read(array, 0, array.Length);
            int num = array.Length;
            if (trailingNull)
            {
                while (num > 0 && array[num - 1] == 0)
                {
                    num--;
                }
            }
            return Encoding.ASCII.GetString(array, 0, num);
        }

        public static string ReadASCIIString(this Stream stream, uint size)
        {
            return stream.ReadASCIIString(size, trailingNull: false);
        }

        public static string ReadASCIINullString(this Stream stream)
		{
			Profiler.BeginSample("ReadStringASCIIZ");
			int num = 0;
            byte[] array = ArrayPool<byte>.Shared.Rent(64);
            try
            {
                while (true)
                {
                    if (num >= array.Length)
					{
                        var nextArray = ArrayPool<byte>.Shared.Rent(array.Length + 64);
                        array.CopyTo(nextArray, 0);
                        ArrayPool<byte>.Shared.Return(array);
						array = nextArray;
                    }
                    stream.Read(array, num, 1);
                    if (array[num] == 0)
                    {
                        break;
                    }
                    num++;
                }
                if (num == 0)
                {
                    return "";
                }
                return Encoding.ASCII.GetString(array, 0, num);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
				Profiler.EndSample();
			}
        }

        public static void WriteASCIIString(this Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        public static void WriteASCIINullString(this Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte(0);
        }

        public static float ReadValueSingle(this Stream stream)
        {
            return stream.ReadValueSingle(littleEndian: true);
        }

        public static float ReadValueSingle(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[4];
            Debug.Assert(stream.Read(array, 0, 4) == 4);
            if (ShouldSwap(littleEndian))
            {
                return BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(array, 0).Swap()), 0);
            }
            return BitConverter.ToSingle(array, 0);
        }

        public static void WriteValueSingle(this Stream stream, float value)
        {
            stream.WriteValueSingle(value, littleEndian: true);
        }

        public static void WriteValueSingle(this Stream stream, float value, bool littleEndian)
        {
            byte[] array = (!ShouldSwap(littleEndian)) ? BitConverter.GetBytes(value) : BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(value), 0).Swap());
            Debug.Assert(array.Length == 4);
            stream.Write(array, 0, 4);
        }

        public static T ReadStruct<T>(this Stream stream)
        {
            int num = Marshal.SizeOf(typeof(T));
            byte[] array = new byte[num];
            if (stream.Read(array, 0, num) != num)
            {
                throw new InvalidOperationException("could not read all of data for structure");
            }
            GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            T result = (T)Marshal.PtrToStructure(gCHandle.AddrOfPinnedObject(), typeof(T));
            gCHandle.Free();
            return result;
        }

        public static T ReadStruct<T>(this Stream stream, int size)
        {
            int num = Marshal.SizeOf(typeof(T));
            if (size > num)
            {
                throw new InvalidOperationException("read size cannot be greater than structure size");
            }
            byte[] array = new byte[num];
            if (stream.Read(array, 0, size) != size)
            {
                throw new InvalidOperationException("could not read all of data for structure");
            }
            GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            T result = (T)Marshal.PtrToStructure(gCHandle.AddrOfPinnedObject(), typeof(T));
            gCHandle.Free();
            return result;
        }

        public static void WriteStruct<T>(this Stream stream, T structure)
        {
            int num = Marshal.SizeOf(typeof(T));
            byte[] array = new byte[num];
            GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            Marshal.StructureToPtr(structure, gCHandle.AddrOfPinnedObject(), fDeleteOld: false);
            gCHandle.Free();
            stream.Write(array, 0, array.Length);
        }

        public static double ReadValueDouble(this Stream stream)
        {
            return stream.ReadValueDouble(littleEndian: true);
        }

        public static double ReadValueDouble(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[8];
            Debug.Assert(stream.Read(array, 0, 8) == 8);
            if (ShouldSwap(littleEndian))
            {
                return BitConverter.Int64BitsToDouble(BitConverter.ToInt64(array, 0).Swap());
            }
            return BitConverter.ToDouble(array, 0);
        }

        public static void WriteValueDouble(this Stream stream, double value)
        {
            stream.WriteValueDouble(value, littleEndian: true);
        }

        public static void WriteValueDouble(this Stream stream, double value, bool littleEndian)
        {
            byte[] array = (!ShouldSwap(littleEndian)) ? BitConverter.GetBytes(value) : BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(value).Swap());
            Debug.Assert(array.Length == 8);
            stream.Write(array, 0, 8);
        }

        public static uint ReadValueUInt32(this Stream stream)
        {
            return stream.ReadValueUInt32(littleEndian: true);
        }

        public static uint ReadValueUInt32(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[4];
            Debug.Assert(stream.Read(array, 0, 4) == 4);
            uint num = BitConverter.ToUInt32(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap();
            }
            return num;
        }

        public static void WriteValueUInt32(this Stream stream, uint value)
        {
            stream.WriteValueUInt32(value, littleEndian: true);
        }

        public static void WriteValueUInt32(this Stream stream, uint value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap();
            }
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 4);
            stream.Write(bytes, 0, 4);
        }

        public static ulong ReadValueUInt64(this Stream stream)
        {
            return stream.ReadValueUInt64(littleEndian: true);
        }

        public static ulong ReadValueUInt64(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[8];
            Debug.Assert(stream.Read(array, 0, 8) == 8);
            ulong num = BitConverter.ToUInt64(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap();
            }
            return num;
        }

        public static void WriteValueUInt64(this Stream stream, ulong value)
        {
            stream.WriteValueUInt64(value, littleEndian: true);
        }

        public static void WriteValueUInt64(this Stream stream, ulong value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap();
            }
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 8);
            stream.Write(bytes, 0, 8);
        }

        public static int ReadValueInt32(this Stream stream)
        {
            return stream.ReadValueInt32(littleEndian: true);
        }

        public static int ReadValueInt32(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[4];
            Debug.Assert(stream.Read(array, 0, 4) == 4);
            int num = BitConverter.ToInt32(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap();
            }
            return num;
        }

        public static void WriteValueInt32(this Stream stream, int value)
        {
            stream.WriteValueInt32(value, littleEndian: true);
        }

        public static void WriteValueInt32(this Stream stream, int value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap();
            }
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 4);
            stream.Write(bytes, 0, 4);
        }

        public static long ReadValueInt64(this Stream stream)
        {
            return stream.ReadValueInt64(littleEndian: true);
        }

        public static long ReadValueInt64(this Stream stream, bool littleEndian)
        {
            byte[] array = new byte[8];
            Debug.Assert(stream.Read(array, 0, 8) == 8);
            long num = BitConverter.ToInt64(array, 0);
            if (ShouldSwap(littleEndian))
            {
                num = num.Swap();
            }
            return num;
        }

        public static void WriteValueUInt64(this Stream stream, long value)
        {
            stream.WriteValueUInt64(value, littleEndian: true);
        }

        public static void WriteValueUInt64(this Stream stream, long value, bool littleEndian)
        {
            if (ShouldSwap(littleEndian))
            {
                value = value.Swap();
            }
            byte[] bytes = BitConverter.GetBytes(value);
            Debug.Assert(bytes.Length == 8);
            stream.Write(bytes, 0, 8);
        }

        public static string ReadUTF8String(this Stream stream, uint size)
        {
            byte[] array = new byte[size];
            stream.Read(array, 0, array.Length);
            return Encoding.UTF8.GetString(array);
        }

        public static string ReadUTF8NullString(this Stream stream)
        {
            int num = 0;
            byte[] array = new byte[64];
            while (true)
            {
                stream.Read(array, num, 1);
                if (array[num] == 0)
                {
                    break;
                }
                if (num >= array.Length)
                {
                    if (array.Length >= 4096)
                    {
                        throw new InvalidOperationException();
                    }
                    Array.Resize(ref array, array.Length + 64);
                }
                num++;
            }
            if (num == 0)
            {
                return "";
            }
            return Encoding.UTF8.GetString(array, 0, num);
        }

        public static void WriteUTF8String(this Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        public static void WriteUTF8NullString(this Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
            stream.WriteByte(0);
        }

        public static sbyte ReadValueSByte(this Stream stream)
        {
            return (sbyte)stream.ReadByte();
        }

        public static void WriteValueSByte(this Stream stream, sbyte value)
        {
            stream.WriteByte((byte)value);
        }
    }
}
