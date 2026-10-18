using System;
using System.IO;
using Dolphin.Hardware;
using Dolphin.Storage;

/// <summary>Native GameCube memory-card save; no SD adapter or filesystem necessary.</summary>
internal sealed class SnakeSaveStore : IDisposable
{
    private const string FileName="SnakeRun";
    internal const float PollSeconds=2f;
    private MemoryCard? card;
    private MemoryCardSlot slot;
    private SnakeGame? pendingSave;
    private float untilPoll;
    internal string CardLabel { get; private set; }="NO CARD";
    internal bool CanSave=>card!=null;
    internal bool HasSave { get; private set; }
    internal string Message { get; private set; }="NO CARD - SAVES OFF";

    internal SnakeSaveStore()
    {
        Update(0f,null);
    }

    // Physical presence is separate from mount readiness. Retry present cards
    // that are busy/unreadable, without probing or mounting every video frame.
    internal bool Update(float seconds,SnakeGame? activeGame)
    {
        if(seconds>0f)untilPoll-=seconds;
        if(untilPoll>0f)return false;
        untilPoll=PollSeconds;
        MemoryCards.Update();
        if(card!=null)
        {
            var state=MemoryCards.GetState(slot);
            if(state.WasDisconnected || (state.IsKnown && !state.IsConnected))
            {
                card.Dispose();card=null;
                HasSave=false;CardLabel="NO CARD";Message="CARD REMOVED";
            }
        }
        bool mounted=false;
        if(card==null)
        {
            mounted=TryMount(MemoryCardSlot.A) || TryMount(MemoryCardSlot.B);
            if(!mounted)return false;
        }
        // Insertion during play preserves the current run. A queued B/New Game
        // save also survives a period without a card, including at the title.
        if(mounted && activeGame!=null)pendingSave=activeGame;
        if(pendingSave!=null)return Save(pendingSave);
        if(mounted)TryLoad(out _);
        return false;
    }

    private bool TryMount(MemoryCardSlot candidate)
    {
        if(!MemoryCards.GetState(candidate).IsConnected)return false;
        try
        {
            card=MemoryCard.Mount(candidate==MemoryCardSlot.A
                    ?StorageDevice.MemoryCardSlotA:StorageDevice.MemoryCardSlotB,
                new MemoryCardIdentity("DSNK","01"));
            slot=candidate;CardLabel=candidate==MemoryCardSlot.A?"CARD A":"CARD B";
            Message="NO SAVED GAME";
            return true;
        }
        catch(IOException){Message="CARD NOT READY";return false;}
    }

    internal bool TryLoad(out SnakeGame? game)
    {
        game=null;
        if(card==null){HasSave=false;return false;}
        try
        {
            bool found=false;
            var entries=card.GetEntries();
            for(int i=0;i<entries.Length;i++)
                if(entries[i].Name==FileName){found=true;break;}
            if(!found){HasSave=false;Message="NO SAVED GAME";return false;}
            var bytes=card.ReadAllBytes(FileName);
            if(!SnakeSaveCodec.TryDecode(bytes,out game))
            {HasSave=false;Message="INVALID SAVE";return false;}
            HasSave=true;Message="GAME LOADED";
            return true;
        }
        catch(IOException)
        {HasSave=false;Message="READ FAILED";return false;}
    }

    internal bool Save(SnakeGame game)
    {
        pendingSave=game;
        if(card==null){Message="NO CARD - SAVES OFF";return false;}
        try
        {
            byte[] bytes=SnakeSaveCodec.Encode(game);
            card.WriteSave(FileName,bytes,new MemoryCardSaveOptions
            {
                Title="Snake",Comment="DolphinDotNet"
            });
            pendingSave=null;HasSave=true;Message="GAME SAVED";
            return true;
        }
        catch(IOException){Message="SAVE FAILED";return false;}
    }

    internal void Clear()
    {
        pendingSave=null;
        if(card==null)return;
        try
        {
            if(HasSave)card.Delete(FileName);
            HasSave=false;Message="NEW GAME";
        }
        catch(IOException){Message="DELETE FAILED";HasSave=false;}
    }

    public void Dispose()=>card?.Dispose();
}
