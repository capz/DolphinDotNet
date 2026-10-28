using System;
namespace Dolphin.Graphics;

/// <summary>Preallocated opaque material grouping and bulk indexed submission.
/// Mesh transparency retains input order. Models use a separate global model
/// transparency batch; intersecting mesh/model transparency needs explicit passes.</summary>
public sealed class RenderQueue
{
    private readonly Entry[] entries;
    private readonly int[] order,merge,tokens=new int[128];
    private readonly float[][] meshWorlds=new float[128][];
    private readonly float[] modelWorlds;
    private readonly int modelCapacity;
    private int count,modelCount;
    private sealed class Entry
    {
        internal Mesh? Mesh;
        internal ModelInstance? Model;
        internal Material? Material;
        internal Transform Transform=null!;
    }
    public RenderQueue(int capacity=512):this(capacity,0){}
    public RenderQueue(int capacity,int modelCapacity)
    {
        if(capacity<1||capacity>16384)throw new ArgumentOutOfRangeException("capacity");
        if(modelCapacity<0||modelCapacity>128)throw new ArgumentOutOfRangeException("modelCapacity");
        this.modelCapacity=modelCapacity;
        modelWorlds=new float[modelCapacity*16];
        entries=new Entry[capacity];order=new int[capacity];merge=new int[capacity];
        for(int i=0;i<capacity;i++)entries[i]=new Entry();
    }
    public int Count=>count;
    public int Capacity=>entries.Length;
    public void Clear()
    {
        for(int i=0;i<count;i++){
            entries[i].Mesh=null;entries[i].Model=null;
            entries[i].Material=null;entries[i].Transform=null!;
        }
        for(int i=0;i<meshWorlds.Length;i++)meshWorlds[i]=null!;
        count=modelCount=0;
    }
    public void Submit(Mesh mesh,Material material,Transform transform)
    {
        if(mesh==null||material==null||transform==null)throw new ArgumentNullException();
        mesh.Check();material.Check();
        var entry=Next();entry.Mesh=mesh;entry.Material=material;entry.Transform=transform;
    }
    public void Submit(ModelInstance model,Transform transform)
    {
        if(model==null||transform==null)throw new ArgumentNullException();
        model.Check();
        if(modelCount==modelCapacity)throw new InvalidOperationException("Reserve model capacity in the queue constructor.");
        var entry=Next();entry.Model=model;entry.Transform=transform;modelCount++;
    }
    private Entry Next()
    {
        if(count==Capacity)throw new InvalidOperationException("Render queue capacity exceeded.");
        return entries[count++];
    }
    private static bool Groupable(Entry entry)
        =>entry.Mesh!=null&&entry.Material!.Blend==BlendMode.Opaque&&entry.Material.DepthTest&&entry.Material.DepthWrite;
    private int ValidateAndSelectOpaque()
    {
        int opaque=0;
        for(int i=0;i<count;i++){
            var entry=entries[i];
            if(entry.Mesh!=null){entry.Mesh.Check();entry.Material!.Check();}
            else entry.Model!.Check();
            _=entry.Transform.WorldMatrix;
            if(Groupable(entry))order[opaque++]=i;
        }
        return opaque;
    }
    private void SortOpaque(int length)
    {
        // Stable bottom-up mergesort, bounded O(n log n), no frame allocations.
        for(int width=1;width<length;width*=2){
            for(int start=0;start<length;start+=width*2){
                int left=start,middle=Math.Min(start+width,length),right=middle;
                int end=Math.Min(start+width*2,length);
                for(int at=start;at<end;at++){
                    bool takeLeft=left<middle&&(right>=end||
                        entries[order[left]].Material!.SortKey<=entries[order[right]].Material!.SortKey);
                    merge[at]=takeLeft?order[left++]:order[right++];
                }
            }
            for(int i=0;i<length;i++)order[i]=merge[i];
        }
    }
    private bool SharedTransform(int start,int end,Transform first)
    {
        for(int i=start+1;i<end;i++)if(entries[order[i]].Transform!=first)return false;
        return true;
    }
    public RenderQueueStatistics Execute(Renderer renderer)
    {
        if(renderer==null)throw new ArgumentNullException("renderer");
        // Validate managed lifetimes before any native submissions. Each native
        // block additionally preflights its tokens and matrices before drawing.
        int opaque=ValidateAndSelectOpaque(),submissions=0,groups=0,mergedMeshes=0;
        SortOpaque(opaque);
        for(int start=0;start<opaque;){
            int groupStart=start,used=0;
            var first=entries[order[start]];
            var material=first.Material!;
            while(start<opaque&&used<128&&entries[order[start]].Material==material){
                var entry=entries[order[start++]];
                tokens[used]=entry.Mesh!.Token;meshWorlds[used++]=entry.Transform.WorldMatrix;
            }
            if(used>1&&SharedTransform(groupStart,start,first.Transform)){
                renderer.DrawMeshBatch(tokens,used,material,first.Transform);mergedMeshes+=used-1;
            }else renderer.DrawMeshTokens(tokens,meshWorlds,used,material);
            submissions++;groups++;
        }
        if(modelCount>0){
            int used=0;
            for(int i=0;i<count;i++)if(entries[i].Model!=null){
                var entry=entries[i];tokens[used]=entry.Model!.Token;
                var matrix=entry.Transform.WorldMatrix;
                for(int j=0;j<16;j++)modelWorlds[used*16+j]=matrix[j];
                used++;
            }
            renderer.DrawModelTokens(tokens,modelWorlds,used);submissions++;groups++;
        }
        for(int i=0;i<count;i++)if(entries[i].Mesh!=null&&!Groupable(entries[i])){
            var entry=entries[i];renderer.DrawMesh(entry.Mesh!,entry.Material!,entry.Transform);
            submissions++;groups++;
        }
        int logical=count;Clear();
        return new RenderQueueStatistics(submissions,groups,logical,mergedMeshes);
    }
}
public readonly struct RenderQueueStatistics
{
    /// <summary>Native submission invocations, not GX draw calls. Distinct world
    /// transforms retain separate GX geometry draws.</summary>
    public int Draws {get;}
    public int Groups {get;}
    /// <summary>Logical submissions, including model instances.</summary>
    public int LogicalMeshes {get;}
    public int MeshesMerged {get;}
    internal RenderQueueStatistics(int draws,int groups,int logical,int merged)
    {Draws=draws;Groups=groups;LogicalMeshes=logical;MeshesMerged=merged;}
}
