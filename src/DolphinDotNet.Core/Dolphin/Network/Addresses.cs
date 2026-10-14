using System;
namespace Dolphin.Network;
public class EndPoint {}
public sealed class IPAddress
{
    internal readonly int Value;
    internal IPAddress(int value){Value=value;}
    public IPAddress(byte[] bytes){if(bytes==null||bytes.Length!=4)throw new ArgumentException("IPv4 needs four bytes.");Value=(bytes[0]<<24)|(bytes[1]<<16)|(bytes[2]<<8)|bytes[3];}
    public static readonly IPAddress Any=new IPAddress(0);
    public static readonly IPAddress Loopback=new IPAddress(0x7f000001);
    public static readonly IPAddress Broadcast=new IPAddress(-1);
    public byte[] GetAddressBytes()=>new byte[]{(byte)((uint)Value>>24),(byte)((uint)Value>>16),(byte)((uint)Value>>8),(byte)Value};
    public static IPAddress Parse(string text){if(!TryParse(text,out var address))throw new FormatException("Invalid IPv4 address.");return address;}
    public static bool TryParse(string text,out IPAddress address)
    {
        address=Any;if(text==null)return false;int value=0,part=0,digits=0,parts=0;
        for(int i=0;i<=text.Length;i++){char c=i==text.Length?'.':text[i];if(c>='0'&&c<='9'){if(++digits>3)return false;part=part*10+c-'0';if(part>255)return false;}else if(c=='.'){if(digits==0||parts==4)return false;value=(value<<8)|part;parts++;part=digits=0;}else return false;}
        if(parts!=4)return false;address=new IPAddress(value);return true;
    }
    public override string ToString()=>Format();
    public string Format(){var b=GetAddressBytes();return string.Concat(string.Concat(string.Concat(((int)b[0]).ToString(),"."),string.Concat(((int)b[1]).ToString(),".")),string.Concat(string.Concat(((int)b[2]).ToString(),"."),((int)b[3]).ToString()));}
    public override bool Equals(object? other)=>other is IPAddress ip&&Value==ip.Value;
    public override int GetHashCode()=>Value;
    public static bool IsLoopback(IPAddress address)=>((uint)address.Value>>24)==127;
}
public sealed class IPEndPoint : EndPoint
{
    public IPAddress Address {get;set;}
    private int port;
    public int Port {get=>port;set{if(value<0||value>65535)throw new ArgumentOutOfRangeException("port");port=value;}}
    public IPEndPoint(IPAddress address,int port){Address=address??throw new ArgumentNullException("address");Port=port;}
    public override string ToString()=>string.Concat(string.Concat(Address.Format(),":"),Port.ToString());
}
public class ExternalException : Exception
{
    public int ErrorCode=>NativeNetwork.NetFailureCode(this);
    protected ExternalException(string message):base(message){}
}
public sealed class SocketException : ExternalException
{
    public SocketException(int error,int result):base(result==-5?"Datagram truncated.":result==-3?"Socket disposed.":result==-4?"Socket limit reached.":"Network operation failed."){}
}
public sealed class IPHostEntry
{
    public string HostName {get;set;}="";
    public string[] Aliases {get;set;}=new string[0];
    public IPAddress[] AddressList {get;set;}=new IPAddress[0];
}
