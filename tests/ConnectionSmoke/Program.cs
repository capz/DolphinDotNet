using System;
using System.Threading.Tasks;
using Dolphin.Hardware;
using Dolphin.Input;

internal static class Program
{
    private static void Require(bool condition){if(!condition)throw new InvalidOperationException("Connection contract failed.");}
    private static void Card(MemoryCardSlot slot,HardwarePresence confirmed,HardwarePresence probe,bool connected=false,bool disconnected=false)
    {
        var state=MemoryCards.GetState(slot);
        Require(state.Presence==confirmed && state.ProbePresence==probe);
        Require(state.IsKnown==(confirmed==HardwarePresence.Present || confirmed==HardwarePresence.Absent));
        Require(state.IsConnected==(confirmed==HardwarePresence.Present));
        Require(state.WasConnected==connected && state.WasDisconnected==disconnected);
    }
    private static async Task<int> Main()
    {
        Card(MemoryCardSlot.A,HardwarePresence.Unknown,HardwarePresence.Unknown);
        Card(MemoryCardSlot.B,HardwarePresence.Unknown,HardwarePresence.Unknown);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Present,HardwarePresence.Present,true);
        Card(MemoryCardSlot.B,HardwarePresence.Absent,HardwarePresence.Absent);
        var copy=MemoryCards.GetState();
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Present,HardwarePresence.Busy);
        Card(MemoryCardSlot.B,HardwarePresence.Present,HardwarePresence.Present,true);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Present,HardwarePresence.Unknown);
        Card(MemoryCardSlot.B,HardwarePresence.Present,HardwarePresence.Present);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Absent,HardwarePresence.Absent,false,true);
        Card(MemoryCardSlot.B,HardwarePresence.Present,HardwarePresence.Busy);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Present,HardwarePresence.Present,true);
        Card(MemoryCardSlot.B,HardwarePresence.Absent,HardwarePresence.Absent,false,true);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Present,HardwarePresence.Unsupported);
        Card(MemoryCardSlot.B,HardwarePresence.Absent,HardwarePresence.Unknown);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Present,HardwarePresence.Unknown);
        Card(MemoryCardSlot.B,HardwarePresence.Absent,HardwarePresence.Absent);
        MemoryCards.Update();
        Card(MemoryCardSlot.A,HardwarePresence.Absent,HardwarePresence.Absent,false,true);
        Card(MemoryCardSlot.B,HardwarePresence.Present,HardwarePresence.Present,true);
        Require(copy.IsConnected && copy.WasConnected && copy.ProbePresence==HardwarePresence.Present);
        try{MemoryCards.GetState((MemoryCardSlot)(-1));return 1;}catch(ArgumentOutOfRangeException){}
        try{MemoryCards.GetState((MemoryCardSlot)2);return 2;}catch(ArgumentOutOfRangeException){}
        bool guarded=await Task.Run(()=>{try{MemoryCards.Update();return false;}catch(InvalidOperationException){return true;}});
        Require(guarded);
        guarded=await Task.Run(()=>{try{MemoryCards.GetState();return false;}catch(InvalidOperationException){return copy.IsConnected;}});
        Require(guarded);

        Controller.Update();
        for(int p=0;p<4;p++)Require(Controller.GetState(p).WasConnected && Controller.GetState(p).IsConnected);
        for(int port=0;port<4;port++)
        {
            var previous=Controller.GetState(port);
            Controller.Update();
            for(int p=0;p<4;p++)
            {
                var state=Controller.GetState(p);
                Require(state.IsConnected==(p!=port) && state.WasDisconnected==(p==port) && !state.WasConnected);
                if(p==port)Require(!state.A.IsHeld && !state.A.WasReleased);
            }
            Require(previous.IsConnected && previous.A.IsHeld);
            Controller.Update();
            for(int p=0;p<4;p++)
            {
                var state=Controller.GetState(p);
                Require(state.IsConnected && state.WasConnected==(p==port) && !state.WasDisconnected);
                Require(state.A.IsHeld && !state.A.WasPressed);
            }
        }
        return 0;
    }
}
