using System.Buffers.Binary;
using System.Numerics;

namespace DolphinDotNet.Assets;

/// <summary>
/// Independently implemented GameCube PSO GJCM/GJTL parser using the documented
/// Flipper/Ninja binary layout. No external model importers are launched.
/// Geometry, transforms, UVs, vertex colors and GX primitive indices are read
/// directly. Textured materials require explicit approximation in this version.
/// </summary>
internal sealed class PsoGjImport
{
    private readonly byte[] source;
    private readonly bool approximate;
    private readonly bool xj;
    private readonly List<Node> nodes = new();
    private readonly List<Mesh> meshes = new();
    private readonly HashSet<int> visited = new();
    private byte[] chunk = Array.Empty<byte>();

    private sealed record Node(int Parent, Vector3 Position, Quaternion Rotation, Vector3 Scale);
    private sealed record Vertex(Vector3 Position, Vector3 Normal, Vector2 Uv, Vector4 Color);
    private sealed record Mesh(int Node, List<Vertex> Vertices, List<int> Indices);
    private sealed class Reader
    {
        internal readonly byte[] Bytes;
        private readonly bool little;
        internal Reader(byte[] bytes,bool little=false) { Bytes = bytes;this.little=little; }
        internal void Range(int offset,int size)
        {
            if(offset<0||size<0||offset>Bytes.Length-size)
                throw new InvalidDataException("PSO GJ offset/length out of bounds.");
        }
        internal byte B(int p){Range(p,1);return Bytes[p];}
        internal ushort U16(int p){Range(p,2);return little?BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(p)):BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(p));}
        internal short I16(int p)=>unchecked((short)U16(p));
        internal uint U32(int p){Range(p,4);return little?BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(p)):BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(p));}
        internal int I32(int p)=>unchecked((int)U32(p));
        internal float F32(int p)
        {
            float v=BitConverter.Int32BitsToSingle(I32(p));
            if(!float.IsFinite(v))throw new InvalidDataException("Non-finite PSO coordinate.");
            return v;
        }
        internal Vector3 Vec3(int p)=>new(F32(p),F32(p+4),F32(p+8));
    }
    private PsoGjImport(byte[] bytes,bool allowApproximation,bool isXj)
    { source=bytes;approximate=allowApproximation;xj=isXj; }
    public static byte[] Convert(string path,bool approximate)
    {
        var importer=new PsoGjImport(File.ReadAllBytes(path),approximate,
            Path.GetExtension(path).Equals(".xj",StringComparison.OrdinalIgnoreCase));
        importer.Parse();
        return importer.Build();
    }
    private void Parse()
    {
        int pos=0;bool geometry=false;
        while(pos<source.Length)
        {
            if(source.Length-pos<8)throw new InvalidDataException("Truncated PSO outer chunk.");
            string tag=System.Text.Encoding.ASCII.GetString(source,pos,4);
            uint len=BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(pos+4,4));
            pos+=8;
            if(len>source.Length-pos)throw new InvalidDataException("PSO chunk length beyond file.");
            if(tag==(xj?"NJCM":"GJCM"))
            {
                chunk=source.AsSpan(pos,(int)len).ToArray();
                if(chunk.Length<52)throw new InvalidDataException("Empty GJCM hierarchy.");
                ReadNode(new Reader(chunk,xj),0,-1,0);
                geometry=true;break;
            }
            pos+=(int)len;
        }
        if(!geometry||nodes.Count==0||meshes.Count==0)
            throw new InvalidDataException("No supported PSO GJ geometry found.");
    }
    private void ReadNode(Reader r,int offset,int parent,int depth)
    {
        if(depth>128||nodes.Count>=1024||!visited.Add(offset))
            throw new InvalidDataException("Invalid PSO GJ node graph.");
        r.Range(offset,52);
        uint flags=r.U32(offset);
        if(flags>0x1fff)throw new InvalidDataException("Invalid PSO GJ node flags.");
        uint meshOffset=r.U32(offset+4),child=r.U32(offset+44),sibling=r.U32(offset+48);
        var translation=(flags&1)!=0?Vector3.Zero:r.Vec3(offset+8);
        Vector3 scale=r.Vec3(offset+32);
        if(scale.X==0||scale.Y==0||scale.Z==0)
            throw new InvalidDataException("Zero PSO GJ node scale.");
        Quaternion q=Quaternion.Identity;
        if((flags&2)==0)
        {
            const float toRadians=MathF.PI*2f/65536f;
            float rx=r.I32(offset+20)*toRadians,ry=r.I32(offset+24)*toRadians,rz=r.I32(offset+28)*toRadians;
            q=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitX,rx)*
                                   Quaternion.CreateFromAxisAngle(Vector3.UnitY,ry)*
                                   Quaternion.CreateFromAxisAngle(Vector3.UnitZ,rz));
        }
        int nodeId=nodes.Count;
        nodes.Add(new Node(parent,translation,q,scale));
        if(meshOffset!=0){if(meshOffset>int.MaxValue)throw new InvalidDataException("Mesh offset.");
            if(xj) ReadXjMesh(r,(int)meshOffset,nodeId); else ReadMesh(r,(int)meshOffset,nodeId);}
        if(child!=0){if(child>int.MaxValue)throw new InvalidDataException("Child offset.");ReadNode(r,(int)child,nodeId,depth+1);}
        if(sibling!=0){if(sibling>int.MaxValue)throw new InvalidDataException("Sibling offset.");ReadNode(r,(int)sibling,parent,depth+1);}
    }
    private void ReadMesh(Reader r,int offset,int nodeId)
    {
        r.Range(offset,36);
        uint props=r.U32(offset),solid=r.U32(offset+8),alpha=r.U32(offset+12);
        short solidCount=r.I16(offset+16),alphaCount=r.I16(offset+18);
        if(solidCount<0||alphaCount<0||solidCount+alphaCount>4096)
            throw new InvalidDataException("PSO strip group count.");
        var positions=new List<Vector3>();var normals=new List<Vector3>();
        var colors=new List<Vector4>();var uv=new List<Vector2>();
        if(props==0)throw new InvalidDataException("PSO mesh has no attributes.");
        int p=checked((int)props);
        for(int i=0;i<16_384;i++)
        {
            byte type=r.B(p);
            if(type==255)break;
            r.Range(p,16);
            int count=r.U16(p+2);
            uint dataOffset=r.U32(p+8),byteLength=r.U32(p+12);
            if(dataOffset>int.MaxValue||byteLength>int.MaxValue)
                throw new InvalidDataException("PSO attribute offsets too large.");
            int width=type switch{1 or 2=>12,3 or 5=>4,_=>0};
            if(width>0)
            {
                if((long)count*width>byteLength)throw new InvalidDataException("PSO attribute range too short.");
                r.Range((int)dataOffset,(int)byteLength);
                for(int j=0;j<count;j++)
                {
                    int e=checked((int)dataOffset+j*width);
                    if(type==1)positions.Add(r.Vec3(e));
                    else if(type==2)normals.Add(r.Vec3(e));
                    else if(type==3)colors.Add(new Vector4(r.B(e)/255f,r.B(e+1)/255f,r.B(e+2)/255f,r.B(e+3)/255f));
                    else uv.Add(new Vector2(r.I16(e)/255f,1f-r.I16(e+2)/255f));
                }
            }
            p+=16;
            if(i==16383)throw new InvalidDataException("Unterminated PSO vertex property table.");
        }
        if(positions.Count==0)throw new InvalidDataException("PSO mesh has no positions.");
        void Groups(uint table,int count)
        {
            if(count==0)return;
            if(table>int.MaxValue)throw new InvalidDataException("Strip table offset.");
            int start=(int)table;
            r.Range(start,checked(count*16));
            for(int g=0;g<count;g++)
            {
                int entry=start+g*16;
                uint mat=r.U32(entry),matCount=r.U32(entry+4);
                uint stream=r.U32(entry+8),streamLength=r.U32(entry+12);
                if(matCount>2048||mat>int.MaxValue||stream>int.MaxValue||streamLength>int.MaxValue)
                    throw new InvalidDataException("PSO material/strip range.");
                r.Range((int)mat,checked((int)matCount*8));
                r.Range((int)stream,(int)streamLength);
                uint flags=0;
                for(int j=0;j<matCount;j++)
                {
                    byte type=r.B((int)mat+j*8);
                    if(type==1)flags=r.U32((int)mat+j*8+4);
                    if(type==8 && !approximate)
                        throw new InvalidDataException("Textured PSO GJ requires texture decoding. Set --allow-material-approximation to import flat-color geometry.");
                }
                if((flags&8)==0)throw new InvalidDataException("PSO GJ primitive has no position index.");
                var mesh=new Mesh(nodeId,new List<Vertex>(),new List<int>());
                int curr=(int)stream,end=checked(curr+(int)streamLength);
                while(curr<end)
                {
                    int primitive=r.B(curr++);
                    if(primitive==0)continue;
                    if(primitive!=0x90&&primitive!=0x98)
                        throw new InvalidDataException("Unsupported PSO GX primitive 0x"+primitive.ToString("x2"));
                    if(end-curr<2)throw new InvalidDataException("Truncated PSO primitive.");
                    int vertices=r.U16(curr);curr+=2;
                    int baseIndex=mesh.Vertices.Count;
                    for(int vi=0;vi<vertices;vi++)
                    {
                        int[] ix={-1,-1,-1,-1};
                        int[] present={8,0x20,0x80,0x800};
                        int[] wide={4,0x10,0x40,0x400};
                        for(int a=0;a<4;a++)if((flags&present[a])!=0)
                        {
                            int width=(flags&wide[a])!=0?2:1;
                            if(curr>end-width)throw new InvalidDataException("Truncated PSO GX index.");
                            ix[a]=width==2?r.U16(curr):r.B(curr);
                            curr+=width;
                        }
                        if(ix[0]>=positions.Count||ix[0]<0||
                          ix[1]>=normals.Count||ix[2]>=colors.Count||ix[3]>=uv.Count)
                            throw new InvalidDataException("PSO GX attribute index beyond array.");
                        mesh.Vertices.Add(new Vertex(positions[ix[0]],ix[1]<0?Vector3.UnitY:normals[ix[1]],
                            ix[3]<0?Vector2.Zero:uv[ix[3]],ix[2]<0?Vector4.One:colors[ix[2]]));
                    }
                    if(primitive==0x90)
                        for(int j=0;j+2<vertices;j+=3)
                        {mesh.Indices.Add(baseIndex+j);mesh.Indices.Add(baseIndex+j+2);mesh.Indices.Add(baseIndex+j+1);}
                    else
                        for(int j=0;j+2<vertices;j++)
                        {
                            int a=baseIndex+j,b=baseIndex+j+1,c=baseIndex+j+2;
                            if((j&1)==0){mesh.Indices.Add(a);mesh.Indices.Add(c);mesh.Indices.Add(b);}
                            else {mesh.Indices.Add(b);mesh.Indices.Add(c);mesh.Indices.Add(a);}
                        }
                }
                if(mesh.Indices.Count>0)meshes.Add(mesh);
            }
        }
        Groups(solid,solidCount);
        Groups(alpha,alphaCount);
    }

    /* Blue Burst XJ uses a fixed, little-endian mesh descriptor rather than
     * NJ/GJ typed chunk streams. All overlapping vertex-info records are read
     * in order without dropping UVs/normal/color from earlier records. */
    private void ReadXjMesh(Reader r,int offset,int nodeId)
    {
        r.Range(offset,44);
        uint attrOfs=r.U32(offset+4),attrCount=r.U32(offset+8);
        uint opaqueOfs=r.U32(offset+12),opaqueCount=r.U32(offset+16);
        uint alphaOfs=r.U32(offset+20),alphaCount=r.U32(offset+24);
        if(attrCount>4096||opaqueCount+alphaCount>4096)
            throw new InvalidDataException("Invalid XJ descriptor count.");
        if(attrOfs>int.MaxValue||opaqueOfs>int.MaxValue||alphaOfs>int.MaxValue)
            throw new InvalidDataException("XJ descriptor offset exceeds range.");
        r.Range((int)attrOfs,checked((int)attrCount*16));
        var pool=new List<Vertex>();
        for(int i=0;i<attrCount;i++)
        {
            int entry=(int)attrOfs+i*16;
            int flags=r.U16(entry);
            uint start=r.U32(entry+4),count=r.U32(entry+12);
            if(count>1_000_000||start>int.MaxValue)
                throw new InvalidDataException("XJ vertex array exceeds limits.");
            int stride=12+((flags&2)!=0?12:0)+((flags&4)!=0?4:0)+((flags&1)!=0?8:0);
            r.Range((int)start,checked((int)count*stride));
            for(int j=0;j<count;j++)
            {
                int p=checked((int)start+j*stride);
                while(pool.Count<=j)pool.Add(new Vertex(Vector3.Zero,Vector3.UnitY,Vector2.Zero,Vector4.One));
                Vertex old=pool[j];
                Vector3 point=r.Vec3(p);p+=12;
                Vector3 normal=(flags&2)!=0?r.Vec3(p):old.Normal;
                if((flags&2)!=0)p+=12;
                Vector4 color=(flags&4)!=0?new Vector4(r.B(p)/255f,r.B(p+1)/255f,r.B(p+2)/255f,r.B(p+3)/255f):old.Color;
                if((flags&4)!=0)p+=4;
                Vector2 uv=(flags&1)!=0?new Vector2(r.F32(p),1f-r.F32(p+4)):old.Uv;
                pool[j]=new Vertex(point,normal,uv,color);
            }
        }
        if(pool.Count==0)throw new InvalidDataException("XJ mesh has no vertices.");
        void Groups(uint location,uint amount)
        {
            if(amount==0)return;
            if(location>int.MaxValue)throw new InvalidDataException("XJ strip table pointer.");
            int table=(int)location;
            r.Range(table,checked((int)amount*20));
            for(int i=0;i<amount;i++)
            {
                int entry=table+i*20;
                uint material=r.U32(entry),properties=r.U32(entry+4);
                uint indices=r.U32(entry+8),length=r.U32(entry+12);
                if(material>int.MaxValue||indices>int.MaxValue||properties>2048||length>1_000_000)
                    throw new InvalidDataException("XJ strip counts/offsets.");
                r.Range((int)material,checked((int)properties*16));
                r.Range((int)indices,checked((int)length*2));
                Vector4 diffuse=Vector4.One;
                for(int j=0;j<properties;j++)
                {
                    int p=(int)material+j*16;
                    int type=r.I32(p);
                    if(type==3&&!approximate)
                        throw new InvalidDataException("Textured XJ materials require --allow-material-approximation.");
                    if(type==5)
                        diffuse=new Vector4(r.B(p+4)/255f,r.B(p+5)/255f,r.B(p+6)/255f,r.B(p+7)/255f);
                }
                var mesh=new Mesh(nodeId,new List<Vertex>(),new List<int>());
                for(int j=0;j<length;j++)
                {
                    int ix=r.I16((int)indices+j*2);
                    if(ix<0||ix>=pool.Count)throw new InvalidDataException("XJ strip vertex index out of range.");
                    Vertex v=pool[ix];
                    mesh.Vertices.Add(v with {Color=v.Color*diffuse});
                }
                // The PSO XJ reference parser emits both triangle windings.
                for(int j=0;j+2<length;j++)
                {
                    int a=j,b=j+1,c=j+2;
                    if(a==b||b==c||c==a)continue;
                    mesh.Indices.Add(a);mesh.Indices.Add(b);mesh.Indices.Add(c);
                    mesh.Indices.Add(a);mesh.Indices.Add(c);mesh.Indices.Add(b);
                }
                if(mesh.Indices.Count>0)meshes.Add(mesh);
            }
        }
        Groups(opaqueOfs,opaqueCount);
        Groups(alphaOfs,alphaCount);
    }

    private byte[] Build()
    {
        using var n=new BigWriter();using var m=new BigWriter();
        using var mat=new BigWriter();using var tex=new BigWriter();
        using var skins=new BigWriter();using var clips=new BigWriter();
        n.I(nodes.Count);
        for(int i=0;i<nodes.Count;i++)
        {
            var node=nodes[i];n.I(node.Parent);n.Name("PSO_Node_"+i);
            n.Floats([node.Position.X,node.Position.Y,node.Position.Z]);
            n.Floats([node.Rotation.X,node.Rotation.Y,node.Rotation.Z,node.Rotation.W]);
            n.Floats([node.Scale.X,node.Scale.Y,node.Scale.Z]);
        }
        m.I(meshes.Count);
        foreach(var mesh in meshes)
        {
            if(mesh.Vertices.Count>1_000_000||mesh.Indices.Count>3_000_000)
                throw new InvalidDataException("PSO mesh exceeds runtime limits.");
            m.I(mesh.Node);m.I(-1);m.I(0);m.I(mesh.Vertices.Count);m.I(mesh.Indices.Count);
            for(int i=0;i<3;i++)m.F(mesh.Vertices.Min(v=>i==0?v.Position.X:i==1?v.Position.Y:v.Position.Z));
            for(int i=0;i<3;i++)m.F(mesh.Vertices.Max(v=>i==0?v.Position.X:i==1?v.Position.Y:v.Position.Z));
            foreach(var v in mesh.Vertices)
            {
                m.Floats([v.Position.X,v.Position.Y,v.Position.Z,v.Normal.X,v.Normal.Y,v.Normal.Z,
                          v.Uv.X,v.Uv.Y,v.Color.X,v.Color.Y,v.Color.Z,v.Color.W]);
                for(int j=0;j<4;j++)m.I(0);
                for(int j=0;j<4;j++)m.F(0);
            }
            foreach(int index in mesh.Indices)m.I(index);
        }
        mat.I(1);mat.Floats([1,1,1,1]);mat.I(-1);mat.I(0);mat.F(.5f);mat.I(0);
        tex.I(0);skins.I(0);clips.I(0);
        return Package.Write([n.Data,m.Data,mat.Data,tex.Data,skins.Data,clips.Data]);
    }
}
