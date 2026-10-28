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
    {if(options==null)options=new float[20];WriteSampler(options,0,primarySampler,primaryUv);WriteSampler(options,10,secondarySampler,secondaryUv);}
    internal static void WriteSampler(float[] data,int at,SamplerState s,UvTransform uv)
    {data[at]=(int)s.Filter;data[at+1]=(int)s.U;data[at+2]=(int)s.V;data[at+3]=0;uv.Write(data,at+4);}
}
