using System;

namespace Dolphin.Graphics;

/// <summary>Groups depth-writing opaque meshes by material and shared world
/// transform and submits real native merged vertex streams. Transparent work
/// remains in original order. One native merged draw can split at 768 vertices.
/// Material and transform are evaluated at Execute, not Submit.</summary>
public sealed class RenderQueue
{
    private readonly Entry[] entries;
    private readonly bool[] processed;
    private readonly int[] scratch;
    private int count;
    private sealed class Entry
    {
        internal Mesh Mesh=null!;
        internal Material Material=null!;
        internal Transform Transform=null!;
    }
    public RenderQueue(int capacity=512)
    {
        if(capacity<1 || capacity>16384)throw new ArgumentOutOfRangeException("capacity");
        entries=new Entry[capacity];processed=new bool[capacity];scratch=new int[128];
        for(int i=0;i<capacity;i++)entries[i]=new Entry();
    }
    public int Count=>count;
    public int Capacity=>entries.Length;
    public void Clear()=>count=0;
    public void Submit(Mesh mesh,Material material,Transform transform)
    {
        if(mesh==null)throw new ArgumentNullException("mesh");
        if(material==null)throw new ArgumentNullException("material");
        if(transform==null)throw new ArgumentNullException("transform");
        if(count==entries.Length)throw new InvalidOperationException("Render queue capacity exceeded.");
        mesh.Check();material.Check();
        var e=entries[count++];e.Mesh=mesh;e.Material=material;e.Transform=transform;
    }
    private static bool Groupable(Material m)
        =>m.Blend==BlendMode.Opaque && m.DepthTest && m.DepthWrite;
    public RenderQueueStatistics Execute(Renderer renderer)
    {
        if(renderer==null)throw new ArgumentNullException("renderer");
        int submissions=0,groups=0,mergedMeshes=0;
        for(int i=0;i<count;i++)processed[i]=false;
        for(int i=0;i<count;i++)
        {
            if(processed[i] || !Groupable(entries[i].Material))continue;
            var target=entries[i];
            int n=0;
            for(int j=i;j<count && n<scratch.Length;j++)
            {
                var e=entries[j];
                if(processed[j] || e.Material!=target.Material || e.Transform!=target.Transform)continue;
                scratch[n++]=e.Mesh.Token;
                processed[j]=true;
            }
            if(n==1)renderer.DrawMesh(target.Mesh,target.Material,target.Transform);
            else {renderer.DrawMeshBatch(scratch,n,target.Material,target.Transform);mergedMeshes+=n-1;}
            submissions++;groups++;
        }
        for(int i=0;i<count;i++)if(!processed[i])
        {
            var e=entries[i];renderer.DrawMesh(e.Mesh,e.Material,e.Transform);submissions++;groups++;
        }
        int logical=count;
        count=0;
        return new RenderQueueStatistics(submissions,groups,logical,mergedMeshes);
    }
}
public readonly struct RenderQueueStatistics
{
    /// <summary>Native batch invocations, not necessarily single GX_Begin calls
    /// when a group exceeds the native 768-vertex staging limit.</summary>
    public int Draws {get;}
    public int Groups {get;}
    public int LogicalMeshes {get;}
    public int MeshesMerged {get;}
    internal RenderQueueStatistics(int draws,int groups,int logical,int merged)
    {Draws=draws;Groups=groups;LogicalMeshes=logical;MeshesMerged=merged;}
}
