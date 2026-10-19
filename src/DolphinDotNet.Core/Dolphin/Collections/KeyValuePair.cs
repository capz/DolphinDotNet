namespace Dolphin.Collections;
/// <summary>Typed pair with a materialized value layout for arrays, boxing and precise GC.</summary>
public readonly struct KeyValuePair<TKey,TValue>
{
    private readonly TKey key;
    private readonly TValue value;
    public KeyValuePair(TKey key,TValue value){this.key=key;this.value=value;}
    public TKey Key=>key;
    public TValue Value=>value;
    public void Deconstruct(out TKey key,out TValue value){key=this.key;value=this.value;}
    public override string ToString()=>"["+(key==null?"":key.ToString())+", "+(value==null?"":value.ToString())+"]";
}
