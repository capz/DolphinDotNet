using System;
using Dolphin.Storage;
using StorageApi = Dolphin.Storage.Storage;

namespace Dolphin.Hardware;

public enum HardwareKind
{
    Controller=0, GameBoyAdvanceLink=1, MemoryCard=2, SdCard=3,
    Sd2Sp2=4, BroadbandAdapter=5, Modem=6, GameDisc=7,
    GcLoader=8, GameBoyPlayer=9
}

public enum HardwarePort
{
    Controller1=0, Controller2=1, Controller3=2, Controller4=3,
    SlotA=4, SlotB=5, Serial1=6, Serial2=7,
    DiscDrive=8, HighSpeed=9
}

public enum HardwarePresence
{
    Absent=0, Present=1, Busy=2, Unknown=3, Unsupported=4
}
public enum HardwareProbeMode { Safe=0, Extended=1 }

public sealed class HardwareDeviceInfo
{
    public HardwareKind Kind { get; }
    public HardwarePort Port { get; }
    public HardwarePresence Presence { get; }
    public uint NativeId { get; }
    public bool IsMounted { get; }
    internal HardwareDeviceInfo(HardwareKind kind,HardwarePort port,HardwarePresence presence,
                                uint nativeId,bool mounted)
    {
        Kind=kind;Port=port;Presence=presence;NativeId=nativeId;IsMounted=mounted;
    }
}

/// <summary>Immutable result of one hardware scan. Unknown/Busy must not be
/// interpreted as absent.</summary>
public sealed class HardwareSnapshot
{
    public HardwareDeviceInfo[] Devices { get; }
    internal HardwareSnapshot(HardwareDeviceInfo[] devices) { Devices=devices; }
    public HardwareDeviceInfo[] ConnectedDevices
    {
        get
        {
            int count=0;
            for(int i=0;i<Devices.Length;i++)
                if(Devices[i].Presence==HardwarePresence.Present)count++;
            var connected=new HardwareDeviceInfo[count];
            int next=0;
            for(int i=0;i<Devices.Length;i++)
                if(Devices[i].Presence==HardwarePresence.Present)connected[next++]=Devices[i];
            return connected;
        }
    }
    public HardwareDeviceInfo? GetDevice(HardwareKind kind,HardwarePort port)
    {
        for(int i=0;i<Devices.Length;i++)
            if(Devices[i].Kind==kind && Devices[i].Port==port)return Devices[i];
        return null;
    }
    public HardwarePresence GetPresence(HardwareKind kind,HardwarePort port)
    {
        var found=GetDevice(kind,port);
        return found==null ? HardwarePresence.Unsupported : found.Presence;
    }
    public bool IsPresent(HardwareKind kind,HardwarePort port) =>
        GetPresence(kind,port)==HardwarePresence.Present;
}

public static class Hardware
{
    private static HardwareSnapshot? current;
    public static HardwareSnapshot Current => current ?? Refresh();

    private static HardwareDeviceInfo Query(HardwareKind kind,HardwarePort port,HardwareProbeMode mode)
    {
        int value=NativeHardware.HardwareProbe((int)kind,(int)port,(int)mode);
        var presence=value>=0 && value<=4?(HardwarePresence)value:HardwarePresence.Unknown;
        bool mounted=false;
        if(kind==HardwareKind.MemoryCard)
            mounted=StorageApi.IsMounted(port==HardwarePort.SlotA?StorageDevice.MemoryCardSlotA:StorageDevice.MemoryCardSlotB);
        else if(kind==HardwareKind.SdCard)
            mounted=StorageApi.IsMounted(port==HardwarePort.SlotA?StorageDevice.SdSlotA:StorageDevice.SdSlotB);
        else if(kind==HardwareKind.Sd2Sp2)mounted=StorageApi.IsMounted(StorageDevice.SdSerialPort2);
        else if(kind==HardwareKind.GcLoader)mounted=StorageApi.IsMounted(StorageDevice.GcLoader);
        else if(kind==HardwareKind.GameDisc)mounted=StorageApi.IsMounted(StorageDevice.DvdDrive);
        uint id=presence==HardwarePresence.Present?(uint)NativeHardware.HardwareId((int)kind,(int)port):0;
        return new HardwareDeviceInfo(kind,port,presence,id,mounted);
    }
    /// <summary>Safe uses cached state; Extended may make read-only hardware probes.
    /// Never mounts or resets devices.</summary>
    public static HardwareSnapshot Refresh(HardwareProbeMode mode=HardwareProbeMode.Safe)
    {
        if(mode!=HardwareProbeMode.Safe && mode!=HardwareProbeMode.Extended)
            throw new ArgumentOutOfRangeException("mode");
        var data=new HardwareDeviceInfo[18];
        int i=0;
        for(int port=0;port<4;port++)
            data[i++]=Query(HardwareKind.Controller,(HardwarePort)port,mode);
        for(int port=0;port<4;port++)
            data[i++]=Query(HardwareKind.GameBoyAdvanceLink,(HardwarePort)port,mode);
        data[i++]=Query(HardwareKind.MemoryCard,HardwarePort.SlotA,mode);
        data[i++]=Query(HardwareKind.MemoryCard,HardwarePort.SlotB,mode);
        data[i++]=Query(HardwareKind.SdCard,HardwarePort.SlotA,mode);
        data[i++]=Query(HardwareKind.SdCard,HardwarePort.SlotB,mode);
        data[i++]=Query(HardwareKind.Sd2Sp2,HardwarePort.Serial2,mode);
        data[i++]=Query(HardwareKind.BroadbandAdapter,HardwarePort.Serial1,mode);
        data[i++]=Query(HardwareKind.Modem,HardwarePort.Serial1,mode);
        data[i++]=Query(HardwareKind.GameDisc,HardwarePort.DiscDrive,mode);
        data[i++]=Query(HardwareKind.GcLoader,HardwarePort.DiscDrive,mode);
        data[i++]=Query(HardwareKind.GameBoyPlayer,HardwarePort.HighSpeed,mode);
        current=new HardwareSnapshot(data);
        return current;
    }
}
