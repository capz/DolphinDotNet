namespace Dolphin.Graphics;
public sealed partial class Material
{
    private SamplerState primarySampler,secondarySampler;
    private UvTransform primaryUv,secondaryUv;
    private float[]? options;
    private static int nextSortKey;
    internal readonly int SortKey=++nextSortKey;
    public SamplerState Sampler {get=>primarySampler;set{primarySampler=value;UpdateOptions();}}
    public SamplerState SecondarySampler {get=>secondarySampler;set{secondarySampler=value;UpdateOptions();}}
    public UvTransform TextureTransform {get=>primaryUv;set{primaryUv=value;UpdateOptions();}}
    public UvTransform SecondaryTextureTransform {get=>secondaryUv;set{secondaryUv=value;UpdateOptions();}}
    internal float[]? NativeOptions=>options;
    private void UpdateOptions()
    {if(options==null)options=new float[40];WriteSampler(options,0,primarySampler,primaryUv);WriteSampler(options,20,secondarySampler,secondaryUv);}
    internal static void WriteSampler(float[] data,int at,SamplerState s,UvTransform uv)
    {data[at]=(int)s.Filter;data[at+1]=(int)s.U;data[at+2]=(int)s.V;data[at+3]=s.Advanced?1:0;uv.Write(data,at+4);data[at+10]=(int)s.MinFilter;data[at+11]=(int)s.MagFilter;data[at+12]=s.MinLod;data[at+13]=s.MaxLod;data[at+14]=s.LodBias;data[at+15]=s.BiasClamp?1:0;data[at+16]=s.EdgeLod?1:0;data[at+17]=(int)s.Anisotropy;}
}
