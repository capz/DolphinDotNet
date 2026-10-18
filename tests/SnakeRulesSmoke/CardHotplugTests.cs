using System;
using System.IO;
using Dolphin.Hardware;
using Dolphin.Storage;

// Run the real sample store against controllable physical/card I/O. Production
// snapshots and native storage have separate integration coverage.
internal static class CardHotplugTests
{
    private static void Check(bool condition,string message)
    { if(!condition)throw new Exception(message); }

    internal static void Run()
    {
        var game=new SnakeGame(77u);
        using(var store=new SnakeSaveStore())
        {
            Check(!store.CanSave && !store.HasSave,"Starting without a card disables Continue.");
            Check(!store.Save(game),"An absent card queues the requested save.");
            MemoryCards.Presence[1]=true;
            Check(!store.Update(1f,null) && MemoryCard.Writes==0,"Card probes are throttled.");
            Check(store.Update(1f,null) && store.CardLabel=="CARD B" && store.HasSave,
                "A queued title-screen save starts when slot B is inserted.");
            Check(MemoryCard.Writes==1,"Insertion writes the queued run once.");
            MemoryCards.Presence[0]=true;
            store.Update(2f,game);
            Check(store.CardLabel=="CARD B" && MemoryCard.Writes==1,
                "Inserting A must not replace an existing B mount.");
            MemoryCards.Presence[1]=null;
            store.Update(2f,game);
            Check(store.CanSave,"Busy/unknown probes must not unmount a card.");
            MemoryCards.Presence[1]=false;
            Check(store.Update(2f,game) && store.CardLabel=="CARD A" && MemoryCard.Disposals==1,
                "Removal releases the mount and saves the current run to the other slot.");
            MemoryCards.Presence[0]=false;
            store.Update(2f,game);
            Check(!store.CanSave && !store.HasSave,"Removal clears stale Continue availability.");
            MemoryCards.Presence[0]=true;
            MemoryCard.FailMount=true;
            store.Update(2f,game);
            Check(!store.CanSave,"Presence alone is not mount readiness.");
            MemoryCard.FailMount=false;
            Check(store.Update(2f,game),"Present cards retry without a new insertion edge.");
            MemoryCard.FailWrite=true;
            Check(!store.Save(game),"Failed writes stay queued.");
            MemoryCard.FailWrite=false;
            Check(store.Update(2f,null),"Failed explicit saves retry even at the title.");
        }
        int writes=MemoryCard.Writes;
        using(var store=new SnakeSaveStore())
        {
            Check(store.HasSave && MemoryCard.Writes==writes,
                "Mounting at the title must not overwrite an existing save.");
            Check(store.TryLoad(out var loaded) && loaded!=null && loaded.Food==game.Food,
                "Inserted saves are available for Continue.");
            MemoryCards.Presence[0]=false;
            store.Update(2f,null);
            store.Save(game);
            store.Clear();
            MemoryCards.Presence[0]=true;
            store.Update(2f,null);
            Check(MemoryCard.Writes==writes,"Clear cancels pending saves before insertion.");
        }
    }
}

namespace Dolphin.Hardware
{
    public enum MemoryCardSlot { A,B }
    public readonly struct MemoryCardState
    {
        public bool IsKnown { get; }
        public bool IsConnected { get; }
        public bool WasDisconnected { get; }
        internal MemoryCardState(bool? presence,MemoryCardState previous)
        {
            IsKnown=presence.HasValue || previous.IsKnown;
            IsConnected=presence??previous.IsConnected;
            WasDisconnected=presence==false && previous.IsConnected;
        }
    }
    public static class MemoryCards
    {
        internal static readonly bool?[] Presence={false,false};
        private static readonly MemoryCardState[] states=new MemoryCardState[2];
        public static void Update()
        { for(int i=0;i<2;i++)states[i]=new MemoryCardState(Presence[i],states[i]); }
        public static MemoryCardState GetState(MemoryCardSlot slot)=>states[(int)slot];
    }
}

namespace Dolphin.Storage
{
    public enum StorageDevice { MemoryCardSlotA,MemoryCardSlotB }
    public sealed class MemoryCardIdentity
    { public MemoryCardIdentity(string game,string company){} }
    public sealed class MemoryCardSaveOptions
    { public string? Title { get; set; } public string? Comment { get; set; } }
    public sealed class MemoryCardEntry
    { public string Name=>"SnakeRun"; }
    public sealed class MemoryCard : IDisposable
    {
        private static readonly byte[]?[] saves=new byte[2][];
        internal static bool FailMount,FailWrite;
        internal static int Writes,Disposals;
        private readonly int slot;
        private bool disposed;
        private MemoryCard(int slot){this.slot=slot;}
        public static MemoryCard Mount(StorageDevice device,MemoryCardIdentity identity)
        {
            if(FailMount || MemoryCards.Presence[(int)device]!=true)throw new IOException();
            return new MemoryCard((int)device);
        }
        private void Check()
        {
            if(disposed)throw new ObjectDisposedException("card");
            if(MemoryCards.Presence[slot]==false)throw new IOException();
        }
        public MemoryCardEntry[] GetEntries()
        { Check();return saves[slot]==null?Array.Empty<MemoryCardEntry>():new[]{new MemoryCardEntry()}; }
        public byte[] ReadAllBytes(string name){Check();return saves[slot]??throw new IOException();}
        public void WriteSave(string name,byte[] bytes,MemoryCardSaveOptions options)
        { Check();if(FailWrite)throw new IOException();saves[slot]=bytes;Writes++; }
        public void Delete(string name){Check();saves[slot]=null;}
        public void Dispose(){if(!disposed){disposed=true;Disposals++;}}
    }
}
