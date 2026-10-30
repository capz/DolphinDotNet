using System;
using System.IO;
namespace Dolphin.Formats;

/// <summary>Deterministic conservative inspection of owned GameCube assets.
/// No disk I/O or graphics calls; all input comes from a caller-bounded buffer.
/// "Supported" means the implemented format reader accepted the fixture;
/// it does not claim original GX material parity.</summary>
public static class JSystemFixtureProbe
{
    public sealed class Result
    {
        public GameCubeFileFormat Format { get; }
        public int Elements { get; }
        public bool HasNativeVisual { get; }
        internal Result(GameCubeFileFormat format,int elements,bool visual)
        {
            Format=format;Elements=elements;HasNativeVisual=visual;
        }
    }

    public static Result Inspect(string name,byte[] data)
    {
        if(name==null)throw new ArgumentNullException(nameof(name));
        if(data==null)throw new ArgumentNullException(nameof(data));
        if(data.Length>GameCubeBinary.MaxAssetBytes)
            throw new InvalidDataException("Asset exceeds the bounded decoder size.");
        var kind=GameCubeFormatDetector.Detect(data,name);
        if(kind==GameCubeFileFormat.Yaz0)
        {
            // Re-run magic detection on the validated decompressed bytes.
            data=GameCubeBinary.UnwrapYaz0(data);
            kind=GameCubeFormatDetector.Detect(data,name);
        }
        switch(kind)
        {
            case GameCubeFileFormat.Rarc:
                return new Result(kind,RarcArchive.Read(data).Entries.Count,false);
            case GameCubeFileFormat.Bti:
                var bti=BtiTexture.Read(data);
                return new Result(kind,bti.MipCount,bti.Width>=4&&bti.Height>=4);
            case GameCubeFileFormat.Jpc:
                // Deliberately header-only on console. The full JPAC parser
                // currently exceeds the AOT stack-analysis profile. Do not
                // report a compatible particle renderer based on the header.
                GameCubeBinary.Check(data,0,0x10);
                string version=GameCubeBinary.Ascii(data,0,8);
                if(version!="JPAC1-00"&&version!="JPAC2-10")
                    throw new InvalidDataException("Unknown JPAC header.");
                int particles=GameCubeBinary.U16(data,8);
                int textures=GameCubeBinary.U16(data,0xA);
                if(particles>4096||textures>4096)
                    throw new InvalidDataException("Excessive JPAC entry counts.");
                return new Result(kind,particles,false);
            case GameCubeFileFormat.J3d:
                if(GameCubeBinary.Ascii(data,0,4)=="J3D2")
                    return new Result(kind,J3dMaterialNames.Read(data).Length,true);
                if(GameCubeBinary.Ascii(data,0,4)=="J3D1")
                    return new Result(kind,J3dMaterialAnimation.Read(data).TrackCount,false);
                throw new NotSupportedException("Unsupported J3D signature.");
            case GameCubeFileFormat.Bmg:
                return new Result(kind,BmgCatalog.Read(data).Entries.Count,false);
            case GameCubeFileFormat.Bfn:
                return new Result(kind,BfnFont.Read(data).CharacterMap.Count,false);
            default:
                throw new NotSupportedException("No compatible JSystem decoder: "+kind);
        }
    }
}
