using System;
namespace Dolphin.Network;
internal static class NativeNetwork
{
    private static Exception Unsupported()=>new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static int TlsConfigure(byte[] roots,byte[] entropy,long unixSeconds)=>throw Unsupported();
    internal static int TlsReset()=>throw Unsupported();
    internal static int TlsStart(int handle,string hostname)=>throw Unsupported();
    internal static int TlsHandshake(int handle)=>throw Unsupported();
    internal static int NetStart(int ip,int mask,int gateway,int dhcp)=>throw Unsupported();
    internal static int NetStatus()=>throw Unsupported();
    internal static int NetTime()=>throw Unsupported();
    internal static int NetOpen(int stream)=>throw Unsupported();
    internal static int NetBind(int handle,int ip,int port)=>throw Unsupported();
    internal static int NetListen(int handle,int backlog)=>throw Unsupported();
    internal static int NetAccept(int handle,int[] endpoint)=>throw Unsupported();
    internal static int NetConnect(int handle,int ip,int port)=>throw Unsupported();
    internal static int NetConnected(int handle)=>throw Unsupported();
    internal static int NetRead(int handle,byte[] buffer,int offset,int count,int[] endpoint)=>throw Unsupported();
    internal static int NetWrite(int handle,byte[] buffer,int offset,int count,int ip,int port)=>throw Unsupported();
    internal static int NetEndpoint(int handle,int[] endpoint,int remote)=>throw Unsupported();
    internal static int NetOption(int handle,int option,int value)=>throw Unsupported();
    internal static int NetClose(int handle)=>throw Unsupported();
    internal static int NetStop()=>throw Unsupported();
    internal static int NetError()=>throw Unsupported();
    internal static int NetFailureCode(object exception)=>throw Unsupported();
}
