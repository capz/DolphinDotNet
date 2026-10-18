using System;
using System.IO;
using Dolphin.Hardware;
using Dolphin.Storage;

// Small versioned/checksummed record, merged with a newly inserted card.
// Writes happen between frames; no mounted card is required to keep playing.
internal sealed class SampleRecord : IDisposable
{
    private readonly string code,fileName;
    private readonly bool lower;
    private MemoryCard? card;
    private MemoryCardSlot slot;
    private float poll;
    private bool dirty;
    internal int Best {get;private set;}
    internal string Status {get;private set;}="NO CARD";
    internal SampleRecord(string code,bool lower=false,string fileName="Record"){this.code=code;this.lower=lower;this.fileName=fileName;}
    private bool Better(int value)=>value>0&&(Best==0||(lower?value<Best:value>Best));
    internal void Observe(int value){if(Better(value)){Best=value;dirty=true;}}
    private static int Get(byte[] b,int at)=>b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24;
    private static void Put(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(i*8));}
    internal void Update(float seconds)
    {
        poll-=seconds;if(poll>0)return;poll=2;
        MemoryCards.Update();
        if(card!=null)
        {
            var state=MemoryCards.GetState(slot);
            if(state.IsKnown&&!state.IsConnected){card.Dispose();card=null;dirty=Best>0;Status="CARD REMOVED";}
        }
        if(card==null && !(Mount(MemoryCardSlot.A)||Mount(MemoryCardSlot.B)))return;
        if(!dirty)return;
        try
        {
            var bytes=new byte[12];Put(bytes,0,0x31525344);Put(bytes,4,Best);Put(bytes,8,Best^0x51a73029);
            card!.WriteSave(fileName,bytes,new MemoryCardSaveOptions {Title=code,Comment="DolphinDotNet record"});
            dirty=false;Status="RECORD SAVED";
        }
        catch(IOException){Status="SAVE RETRY";}
    }
    private bool Mount(MemoryCardSlot candidate)
    {
        if(!MemoryCards.GetState(candidate).IsConnected)return false;
        try
        {
            card=MemoryCard.Mount(candidate==MemoryCardSlot.A?StorageDevice.MemoryCardSlotA:StorageDevice.MemoryCardSlotB,new MemoryCardIdentity(code,"01"));
            slot=candidate;Status=candidate==MemoryCardSlot.A?"CARD A":"CARD B";
            var entries=card.GetEntries();
            for(int i=0;i<entries.Length;i++)if(entries[i].Name==fileName)
            {
                var b=card.ReadAllBytes(fileName);
                if(b.Length==12&&Get(b,0)==0x31525344&&Get(b,4)>0&&Get(b,8)==(Get(b,4)^0x51a73029))
                {int disk=Get(b,4);if(Better(disk))Best=disk;dirty=Best!=disk;}
                break;
            }
            return true;
        }
        catch(IOException){card?.Dispose();card=null;Status="CARD NOT READY";return false;}
    }
    public void Dispose(){if(dirty){poll=0;Update(0);}card?.Dispose();card=null;}
}
