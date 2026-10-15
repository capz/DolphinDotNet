using System.Buffers.Binary;
using System.Text;

// Original synthetic streams, independent of the production native readers.
internal static class RenderWareMeleeFixtures
{
    static void Be32(byte[] p,int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(at,4),value);
    static void Be16(byte[] p,int at,int value)=>BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(at,2),(ushort)value);
    static void Bf(byte[] p,int at,float value)=>Be32(p,at,BitConverter.SingleToUInt32Bits(value));
    static void Le32(byte[] p,int at,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(at,4),value);
    static void Le16(byte[] p,int at,int value)=>BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(at,2),(ushort)value);
    static void Lf(byte[] p,int at,float value)=>Le32(p,at,BitConverter.SingleToUInt32Bits(value));
    static byte[] Cat(params byte[][] data){var result=new byte[data.Sum(p=>p.Length)];int at=0;foreach(var p in data){p.CopyTo(result,at);at+=p.Length;}return result;}
    static byte[] Rw(uint type,params byte[][] data){byte[] body=Cat(data),p=new byte[12+body.Length];Le32(p,0,type);Le32(p,4,(uint)body.Length);Le32(p,8,0x1803ffff);body.CopyTo(p,12);return p;}
    static byte[] Word(uint value){var p=new byte[4];Le32(p,0,value);return p;}
    static byte[] Text(string text)=>Rw(2,Encoding.ASCII.GetBytes(text+"\0"));
    static byte[] Mat(){var p=new byte[28];Array.Fill(p,(byte)255,4,4);Le32(p,12,1);Lf(p,16,1);Lf(p,24,1);return Rw(7,Rw(1,p),Rw(6,Rw(1,Word(0x1102)),Text("Diffuse"),Text(""),Rw(3)),Rw(3));}
    static byte[] Frames(bool skeleton)
    {
        int count=skeleton?2:1;var p=new byte[4+count*56];Le32(p,0,(uint)count);
        for(int i=0;i<count;i++){int at=4+i*56;Lf(p,at,1);Lf(p,at+16,1);Lf(p,at+32,1);Lf(p,at+36,1);Le32(p,at+48,i==0?uint.MaxValue:0);}
        var extensions=new List<byte[]>();
        for(int i=0;i<count;i++){var hanim=new byte[i==0?44:12];Le32(hanim,0,0x100);Le32(hanim,4,(uint)(10+i));
            if(i==0){Le32(hanim,8,2);Le32(hanim,12,0);Le32(hanim,16,36);Le32(hanim,20,10);Le32(hanim,24,0);Le32(hanim,32,11);Le32(hanim,36,1);}
            extensions.Add(skeleton?Rw(3,Rw(0x11e,hanim)):Rw(3));}
        return Rw(0xe,Cat([Rw(1,p),..extensions]));
    }
    static byte[] Skin(bool native)
    {
        int at=6;var p=new byte[6+(native?12:60)+128];p[0]=2;p[1]=2;p[2]=2;p[4]=0;p[5]=1;
        for(int i=0;i<3;i++){p[at+i*(native?2:4)]=0;p[at+i*(native?2:4)+1]=1;}at+=native?6:12;
        for(int i=0;i<3;i++){if(native){p[at+i*2]=64;p[at+i*2+1]=64;}else{Lf(p,at+i*16,.5f);Lf(p,at+i*16+4,.5f);}}at+=native?6:48;
        for(int i=0;i<2;i++)for(int k=0;k<16;k++){float v=k is 0 or 5 or 10 or 15?1:k==12?-1-i:0;if(native)Bf(p,at+i*64+k*4,v);else Lf(p,at+i*64+k*4,v);}
        return Rw(0x116,native?Rw(1,Cat(Word(6),p)):p);
    }
    static byte[] Geometry(bool native,bool skeleton,bool prefix=false)
    {
        uint flags=0x5e|(native?0x01000000u:0);var g=new byte[native?40:16+12+24+8+24+36+36];Le32(g,0,flags);Le32(g,4,1);Le32(g,8,3);Le32(g,12,1);
        var matList=Rw(8,Rw(1,Cat(Word(1),Word(uint.MaxValue))),Mat());
        if(!native){int at=16;Array.Fill(g,(byte)255,at,12);at+=12;Lf(g,at+8,1);Lf(g,at+20,1);at+=24;Le16(g,at,1);Le16(g,at+2,0);Le16(g,at+6,2);at+=8;
            Lf(g,at+12,2);Le32(g,at+16,1);Le32(g,at+20,1);at+=24;Lf(g,at+12,1);Lf(g,at+28,1);at+=36;for(int i=0;i<3;i++)Lf(g,at+i*12+8,1);
            return Rw(0xf,Rw(1,g),matList,skeleton?Rw(3,Skin(false)):Rw(3));}
        Le32(g,32,1);Le32(g,36,1);var splits=new byte[20];Le32(splits,0,1);Le32(splits,4,1);Le32(splits,8,3);Le32(splits,12,3);
        var n=new byte[8+52+160];Le32(n,0,52);Le32(n,4,160);Be32(n,16,4);
        int[] attr=[9,10,11,13],offset=[32,80,96,112],stride=[12,12,4,8],types=[3,2,2,3];
        for(int i=0;i<4;i++){int at=20+i*8;Be32(n,at,(uint)offset[i]);n[at+4]=(byte)attr[i];n[at+5]=(byte)stride[i];n[at+6]=(byte)types[i];}
        Be32(n,52,0);Be32(n,56,32);int d=60;n[d]=0x90;Be16(n,d+1,3);int cur=d+3;
        for(int i=0;i<3;i++){if(prefix)n[cur++]=0;Be16(n,cur,i);cur+=2;n[cur++]=0;n[cur++]=0;Be16(n,cur,i);cur+=2;}
        Bf(n,60+32+12,1);Bf(n,60+32+28,1);Bf(n,60+80+8,1);Array.Fill(n,(byte)255,60+96,4);Bf(n,60+112+8,1);Bf(n,60+112+20,1);
        var nativePlugin=Rw(0x510,Rw(1,Cat(Word(6),n)));
        return Rw(0xf,Rw(1,g),matList,skeleton?Rw(3,Rw(0x50e,splits),nativePlugin,Skin(true)):Rw(3,Rw(0x50e,splits),nativePlugin));
    }
    static byte[] Dff(bool native=false,bool skeleton=false,bool prefix=false)
    {
        var atomic=new byte[16];Le32(atomic,8,5);
        return Rw(0x10,Rw(1,Cat(Word(1),Word(0),Word(0))),Frames(skeleton),Rw(0x1a,Rw(1,Word(1)),Geometry(native,skeleton,prefix)),Rw(0x14,Rw(1,atomic),Rw(3)),Rw(3));
    }
    static byte[] Txd(int format=6)
    {
        int tileWidth=format is 0 or 1 or 2 or 8 or 9 or 14?8:4,tileHeight=format is 0 or 8 or 14?8:4;
        int imageSize=((8+tileWidth-1)/tileWidth)*((8+tileHeight-1)/tileHeight)*(format==6?64:32);
        int palette=format==8?32:format==9?512:0;var p=new byte[108+palette+imageSize];Be32(p,0,6);Be32(p,4,0x1102);Encoding.ASCII.GetBytes("Diffuse").CopyTo(p,24);Be16(p,92,8);Be16(p,94,8);p[96]=32;p[97]=1;p[98]=(byte)format;p[99]=1;Be32(p,104+palette,(uint)imageSize);
        if(palette!=0)Be16(p,104,0xf800);
        int at=108+palette;
        if(format is 0 or 1 or 2)Array.Fill(p,(byte)255,at,imageSize);
        else if(format==3)for(int i=at;i<p.Length;i+=2){p[i]=255;p[i+1]=127;}
        else if(format is 4 or 5)for(int i=at;i<p.Length;i+=2)Be16(p,i,format==4?0xf800:0xfc00);
        else if(format==6)for(int t=at;t<p.Length;t+=64)for(int i=0;i<16;i++){p[t+i*2]=255;p[t+i*2+1]=255;}
        else if(format==14)for(int i=at;i<p.Length;i+=8){Be16(p,i,0xf800);Be16(p,i+2,0x7e0);}
        var count=new byte[4];Le16(count,0,1);Le16(count,2,6);return Rw(0x16,Rw(1,count),Rw(0x15,Rw(1,p),Rw(3)),Rw(3));
    }
    static byte[] Anm()
    {
        var p=new byte[20+4*36];Le32(p,0,0x100);Le32(p,4,1);Le32(p,8,4);Lf(p,16,1);
        for(int i=0;i<4;i++){int at=20+i*36;Lf(p,at,i<2?0:1);Lf(p,at+16,1);Lf(p,at+20,i==2?3:1);Le32(p,at+32,(uint)(i<2?(i+2)*36:(i-2)*36));}return Rw(0x1b,p);
    }
    static byte[] Hsd(byte[] data,string name,uint root=32)
    {
        byte[] text=Encoding.ASCII.GetBytes(name+"\0");var p=new byte[32+data.Length+8+text.Length];Be32(p,0,(uint)p.Length);Be32(p,4,(uint)data.Length);Be32(p,12,1);Encoding.ASCII.GetBytes("HSD0").CopyTo(p,20);data.CopyTo(p,32);Be32(p,32+data.Length,root);text.CopyTo(p,40+data.Length);return p;
    }
    static byte[] Melee(bool weighted=false,int format=6)
    {
        var p=new byte[1280];int root=32;Be32(p,root+4,2);Be32(p,root+16,160);Bf(p,root+44,1);for(int k=0;k<3;k++)Bf(p,root+32+k*4,1);Be32(p,root+56,1088);
        if(weighted){Be32(p,root+8,96);Be32(p,100,1);Bf(p,140,1);for(int k=0;k<3;k++)Bf(p,128+k*4,1);Be32(p,152,1136);}
        Be32(p,168,192);Be32(p,172,704);Be32(p,196,3|3<<13);Be32(p,200,256);Be32(p,204,224);Array.Fill(p,(byte)255,228,4);Bf(p,236,1);
        Be32(p,268,4);for(int k=0;k<3;k++)Bf(p,284+k*4,1);Be32(p,308,1);Be32(p,312,1);p[316]=1;p[317]=1;Be32(p,320,0x00340000);Bf(p,324,1);Be32(p,328,1);Be32(p,332,352);
        Be32(p,352,416);Be16(p,356,8);Be16(p,358,8);Be32(p,360,(uint)format);
        if(format is 8 or 9 or 10){Be32(p,336,384);Be32(p,384,408);Be32(p,388,1);Be16(p,396,1);Be16(p,408,0xf800);}
        if(format==6)for(int t=416;t<672;t+=64)for(int k=0;k<16;k++){p[t+k*2]=255;p[t+k*2+1]=255;}
        else if(format is 0 or 1 or 2)Array.Fill(p,(byte)255,416,128);
        else if(format==3)for(int i=416;i<544;i+=2){p[i]=255;p[i+1]=127;}
        else if(format is 4 or 5)for(int i=416;i<544;i+=2)Be16(p,i,format==4?0xf800:0xfc00);
        else if(format==14)for(int i=416;i<448;i+=8){Be16(p,i,0xf800);Be16(p,i+2,0x7e0);}
        Be32(p,712,736);Be16(p,716,0x8000|(weighted?0x2000:0));Be16(p,718,1);Be32(p,720,896);if(weighted)Be32(p,724,1184);
        int[] attrs=[0,9,10,11,13],type=[1,3,2,2,3],comp=[0,1,0,1,1],component=[0,4,4,5,4],stride=[1,12,12,4,8],array=[0,960,1008,1024,1056];
        for(int i=0;i<5;i++){int at=736+i*24;Be32(p,at,(uint)attrs[i]);Be32(p,at+4,(uint)type[i]);Be32(p,at+8,(uint)comp[i]);Be32(p,at+12,(uint)component[i]);Be16(p,at+18,stride[i]);Be32(p,at+20,(uint)array[i]);}Be32(p,856,255);
        p[896]=0x90;Be16(p,897,3);for(int i=0;i<3;i++){int at=899+i*7;p[at]=0;Be16(p,at+1,i);p[at+3]=0;p[at+4]=0;Be16(p,at+5,i);}
        Bf(p,972,1);Bf(p,988,1);Bf(p,1016,1);Array.Fill(p,(byte)255,1024,4);Bf(p,1064,1);Bf(p,1076,1);
        for(int i=0;i<2;i++){int at=1088+i*48;Bf(p,at,1);Bf(p,at+20,1);Bf(p,at+40,1);Bf(p,at+12,-1-i);}Be32(p,1184,1200);Be32(p,1200,32);Bf(p,1204,.5f);Be32(p,1208,96);Bf(p,1212,.5f);
        return Hsd(p,"Fighter_joint");
    }
    static byte[] Fobj(int format=0,int opcode=2)
    {
        using var stream=new MemoryStream();stream.WriteByte((byte)(0x10|opcode));
        for(int i=0;i<2;i++){float value=i==0?1:3;if(format==0){var p=new byte[4];Lf(p,0,value);stream.Write(p);}else if(format is 1 or 2){var p=new byte[2];Le16(p,0,(int)(value*256));stream.Write(p);}else stream.WriteByte((byte)value);
            if(opcode==4){var p=new byte[4];Lf(p,0,i==0?1f/30:-1f/30);stream.Write(p);}if(opcode!=6)stream.WriteByte((byte)(i==0?60:0));}
        return stream.ToArray();
    }
    static byte[] Figatree(bool weighted=false,int format=0,int opcode=2)
    {
        byte[] f=Fobj(format,opcode);var p=new byte[160];Be32(p,32,1);Bf(p,40,60);Be32(p,44,64);Be32(p,48,80);p[64]=1;p[weighted?66:65]=255;
        Be16(p,80,f.Length);p[84]=5;p[85]=(byte)((format<<5)|(format is 1 or 2?8:0));p[86]=0;Be32(p,88,112);f.CopyTo(p,112);return Hsd(p,"Walk_figatree");
    }
    static byte[] Animjoint()
    {
        byte[] f=Fobj();var p=new byte[160];Be32(p,40,64);Be32(p,64,1u<<29);Bf(p,68,60);Be32(p,72,96);Be32(p,100,(uint)f.Length);p[108]=5;Be32(p,112,128);f.CopyTo(p,128);return Hsd(p,"Walk_animjoint");
    }
    internal static void Run(string output)
    {
        string sd=Path.Combine(output,"sd");void Save(string name,byte[] p)=>File.WriteAllBytes(Path.Combine(sd,name),p);
        Save("rw.dff",Dff());Save("rw-native.dff",Dff(true));Save("rw-skin.dff",Dff(skeleton:true));Save("rw-native-skin.dff",Dff(true,true));Save("rw-native-prefix.dff",Dff(true,true,true));Save("rw.txd",Txd());Save("rw.anm",Anm());
        foreach(int format in new[]{0,1,2,3,4,5,6,8,9,14})Save($"rw-tex-{format}.txd",Txd(format));
        var alpha=Txd();Be32(alpha,152,1);Save("rw-alpha.txd",alpha);
        Save("melee.dat",Melee());Save("melee-weighted.dat",Melee(true));Save("melee-motion.dat",Figatree());Save("melee-weighted-motion.dat",Figatree(true));Save("melee-animjoint.dat",Animjoint());Save("melee-aj.dat",Cat(Figatree(),Figatree()));
        foreach(int format in new[]{0,1,2,3,4,5,6,8,9,10,14})Save($"melee-tex-{format}.dat",Melee(format:format));
        foreach(int format in new[]{0,1,2,3,4})Save($"melee-fobj-{format}.dat",Figatree(format:format));
        foreach(int opcode in new[]{1,2,3,4,6})Save($"melee-op-{opcode}.dat",Figatree(opcode:opcode));
        var bad=Dff();Le32(bad,4,uint.MaxValue);Save("rw-bad.dff",bad);
        bad=Melee();Be32(bad,32+32+8,32);Save("melee-cycle.dat",bad);
        bad=Melee();Be32(bad,32+32+4,0x200);Save("melee-billboard.dat",bad);
    }
}
