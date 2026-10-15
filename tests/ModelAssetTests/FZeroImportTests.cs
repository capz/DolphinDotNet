using System.Buffers.Binary;
using System.Text;

// Original synthetic files written from the published F-Zero GX layouts.
// These fixtures are consumed by native C and managed AOT tests, not an offline converter.
internal static class FZeroImportTests
{
    private static int Align(int n)=>(n+31)&~31;
    private static void U32(byte[] p,int at,uint n)=>BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(at,4),n);
    private static void U16(byte[] p,int at,int n)=>BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(at,2),(ushort)n);
    private static void F32(byte[] p,int at,float n)=>U32(p,at,unchecked((uint)BitConverter.SingleToInt32Bits(n)));
    private static void Write32(Stream s,uint n){Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,n);s.Write(b);}
    private static void Write16(Stream s,int n){Span<byte> b=stackalloc byte[2];BinaryPrimitives.WriteInt16BigEndian(b,(short)n);s.Write(b);}
    private static void WriteFloat(Stream s,float n)=>Write32(s,unchecked((uint)BitConverter.SingleToInt32Bits(n)));
    private static byte[] Model(int primitive=0x98,bool fixedPoint=false,bool texture=false,bool nbt=false,bool matrix=false,bool translucent=false,bool additive=false,float z=0,bool indexed=false,int layers=1,bool normal=true,bool extra=false,bool customAmbient=false)
    {
        int layerCount=texture?layers:0,header=Align(64+layerCount*32+(matrix?48:0));
        uint attrs=(1u<<9)|(normal?1u<<10:0)|(texture?1u<<13:0)|(matrix?1u:0)|(nbt?1u<<25:0)|(1u<<11);
        if(nbt)attrs&=~(1u<<10);
        if(texture&&layers>1)attrs|=1u<<14;
        using var display=new MemoryStream();
        if(!indexed){display.WriteByte(0);display.WriteByte((byte)(primitive|(fixedPoint?1:0)));Write16(display,primitive==0x90?3:4);}
        else Write32(display,4);
        float[][] pos=[[0,0,z],[1,0,z],[1,1,z],[0,1,z]];
        using var pool=new MemoryStream();
        for(int i=0;i<(primitive==0x90?3:4);i++)
        {
            if(indexed)
            {
                Write32(display,(uint)(i*64));
                foreach(float p in pos[i])WriteFloat(pool,p);
                foreach(float n in new float[]{0,0,1})WriteFloat(pool,n);
                for(int j=0;j<3;j++){WriteFloat(pool,i is 1 or 2?1:0);WriteFloat(pool,i>=2?1:0);}
                Write32(pool,0xffffffff);Write32(pool,0);Write32(pool,0);Write32(pool,0);continue;
            }
            if(matrix)display.WriteByte(3); // Slot 1 -> model/shader mapping index zero.
            foreach(float p in pos[i]){if(fixedPoint)Write16(display,(int)(p*8192));else WriteFloat(display,p);}
            if(normal||nbt)foreach(float n in nbt?new float[]{0,0,1,1,0,0,0,1,0}:new float[]{0,0,1})
            {if(fixedPoint)Write16(display,(int)(n*16384));else WriteFloat(display,n);}
            Write32(display,0xffffffff);
            for(int j=0;j<(texture?layers:0);j++)
            {
                float u=i is 1 or 2?1:0,v=i>=2?1:0;
                if(fixedPoint){Write16(display,(int)(u*8192));Write16(display,(int)(v*8192));}
                else{WriteFloat(display,u);WriteFloat(display,v);}
            }
        }
        byte[] dl=display.ToArray();if(!indexed)Array.Resize(ref dl,Align(dl.Length));
        int shapeAt=header+(indexed?32:0),poolAt=shapeAt+96,displayAt=indexed?poolAt+(int)pool.Length:shapeAt+96;
        var b=new byte[displayAt+dl.Length+(extra?32+dl.Length*2:0)];
        Encoding.ASCII.GetBytes("GCMF").CopyTo(b,0);U32(b,4,indexed?8u:(fixedPoint?1u:0)|(matrix?4u:0));
        F32(b,20,10);U16(b,24,layerCount);U16(b,26,translucent?0:1);U16(b,28,translucent?1:0);b[30]=(byte)(matrix?1:0);U32(b,32,(uint)header);
        Array.Fill(b,(byte)255,40,8);
        for(int j=0;j<layerCount;j++)
        {
            int t=64+j*32;U32(b,t,0x114); // repeat T + repeat S + mipmaps
            U16(b,t+4,j);U16(b,t+14,j);
        }
        if(matrix)
        {
            int t=64+layerCount*32;F32(b,t,2);F32(b,t+12,3);F32(b,t+20,1);F32(b,t+40,1);b[40]=0;
        }
        if(indexed){U32(b,header,4);U32(b,header+8,(uint)(poolAt-header));U32(b,header+12,(uint)(displayAt-header));pool.ToArray().CopyTo(b,poolAt);}
        uint flags=1u|(additive?0x60u:0)|(customAmbient?8u:0);
        U32(b,shapeAt,flags);U32(b,shapeAt+4,0x804020ff);b[shapeAt+17]=(byte)(translucent?128:255);b[shapeAt+18]=(byte)layerCount;b[shapeAt+19]=(byte)(extra?14:2);
        for(int j=0;j<3;j++)U16(b,shapeAt+22+j*2,j<layerCount?j:-1);
        U32(b,shapeAt+28,attrs);Array.Fill(b,(byte)255,shapeAt+32,8);
        U32(b,shapeAt+44,(uint)(indexed?dl.Length/4:dl.Length));if(additive)U32(b,shapeAt+64,0x14);
        dl.CopyTo(b,displayAt);
        if(extra)
        {
            int t=displayAt+dl.Length;Array.Fill(b,(byte)255,t,8);U32(b,t+8,(uint)dl.Length);U32(b,t+12,(uint)dl.Length);
            dl.CopyTo(b,t+32);dl.CopyTo(b,t+32+dl.Length);
        }
        return b;
    }
    private static byte[] Gma(params (string Name,byte[] Data)[] models)
    {
        using var names=new MemoryStream();var offsets=new int[models.Length];
        for(int i=0;i<models.Length;i++){offsets[i]=(int)names.Position;names.Write(Encoding.ASCII.GetBytes(models[i].Name));names.WriteByte(0);}
        int start=Align(8+models.Length*8+(int)names.Length),length=start+models.Sum(m=>Align(m.Data.Length));var b=new byte[length];
        U32(b,0,(uint)models.Length);U32(b,4,(uint)start);names.ToArray().CopyTo(b,8+models.Length*8);
        int at=start;
        for(int i=0;i<models.Length;i++){U32(b,8+i*8,(uint)(at-start));U32(b,12+i*8,(uint)offsets[i]);models[i].Data.CopyTo(b,at);at+=Align(models[i].Data.Length);}
        return b;
    }
    private static int TexSize(int f,int w,int h)
    {
        int tw=f is 0 or 1 or 2 or 14?8:4,th=f is 0 or 14?8:4;
        return ((w+tw-1)/tw)*((h+th-1)/th)*(f==6?64:32);
    }
    private static byte[] Tpl(int format=6,int width=8,int height=8,int levels=4)
    {
        int length=32,w=width,h=height;
        for(int i=0;i<levels;i++){length+=format==14?Align(((w+3)&~3)*((h+3)&~3)/2):TexSize(format,w,h);w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
        var b=new byte[length];U32(b,0,1);U32(b,4,(uint)format);U32(b,8,32);U16(b,12,width);U16(b,14,height);U16(b,16,levels);U16(b,18,0x1234);
        int at=32;w=width;h=height;
        for(int level=0;level<levels;level++)
        {
            int size=format==14?Align(((w+3)&~3)*((h+3)&~3)/2):TexSize(format,w,h);
            if(format==6)for(int tile=0;tile<size;tile+=64)for(int k=0;k<16;k++){b[at+tile+k*2]=255;b[at+tile+k*2+1]=(byte)(30+level*10);b[at+tile+32+k*2]=40;b[at+tile+33+k*2]=50;}
            else if(format==14)for(int block=0;block<size;block+=8){U16(b,at+block,0xf800);U16(b,at+block+2,0x07e0);}
            else if(format is 4 or 5)for(int k=0;k<size;k+=2)U16(b,at+k,format==4?0xf800:0xfc00);
            else Array.Fill(b,format is 2 or 3?(byte)255:(byte)128,at,size);
            at+=size;w=Math.Max(1,w/2);h=Math.Max(1,h/2);
        }
        return b;
    }
    private static byte[] Lz(byte[] data,bool includeHeader=false,bool backrefs=false)
    {
        using var output=new MemoryStream();output.Write(new byte[8]);var ring=new byte[4096];int cursor=4078,at=0;
        while(at<data.Length)
        {
            long flagAt=output.Position;output.WriteByte(0);byte flags=0;
            for(int bit=0;bit<8&&at<data.Length;bit++)
            {
                int count=0;
                if(backrefs)while(count<18&&at+count<data.Length&&data[at+count]==0&&ring[count]==0)count++;
                if(count>=3){output.WriteByte(0);output.WriteByte((byte)(count-3));}
                else{count=1;flags|=(byte)(1<<bit);output.WriteByte(data[at]);}
                for(int k=0;k<count;k++)ring[cursor++&4095]=data[at++];
            }
            long end=output.Position;output.Position=flagAt;output.WriteByte(flags);output.Position=end;
        }
        var b=output.ToArray();BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0,4),b.Length-(includeHeader?0:8));BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4,4),data.Length);return b;
    }
    private static byte[] Arc(params (string Name,byte[] Data)[] files)
    {
        using var names=new MemoryStream();names.WriteByte(0);names.Write("assets\0"u8);var offsets=new int[files.Length];
        for(int i=0;i<files.Length;i++){offsets[i]=(int)names.Position;names.Write(Encoding.ASCII.GetBytes(files[i].Name));names.WriteByte(0);}
        int count=files.Length+2,root=32,header=count*12+(int)names.Length,data=Align(root+header);
        var b=new byte[data+files.Sum(f=>Align(f.Data.Length))];U32(b,0,0x55aa382d);U32(b,4,(uint)root);U32(b,8,(uint)header);U32(b,12,(uint)data);
        U32(b,root,0x01000000);U32(b,root+8,(uint)count);U32(b,root+12,0x01000001);U32(b,root+20,(uint)count);names.ToArray().CopyTo(b,root+count*12);
        for(int i=0;i<files.Length;i++){int n=root+(i+2)*12;U32(b,n,(uint)offsets[i]);U32(b,n+4,(uint)data);U32(b,n+8,(uint)files[i].Data.Length);files[i].Data.CopyTo(b,data);data+=Align(files[i].Data.Length);}
        return b;
    }
    internal static void Run(string output)
    {
        string sd=Path.Combine(output,"sd");void Save(string name,byte[] b)=>File.WriteAllBytes(Path.Combine(sd,name),b);
        byte[] model=Model(texture:true),tpl=Tpl(),gma=Gma(("Vehicle",model));
        Save("fz.gcmf",model);Save("fz.gma",gma);Save("fz.tpl",tpl);
        byte[] sampler=(byte[])model.Clone();U32(sampler,64,0x888);Save("fz-sampler.gcmf",sampler); // mirror S, clamp T, nearest, maxLOD=1
        byte[] noMip=(byte[])model.Clone();U32(noMip,64,0x14);Save("fz-no-mip.gcmf",noMip);
        Save("fz.gma.lz",Lz(gma));Save("fz.tpl.lz",Lz(tpl,true));Save("fz-backref.gma.lz",Lz(gma,true,true));
        byte[] arc=Arc(("vehicle.gma",gma),("vehicle.tpl",tpl));Save("fz.arc",arc);Save("fz.arc.lz",Lz(arc,true,true));
        Save("fz-inner.arc",Arc(("vehicle.gma.lz",Lz(gma,false,true)),("vehicle.tpl.lz",Lz(tpl,true,true))));
        Save("fz-ambiguous.arc",Arc(("one.gma",gma),("two.gma",gma),("vehicle.tpl",tpl)));
        var aliases=Enumerable.Range(0,256).Select(i=>("Alias"+i,Model())).ToArray();
        byte[] ambiguousRaw=Gma(aliases);
        Array.Resize(ref ambiguousRaw,65536);
        // All table entries share one valid GCMF and the remainder is padding.
        int aliasBase=(int)BinaryPrimitives.ReadUInt32BigEndian(ambiguousRaw.AsSpan(4,4));
        for(int i=0;i<256;i++)U32(ambiguousRaw,8+i*8,0);
        Save("fz-lz-header-collision.gma",ambiguousRaw);
        byte[] nestedCollision=Lz(Lz(Lz(ambiguousRaw,true,true),true,true),true,true);
        Save("fz-nested-3.gma.lz",nestedCollision);Save("fz-nested-4.gma.lz",Lz(nestedCollision,true,true));
        Save("fz-multiple.gma",Gma(("Float",Model(normal:false)),("Fixed",Model(fixedPoint:true)),("NBT",Model(nbt:true)),("Matrix",Model(matrix:true)),("Extra",Model(extra:true))));
        foreach(int primitive in new[]{0x80,0x88,0x90,0x98,0xa0})Save("fz-primitive-"+primitive+".gma",Gma(("Primitive",Model(primitive))));
        foreach(int format in new[]{0,1,2,3,4,5,6,14})Save("fz-format-"+format+".tpl",Tpl(format));
        Save("fz-small.tpl",Tpl(6,2,2,2));Save("fz-cmpr-bug.tpl",Tpl(14,4,16,1));
        Save("fz-layered.gma",Gma(("Layers",Model(texture:true,layers:2))));
        // A second TPL entry allows the approximation test to validate all references.
        byte[] two=new byte[tpl.Length+32];U32(two,0,2);Array.Copy(tpl,4,two,4,16);Array.Copy(tpl,4,two,20,16);U32(two,8,64);U32(two,24,64);Array.Copy(tpl,32,two,64,tpl.Length-32);Save("fz-two.tpl",two);
        Save("fz-indexed.gma",Gma(("SkinSnapshot",Model(indexed:true))));
        Save("fz-with-skin.gma",Gma(("Rigid",Model()),("Skin",Model(indexed:true))));
        byte[] blendModel=Model(translucent:true,additive:true);U32(blendModel,64+64,0x32);
        Save("fz-custom-blend.gma",Gma(("CustomBlend",blendModel)));
        Save("fz-translucent.gma",Gma(("Near",Model(translucent:true,z:-2)),("Opaque",Model(z:-4)),("Far",Model(translucent:true,z:-8)),("Additive",Model(translucent:true,additive:true,z:-6))));
        Save("fz-ambient.gma",Gma(("Ambient",Model(customAmbient:true))));
        byte[] huge=new byte[2*1024*1024+32];gma.CopyTo(huge,0);Save("fz-large.gma",huge);
        // Invalid format variants must fail transactionally, never publish partial models.
        var bad=new List<byte[]>();
        void Mutate(Action<byte[]> action){var b=(byte[])gma.Clone();action(b);bad.Add(b);}
        int modelAt=(int)BinaryPrimitives.ReadUInt32BigEndian(gma.AsSpan(4,4)),shape=modelAt+96;
        Mutate(b=>U32(b,0,0xffffffff));Mutate(b=>U32(b,4,16));Mutate(b=>U32(b,8,0xfffffff0));Mutate(b=>U32(b,12,0xfffffff0));
        Mutate(b=>U32(b,modelAt+32,0xfffffff0));Mutate(b=>U32(b,modelAt+4,0x80000000));Mutate(b=>U32(b,shape+44,0xfffffff0));
        Mutate(b=>b[shape+96+1]=0x61);Mutate(b=>b[shape+96+1]=0x9f);Mutate(b=>U16(b,shape+96+2,65535));
        Mutate(b=>U32(b,shape+28,0xffffffff));Mutate(b=>U16(b,shape+22,32767));Mutate(b=>U32(b,modelAt+64+4,0x7fff0000));
        Mutate(b=>U32(b,shape+96+4,0x7fc00000));Mutate(b=>b[shape+19]=0x82);
        foreach(int n in new[]{1,7,15,63,100,gma.Length-40})bad.Add(gma[..n]);
        byte[] truncated=Lz(gma);Array.Resize(ref truncated,truncated.Length-1);BinaryPrimitives.WriteInt32LittleEndian(truncated.AsSpan(0,4),truncated.Length-8);bad.Add(truncated);
        byte[] bomb=Lz(gma);BinaryPrimitives.WriteInt32LittleEndian(bomb.AsSpan(4,4),32*1024*1024);bad.Add(bomb);
        byte[] invalidArc=(byte[])arc.Clone();U32(invalidArc,32+20,4096);bad.Add(invalidArc);
        for(int i=0;i<bad.Count;i++)Save("fz-bad-"+i+".bin",bad[i]);
        Save("fz-bad-count.bin",BitConverter.GetBytes(bad.Count));
        byte[] badCommand=Model(texture:true);badCommand[96+96+1]=0x61;
        Save("fz-partial-failure.gma",Gma(("Valid",model),("Bad",badCommand)));
        byte[] badTpl=(byte[])tpl.Clone();U16(badTpl,18,0);Save("fz-bad.tpl",badTpl);
        Save("fz-unsupported.skl",new byte[64]);Save("fz-unsupported.mta",new byte[64]);Save("fz-unsupported.mal",new byte[64]);
        Console.WriteLine("Generated original F-Zero GX GMA/GCMF, TPL, LZSS, U8 and malformed-input fixtures.");
    }
}
