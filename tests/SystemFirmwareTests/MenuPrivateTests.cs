using Dolphin.System;

// Explicit local qualification only. The caller supplies a private firmware
// file; neither it nor resulting packets are CI fixtures/artifacts.
internal static class MenuPrivateTests
{
    private static void Check(bool value){if(!value)throw new Exception("Private menu qualification failed");}
    private static void Big(byte[] d,int at,int n)=>FirmwareMenuRecipe.Put(d,at,n);
    private static void Unknown(byte[] data)
    {using var image=FirmwareImage.Read(data);using var catalog=image.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default);Check(!catalog.TryGetMenuSoundInfo(SystemMenuSound.Select,out _)&&catalog.MenuAudioRetainedBytes==0);}
    internal static void Validate(FirmwareImage image,FirmwareCatalog catalog)
    {
        var archive=catalog.MenuAudio.Archives[0];int bank=archive.System.Offset-0x8e0,seq=(archive.Offset+archive.Length+31)&~31;
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<100;i++)Check(catalog.TryGetMenuSoundInfo(SystemMenuSound.Select,out _));
        Check(GC.GetAllocatedBytesForCurrentThread()==before);
        foreach(int at in new[]{bank+0x828,seq+0x400,bank-128+16})
        {var changed=(byte[])image.Data.Clone();changed[at]^=1;Unknown(changed);}
        var invalid=(byte[])image.Data.Clone();
        // The modern ID-9 record is reached through the checked C-DF table;
        // replace its identifier with ID 11 to create a conflicting mapping.
        int root=archive.System.Offset;
        int Read(int at)=>(int)(((uint)invalid[at]<<24)|((uint)invalid[at+1]<<16)|((uint)invalid[at+2]<<8)|invalid[at+3]);
        int wbct=Read(root+20),scene=Read(root+wbct+12),cdf=Read(root+scene+12),count=Read(root+cdf+4);
        for(int i=0;i<count;i++){int entry=Read(root+cdf+8+i*4);if(Read(root+entry)==9){Big(invalid,root+entry,11);break;}}
        try{using var bad=FirmwareImage.Read(invalid);using var rejected=bad.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default);throw new Exception("Accepted corrupt instrument map");}
        catch(InvalidDataException){}
        // Relocate the complete bounded resource group. Two original generated
        // Yay0 records provide encoding evidence without copying graphics.
        var moved=new byte[FirmwareImage.ImageBytes];Array.Copy(image.Data,moved,256);
        for(int i=0;i<2;i++)
        {int at=0x1000+i*0x1000;moved[at]=89;moved[at+1]=97;moved[at+2]=121;moved[at+3]=48;Big(moved,at+4,4);Big(moved,at+8,20);Big(moved,at+12,20);moved[at+16]=240;for(int b=0;b<4;b++)moved[at+20+b]=(byte)(b+1);}
        int start=bank-65536,length=seq+3117-start;Array.Copy(image.Data,start,moved,0x20000,length);
        using var relocated=FirmwareImage.Read(moved);using var resolved=relocated.CreateCatalog(FirmwareCatalogOptions.IncludeMenuAudio,default);
        for(int i=0;i<3;i++)Check(catalog.SnapshotMenuSound((SystemMenuSound)i,default).SequenceEqual(resolved.SnapshotMenuSound((SystemMenuSound)i,default)));
        Console.WriteLine("Private profile mutation, true-ID conflict, allocation-free query and complete-resource relocation passed");
    }
}
