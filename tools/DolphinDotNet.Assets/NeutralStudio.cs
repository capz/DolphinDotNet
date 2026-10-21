using System.Numerics;
using System.Text.Json;

namespace DolphinDotNet.Assets;

internal static class NeutralStudio
{
    internal static readonly Vector3[] Directions =
    [Vector3.Normalize(new(-.45f,.65f,.6f)),Vector3.Normalize(new(.55f,.2f,.8f)),Vector3.Normalize(new(.2f,.7f,-.65f))];
    internal static readonly float[] Intensities = [2.5f,.7f,1.5f];
    internal static readonly Vector3 View = Vector3.UnitZ;
    internal const float Ambient = .08f;
    internal static float Linear(float v)=>v<=.04045f?v/12.92f:MathF.Pow((v+.055f)/1.055f,2.4f);
    private static float Srgb(float v)=>v<=.0031308f?v*12.92f:1.055f*MathF.Pow(v,1/2.4f)-.055f;
    internal static byte Display(float value,bool toneMap=true)
    {
        // Exposure 0 EV, ACES fitted curve, then standard sRGB encoding.
        value=MathF.Max(0,value);
        float mapped=toneMap?Math.Clamp(value*(2.51f*value+.03f)/(value*(2.43f*value+.59f)+.14f),0,1):Math.Clamp(value,0,1);
        return (byte)Math.Clamp((int)MathF.Round(Srgb(mapped)*255),0,255);
    }
    internal static Vector3 Evaluate(Vector3 color,float metal,float rough,Vector3 normal,float ao,Vector3 emission,bool unlit)
    {
        if(unlit)return color;
        normal=Vector3.Normalize(normal);float nv=MathF.Max(Vector3.Dot(normal,View),.0001f);
        Vector3 f0=Vector3.Lerp(new(.04f),color,metal);
        Vector3 result=color*((1-metal)*Ambient*ao)+emission;
        float alpha=MathF.Max(.045f,rough);alpha*=alpha;float a2=alpha*alpha;
        for(int i=0;i<Directions.Length;i++)
        {
            Vector3 light=Directions[i];float nl=MathF.Max(0,Vector3.Dot(normal,light));if(nl<=0)continue;
            Vector3 half=Vector3.Normalize(light+View);
            float nh=MathF.Max(0,Vector3.Dot(normal,half)),vh=MathF.Max(0,Vector3.Dot(View,half));
            Vector3 fresnel=f0+(Vector3.One-f0)*MathF.Pow(1-vh,5);
            float denominator=nh*nh*(a2-1)+1;
            float distribution=a2/(MathF.PI*denominator*denominator);
            float visibility=.5f/(nl*MathF.Sqrt(nv*nv*(1-a2)+a2)+nv*MathF.Sqrt(nl*nl*(1-a2)+a2));
            Vector3 diffuse=(Vector3.One-fresnel)*(1-metal)*color/MathF.PI;
            result+=(diffuse+fresnel*(distribution*visibility))*(Intensities[i]*nl);
        }
        return result;
    }
}

internal static class NeutralStudioReport
{
    internal static void Write(string output,int resolution)
    {
        var lights=NeutralStudio.Directions.Select((d,i)=>new { directionToLight=new[]{d.X,d.Y,d.Z},radiance=NeutralStudio.Intensities[i] }).ToArray();
        File.WriteAllText(output+".studio.json",JsonSerializer.Serialize(new {
            preset="neutral-studio-v1",resolution,coordinateSystem="world-space, Y up, +Z front",
            view=new { projection="orthographic",directionToCamera=new[]{0,0,1} },
            lights,ambientDiffuseIrradiance=NeutralStudio.Ambient,exposureEv=0,
            toneMapper="ACES fitted",outputTransfer="sRGB",brdf="GGX / height-correlated Smith / Schlick, dielectric F0=0.04",
            limitations=new[]{"static UV0-unwrapped geometry only","fixed view and lighting","no shadows, GI, or specular IBL","no transmission, clearcoat, anisotropy, or changing highlights","8-bit GX texture and existing mip filters"}
        },new JsonSerializerOptions { WriteIndented=true }));
    }
}
