using System;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

public enum GameCubeFileFormat
{
    Unknown, GameCubeDisc, Rarc, Yaz0, Yay0, Bmg, Bfn, J3d, Jpc, Tpl, Bti
}

/// <summary>Conservative header-based format detection. BTI has no magic and requires a file hint.</summary>
public static class GameCubeFormatDetector
{
    public static GameCubeFileFormat Detect(byte[] bytes, string? fileName = null)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length >= 0x20 && U32(bytes, 0x1C) == 0xC2339F3D)
            return GameCubeFileFormat.GameCubeDisc;
        if (bytes.Length >= 4)
        {
            switch (Ascii(bytes, 0, 4))
            {
                case "RARC": return GameCubeFileFormat.Rarc;
                case "Yaz0": return GameCubeFileFormat.Yaz0;
                case "Yay0": return GameCubeFileFormat.Yay0;
                case "MESG":
                    if (bytes.Length >= 8 && Ascii(bytes, 0, 8) == "MESGbmg1") return GameCubeFileFormat.Bmg;
                    break;
                case "FONT":
                    if (bytes.Length >= 8 && Ascii(bytes, 0, 8) == "FONTbfn1") return GameCubeFileFormat.Bfn;
                    break;
                case "J3D1": case "J3D2": return GameCubeFileFormat.J3d;
                case "JPAC":
                    if (bytes.Length >= 8 &&
                        (Ascii(bytes, 0, 8) == "JPAC1-00" || Ascii(bytes, 0, 8) == "JPAC2-10"))
                        return GameCubeFileFormat.Jpc;
                    break;
            }
            if (U32(bytes, 0) == 0x0020AF30) return GameCubeFileFormat.Tpl;
        }
        if (fileName != null && fileName.EndsWith(".bti", StringComparison.OrdinalIgnoreCase))
        {
            try { BtiTexture.Read(bytes); return GameCubeFileFormat.Bti; }
            catch (InvalidDataException) { }
            catch (NotSupportedException) { }
        }
        return GameCubeFileFormat.Unknown;
    }
}
