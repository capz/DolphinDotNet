using System;
namespace Dolphin.Graphics;
/// <summary>Preallocated opaque material grouping and bulk indexed submission.
/// Mesh transparency retains input order. Models use a separate global model
/// transparency batch; mesh/model intersecting transparency needs explicit passes.</summary>
public sealed class RenderQueue
{
    private readonly Entry[] entries;
    private readonly int[] order,merge,tokens=new int[128];
    private readonly float[][] meshWorlds=new float[128][];
    private readonly float[] modelWorlds;
    private readonly int modelCapacity;
    private int count,modelCount;
    private sealed class Entry {internal Mesh? Mesh;internal ModelInstance? Model;internal Material? Material;internal Transform Transform=null!;}
    public RenderQueue(int capacity=512):this(capacity,0){}
    public RenderQueue(int capacity,int modelCapacity)
    {if(modelCapacity<0||modelCapacity>128)throw new ArgumentOutOfRangeException("modelCapacity");this.modelCapacity=modelCapacity;modelWorlds=new float[modelCapacity*16];if(capacity<1||capacity>16384)throw new ArgumentOutOfRangeException("capacity");entries=new Entry[capacity];order=new int[capacity];merge=new int[capacity];for(int i=0;i<capacity;i++)entries[i]=new Entry();}
    public int Count=>count;
    public int Capacity=>entries.Length;
    public void Clear(){for(int i=0;i<count;i++){entries[i].Mesh=null;entries[i].Model=null;entries[i].Material=null;entries[i].Transform=null!;}for(int i=0;i<meshWorlds.Length;i++)meshWorlds[i]=null!;count=modelCount=0;}
    public void Submit(Mesh mesh,Material material,Transform transform)
    {if(mesh==null||material==null||transform==null)throw new ArgumentNullException();mesh.Check();material.Check();var e=Next();e.Mesh=mesh;e.Material=material;e.Transform=transform;}
    public void Submit(ModelInstance model,Transform transform)
    {if(model==null||transform==null)throw new ArgumentNullException();model.Check();if(modelCount==modelCapacity)throw new InvalidOperationException("Reserve model capacity in the queue constructor.");var e=Next();e.Model=model;e.Transform=transform;modelCount++;}
    private Entry Next(){if(count==Capacity)throw new InvalidOperationException("Render queue capacity exceeded.");return entries[count++];}
    private static bool Groupable(Entry e)=>e.Mesh!=null&&e.Material!.Blend==BlendMode.Opaque&&e.Material.DepthTest&&e.Material.DepthWrite;
    private void Matrix(Entry e,int slot){var m=e.Transform.WorldMatrix;for(int j=0;j<16;j++)modelWorlds[slot*16+j]=m[j];}
    public RenderQueueStatistics Execute(Renderer renderer)
    {
        if(renderer==null)throw new ArgumentNullException("renderer");
        int n=0,submissions=0,groups=0,mergedMeshes=0;
        // Validate managed lifetimes before any native submissions. Native blocks
        // additionally preflight every token/matrix before submitting that block.
        for(int i=0;i<count;i++){var e=entries[i];if(e.Mesh!=null){e.Mesh.Check();e.Material!.Check();}else e.Model!.Check();_=e.Transform.WorldMatrix;if(Groupable(e))order[n++]=i;}
        // Stable bottom-up mergesort, bounded O(n log n), no frame allocations.
        for(int width=1;width<n;width*=2){for(int start=0;start<n;start+=width*2){int a=start,b=Math.Min(start+width,n),end=Math.Min(start+width*2,n),mid=b;for(int k=start;k<end;k++)merge[k]=a<mid&&(b>=end||entries[order[a]].Material!.SortKey<=entries[order[b]].Material!.SortKey)?order[a++]:order[b++];}for(int i=0;i<n;i++)order[i]=merge[i];}
        for(int start=0;start<n;){var material=entries[order[start]].Material!;int used=0;while(start<n&&used<128&&entries[order[start]].Material==material){var e=entries[order[start++]];tokens[used]=e.Mesh!.Token;meshWorlds[used++]=e.Transform.WorldMatrix;}bool shared=true;var firstTransform=entries[order[start-used]].Transform;for(int k=start-used+1;k<start;k++)if(entries[order[k]].Transform!=firstTransform)shared=false;if(shared&&used>1){renderer.DrawMeshBatch(tokens,used,material,firstTransform);mergedMeshes+=used-1;}else renderer.DrawMeshTokens(tokens,meshWorlds,used,material);submissions++;groups++;}
        if(modelCount>0){int used=0;for(int i=0;i<count;i++)if(entries[i].Model!=null){var e=entries[i];tokens[used]=e.Model!.Token;Matrix(e,used++);}renderer.DrawModelTokens(tokens,modelWorlds,used);submissions++;groups++;}
        for(int i=0;i<count;i++)if(entries[i].Mesh!=null&&!Groupable(entries[i])){var e=entries[i];renderer.DrawMesh(e.Mesh!,e.Material!,e.Transform);submissions++;groups++;}
        int logical=count;Clear();return new RenderQueueStatistics(submissions,groups,logical,mergedMeshes);
    }
}
public readonly struct RenderQueueStatistics
{
    /// <summary>Native submission invocations, not GX draw calls. Distinct world
    /// transforms retain separate GX geometry draws.</summary>
    public int Draws {get;}
    public int Groups {get;}
    public int LogicalMeshes {get;}
    public int MeshesMerged {get;}
    internal RenderQueueStatistics(int draws,int groups,int logical,int merged)
    {Draws=draws;Groups=groups;LogicalMeshes=logical;MeshesMerged=merged;}
}
