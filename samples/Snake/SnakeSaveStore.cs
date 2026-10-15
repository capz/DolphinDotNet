using System;
using System.IO;
using Dolphin.Storage;

/// <summary>Native GameCube memory-card save; no SD adapter or filesystem necessary.</summary>
internal sealed class SnakeSaveStore : IDisposable
{
    private const string FileName="SnakeRun";
    private readonly MemoryCard? card;
    internal string CardLabel { get; }
    internal bool CanSave=>card!=null;
    internal bool HasSave { get; private set; }
    internal string Message { get; private set; }

    internal SnakeSaveStore()
    {
        MemoryCard? mounted=null;
        string label="NO CARD";
        try
        {
            mounted=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,
                new MemoryCardIdentity("DSNK","01"));
            label="CARD A";
        }
        catch(IOException)
        {
            try
            {
                mounted=MemoryCard.Mount(StorageDevice.MemoryCardSlotB,
                    new MemoryCardIdentity("DSNK","01"));
                label="CARD B";
            }
            catch(IOException) {}
        }
        card=mounted;CardLabel=label;
        Message=card==null?"NO CARD - SAVES OFF":"NO SAVED GAME";
        if(card!=null)TryLoad(out _);
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
        if(card==null){Message="NO CARD - SAVES OFF";return false;}
        try
        {
            byte[] bytes=SnakeSaveCodec.Encode(game);
            card.WriteSave(FileName,bytes,new MemoryCardSaveOptions
            {
                Title="Snake",Comment="DolphinDotNet"
            });
            HasSave=true;Message="GAME SAVED";
            return true;
        }
        catch(IOException){Message="SAVE FAILED";return false;}
    }

    internal void Clear()
    {
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
