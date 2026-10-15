using System.Buffers.Binary;
using System.Text;

// Independently authored miniature assets; no game data is distributed.
internal static class PsoEpisode3Fixtures
{
    static void U(byte[] p,int a,uint n)=>BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(a),n);
    static void H(byte[] p,int a,int n)=>BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(a),unchecked((ushort)n));
    static void F(byte[] p,int a,float n)=>U(p,a,BitConverter.SingleToUInt32Bits(n));
    static byte[] Prs(byte[] p,bool copies=false)
    {
        var result=new List<byte>();int control=-1,bits=8;
        void Bit(int b){if(bits==8){control=result.Count;result.Add(0);bits=0;}result[control]|=(byte)(b<<bits++);}
        for(int i=0;i<p.Length;){
            // Exercise overlapping short references, including control-byte boundaries.
            if(copies&&i>0&&i+1<p.Length&&p[i]==p[i-1]&&p[i+1]==p[i-1]){int n=2;while(n<5&&i+n<p.Length&&p[i+n]==p[i-1])n++;Bit(0);Bit(0);Bit((n-2)>>1);Bit((n-2)&1);result.Add(255);i+=n;}
            else{Bit(1);result.Add(p[i++]);}
        }
        Bit(0);Bit(1);result.Add(0);result.Add(0);return result.ToArray();
    }
    static byte[] Ninja(bool weighted=false,int vertexType=41)
    {
        var p=new byte[768];
        void Node(int a,int attach,float x){U(p,a+4,(uint)attach);F(p,a+8,x);for(int k=0;k<3;k++)F(p,a+32+k*4,1);}
        Node(32,160,1);if(weighted){U(p,76,96);Node(96,184,1);}
        U(p,160,224);U(p,164,weighted?0u:512u);if(weighted){U(p,184,336);U(p,188,512);}
        void Vertices(int at,int bone){int type=weighted?44:vertexType;int stride=type==44?28:type==48?16:24;U(p,at,(uint)((1+3*stride/4)<<16|((bone==1?2:0)<<8)|type));H(p,at+4,3);
            for(int i=0;i<3;i++){int v=at+8+i*stride;F(p,v,(i==1?1:0)-bone);F(p,v+4,i==2?1:0);if(type==48)U(p,v+12,511);else F(p,v+20,1);if(weighted)U(p,v+24,(uint)((bone==0?128:127)<<16|i));}
            U(p,at+8+3*stride,255);
        }
        Vertices(224,0);if(weighted)Vertices(336,1);
        H(p,512,0x2511);H(p,514,2);U(p,516,0xffffffff);H(p,520,8);H(p,522,0);
        H(p,524,65);H(p,526,11);H(p,528,1);H(p,530,3);for(int i=0;i<3;i++){H(p,532+i*6,i);H(p,534+i*6,i==1?255:0);H(p,536+i*6,i==2?255:0);}H(p,550,255);
        // One root slot, followed by real delta-coded relocation entries.
        U(p,640,32);int[] fields=weighted?[36,76,100,160,184,188,640]:[36,160,164,640];
        int previous=0;for(int i=0;i<fields.Length;i++){H(p,656+i*2,(fields[i]-previous)/4);previous=fields[i];}
        U(p,736,656);U(p,740,(uint)fields.Length);U(p,744,1);U(p,752,640);return p;
    }
    static byte[] Motion(int bones=1,int count=1)
    {
        int table=32,boneTable=64,header=boneTable+bones*8,keys=header+12,slot=(keys+32*count+3)&~3;
        var p=new byte[slot+4+16];
        // Put the entry table immediately before the root slot, as in REL libraries.
        table=slot-count*8; // allocate a separate final table after all key arrays below
        slot=(keys+32*count+count*8+3)&~3;table=slot-count*8;p=new byte[slot+20];
        for(int i=0;i<count;i++){U(p,table+i*8+4,(uint)header);}
        U(p,boneTable,(uint)keys);U(p,boneTable+4,2);U(p,header,(uint)boneTable);U(p,header+4,30);H(p,header+8,1);H(p,header+10,1);
        F(p,keys+4,1);U(p,keys+16,30);F(p,keys+20,3);U(p,slot,(uint)table);U(p,p.Length-16,(uint)slot);return p;
    }
    static byte[] Bml(byte[] model,byte[] texture,bool duplicate=false)
    {
        var m=Prs(model,true);var t=Prs(texture,true);int n=duplicate?2:1,end=2048;for(int i=0;i<n;i++){end=(end+m.Length+31)&~31;end=(end+t.Length+31)&~31;}
        var p=new byte[end];U(p,4,(uint)n);int at=2048;for(int i=0;i<n;i++){int e=64+i*64;Encoding.ASCII.GetBytes(i==0?"npc.rel":"other.rel").CopyTo(p,e);U(p,e+32,(uint)m.Length);U(p,e+40,(uint)model.Length);U(p,e+44,(uint)t.Length);U(p,e+48,(uint)texture.Length);m.CopyTo(p,at);at=(at+m.Length+31)&~31;t.CopyTo(p,at);at=(at+t.Length+31)&~31;}return p;
    }
    internal static void Run(string output)
    {
        string sd=Path.Combine(output,"sd");void Save(string n,byte[] p)=>File.WriteAllBytes(Path.Combine(sd,n),p);
        var gj=File.ReadAllBytes(Path.Combine(sd,"direct.gj"));var gvm=File.ReadAllBytes(Path.Combine(sd,"direct.gvm"));var nj=Ninja();
        Save("ep3.rel",nj);
        var sampler=(byte[])nj.Clone();H(sampler,520,0x6008);H(sampler,522,0x4000);Save("ep3-sampler.rel",sampler);
        sampler=(byte[])nj.Clone();H(sampler,522,0x8000);Save("ep3-mip.rel",sampler);
        sampler=(byte[])nj.Clone();H(sampler,520,9);Save("ep3-second.rel",sampler);Save("ep3-weighted.rel",Ninja(true));Save("ep3-packed.rel",Ninja(vertexType:48));
        Save("ep3.prs",Prs(nj));Save("ep3-copy.prs",Prs(nj,true));Save("ep3-gj.prs",Prs(gj,true));Save("ep3-gvm.prs",Prs(gvm,true));
        Save("ep3.bml",Bml(nj,gvm));Save("ep3-multiple.bml",Bml(nj,gvm,true));Save("ep3-motion.rel",Motion());Save("ep3-motion-two.rel",Motion(count:2));Save("ep3-weighted-motion.rel",Motion(2));
        var bad=(byte[])nj.Clone();U(bad,640,0x666d7432);Save("ep3-fmt2.rel",bad);
        bad=(byte[])nj.Clone();H(bad,656,65535);Save("ep3-bad-reloc.rel",bad);
        bad=(byte[])nj.Clone();U(bad,76,32);Save("ep3-cycle.rel",bad);
        bad=(byte[])nj.Clone();H(bad,532,255);Save("ep3-bad-index.rel",bad);
        bad=Motion();H(bad,72+8,0x10);Save("ep3-camera.rel",bad);
        Save("ep3-bad.prs",new byte[]{0,0});Save("ep3-truncated.prs",Prs(nj)[..^1]);
        var chunk=new byte[8+nj.Length];"NJCM"u8.CopyTo(chunk);BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4),(uint)nj.Length);nj.CopyTo(chunk,8);
        // Wrapped NJCM's node starts at zero; redirect its root descriptor to our attach.
        nj.AsSpan(32,52).CopyTo(chunk.AsSpan(8));Save("ep3.nj",chunk);
    }
}
