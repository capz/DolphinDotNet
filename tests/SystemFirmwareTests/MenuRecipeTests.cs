using Dolphin.System;
using System.Text;

internal static class MenuRecipeTests
{
    private static void Check(bool condition){if(!condition)throw new Exception("Menu recipe assertion");}
    private static void Big(byte[] d,int p,int n)=>FirmwareMenuRecipe.Put(d,p,n);
    internal static void Run(Func<int,byte[]> descriptor,string? directory)
    {
        foreach(int length in new[]{0,1,3,55,56,63,64,65,128,4096})
        {
            var input=new byte[length];for(int i=0;i<length;i++)input[i]=(byte)i;
            string expected=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).ToLowerInvariant();
            Check(FirmwareAudioFingerprint.Hash(input,0,input.Length)==expected);
        }
        var bank=MenuBankFixture.Create();
        // Authored test sequence: unrelated to the retail sequences. A pitched
        // note, timed bend and pan, early loop end, custom ADSR and note release.
        byte[] sequence={0xac,6,0,7,0xd9,0,0x98,0,64,0xa4,7,12,
            0xd8,0,2,0,3,0,20,0x40,0,0,60,0xf0,0,
            0x30,1,127,0x9e,1,1,0,4,0x9a,3,0,10,0x80,30,0x81,0xff};
        var recipe=FirmwareMenuRecipe.Compile(sequence,0,bank);Check(recipe.Frames==6560);
        Check(recipe.SourceWaveIds.SequenceEqual(new[]{9}));
        // Different programs must use their own bank release defaults, even
        // when a raw source is shorter or has a different WSYS position.
        byte[] option={0xac,6,0,9,0x30,1,100,0x80,30,0x81,0xff};
        var shortRelease=FirmwareMenuRecipe.Compile(option,0,MenuBankFixture.Create(12));
        Check(shortRelease.Frames==4000&&shortRelease.SourceWaveIds.SequenceEqual(new[]{11}));
        byte[] cancel={0xac,6,0,3,0x30,1,100,0x80,30,0x81,0xff};
        var cancellation=FirmwareMenuRecipe.Compile(cancel,0,bank);
        Check(cancellation.Frames==5440&&cancellation.SourceWaveIds.SequenceEqual(new[]{6}));
        var corruptBank=(byte[])bank.Clone();Big(corruptBank,0x350+12,0x3f000000);
        try{FirmwareMenuRecipe.Compile(sequence,0,corruptBank);throw new Exception("Accepted unqualified tuning");}catch(InvalidDataException){}
        for(int n=0;n<sequence.Length;n++)
        {
            try{FirmwareMenuRecipe.Compile(sequence[..n],0,bank);throw new Exception("Accepted truncated recipe");}
            catch(InvalidDataException){}
        }
        var bad=(byte[])sequence.Clone();bad[0]=0xc0;
        try{FirmwareMenuRecipe.Compile(bad,0,bank);throw new Exception("Accepted unknown opcode");}catch(InvalidDataException){}
        // A call cycle must hit a fixed depth/work limit, never hang.
        try{FirmwareMenuRecipe.Compile(new byte[]{0xc4,0,0,0,0},0,bank);throw new Exception("Accepted call cycle");}catch(InvalidDataException){}
        var desc=descriptor(19);desc[193]=3;desc[194]=48;Big(desc,200,0);Big(desc,204,38);Big(desc,208,-1);Big(desc,212,3);Big(desc,216,17);
        var image=new byte[1024];Array.Copy(desc,image,desc.Length);
        for(int i=0;i<19;i++){short v=(short)((i-9)*1500);image[512+i*2]=(byte)(v>>8);image[513+i*2]=(byte)v;}
        var system=new FirmwareWaveSystemInfo(0,desc.Length,FirmwareWaveSystem.Inspect(desc));
        var archive=new FirmwareAudioArchive(system,512,38,"ipl_0.aw",FirmwareAudioLayout.FileTable);
        using var source=FirmwareMenuWaveSource.Capture(image,archive,0,default);
        var packet=recipe.Packet(new[]{source},default);Array.Clear(image);
        Check(packet.Length==64+36+32+40+256+38);
        if(directory!=null)File.WriteAllBytes(Path.Combine(directory,"recipe.dme"),packet);
        var coefficients=new byte[512];for(int i=0;i<64;i++){coefficients[i*8+2]=0x7f;coefficients[i*8+3]=0xff;}
        var dspRecipe=FirmwareMenuRecipe.Compile(sequence,0,bank,resampling:coefficients);
        if(directory!=null)File.WriteAllBytes(Path.Combine(directory,"recipe-dsp.dme"),dspRecipe.Packet(new[]{source},default));
        var fastSequence=(byte[])sequence.Clone();fastSequence[Array.IndexOf(fastSequence,(byte)0x30)]=76;
        var fastRecipe=FirmwareMenuRecipe.Compile(fastSequence,0,bank,resampling:coefficients);
        if(directory!=null)File.WriteAllBytes(Path.Combine(directory,"recipe-fast.dme"),fastRecipe.Packet(new[]{source},default));
        var reverb=new byte[64];
        for(int i=0;i<2;i++)
        {int at=i*32;reverb[at]=1;reverb[at+7]=1;reverb[at+4+i*4]=0x20;Big(reverb,at+12,1);reverb[at+30]=0x40;}
        var wetRecipe=FirmwareMenuRecipe.Compile(sequence,0,bank,reverb);Check(wetRecipe.Frames==38560);
        if(directory!=null)File.WriteAllBytes(Path.Combine(directory,"recipe-wet.dme"),wetRecipe.Packet(new[]{source},default));
        var layered=new byte[128];new byte[]{0xc1,0,0,0,16,0xc1,1,0,0,80,0xff}.CopyTo(layered,0);
        sequence.CopyTo(layered,16);
        new byte[]{0xac,6,0,9,0x98,0,32,0x98,3,0,0x30,1,64,0x80,20,0x81,0xff}.CopyTo(layered,80);
        var layers=FirmwareMenuRecipe.Compile(layered,0,bank);Check(layers.Frames==6560);
        Check(layers.SourceWaveIds.SequenceEqual(new[]{9,11}));
        var layeredPacket=layers.Packet(new[]{source,source},default);source.Dispose();
        if(directory!=null)File.WriteAllBytes(Path.Combine(directory,"recipe-layered.dme"),layeredPacket);
        Console.WriteLine("SHA-256 vectors and bounded menu recipe compilation passed");
    }
}
