using System;

namespace Dolphin.Formats;

/// <summary>Runtime JPA-style bounded force field. This is an explicitly
/// configured simulation adapter, not a claim to decode every retail FLD1.</summary>
public enum JpcForceKind { Directional, RadialAttraction, VortexY }

public sealed class JpcParticleField
{
    public JpcForceKind Kind { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float Strength { get; }
    public float Range { get; }

    public JpcParticleField(JpcForceKind kind,float x,float y,float z,float strength,float range=32)
    {
        if((int)kind<0||(int)kind>2 || !Finite(x)||!Finite(y)||!Finite(z)||
           !Finite(strength)||!Finite(range)||Math.Abs(x)>65536||Math.Abs(y)>65536||
           Math.Abs(z)>65536||Math.Abs(strength)>100||range<=0||range>65536)
            throw new ArgumentOutOfRangeException(nameof(kind),"Unbounded JPA force field.");
        Kind=kind;X=x;Y=y;Z=z;Strength=strength;Range=range;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public void Sample(float px,float py,float pz,out float fx,out float fy,out float fz)
    {
        if(Kind==JpcForceKind.Directional)
        {
            fx=X*Strength;fy=Y*Strength;fz=Z*Strength;
            return;
        }
        float dx=X-px,dy=Y-py,dz=Z-pz;
        float distanceSq=dx*dx+dy*dy+dz*dz;
        float attenuation=Strength/(1+distanceSq/(Range*Range));
        float inv=(float)(1.0/Math.Sqrt(distanceSq+1e-6f));
        if(Kind==JpcForceKind.RadialAttraction)
        {
            fx=dx*inv*attenuation;fy=dy*inv*attenuation;fz=dz*inv*attenuation;
        }
        else
        {
            fx=-dz*inv*attenuation;fy=0;fz=dx*inv*attenuation;
        }
    }
}
