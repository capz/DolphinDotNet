using System.Buffers.Binary;

// Original binary fixtures only. The C/AOT tests execute all source importers.
internal static class PsoImportTests
{
    internal static void Run(string output)
    {
        string sd=Path.Combine(output,"sd");
        string gj=Path.Combine(sd,"direct.gj"),gvm=Path.Combine(sd,"direct.gvm"),motion=Path.Combine(sd,"direct.njm");
        byte[] geometry=new byte[8+224];"GJCM"u8.CopyTo(geometry);BinaryPrimitives.WriteUInt32LittleEndian(geometry.AsSpan(4),224);
        void U(int p,uint v)=>BinaryPrimitives.WriteUInt32BigEndian(geometry.AsSpan(8+p),v);
        void H(int p,ushort v)=>BinaryPrimitives.WriteUInt16BigEndian(geometry.AsSpan(8+p),v);
        void F(int p,float v)=>U(p,unchecked((uint)BitConverter.SingleToInt32Bits(v)));
        U(4,52);F(32,1);F(36,1);F(40,1);U(52,88);U(60,180);H(68,1);
        geometry[8+88]=1;H(90,3);U(96,128);U(100,36);
        geometry[8+104]=5;H(106,3);U(112,164);U(116,12);geometry[8+120]=255;
        F(140,1);F(156,1);H(168,255);H(174,255);
        U(180,196);U(184,2);U(188,212);U(192,9);
        geometry[8+196]=1;U(200,0x808);geometry[8+204]=8;U(208,0);
        geometry[8+212]=0x90;H(213,3);geometry[8+217]=1;geometry[8+218]=1;geometry[8+219]=2;geometry[8+220]=2;
        File.WriteAllBytes(gj,geometry);
        byte[] archive=new byte[8+6+8+8+64];"GVMH"u8.CopyTo(archive);BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4),6);
        BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(10),1);
        "GVRT"u8.CopyTo(archive.AsSpan(14));BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(18),72);
        archive[25]=6;BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(26),4);BinaryPrimitives.WriteUInt16BigEndian(archive.AsSpan(28),4);
        for(int k=0;k<16;k++){archive[30+k*2]=255;archive[31+k*2]=230;archive[62+k*2]=40;archive[63+k*2]=10;}
        File.WriteAllBytes(gvm,archive);
        byte[] njm=new byte[8+52];"NMDM"u8.CopyTo(njm);BinaryPrimitives.WriteUInt32LittleEndian(njm.AsSpan(4),52);
        void M(int p,uint v)=>BinaryPrimitives.WriteUInt32BigEndian(njm.AsSpan(8+p),v);
        M(0,12);M(4,30);njm[17]=1;njm[19]=1;M(12,20);M(16,2);M(36,30);M(40,0x40000000);
        File.WriteAllBytes(motion,njm);
        byte[] rotation=(byte[])njm.Clone();Array.Clear(rotation,28,rotation.Length-28);rotation[17]=2;
        BinaryPrimitives.WriteUInt32BigEndian(rotation.AsSpan(44),30);BinaryPrimitives.WriteUInt32BigEndian(rotation.AsSpan(56),16384);
        File.WriteAllBytes(Path.Combine(sd,"rotate.njm"),rotation);
        foreach(byte primitive in new byte[]{0x98,0xa0}) {
            byte[] quad=new byte[8+240];geometry.AsSpan(0,8+128).CopyTo(quad);
            BinaryPrimitives.WriteUInt32LittleEndian(quad.AsSpan(4),240);
            void Q(int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(quad.AsSpan(8+at),value);
            void QH(int at,ushort value)=>BinaryPrimitives.WriteUInt16BigEndian(quad.AsSpan(8+at),value);
            QH(90,4);Q(100,48);QH(106,4);Q(112,176);Q(116,16);Q(60,192);
            float[] positions=primitive==0xa0?new float[]{0,0,0,1,0,0,0,1,0,-1,0,0}:new float[]{0,0,0,1,0,0,0,1,0,1,1,0};
            for(int i=0;i<positions.Length;i++)Q(128+i*4,unchecked((uint)BitConverter.SingleToInt32Bits(positions[i])));
            Q(192,208);Q(196,2);Q(200,224);Q(204,11);quad[8+208]=1;Q(212,0x808);quad[8+216]=8;
            quad[8+224]=primitive;QH(225,4);
            for(int i=0;i<4;i++){quad[8+227+i*2]=(byte)i;quad[8+228+i*2]=(byte)i;}
            File.WriteAllBytes(Path.Combine(sd,primitive==0xa0?"fan.gj":"strip.gj"),quad);
        }
        void Texture(int format,int width,int height,byte[] pixels) {
            byte[] bytes=new byte[16+pixels.Length];"GVRT"u8.CopyTo(bytes);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),(uint)(8+pixels.Length));bytes[11]=(byte)format;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12),(ushort)width);BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14),(ushort)height);pixels.CopyTo(bytes,16);
            File.WriteAllBytes(Path.Combine(sd,"format-"+format+".gvr"),bytes);
        }
        byte[] tile=new byte[32];tile[0]=0xa3;Texture(0,8,8,tile);tile[0]=73;Texture(1,8,4,tile);tile[0]=0xa3;Texture(2,8,4,tile);
        tile[0]=17;tile[1]=99;Texture(3,4,4,tile);tile[0]=0xf8;tile[1]=0;Texture(4,4,4,tile);tile[0]=0x35;tile[1]=0x7b;Texture(5,4,4,tile);
        byte[] rgba=new byte[64];rgba[0]=5;rgba[1]=7;rgba[32]=11;rgba[33]=13;Texture(6,4,4,rgba);
        Array.Clear(tile);tile[0]=0xf8;tile[3]=0x1f;tile[4]=0x1b;Texture(14,8,8,tile);
        File.WriteAllBytes(Path.Combine(sd,"truncated.gvm"),archive[..^1]);
        byte[] bad=(byte[])geometry.Clone();BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(8+188),0xfffffff0);File.WriteAllBytes(Path.Combine(sd,"bad.gj"),bad);
        Console.WriteLine("Generated original GJ/GVM/NMDM, GX tile and malformed PSO fixtures.");
    }
}
