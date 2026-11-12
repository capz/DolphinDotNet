using System;
using System.Reflection;
using Dolphin.Graphics;

static class Program
{
    private static void Check(bool value,string message)
    {if(!value)throw new InvalidOperationException(message);}
    private static object? Storage(Transform transform,string field)
        =>typeof(Transform).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(transform);
    private static void At(Transform transform,float x)
    {Check(Math.Abs(transform.WorldMatrix[12]-x)<.001f,"Unexpected world placement.");}
    public static void Main(string[] args)
    {
        // Warm JIT and static initialization outside the measurements.
        MeasureRoots();MeasureChildren();
        long roots=MeasureRoots(),children=MeasureChildren();
        Console.WriteLine("CLR allocations: 128 roots="+roots+" B; 128 children of one existing root="+children+" B");
        if(args.Length!=0&&args[0]=="--measure-only")return;

        var root=new Transform();var other=new Transform();var leaf=new Transform();
        var stable=leaf.WorldMatrix;
        Check(Storage(root,"local")==null&&Storage(root,"children")==null,"Unused hierarchy storage must stay absent.");
        leaf.LocalPosition=new Vector3(2,0,0);At(leaf,2);
        Check(Storage(leaf,"local")==null,"Drawing an unparented transform must not allocate a local cache.");
        root.LocalPosition=new Vector3(10,0,0);leaf.Parent=root;At(leaf,12);
        Check(Storage(root,"children")!=null&&Storage(leaf,"children")==null,"Only parents reserve a child list.");
        object local=Storage(leaf,"local")!;
        leaf.Parent=null;leaf.LocalPosition=new Vector3(4,0,0);At(leaf,4);
        other.LocalPosition=new Vector3(20,0,0);leaf.Parent=other;At(leaf,24);
        Check(ReferenceEquals(local,Storage(leaf,"local")),"Reattachment must retain its local cache.");
        Check(ReferenceEquals(stable,leaf.WorldMatrix),"The world cache must remain stable across parenting changes.");
        Check(!ReferenceEquals(root.WorldMatrix,leaf.WorldMatrix),"Batch transforms must have distinct world caches.");
        other.LocalPosition=new Vector3(30,0,0);At(leaf,34);
        root.LocalPosition=new Vector3(100,0,0);At(leaf,34);

        // Rejected hierarchy changes must leave the old graph and clean caches usable.
        var grandchild=new Transform{Parent=leaf};At(grandchild,34);
        try{other.Parent=grandchild;throw new InvalidOperationException("Cycle accepted.");}catch(ArgumentException){}
        Check(other.Parent==null&&leaf.Parent==other&&grandchild.Parent==leaf,"Rejected cycle changed links.");
        other.LocalPosition=new Vector3(40,0,0);At(grandchild,44);
        var deepest=new Transform();
        for(int i=1;i<128;i++)deepest=new Transform{Parent=deepest};
        try{leaf.Parent=deepest;throw new InvalidOperationException("Excess depth accepted.");}catch(ArgumentException){}
        Check(leaf.Parent==other,"Rejected depth changed the old parent.");At(grandchild,44);
        var rejected=new Transform();
        try{rejected.Parent=deepest;throw new InvalidOperationException("Excess depth accepted.");}catch(ArgumentException){}
        Check(Storage(rejected,"local")==null,"Validation must precede hierarchy allocations.");

        var destination=new float[16];
        Update(root,leaf,grandchild,destination,1);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)Update(root,leaf,grandchild,destination,i);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"Repeated transform updates and matrix copies allocated.");
        Check(ReferenceEquals(stable,leaf.WorldMatrix),"Repeated updates replaced the world cache.");
        Console.WriteLine("Transform lazy storage, hierarchy transitions and allocation checks passed.");
    }
    private static void Update(Transform root,Transform leaf,Transform child,float[] destination,int tick)
    {
        root.LocalPosition=new Vector3(tick,0,0);root.CopyWorldMatrix(destination);
        leaf.LocalPosition=new Vector3(tick,0,0);child.CopyWorldMatrix(destination);
    }
    private static long MeasureRoots()
    {
        long before=GC.GetAllocatedBytesForCurrentThread();var transforms=new Transform[128];
        for(int i=0;i<transforms.Length;i++)transforms[i]=new Transform();
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(transforms);return bytes;
    }
    private static long MeasureChildren()
    {
        var root=new Transform();long before=GC.GetAllocatedBytesForCurrentThread();var transforms=new Transform[128];
        for(int i=0;i<transforms.Length;i++)transforms[i]=new Transform{Parent=root};
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(transforms);GC.KeepAlive(root);return bytes;
    }
}
