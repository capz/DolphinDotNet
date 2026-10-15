using System.Buffers.Binary;
using System.Text;

// Authored from the format layouts, without game data or the production loader.
// The native suite checks decoded geometry, pixels, weights and animation poses.
internal static class GameCubeImportTests
{
    private static int Align(int n)=>(n+31)&~31;
    private static void U32(byte[] p,int at,int n)=>BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(at,4),unchecked((uint)n));
    private static void U16(byte[] p,int at,int n)=>BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(at,2),unchecked((ushort)n));
    private static void F32(byte[] p,int at,float n)=>U32(p,at,BitConverter.SingleToInt32Bits(n));
    private static byte[] Chunk(string magic,int size){var p=new byte[Align(size)];Encoding.ASCII.GetBytes(magic).CopyTo(p,0);U32(p,4,p.Length);return p;}
    private static byte[] J3dFile(string magic,params byte[][] chunks)
    {
        var p=new byte[32+chunks.Sum(c=>c.Length)];Encoding.ASCII.GetBytes(magic).CopyTo(p,0);U32(p,8,p.Length);U32(p,12,chunks.Length);
        int at=32;foreach(var c in chunks){c.CopyTo(p,at);at+=c.Length;}return p;
    }
    private static int Names(byte[] p,int at,params string[] names)
    {
        U16(p,at,names.Length);U16(p,at+2,0xffff);int text=4+names.Length*4;
        for(int i=0;i<names.Length;i++){U16(p,at+6+i*4,text);var s=Encoding.ASCII.GetBytes(names[i]);s.CopyTo(p,at+text);text+=s.Length+1;}return text;
    }
    private static byte[] Texels(int format,int width=8,int height=8)
    {
        int tw=format is 0 or 1 or 2 or 8 or 9 or 14?8:4,th=format is 0 or 8 or 14?8:4;
        byte[] p=new byte[((width+tw-1)/tw)*((height+th-1)/th)*(format==6?64:32)];
        if(format is 0 or 1 or 2)Array.Fill(p,(byte)0xff);
        if(format==3)for(int i=0;i<p.Length;i+=2){p[i]=255;p[i+1]=127;}
        if(format is 4 or 5)for(int i=0;i<p.Length;i+=2)U16(p,i,format==4?0xf800:0xfc00);
        if(format==6)for(int i=0;i<p.Length;i+=64)for(int k=0;k<16;k++){p[i+k*2]=255;p[i+k*2+1]=255;}
        if(format==14)for(int i=0;i<p.Length;i+=8){U16(p,i,0xf800);U16(p,i+2,0x07e0);}
        return p;
    }
    private static byte[] Bti(int format=6,int paletteFormat=1,int width=8,int height=8,int levels=2)
    {
        var data=new List<byte>();int w=width,h=height;
        for(int i=0;i<levels;i++){data.AddRange(Texels(format,w,h));w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
        var p=new byte[64+data.Count];p[0]=(byte)format;U16(p,2,width);U16(p,4,height);p[6]=1;p[7]=2;
        if(format is 8 or 9 or 10){p[8]=1;p[9]=(byte)paletteFormat;U16(p,10,1);U32(p,12,32);U16(p,32,paletteFormat==0?0xff7f:paletteFormat==1?0xf800:0xfc00);}
        p[20]=(byte)(levels>1?5:1);p[21]=1;p[24]=(byte)levels;p[23]=(byte)((levels-1)*8);U32(p,28,64);data.CopyTo(p,64);return p;
    }
    private static byte[] PalettePattern(int format)
    {
        var p=Bti(format,0);U16(p,10,16);for(int i=0;i<16;i++)U16(p,32+i*2,0xff00|i*17);
        int at=64;
        foreach(int size in new[]{8,4}){
            int tw=format==10?4:8,th=format==8?8:4;
            for(int by=0;by<size;by+=th)for(int bx=0;bx<size;bx+=tw){
                for(int y=0;y<th;y++)for(int x=0;x<tw;x++){
                    int index=(bx+x+2*(by+y))%16,k=y*tw+x;
                    if(format==8)p[at+k/2]|=(byte)(index<<(k%2==0?4:0));
                    else if(format==9)p[at+k]=(byte)index;else U16(p,at+k*2,index);}
                at+=32;}}
        return p;
    }
    private static byte[] Model(int format=6,int palette=1,bool weighted=false,bool bdl=false,int primitive=0x90,bool fixedPoint=false,bool carry=false,bool texturePattern=false)
    {
        var inf=Chunk("INF1",80);U16(inf,8,0);U32(inf,20,24);
        int[] hierarchy=weighted?[0x10,0,1,0,0x10,1,1,0,0x11,0,1,0,0x12,0,2,0,2,0,2,0,0,0]:[0x10,0,1,0,0x11,0,1,0,0x12,0,2,0,2,0,0,0];
        for(int i=0;i<hierarchy.Length;i++)U16(inf,24+i*2,hierarchy[i]);
        var vtx=Chunk("VTX1",320);U32(vtx,8,64);U32(vtx,12,160);U32(vtx,16,208);U32(vtx,24,224);U32(vtx,32,240);
        int[] attrs=[9,10,11,13];for(int i=0;i<4;i++){int at=64+i*16;U32(vtx,at,attrs[i]);U32(vtx,at+4,attrs[i]==10?0:1);U32(vtx,at+8,attrs[i]==11?5:fixedPoint?3:4);vtx[at+12]=8;}
        U32(vtx,128,255);
        float[][] positions=[[0,0,0],[1,0,0],[0,1,0],[1,1,0]];
        for(int i=0;i<4;i++)for(int k=0;k<3;k++){if(fixedPoint)U16(vtx,160+i*6+k*2,(int)(positions[i][k]*256));else F32(vtx,160+i*12+k*4,positions[i][k]);}
        if(fixedPoint)U16(vtx,212,16384);else F32(vtx,216,1);
        Array.Fill(vtx,(byte)255,224,4);
        for(int i=0;i<4;i++){if(fixedPoint){U16(vtx,240+i*4,i is 1 or 3?256:0);U16(vtx,242+i*4,i>=2?256:0);}else{F32(vtx,240+i*8,i is 1 or 3?1:0);F32(vtx,244+i*8,i>=2?1:0);}}
        int joints=weighted?2:1;
        var jnt=Chunk("JNT1",256);U16(jnt,8,joints);U16(jnt,10,0xffff);U32(jnt,12,32);U32(jnt,16,160);U32(jnt,20,192);
        for(int i=0;i<joints;i++){int at=32+i*64;F32(jnt,at+4,1);F32(jnt,at+8,1);F32(jnt,at+12,1);F32(jnt,at+24,1);U16(jnt,160+i*2,i);}
        Names(jnt,192,weighted?["Root","Child"]:["Root"]);
        var drw=Chunk("DRW1",64);U16(drw,8,1);U32(drw,12,32);U32(drw,16,40);drw[32]=(byte)(weighted?1:0);
        var evp=Chunk("EVP1",weighted?192:32);
        if(weighted){U16(evp,8,1);U32(evp,12,32);U32(evp,16,40);U32(evp,20,48);U32(evp,24,64);evp[32]=2;U16(evp,40,0);U16(evp,42,1);F32(evp,48,.5f);F32(evp,52,.5f);
            for(int i=0;i<2;i++){int at=64+i*48;F32(evp,at,1);F32(evp,at+20,1);F32(evp,at+40,1);F32(evp,at+12,-1-i);}}
        var shp=Chunk("SHP1",carry?384:320);U16(shp,8,1);U32(shp,12,64);U32(shp,16,112);U32(shp,24,128);U32(shp,28,176);U32(shp,32,224);U32(shp,36,184);U32(shp,40,208);
        shp[64]=(byte)(weighted?3:0);U16(shp,66,carry?2:1);U16(shp,68,0);U16(shp,70,0);U16(shp,72,0);
        int[] declarations=[0,1,9,3,10,2,11,2,13,3,255,0];for(int i=0;i<declarations.Length;i++)U32(shp,128+i*4,declarations[i]);
        U16(shp,176,0);U16(shp,178,65535);U16(shp,186,1);U32(shp,188,0);U32(shp,208,32);U32(shp,212,0);
        if(carry){U16(shp,194,1);U32(shp,196,1);U32(shp,216,32);U32(shp,220,32);}
        int num=primitive is 0x80 or 0x88?4:3;
        for(int group=0;group<(carry?2:1);group++){int at=224+group*32;shp[at]=(byte)primitive;U16(shp,at+1,num);
            for(int i=0;i<num;i++){int d=at+3+i*7;shp[d]=0;U16(shp,d+1,i);shp[d+3]=0;shp[d+4]=0;U16(shp,d+5,i);}}
        var mat=Chunk("MAT3",640);U16(mat,8,1);U32(mat,12,160);U32(mat,16,496);U32(mat,20,500);
        Array.Fill(mat,(byte)255,160,332);mat[160]=1;for(int i=1;i<8;i++)mat[160+i]=0;
        U16(mat,168,0);U16(mat,172,0);U16(mat,174,0);U16(mat,200,0);U16(mat,292,0);U16(mat,348,0);U16(mat,388,0);U16(mat,486,0);U16(mat,488,0);
        U32(mat,28,528);U32(mat,32,532);U32(mat,40,536);U32(mat,72,544);U32(mat,76,548);U32(mat,92,552);U32(mat,108,576);U32(mat,112,584);U32(mat,116,588);U32(mat,36,592);U32(mat,52,593);U32(mat,88,594);U32(mat,56,600);mat[592]=mat[593]=mat[594]=1;mat[600]=1;mat[601]=4;mat[602]=60;mat[603]=255;
        U32(mat,528,2);Array.Fill(mat,(byte)255,532,4);U16(mat,544,0);mat[548]=0;mat[549]=0;mat[550]=4;mat[551]=255;
        byte[] tev=[255,15,8,10,15,0,0,0,1,0,7,4,5,7,0,0,0,1,0,255];tev.CopyTo(mat,552);
        mat[576]=7;mat[579]=7;mat[584]=0;mat[588]=1;mat[589]=3;mat[590]=1;Names(mat,500,"Diffuse");
        var bti=texturePattern?PalettePattern(format):Bti(format,palette);var tex=Chunk("TEX1",32+bti.Length);U16(tex,8,1);U32(tex,12,32);bti.CopyTo(tex,32);
        return J3dFile(bdl?"J3D2bdl4":"J3D2bmd3",bdl?[inf,vtx,evp,drw,jnt,shp,mat,tex,Chunk("MDL3",32)]:[inf,vtx,evp,drw,jnt,shp,mat,tex]);
    }
    private static byte[] Animation(bool full=false)
    {
        var a=Chunk(full?"ANF1":"ANK1",full?512:256);a[8]=2;if(full)a[9]=255;U16(a,10,30);U16(a,12,1);U32(a,20,64);U32(a,24,128);U32(a,28,144);U32(a,32,160);
        U16(a,14,1);U16(a,16,1);U16(a,18,full?32:7);F32(a,128,1);U16(a,144,0);
        if(full){for(int i=0;i<31;i++)F32(a,160+i*4,1+2*i/30f);F32(a,284,0);}
        else{float[] tx=[0,1,0,30,3,0,0];for(int i=0;i<tx.Length;i++)F32(a,160+i*4,tx[i]);}
        for(int axis=0;axis<3;axis++)for(int property=0;property<3;property++){
            int d=64+(axis*3+property)*(full?4:6);U16(a,d,property==2&&axis==0?(full?31:2):1);
            U16(a,d+2,property==2&&axis!=0?(full?31:6):0);}
        return J3dFile(full?"J3D1bca1":"J3D1bck1",a);
    }
    private static byte[] Compress(byte[] data,bool yay)
    {
        int masks=(data.Length+7)/8;
        if(yay){var p=new byte[16+masks+data.Length];"Yay0"u8.CopyTo(p);U32(p,4,data.Length);U32(p,8,16+masks);U32(p,12,16+masks);Array.Fill(p,(byte)255,16,masks);data.CopyTo(p,16+masks);return p;}
        using var s=new MemoryStream();s.Write(new byte[16]);for(int i=0;i<data.Length;i+=8){s.WriteByte(255);s.Write(data,i,Math.Min(8,data.Length-i));}
        var outp=s.ToArray();"Yaz0"u8.CopyTo(outp);U32(outp,4,data.Length);return outp;
    }
    private static byte[] Rarc(params (string name,byte[] data)[] entries)
    {
        int files=entries.Length,ft=80,strings=Align(ft+files*20),data=Align(strings+entries.Sum(e=>e.name.Length+1));
        var p=new byte[data+entries.Sum(e=>Align(e.data.Length))];"RARC"u8.CopyTo(p);U32(p,4,p.Length);U32(p,8,32);U32(p,12,data-32);U32(p,16,p.Length-data);U32(p,20,p.Length-data);
        U32(p,32,1);U32(p,36,32);U32(p,40,files);U32(p,44,ft-32);U32(p,48,entries.Sum(e=>e.name.Length+1));U32(p,52,strings-32);
        "ROOT"u8.CopyTo(p.AsSpan(64));U16(p,74,files);int so=0,offset=0;
        for(int i=0;i<files;i++){int at=ft+i*20;U16(p,at,i);U32(p,at+4,0x11000000|so);U32(p,at+8,offset);U32(p,at+12,entries[i].data.Length);Encoding.ASCII.GetBytes(entries[i].name).CopyTo(p,strings+so);entries[i].data.CopyTo(p,data+offset);so+=entries[i].name.Length+1;offset+=Align(entries[i].data.Length);}return p;
    }
    private static byte[] CompressRuns(byte[] data,bool yay)
    {
        var masks=new List<byte>();var links=new List<byte>();var literals=new List<byte>();var interleaved=new List<byte>();int at=0;
        while(at<data.Length){int maskIndex=interleaved.Count;interleaved.Add(0);byte flags=0;
            for(int bit=0;bit<8&&at<data.Length;bit++){
                int count=0;if(at>0)while(at+count<data.Length&&count<273&&data[at+count]==data[at-1])count++;
                if(count>=3){int code=count>=18?0:(count-2)<<12;byte hi=(byte)(code>>8),lo=(byte)code;links.Add(hi);links.Add(lo);interleaved.Add(hi);interleaved.Add(lo);
                    if(count>=18){literals.Add((byte)(count-18));interleaved.Add((byte)(count-18));}at+=count;}
                else{flags|=(byte)(0x80>>bit);literals.Add(data[at]);interleaved.Add(data[at++]);}}
            interleaved[maskIndex]=flags;masks.Add(flags);}
        if(!yay){var p=new byte[16+interleaved.Count];"Yaz0"u8.CopyTo(p);U32(p,4,data.Length);interleaved.CopyTo(p,16);return p;}
        while(masks.Count%4!=0)masks.Add(0);
        var outp=new byte[16+masks.Count+links.Count+literals.Count];"Yay0"u8.CopyTo(outp);U32(outp,4,data.Length);U32(outp,8,16+masks.Count);U32(outp,12,16+masks.Count+links.Count);masks.CopyTo(outp,16);links.CopyTo(outp,16+masks.Count);literals.CopyTo(outp,16+masks.Count+links.Count);return outp;
    }
    private static byte[] Re4(bool weighted=false,bool s8=false,int primitive=0x90,bool differentNormal=false)
    {
        var p=new byte[416];U32(p,0,96);U32(p,8,80);U32(p,12,192);U32(p,16,224);U32(p,20,256);p[24]=1;p[25]=(byte)(weighted?2:1);U16(p,26,1);U32(p,28,320);
        U32(p,32,unchecked((int)(0x80000000u|(s8?0x20000000u:0))));U32(p,36,1);U32(p,48,128);U32(p,52,160);U16(p,56,3);U16(p,58,1);U32(p,60,0x20030818);
        p[97]=255;F32(p,100,1);if(weighted){p[113]=0;F32(p,116,1);}
        U16(p,136,1);U16(p,146,1);
        if(s8)p[162]=64;else U16(p,164,16384);
        p[256]=0;p[257]=1;p[259]=(byte)(weighted?2:1);p[260]=50;p[261]=50;
        if(differentNormal){p[24]=2;p[267]=1;p[268]=100;if(s8)p[163]=1;else U16(p,166,1);}
        Array.Fill(p,(byte)255,192,4);U16(p,228,256);U16(p,234,256);
        p[332]=0;U32(p,344,64);U32(p,348,1);p[352]=(byte)primitive;U16(p,353,3);
        for(int i=0;i<3;i++){int at=355+i*8;U16(p,at,i);U16(p,at+2,0);U16(p,at+4,0);U16(p,at+6,i);}return p;
    }
    private static byte[] Tpl(int format=6,int palette=1)
    {
        byte[] bti=Bti(format,palette);var p=new byte[128+bti.Length-64];U32(p,0,0x0020af30);U32(p,4,1);U32(p,8,12);U32(p,12,32);U32(p,16,format is 8 or 9 or 10?80:0);
        U16(p,32,8);U16(p,34,8);U32(p,36,format);U32(p,40,128);U32(p,44,1);U32(p,48,2);U32(p,52,5);U32(p,56,1);p[66]=1;
        U16(p,80,1);U32(p,84,palette);U32(p,88,96);bti.AsSpan(32,2).CopyTo(p.AsSpan(96));bti.AsSpan(64).CopyTo(p.AsSpan(128));return p;
    }
    private static byte[] Fcv(int format=0,int kind=4)
    {
        var p=new byte[256];U16(p,0,30);p[2]=1;U16(p,3,(format<<12)|kind);p[5]=0;U32(p,8,256);U32(p,12,16);int at=16;
        int vtype=format/4,ttype=format%4;void Value(float value,int type){if(type==0){F32(p,at,value);at+=4;}else if(type==1){U16(p,at,(int)(value*10000));at+=2;}else{p[at++]=unchecked((byte)(sbyte)(value*10000));}}
        for(int axis=0;axis<3;axis++){U16(p,at,axis==0?2:1);at+=2;if(axis==0){U16(p,at,0);U16(p,at+2,30);at+=4;}else{U16(p,at,0);at+=2;}
            for(int key=0;key<(axis==0?2:1);key++){Value(axis==0?(vtype==2?.005f+.005f*key:1+key):0,vtype);Value(0,ttype);Value(0,ttype);}}
        return p;
    }
    private static byte[] Body(params (string tag,byte[] data)[] entries)
    {
        int offset=Align(16+entries.Length*8);var p=new byte[offset+entries.Sum(e=>Align(e.data.Length))];U32(p,0,entries.Length);
        for(int i=0;i<entries.Length;i++){U32(p,16+i*4,offset);Encoding.ASCII.GetBytes(entries[i].tag).CopyTo(p,16+entries.Length*4+i*4);entries[i].data.CopyTo(p,offset);offset+=Align(entries[i].data.Length);}return p;
    }
    private static byte[] Drs(byte[] body)
    {
        var p=new byte[1024+body.Length];U32(p,32,0);U32(p,36,body.Length);U32(p,44,1024);U32(p,64,-2);U32(p,76,p.Length);U32(p,96,-1);body.CopyTo(p,1024);return p;
    }
    private static byte[] RotationMotion(bool j3d)
    {
        if(j3d){var b=Animation();U16(b,48,3);for(int axis=0;axis<3;axis++){U16(b,32+64+(axis*3+1)*6+2,axis);U16(b,32+144+axis*2,4096*(axis+1));}return b;}
        var p=new byte[128];U16(p,0,30);p[2]=1;U16(p,3,2);p[5]=0;U32(p,8,128);U32(p,12,16);
        for(int axis=0;axis<3;axis++){int at=16+axis*16;U16(p,at,1);U16(p,at+2,0);F32(p,at+4,(axis+1)*.25f);}return p;
    }
    internal static void Run(string output)
    {
        string sd=Path.Combine(output,"sd");void Save(string name,byte[] data)=>File.WriteAllBytes(Path.Combine(sd,name),data);
        byte[] model=Model(),anim=Animation(),bca=Animation(true),re4=Re4(),tpl=Tpl(),fcv=Fcv();
        Save("sms.bmd",model);Save("sms.bdl",Model(bdl:true));Save("sms-weighted.bmd",Model(weighted:true));Save("sms-fixed.bmd",Model(fixedPoint:true));Save("sms-carry.bmd",Model(carry:true));Save("sms.bck",anim);Save("sms.bca",bca);
        Save("sms-rotation.bck",RotationMotion(true));Save("re4-rotation.fcv",RotationMotion(false));
        var separate=(byte[])anim.Clone();U16(separate,32+18,9);U16(separate,32+64+2*6+4,1);U16(separate,32+64+5*6+2,8);U16(separate,32+64+8*6+2,8);
        float[] hermite=[0,1,0,1f/15,30,3,-1f/15,0,0];for(int i=0;i<hermite.Length;i++)F32(separate,32+160+i*4,hermite[i]);Save("sms-separate.bck",separate);
        var fccHermite=(byte[])fcv.Clone();F32(fccHermite,30,2);F32(fccHermite,38,-2);Save("re4-hermite.fcv",fccHermite);
        Save("sms.szs",Compress(model,false));Save("sms.szp",Compress(model,true));Save("sms.arc",Rarc(("model.bmd",model),("move.bck",anim)));Save("sms-ambiguous.arc",Rarc(("one.bmd",model),("two.bmd",model)));
        Save("sms-runs.szs",CompressRuns(model,false));Save("sms-runs.szp",CompressRuns(model,true));
        Save("sms-member.arc",Rarc(("model.bmd",Compress(model,false)),("move.bck",Compress(anim,true))));Save("sms-archive.szs",Compress(Rarc(("model.bmd",model),("move.bck",anim)),false));
        foreach(int f in new[]{0,1,2,3,4,5,6,8,9,10,14}){Save($"sms-format-{f}.bmd",Model(f));Save($"re4-format-{f}.tpl",Tpl(f));}
        for(int palette=0;palette<3;palette++)Save($"sms-palette-{palette}.bmd",Model(8,palette));
        foreach(int format in new[]{8,9,10})Save($"sms-pattern-{format}.bmd",Model(format,texturePattern:true));
        foreach(int primitive in new[]{0x80,0x88,0x90,0x98,0xa0})Save($"sms-primitive-{primitive}.bmd",Model(primitive:primitive));
        Save("re4.bin",re4);Save("re4-weighted.bin",Re4(weighted:true));Save("re4-s8.bin",Re4(weighted:true,s8:true));Save("re4-normal-palette.bin",Re4(weighted:true,differentNormal:true));Save("re4.tpl",tpl);Save("re4.fcv",fcv);
        for(int fmt=0;fmt<11;fmt++)if(fmt%4<3)Save($"re4-fcc-{fmt}.fcv",Fcv(fmt));
        Save("re4-ik.fcv",Fcv(kind:0x10));Save("re4-camera.fcv",Fcv(kind:0x604));
        byte[] body=Body(("BIN",re4),("TPL",tpl),("FCV",fcv));Save("re4.dat",body);Save("re4.drs",Drs(body));
        Save("re4-ambiguous.dat",Body(("BIN",re4),("BIN",re4),("TPL",tpl)));
        var bad=(byte[])model.Clone();U32(bad,36,int.MaxValue);Save("sms-bad-chunk.bmd",bad);
        bad=(byte[])anim.Clone();U32(bad,32+20,int.MaxValue);Save("sms-bad-motion.bck",bad);
        bad=(byte[])re4.Clone();U32(bad,48,int.MaxValue);Save("re4-bad.bin",bad);
        bad=(byte[])tpl.Clone();U32(bad,40,int.MaxValue);Save("re4-bad.tpl",bad);
        bad=(byte[])fcv.Clone();U32(bad,12,int.MaxValue);Save("re4-bad.fcv",bad);
    }
}
