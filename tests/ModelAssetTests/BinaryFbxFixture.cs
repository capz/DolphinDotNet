using System.IO.Compression;
using System.Text;

// Independently generated FBX 7.4 binary with compressed arrays and embedded PNG.
internal static class BinaryFbxFixture
{
    private sealed record Node(string Name,object[] Values,params Node[] Children);
    private static Node N(string name,params object[] values)=>new(name,values);
    private static Node Group(string name,object[] values,params Node[] children)=>new(name,values,children);
    internal static void Write(string path,byte[] png)
    {
        var roots=new[]{
            Group("FBXHeaderExtension",[],N("FBXHeaderVersion",1003),N("FBXVersion",7400)),
            Group("GlobalSettings",[],N("Version",1000),Group("Properties70",[],
                N("P","UpAxis","int","Integer","",1),N("P","UpAxisSign","int","Integer","",1),
                N("P","FrontAxis","int","Integer","",2),N("P","FrontAxisSign","int","Integer","",-1),
                N("P","CoordAxis","int","Integer","",0),N("P","CoordAxisSign","int","Integer","",1),
                N("P","UnitScaleFactor","double","Number","",100.0))),
            Group("Objects",[],
                Group("Geometry",[100L,"Geometry::Triangle","Mesh"],
                    N("Vertices",new double[]{0,0,0,1,0,0,0,1,0}),N("PolygonVertexIndex",new int[]{0,1,-3}),
                    Group("LayerElementUV",[0],N("Version",101),N("Name","UVMap"),N("MappingInformationType","ByPolygonVertex"),N("ReferenceInformationType","Direct"),N("UV",new double[]{0,0,1,0,0,1})),
                    Group("Layer",[0],N("Version",100),Group("LayerElement",[],N("Type","LayerElementUV"),N("TypedIndex",0)))),
                Group("Model",[101L,"Model::Triangle","Mesh"],N("Version",232),Group("Properties70",[],
                    N("P","Lcl Translation","Lcl Translation","","A",0.0,0.0,0.0),N("P","Lcl Rotation","Lcl Rotation","","A",0.0,0.0,0.0),N("P","Lcl Scaling","Lcl Scaling","","A",1.0,1.0,1.0))),
                Group("Material",[600L,"Material::Surface",""],N("Version",102),N("ShadingModel","lambert"),Group("Properties70",[],N("P","DiffuseColor","Color","","A",1.0,1.0,1.0))),
                Group("Texture",[601L,"Texture::Texture","TextureVideoClip"],N("Type","TextureVideoClip"),N("Version",202),N("FileName","unused.png"),N("RelativeFilename","unused.png")),
                Group("Video",[602L,"Video::Video","Clip"],N("Type","Clip"),N("Filename","unused.png"),N("RelativeFilename","unused.png"),N("Content",png))),
            Group("Connections",[],N("C","OO",100L,101L),N("C","OO",101L,0L),N("C","OO",600L,101L),N("C","OP",601L,600L,"DiffuseColor"),N("C","OO",602L,601L))
        };
        using var file=File.Create(path);using var writer=new BinaryWriter(file,Encoding.UTF8,true);
        writer.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0"));writer.Write(7400);
        foreach(var node in roots)WriteNode(writer,node);writer.Write(new byte[13]);
    }
    private static void WriteNode(BinaryWriter writer,Node node)
    {
        long start=writer.BaseStream.Position;writer.Write(0);writer.Write(node.Values.Length);writer.Write(0);
        byte[] name=Encoding.ASCII.GetBytes(node.Name);writer.Write((byte)name.Length);writer.Write(name);
        long properties=writer.BaseStream.Position;
        foreach(var value in node.Values)
        {
            switch(value)
            {
                case int i:writer.Write((byte)'I');writer.Write(i);break;
                case long l:writer.Write((byte)'L');writer.Write(l);break;
                case double d:writer.Write((byte)'D');writer.Write(d);break;
                case string s:var text=Encoding.UTF8.GetBytes(s);writer.Write((byte)'S');writer.Write(text.Length);writer.Write(text);break;
                case byte[] bytes:writer.Write((byte)'R');writer.Write(bytes.Length);writer.Write(bytes);break;
                case double[] doubles:WriteArray(writer,'d',doubles.Length,w=>{foreach(double d in doubles)w.Write(d);});break;
                case int[] integers:WriteArray(writer,'i',integers.Length,w=>{foreach(int i in integers)w.Write(i);});break;
                default:throw new InvalidOperationException("Unexpected binary FBX fixture property.");
            }
        }
        long propertiesLength=writer.BaseStream.Position-properties;
        foreach(var child in node.Children)WriteNode(writer,child);
        if(node.Children.Length>0)writer.Write(new byte[13]);
        long end=writer.BaseStream.Position;writer.BaseStream.Position=start;writer.Write(checked((int)end));writer.BaseStream.Position=start+8;writer.Write(checked((int)propertiesLength));writer.BaseStream.Position=end;
    }
    private static void WriteArray(BinaryWriter writer,char type,int count,Action<BinaryWriter> write)
    {
        using var raw=new MemoryStream();using(var values=new BinaryWriter(raw,Encoding.UTF8,true))write(values);
        using var compressed=new MemoryStream();using(var zlib=new ZLibStream(compressed,CompressionLevel.SmallestSize,true))zlib.Write(raw.ToArray());
        writer.Write((byte)type);writer.Write(count);writer.Write(1);writer.Write(checked((int)compressed.Length));writer.Write(compressed.ToArray());
    }
}
