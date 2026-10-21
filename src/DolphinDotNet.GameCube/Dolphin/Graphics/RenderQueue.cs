using System;

namespace Dolphin.Graphics;

/// <summary>Bounded, reusable draw queue. Batches opaque meshes by identical
/// material instance; other draws retain insertion order after opaque work.
/// Not a render graph or a GPU command buffer.</summary>
public sealed class RenderQueue
{
    private readonly Entry[] entries;
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
        entries=new Entry[capacity];
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
    private static bool Groupable(Material material)
        =>material.Blend==BlendMode.Opaque && material.DepthTest && material.DepthWrite;
    /// <summary>Submit grouped opaque work, then remaining work in insertion order.
    /// Call only within Renderer.Begin3D/End3D, not during a raw GX pass.</summary>
    public RenderQueueStatistics Execute(Renderer renderer)
    {
        if(renderer==null)throw new ArgumentNullException("renderer");
        int draws=0,groups=0;
        for(int i=0;i<count;i++)
        {
            var target=entries[i].Material;
            if(!Groupable(target))continue;
            bool first=true;
            for(int j=0;j<i;j++)if(entries[j].Material==target){first=false;break;}
            if(!first)continue;
            groups++;
            for(int j=i;j<count;j++)if(entries[j].Material==target)
            {
                var e=entries[j];renderer.DrawMesh(e.Mesh,e.Material,e.Transform);draws++;
            }
        }
        for(int i=0;i<count;i++)if(!Groupable(entries[i].Material))
        {
            var e=entries[i];renderer.DrawMesh(e.Mesh,e.Material,e.Transform);draws++;groups++;
        }
        count=0;
        return new RenderQueueStatistics(draws,groups);
    }
}
public readonly struct RenderQueueStatistics
{
    public int Draws {get;}
    public int Groups {get;}
    internal RenderQueueStatistics(int draws,int groups){Draws=draws;Groups=groups;}
}
