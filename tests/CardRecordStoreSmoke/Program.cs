using Dolphin.Storage;
internal static class Program
{
    static int Main()
    {
        using(var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("DRST","01"))){
            var store=card.OpenRecordStore("DNDRecords",16,new MemoryCardSaveOptions{Title="Record store",Comment="SDK fixture",Banner=new byte[6144],Icon=new byte[2048]},123);
            store.Write("options",new byte[]{4,5});var back=store.Read("options");
            if(back.Length!=2||back[0]!=4||back[1]!=5)return 1;
            store.Delete("options");return store.Names().Length;
        }
    }
}
