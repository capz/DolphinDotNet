using System;
using System.Threading.Tasks;
internal interface IReader { int Read(); }
internal sealed class Reader : IReader
{
    private readonly int value;
    internal Reader(int value){this.value=value;}
    public int Read()=>value;
}
internal struct Mixed { public long Number; public string Text; }
internal static class Program
{
    private static T Load<T>(ref T value)=>value;
    private static IReader Interface<T>(ref T value) where T:IReader=>value;
    private static async Task<int> Deferred()
    {
        var value=new Reader(41);
        await Task.Yield();
        var increment=await Task.Run(()=>1);
        await Task.Yield();
        return Interface(ref value).Read()+increment;
    }
    private static int Main()
    {
        var reader=new Reader(41);
        if(!object.ReferenceEquals(Load(ref reader),reader))return 1;
        if(Interface(ref reader).Read()!=41)return 2;
        Reader? missing=null;if(Load(ref missing)!=null)return 3;
        int narrow=-123;long wide=0x100000002L;float single=3.25f;double real=7.125;
        if(Load(ref narrow)!=narrow||Load(ref wide)!=wide||Load(ref single)!=single||Load(ref real)!=real)return 4;
        var mixed=new Mixed{Number=wide,Text=string.Concat("root", "ed")};
        var copy=Load(ref mixed);
        if(copy.Number!=wide||copy.Text.Length!=6)return 5;
        if(Deferred().GetAwaiter().GetResult()!=42)return 6;
        return 0;
    }
}
