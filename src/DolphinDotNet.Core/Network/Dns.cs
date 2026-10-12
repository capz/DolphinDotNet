using System;
using System.Collections.Generic;
using Dolphin.Threading;
using Dolphin.Threading.Tasks;
namespace Dolphin.Network;
public static class Dns
{
    private sealed class CacheEntry {internal string Host="";internal IPAddress[] Addresses=new IPAddress[0];internal int At,Milliseconds;}
    private static readonly List<CacheEntry> cache=new List<CacheEntry>();
    private static int queryId;
    internal static void ClearCache()=>cache.Clear();
    public static Task<IPAddress[]> GetHostAddressesAsync(string host)=>GetHostAddressesAsync(host,default);
    public static Task<IPAddress[]> GetHostAddressesAsync(string host,CancellationToken token)
    {
        NetworkRuntime.RequireReady();token.ThrowIfCancellationRequested();if(IPAddress.TryParse(host,out var ip))return Task.FromResult(new[]{ip});host=Normalize(host);
        for(int i=cache.Count-1;i>=0;i--){var e=cache[i];if(unchecked(NetworkRuntime.Now-e.At)>=e.Milliseconds)cache.RemoveAt(i);else if(e.Host==host)return Task.FromResult(Copy(e.Addresses));}
        if(NetworkRuntime.Options.DnsServers.Length==0)throw new InvalidOperationException("Configure at least one DNS server.");
        var work=new ResolveWork(host,token);try{NetworkRuntime.Add(work);}catch{work.Abort();throw;}return work.Completion.Task;
    }
    public static Task<IPHostEntry> GetHostEntryAsync(string host){var work=new EntryWork(host,GetHostAddressesAsync(host));work.Start();return work.Completion.Task;}
    private sealed class EntryWork
    {
        private readonly string host;private readonly Task<IPAddress[]> lookup;internal readonly TaskCompletionSource<IPHostEntry> Completion=new TaskCompletionSource<IPHostEntry>();
        internal EntryWork(string h,Task<IPAddress[]> t){host=h;lookup=t;}
        internal void Start()=>lookup.Continue(Finish);
        private void Finish(){try{Completion.SetResult(new IPHostEntry{HostName=host,AddressList=lookup.GetAwaiter().GetResult()});}catch(Exception e){Completion.SetException(e);}}
    }
    private static IPAddress[] Copy(IPAddress[] source){var result=new IPAddress[source.Length];Array.Copy(source,result,source.Length);return result;}
    private static string Normalize(string host)
    {
        if(host==null||host.Length==0||host.Length>253)throw new ArgumentException("Invalid DNS hostname.");if(host[host.Length-1]=='.')host=host.Substring(0,host.Length-1);var chars=new char[host.Length];int label=0;
        for(int i=0;i<host.Length;i++){char c=host[i];if(c=='.'){if(label==0||label>63)throw new ArgumentException("Invalid DNS label.");label=0;}else{if(!(c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='-'))throw new ArgumentException("Only ASCII DNS names are supported.");label++;}chars[i]=c>='A'&&c<='Z'?(char)(c+32):c;}
        if(label==0||label>63)throw new ArgumentException("Invalid DNS label.");return new string(chars);
    }
    private sealed class ResolveWork : Operation
    {
        private readonly string original;private string host;private byte[] query;private readonly byte[] response=new byte[4096];private readonly int[] endpoint=new int[2];private int handle,server,lastSend,attempts,stage,offset,length,aliases;private int ttl=3600;private bool sent;
        internal readonly TaskCompletionSource<IPAddress[]> Completion=new TaskCompletionSource<IPAddress[]>();
        internal ResolveWork(string h,CancellationToken t):base(t){original=host=h;query=Build(h);OpenUdp();}
        private void OpenUdp(){handle=NativeNetwork.NetOpen(0);NetworkRuntime.Check(handle);NetworkRuntime.Check(NativeNetwork.NetBind(handle,0,0));stage=0;sent=false;lastSend=NetworkRuntime.Now;}
        private static byte[] Build(string h){var q=new byte[h.Length+18];int id=unchecked(++queryId+NetworkRuntime.Now)&65535;q[0]=(byte)(id>>8);q[1]=(byte)id;q[2]=1;q[5]=1;int p=12,start=0;for(int i=0;i<=h.Length;i++)if(i==h.Length||h[i]=='.'){q[p++]=(byte)(i-start);while(start<i)q[p++]=(byte)h[start++];start=i+1;}q[p++]=0;q[p++]=0;q[p++]=1;q[p++]=0;q[p++]=1;return q;}
        protected override bool Step()
        {
            var options=NetworkRuntime.Options;int ip=options.DnsServers[server].Value;
            if(stage==0){if(!sent){int n=NativeNetwork.NetWrite(handle,query,0,query.Length,ip,options.DnsPort);NetworkRuntime.Check(n);if(n==-1)return false;sent=true;lastSend=NetworkRuntime.Now;return false;}int size=NativeNetwork.NetRead(handle,response,0,response.Length,endpoint);NetworkRuntime.Check(size);if(size==-1){if(unchecked(NetworkRuntime.Now-lastSend)>=1000){if(++attempts>=4)throw new TimeoutException("DNS lookup timed out.");server=(server+1)%options.DnsServers.Length;sent=false;}return false;}if(endpoint[0]!=ip||endpoint[1]!=options.DnsPort||size<12||response[0]!=query[0]||response[1]!=query[1])return false;if((response[2]&2)!=0){NativeNetwork.NetClose(handle);handle=NativeNetwork.NetOpen(1);NetworkRuntime.Check(handle);int r=NativeNetwork.NetConnect(handle,ip,options.DnsPort);NetworkRuntime.Check(r);stage=r==-1?1:2;offset=0;return false;}return Parse(size);}
            if(stage==1){int r=NativeNetwork.NetConnected(handle);NetworkRuntime.Check(r);if(r==-1)return false;stage=2;return false;}
            if(stage==2){var framed=new byte[query.Length+2];framed[0]=(byte)(query.Length>>8);framed[1]=(byte)query.Length;Array.Copy(query,0,framed,2,query.Length);int n=NativeNetwork.NetWrite(handle,framed,offset,framed.Length-offset,0,0);NetworkRuntime.Check(n);if(n==-1)return false;if(n==0)throw new FormatException("DNS connection closed.");offset+=n;if(offset==framed.Length){offset=0;stage=3;}return false;}
            if(stage==3){int n=NativeNetwork.NetRead(handle,response,offset,2-offset,null!);NetworkRuntime.Check(n);if(n==-1)return false;if(n==0)throw new FormatException("DNS length truncated.");offset+=n;if(offset==2){length=(response[0]<<8)|response[1];if(length<12||length>response.Length)throw new FormatException("DNS response exceeds limit.");offset=0;stage=4;}return false;}
            int read=NativeNetwork.NetRead(handle,response,offset,length-offset,null!);NetworkRuntime.Check(read);if(read==-1)return false;if(read==0)throw new FormatException("DNS response truncated.");offset+=read;return offset==length&&Parse(length);
        }
        private bool Parse(int size)
        {
            if(response[0]!=query[0]||response[1]!=query[1]||(response[2]&128)==0||(response[2]&120)!=0||(response[3]&15)!=0||U16(response,4,size)!=1)throw new FormatException("DNS response rejected.");
            int p=12;string question=ReadName(response,ref p,size);if(question!=host||U16(response,p,size)!=1||U16(response,p+2,size)!=1)throw new FormatException("DNS question mismatch.");p+=4;int records=U16(response,6,size);if(records>128)throw new FormatException("Too many DNS answers.");var owners=new string[records];var targets=new string[records];var addresses=new int[records];var kinds=new int[records];var lifetimes=new int[records];
            for(int i=0;i<records;i++){owners[i]=ReadName(response,ref p,size);U16(response,p+8,size);int type=U16(response,p,size),cls=U16(response,p+2,size);int seconds=(response[p+4]&128)!=0?3600:(response[p+4]<<24)|(response[p+5]<<16)|(response[p+6]<<8)|response[p+7];int bytes=U16(response,p+8,size);p+=10;if(bytes>size-p)throw new FormatException("DNS record truncated.");if(cls==1){kinds[i]=type;lifetimes[i]=seconds>3600?3600:seconds;if(type==1&&bytes!=4)throw new FormatException("Invalid DNS IPv4 record.");if(type==1&&bytes==4)addresses[i]=(response[p]<<24)|(response[p+1]<<16)|(response[p+2]<<8)|response[p+3];else if(type==5){int rp=p;targets[i]=ReadName(response,ref rp,size);if(rp>p+bytes)throw new FormatException("CNAME exceeds record.");}else kinds[i]=0;}p+=bytes;}
            string name=host;for(int chain=0;chain<8;chain++){var found=new List<IPAddress>();string? next=null;for(int i=0;i<records;i++)if(owners[i]==name){if(kinds[i]==1){found.Add(new IPAddress(addresses[i]));if(lifetimes[i]<ttl)ttl=lifetimes[i];}if(kinds[i]==5){next=targets[i];if(lifetimes[i]<ttl)ttl=lifetimes[i];}}if(found.Count>0){var result=new IPAddress[found.Count];for(int i=0;i<result.Length;i++)result[i]=found[i];if(ttl>0){if(cache.Count==16)cache.RemoveAt(0);cache.Add(new CacheEntry{Host=original,Addresses=result,At=NetworkRuntime.Now,Milliseconds=ttl*1000});}Close();Completion.SetResult(Copy(result));return true;}if(next==null){if(name!=host&&++aliases<8){host=Normalize(name);query=Build(host);Close();OpenUdp();return false;}throw new FormatException("DNS has no IPv4 result.");}name=next;}throw new FormatException("CNAME chain exceeds limit.");
        }
        private void Close(){if(handle>0){NativeNetwork.NetClose(handle);handle=0;}}
        protected override void Fail(Exception e){Close();Completion.SetException(e);}
    }
    private static int U16(byte[] bytes,int p,int size){if(p<0||p>size-2)throw new FormatException("DNS field truncated.");return (bytes[p]<<8)|bytes[p+1];}
    private static string ReadName(byte[] bytes,ref int position,int size)
    {
        int p=position,end=-1,jumps=0,n=0;var chars=new char[253];while(true){if(p>=size||++jumps>128)throw new FormatException("DNS name invalid.");int count=bytes[p++];if(count==0){position=end<0?p:end;return new string(chars,0,n);}if((count&192)==192){if(p>=size)throw new FormatException();if(end<0)end=p+1;p=((count&63)<<8)|bytes[p];continue;}if(count>63||count>size-p)throw new FormatException();if(n>0){if(n==chars.Length)throw new FormatException();chars[n++]='.';}if(count>chars.Length-n)throw new FormatException();for(int i=0;i<count;i++){char c=(char)bytes[p++];chars[n++]=c>='A'&&c<='Z'?(char)(c+32):c;}}
    }
}
