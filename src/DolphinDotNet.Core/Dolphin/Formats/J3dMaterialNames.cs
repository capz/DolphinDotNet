using System;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Read material names from a J3D2 BMD3/BDL4 MAT3 chunk without changing
/// the imported binary model or requiring a desktop conversion step.</summary>
public static class J3dMaterialNames
{
    public static string[] Read(byte[] source)
    {
        byte[] bytes = UnwrapYaz0(source);
        if (Ascii(bytes, 0, 4) != "J3D2") throw new InvalidDataException("Not a J3D2 model.");
        string kind = Ascii(bytes, 4, 4);
        if (kind != "bmd3" && kind != "bdl4") throw new NotSupportedException("Unsupported J3D source model.");
        uint declared = U32(bytes, 8), chunkCount = U32(bytes, 0xC);
        if (declared < 0x20 || declared > bytes.Length || chunkCount < 1 || chunkCount > 64)
            throw new InvalidDataException("Invalid J3D model header.");
        int cursor = 0x20;
        for (int i = 0; i < chunkCount; i++)
        {
            if (cursor + 8 > declared) throw new InvalidDataException("Truncated J3D model chunk.");
            string name = Ascii(bytes, cursor, 4);
            int size = checked((int)U32(bytes, cursor + 4));
            if (size < 8 || (long)cursor + size > declared)
                throw new InvalidDataException("Invalid J3D model chunk size.");
            if (name == "MAT3")
            {
                if (size < 0x18) throw new InvalidDataException("Missing MAT3 material metadata.");
                int count = U16(bytes, cursor + 8);
                uint names = U32(bytes, cursor + 0x14);
                if (count < 1 || count > 4096 || names < 8 || names > size - 4)
                    throw new InvalidDataException("Invalid MAT3 material table.");
                long table = (long)cursor + names;
                int listed = U16(bytes, table);
                if (listed != count || (long)names + 4 + count * 4L > size)
                    throw new InvalidDataException("Mismatched MAT3 name count.");
                var result = new string[count];
                for (int j = 0; j < count; j++)
                {
                    ushort off = U16(bytes, table + 6 + j * 4L);
                    if (off < 4 + count * 4 || names + off >= size)
                        throw new InvalidDataException("MAT3 string offset out of range.");
                    result[j] = NullTerminated(bytes, table + off, cursor + size, 64);
                }
                return result;
            }
            cursor = checked(cursor + size);
        }
        throw new InvalidDataException("J3D model is missing MAT3.");
    }
}
