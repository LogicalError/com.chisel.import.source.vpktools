/* * * * * * * * * * * * * * * * * * * * * *
Chisel.Import.Source.VPKTools.Helpers.ByteHelpers.cs

License:
Author: Daniel Cornelius

* * * * * * * * * * * * * * * * * * * * * */

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Chisel.Import.Source.VPKTools.Helpers
{
    public static class ByteHelpers
    {
        public static void Reset(this byte[] data, byte value)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = value;
            }
        }

        public static T ToStructure<T>(this byte[] data, int index)
        {
            int num = Marshal.SizeOf(typeof(T));
            if (index + num > data.Length)
            {
                throw new Exception("not enough data to fit the structure");
            }
            byte[] array = new byte[num];
            Array.Copy(data, index, array, 0, num);
            GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            T result = (T)Marshal.PtrToStructure(gCHandle.AddrOfPinnedObject(), typeof(T));
            gCHandle.Free();
            return result;
        }

        public static T ToStructure<T>(this byte[] data)
        {
            return data.ToStructure<T>(0);
        }

        public static string ToStringASCIIZ(this byte[] data, int offset)
        {
            int i;
            for (i = offset; i < data.Length && data[i] != 0; i++)
            {
            }
            if (i == offset)
            {
                return "";
            }
            return Encoding.ASCII.GetString(data, offset, i - offset);
        }

        public static string ToStringASCIIZ(this byte[] data, uint offset)
        {
            return data.ToStringASCIIZ((int)offset);
        }

        public static string ToStringUTF8Z(this byte[] data, int offset)
        {
            int i;
            for (i = offset; i < data.Length && data[i] != 0; i++)
            {
            }
            if (i == offset)
            {
                return "";
            }
            return Encoding.UTF8.GetString(data, offset, i - offset);
        }

        public static string ToStringUTF8Z(this byte[] data, uint offset)
        {
            return data.ToStringUTF8Z((int)offset);
        }

        public static string ToStringUTF16Z(this byte[] data, int offset)
        {
            int i;
            for (i = offset; i < data.Length && BitConverter.ToUInt16(data, i) != 0; i += 2)
            {
            }
            if (i == offset)
            {
                return "";
            }
            return Encoding.Unicode.GetString(data, offset, i - offset);
        }

        public static string ToStringUTF16Z(this byte[] data, uint offset)
        {
            return data.ToStringUTF16Z((int)offset);
        }
    }
}
