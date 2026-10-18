using System;
using System.Collections.Generic;
using System.IO;
using Dolphin.Hardware;
using Dolphin.Storage;
internal static class Program
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Main()
    {
        using(var first=new SampleRecord("TEST"))
        {
            first.Observe(42);first.Update(0);Check(MemoryCard.Writes==0,"No card remains playable with a pending record.");
            MemoryCards.Present[1]=true;first.Update(2);Check(MemoryCard.Writes==1&&first.Best==42,"Insertion flushes pending best to B.");
            first.Observe(41);first.Update(2);Check(MemoryCard.Writes==1,"Worse scores do not wear the card.");
            MemoryCards.Present[1]=false;first.Observe(45);first.Update(2);Check(first.Best==45,"Removal preserves pending score.");
            MemoryCards.Present[1]=true;first.Update(2);Check(MemoryCard.Writes==2,"Reinsertion remounts and merges.");
        }
        using(var second=new SampleRecord("TEST"))
        {second.Update(0);Check(second.Best==45,"New owner loads validated record.");second.Observe(48);}
        using(var third=new SampleRecord("TEST"))
        {third.Update(0);Check(third.Best==48,"Exit flushes before the polling interval expires.");}
        using(var times=new SampleRecord("TIME",true))
        {times.Observe(1200);times.Update(0);times.Observe(1300);Check(times.Best==1200,"Lower race time wins.");times.Observe(1100);}
        using(var times=new SampleRecord("TIME",true)){times.Update(0);Check(times.Best==1100,"Race record persisted.");}
        MemoryCard.Data["TEST/Record"][8]^=1;
        using(var broken=new SampleRecord("TEST")){broken.Update(0);Check(broken.Best==0,"Corrupt record rejected.");}
        MemoryCard.Fail=true;
        using(var retry=new SampleRecord("FAIL"))
        {
            retry.Observe(5);retry.Update(0);Check(retry.Best==5,"Mount failure preserves pending value.");
            MemoryCard.Fail=false;retry.Update(2);Check(MemoryCard.Data.ContainsKey("FAIL/Record"),"Present cards retry without another insertion edge.");
        }
        Console.WriteLine("Shared record hotplug, merge, retry, checksum and exit flush passed.");
    }
}
namespace Dolphin.Hardware
{
 public enum MemoryCardSlot { A,B }
 public readonly struct MemoryCardState
 {public bool IsKnown=>true;public bool IsConnected{get;}public MemoryCardState(bool yes){IsConnected=yes;}}
 public static class MemoryCards
 {public static readonly bool[] Present={false,false};public static void Update(){}public static MemoryCardState GetState(MemoryCardSlot slot)=>new MemoryCardState(Present[(int)slot]);}
}
namespace Dolphin.Storage
{
 public enum StorageDevice {MemoryCardSlotA,MemoryCardSlotB}
 public sealed class MemoryCardIdentity {public readonly string Code;public MemoryCardIdentity(string code,string company){Code=code;}}
 public sealed class MemoryCardSaveOptions {public string? Title{get;set;}public string? Comment{get;set;}}
 public sealed class MemoryCardEntry {public string Name{get;}public MemoryCardEntry(string name){Name=name;}}
 public sealed class MemoryCard : IDisposable
 {
  public static readonly Dictionary<string,byte[]> Data=new Dictionary<string,byte[]>();public static int Writes;public static bool Fail;
  private readonly string code;private readonly int slot;private bool disposed;
  private MemoryCard(string code,int slot){this.code=code;this.slot=slot;}
  public static MemoryCard Mount(StorageDevice slot,MemoryCardIdentity identity)
  {if(Fail||!MemoryCards.Present[(int)slot])throw new IOException();return new MemoryCard(identity.Code,(int)slot);}
  private void Check(){if(disposed)throw new ObjectDisposedException("card");if(Fail||!MemoryCards.Present[slot])throw new IOException();}
  public void WriteSave(string name,byte[] bytes,MemoryCardSaveOptions options){Check();Data[code+"/"+name]=(byte[])bytes.Clone();Writes++;}
  public byte[] ReadAllBytes(string name){Check();return (byte[])Data[code+"/"+name].Clone();}
  public MemoryCardEntry[] GetEntries(){Check();var entries=new List<MemoryCardEntry>();foreach(var key in Data.Keys)if(key.StartsWith(code+"/"))entries.Add(new MemoryCardEntry(key.Substring(5)));return entries.ToArray();}
  public void Dispose(){disposed=true;}
 }
}
