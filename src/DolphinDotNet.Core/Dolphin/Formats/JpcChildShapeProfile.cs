using System;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Subset of JPAC2 SSP1 billboard child emission with bounded spawn
/// count, timing, gravity and inherited parent velocity. Unsupported child
/// rotation and material stages are rejected.</summary>
public sealed class JpcChildShapeProfile
{
    public int LifetimeFrames { get; private set; }
    public int ChildrenPerParent { get; private set; }
    public float Timing { get; private set; }
    public float BaseVelocity { get; private set; }
    public float VelocityInheritance { get; private set; }
    public float Gravity { get; private set; }
    public float ScaleX { get; private set; }
    public float ScaleY { get; private set; }

    public static JpcChildShapeProfile? Read(JpcArchive archive, ushort particleId)
    {
        if(archive==null)throw new ArgumentNullException(nameof(archive));
        foreach(var particle in archive.Particles)
        {
            if(particle.Id!=particleId)continue;
            JpcChildShapeProfile? result=null;
            foreach(var chunk in particle.Chunks)
            {
                if(chunk.Type!="SSP1")continue;
                if(result!=null)throw new InvalidDataException("Duplicate JPA SSP1.");
                if(archive.Version!="JPAC2-10")
                    throw new NotSupportedException("JPAC1 child shapes need a separate profile.");
                byte[] data=archive.CopyChunk(chunk);
                Check(data,0,0x48);
                uint flags=U32(data,8);
                if((flags & 15)!=2 || (flags & ~15u)!=0)
                    throw new NotSupportedException("Only unrotated SSP1 child billboards are supported.");
                float velocityRandom=F32(data,20),positionRandom=F32(data,12);
                float scaleX=F32(data,32),scaleY=F32(data,36);
                float inheritedScale=F32(data,40),inheritedAlpha=F32(data,44),inheritedRgb=F32(data,48);
                float timing=F32(data,60),velocity=F32(data,16),inheritance=F32(data,24),
                      gravity=F32(data,28);
                int lifetime=S16(data,64),rate=S16(data,66);
                if(!Finite(positionRandom) || positionRandom!=0 ||
                   !Finite(velocityRandom) || velocityRandom!=0 ||
                   !Finite(scaleX)||!Finite(scaleY)||scaleX<=0||scaleY<=0||scaleX>32||scaleY>32||
                   !Finite(inheritedScale)||!Finite(inheritedAlpha)||!Finite(inheritedRgb)||
                   inheritedScale!=0||inheritedAlpha!=0||inheritedRgb!=0||
                   !Finite(timing)||timing<0||timing>1||
                   !Finite(velocity)||Math.Abs(velocity)>100||
                   !Finite(inheritance)||Math.Abs(inheritance)>16||
                   !Finite(gravity)||Math.Abs(gravity)>16||
                   lifetime<1||lifetime>32767||rate<1||rate>16||
                   data[68]!=0 || data[69]!=0 || S16(data,70)!=0)
                    throw new NotSupportedException("Unsupported SSP1 child physics or animation parameters.");
                result=new JpcChildShapeProfile
                {
                    Timing=timing,LifetimeFrames=lifetime,ChildrenPerParent=rate,
                    BaseVelocity=velocity,VelocityInheritance=inheritance,
                    Gravity=gravity,ScaleX=scaleX,ScaleY=scaleY
                };
            }
            return result;
        }
        throw new ArgumentException("Unknown JPA particle ID.",nameof(particleId));
    }

    private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
}
