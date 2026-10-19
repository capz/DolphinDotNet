using System;
namespace Dolphin.Numerics;
/// <summary>Seed-compatible subtractive PRNG; not cryptographic or thread safe.</summary>
public class Random
{
    private readonly int[] seedArray=new int[56];
    private int inext,inextp=21;
    private static int defaultSeed=161803398;
    public Random():this(defaultSeed=defaultSeed==int.MaxValue?1:defaultSeed+1){}
    public Random(int seed)
    {
        int subtraction=seed==int.MinValue?int.MaxValue:Math.Abs(seed);
        int mj=161803398-subtraction;seedArray[55]=mj;int mk=1;
        for(int i=1;i<55;i++){
            int ii=21*i%55;seedArray[ii]=mk;mk=Difference(mj,mk);if(mk<0)mk+=int.MaxValue;mj=seedArray[ii];
        }
        for(int k=1;k<5;k++)for(int i=1;i<56;i++){
            seedArray[i]=Difference(seedArray[i],seedArray[1+(i+30)%55]);if(seedArray[i]<0)seedArray[i]+=int.MaxValue;
        }
    }
    private static int Difference(int a,int b)=>(int)((long)a-b);
    private int InternalSample()
    {
        int next=inext+1;if(next>=56)next=1;int nextp=inextp+1;if(nextp>=56)nextp=1;
        int result=seedArray[next]-seedArray[nextp];if(result==int.MaxValue)result--;if(result<0)result+=int.MaxValue;
        seedArray[next]=result;inext=next;inextp=nextp;return result;
    }
    protected virtual double Sample()=>InternalSample()*(1.0/int.MaxValue);
    public virtual int Next()=>InternalSample();
    public virtual int Next(int maxValue){if(maxValue<0)throw new ArgumentOutOfRangeException("maxValue");return (int)(Sample()*maxValue);}
    public virtual int Next(int minValue,int maxValue)
    {
        if(minValue>maxValue)throw new ArgumentOutOfRangeException("minValue");
        long range=(long)maxValue-minValue;
        if(range<=int.MaxValue)return (int)(Sample()*range)+minValue;
        int result=InternalSample();if(InternalSample()%2==0)result=-result;
        double sample=((double)result+(int.MaxValue-1))/(2.0*int.MaxValue-1);
        return (int)((long)(sample*range)+minValue);
    }
    public virtual double NextDouble()=>Sample();
    public virtual float NextSingle()=>(float)((NextUInt32()>>8)*(1.0/16777216));
    private uint NextUInt32(){uint a,b;do{a=(uint)InternalSample();}while(a>=2147418112U);do{b=(uint)InternalSample();}while(b>=2147418112U);return (a&65535)<<16|(b&65535);}
    public virtual long NextInt64()=>NextInt64(0,long.MaxValue);
    public virtual long NextInt64(long maxValue)=>NextInt64(0,maxValue);
    public virtual long NextInt64(long minValue,long maxValue)
    {
        if(minValue>maxValue)throw new ArgumentOutOfRangeException("minValue");
        ulong range=(ulong)maxValue-(ulong)minValue;if(range==0)return minValue;
        ulong threshold=(0UL-range)%range,value;
        do{value=(ulong)NextUInt32()<<32|NextUInt32();}while(value<threshold);
        return (long)((ulong)minValue+value%range);
    }
    public virtual void NextBytes(byte[] buffer)
    {
        if(buffer==null)throw new ArgumentNullException("buffer");
        for(int i=0;i<buffer.Length;i++)buffer[i]=(byte)(InternalSample()%256);
    }
}
