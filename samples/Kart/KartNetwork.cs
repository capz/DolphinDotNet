using System;
using System.Threading.Tasks;
using Dolphin.Network;
using Dolphin.Timing;

internal sealed class KartNetwork : IDisposable
{
    internal const int Port=28777;
    // Optional direct peer address for LANs that suppress broadcast. Both
    // consoles still own their kart; this does not select a server/host.
    internal static string DirectPeer="";
    internal readonly KartPeer Peer;
    internal string Status="OFFLINE DRIVE";
    private Task? starting;
    private Socket? socket;
    private IPEndPoint? remote;
    private readonly IPEndPoint discovery;
    private readonly byte[] send=new byte[KartPeer.PacketSize],receive=new byte[KartPeer.PacketSize+1];
    private readonly int[] endpoint=new int[2];
    private float sendIn,retryIn;
    private long lastTime=Clock.Timestamp;
    private readonly int localPort,peerPort;
    private bool disposed;
    internal KartNetwork(int id,int localPort=Port,int peerPort=Port)
    {
        this.localPort=localPort;this.peerPort=peerPort;
        Peer=new KartPeer(id);
        discovery=new IPEndPoint(DirectPeer.Length==0?IPAddress.Broadcast:IPAddress.Parse(DirectPeer),peerPort);
    }
    private void Open()
    {
        var opened=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
        try{opened.EnableBroadcast=true;opened.Bind(new IPEndPoint(IPAddress.Any,localPort));}
        catch{opened.Dispose();throw;}
        socket=opened;Status="LAN SEARCHING";
    }
    internal void Update(float seconds,KartGame game)
    {
        long now=Clock.Timestamp;seconds=(float)((now-lastTime)/(double)Clock.Frequency);lastTime=now;
        Peer.Update(seconds);if(!Peer.Connected)remote=null;
        if(disposed)return;
        retryIn-=seconds;
        try
        {
            if(socket==null)
            {
                if(starting!=null)
                {
                    if(!starting.IsCompleted)return;
                    var completed=starting;starting=null;completed.GetAwaiter().GetResult();
                    Open();
                }
                else if(retryIn<=0)
                {
                    retryIn=5;
                    if(NetworkRuntime.IsReady)Open();
                    else{Status="DHCP CONNECTING";starting=NetworkRuntime.StartAsync();}
                }
                if(socket==null)return;
            }
            // Bound work per video frame, even on a flooded broadcast network.
            for(int i=0;i<8;i++)
            {
                int count=socket.TryReceiveFrom(receive,0,receive.Length,endpoint);
                if(count<0)break;
                var address=new IPAddress(new[]{(byte)((uint)endpoint[0]>>24),(byte)((uint)endpoint[0]>>16),(byte)((uint)endpoint[0]>>8),(byte)endpoint[0]});
                if(endpoint[1]!=peerPort || (remote!=null && !remote.Address.Equals(address)))continue;
                if(Peer.Accept(receive,count))remote=new IPEndPoint(address,endpoint[1]);
            }
            if(Peer.Paired)
            {
                game.Items.Acknowledge(Peer);
                game.Items.HitRemote(game,Peer);
            }
            sendIn-=seconds;
            if(sendIn<=0)
            {
                sendIn=.05f;Peer.Write(send,game);
                // UDP is nonblocking; a full send buffer drops this snapshot.
                socket.TrySendTo(send,0,send.Length,remote??discovery);
            }
            Status=Peer.Connected?(Peer.Age>.5f?"PEER SIGNAL WEAK":Peer.Paired?"PEER CONNECTED":"LAN PAIRING"):"LAN SEARCHING";
        }
        catch(Exception)
        {
            socket?.Dispose();socket=null;remote=null;starting=null;
            Peer.Connected=false;Status="OFFLINE RETRYING";retryIn=5;
        }
    }
    internal int Position(KartGame game)
    {
        if(!Peer.Paired)return 1;
        if(game.Finished && Peer.Lap==3)return game.Elapsed<=Peer.RemoteTime?1:2;
        float remoteAngle=MathF.Atan2(Peer.Z,Peer.X);
        if(remoteAngle<0)remoteAngle+=2*MathF.PI;
        return game.Lap*2*MathF.PI+game.Angle>=Peer.Lap*2*MathF.PI+remoteAngle?1:2;
    }
    public void Dispose()
    {
        disposed=true;socket?.Dispose();socket=null;
        // A native DHCP setup may still be in progress. Stop rejects that
        // state; process exit owns shutdown rather than blocking gameplay.
        try{if(NetworkRuntime.IsReady)NetworkRuntime.Stop();}catch(Exception){}
    }
}
