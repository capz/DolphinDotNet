using System;
using System.Collections.Generic;
using Dolphin.Threading;
using Dolphin.Threading.Tasks;
namespace Dolphin.Network;
public sealed class NetworkOptions
{
    public bool UseDhcp {get;set;}=true;
    public IPAddress Address {get;set;}=IPAddress.Any;
    public IPAddress Netmask {get;set;}=IPAddress.Any;
    public IPAddress Gateway {get;set;}=IPAddress.Any;
    public IPAddress[] DnsServers {get;set;}=new IPAddress[0];
    public int DnsPort {get;set;}=53;
    public int OperationTimeoutMilliseconds {get;set;}=10000;
}
public static class NetworkRuntime
{
    private static readonly List<Operation> pending=new List<Operation>();
    private static int cursor;
    public static bool IsReady {get;private set;}
    public static NetworkOptions Options {get;private set;}=new NetworkOptions();
    public static int PendingOperations=>pending.Count;
    internal static int Now=>NativeNetwork.NetTime();
    internal static void Add(Operation operation){if(pending.Count>=32)throw new InvalidOperationException("Network operation limit reached.");pending.Add(operation);}
    public static Task StartAsync()=>StartAsync(new NetworkOptions(),default);
    public static Task StartAsync(NetworkOptions options,CancellationToken token)
    {
        if(options==null)throw new ArgumentNullException("options");if(options.OperationTimeoutMilliseconds<1||options.OperationTimeoutMilliseconds>600000)throw new ArgumentOutOfRangeException("timeout");
        if(options.DnsPort<1||options.DnsPort>65535||options.DnsServers==null||options.DnsServers.Length>4)throw new ArgumentException("Invalid DNS configuration.");
        if(IsReady||pending.Count!=0)throw new InvalidOperationException("Network already started or busy.");
        Options=options;Dns.ClearCache();Check(NativeNetwork.NetStart(options.Address.Value,options.Netmask.Value,options.Gateway.Value,options.UseDhcp?1:0));
        var work=new StartOperation(token);Add(work);return work.Completion.Task;
    }
    public static int Pump(int maximumOperations=32,int maximumMilliseconds=1)
    {
        if(maximumOperations<1||maximumMilliseconds<1)throw new ArgumentOutOfRangeException();int start=Now,done=0,available=pending.Count;
        while(done<maximumOperations&&available-->0&&pending.Count>0){if(cursor>=pending.Count)cursor=0;var operation=pending[cursor];bool finished=operation.Advance();done++;if(finished)pending.RemoveAt(cursor);else cursor++;if(unchecked(Now-start)>=maximumMilliseconds)break;}
        return done;
    }
    public static void Stop(){if(NativeNetwork.NetStatus()==0)throw new InvalidOperationException("Interface setup is still running.");IsReady=false;NativeNetwork.NetStop();for(int i=0;i<pending.Count;i++)pending[i].Abort();pending.Clear();cursor=0;Dns.ClearCache();}
    internal static void RequireReady(){if(!IsReady)throw new InvalidOperationException("Network is not ready.");}
    internal static void Check(int result){if(result<0&&result!=-1)throw new SocketException(result==-2?NativeNetwork.NetError():0,result);}
    private sealed class StartOperation : Operation
    {
        internal readonly TaskCompletionSource<int> Completion=new TaskCompletionSource<int>();
        internal StartOperation(CancellationToken token):base(token){}
        protected override bool Step(){int state=NativeNetwork.NetStatus();if(state==0)return false;if(state!=1)throw new SocketException(NativeNetwork.NetError(),state);IsReady=true;Completion.SetResult(0);return true;}
        protected override void Fail(Exception error)=>Completion.SetException(error);
    }
}
internal abstract class Operation
{
    private readonly CancellationToken token;private readonly int started,timeout;private bool finished;
    protected Operation(CancellationToken token){this.token=token;started=NetworkRuntime.Now;timeout=NetworkRuntime.Options.OperationTimeoutMilliseconds;}
    internal bool Advance(){if(finished)return true;try{token.ThrowIfCancellationRequested();if(unchecked(NetworkRuntime.Now-started)>=timeout)throw new TimeoutException("Network operation timed out.");finished=Step();}catch(Exception error){finished=true;Fail(error);}return finished;}
    internal void Abort(){if(!finished){finished=true;Fail(new OperationCanceledException());}}
    protected abstract bool Step();
    protected abstract void Fail(Exception error);
}
