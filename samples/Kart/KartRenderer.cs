using System;
using Dolphin.Graphics;

internal sealed class KartRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Renderer renderer;
    private readonly Camera camera=new Camera(60,4f/3,.5f,230);
    private readonly Transform transform=new Transform();
    private readonly Material material=new Material { Cull=CullMode.None };
    private readonly Mesh box,track;
    private readonly KartHud hud;
    internal KartRenderer(GraphicsDevice device)
    {
        this.device=device;renderer=new Renderer(device);box=KartGeometry.Box();
        try{track=KartGeometry.Track();}catch{box.Dispose();throw;}
        try{hud=new KartHud(device);}catch{track.Dispose();box.Dispose();throw;}
    }
    private void Draw(Mesh mesh,float x,float y,float z,float sx,float sy,float sz,Color32 color,float yaw=0)
    {
        transform.LocalPosition=new Vector3(x,y,z);transform.LocalScale=new Vector3(sx,sy,sz);
        transform.LocalRotation=Quaternion.Identity;if(yaw!=0)transform.Rotate(0,yaw,0);
        material.Color=color;renderer.DrawMesh(mesh,material,transform);
    }
    private void Kart(float x,float y,float z,float heading,Color32 color,bool glider)
    {
        float yaw=heading*180/MathF.PI;
        Draw(box,x,y+.55f,z,1.5f,.5f,2.5f,color,yaw);
        Draw(box,x,y+1,z-.15f,.9f,.6f,1.1f,new Color32(250,230,190),yaw);
        Draw(box,x,y+1.65f,z-.15f,.75f,.65f,.75f,color,yaw);
        for(int i=0;i<4;i++)
        {
            float dx=i%2==0?-.85f:.85f,dz=i<2?-.8f:.8f;
            Draw(box,x+MathF.Cos(heading)*dx+MathF.Sin(heading)*dz,y+.35f,
                z-MathF.Sin(heading)*dx+MathF.Cos(heading)*dz,.45f,.65f,.65f,new Color32(25,30,46),yaw);
        }
        if(glider)Draw(box,x,y+2.6f,z,5.2f,.12f,1.6f,new Color32(255,237,120),yaw);
    }
    private void ItemObjects(KartObject[] objects)
    {
        for(int i=0;i<KartItems.Capacity;i++)
        {
            var item=objects[i];if(item.Life<=0)continue;
            if(item.Kind==KartItem.Hazard)
            {
                Draw(box,item.X,.15f,item.Z,1.6f,.3f,1.6f,new Color32(205,87,194),45);
                Draw(box,item.X,.4f,item.Z,.6f,.35f,.6f,new Color32(255,192,237));
            }
            else
            {
                Draw(box,item.X,item.Height+.65f,item.Z,.7f,.7f,1.2f,new Color32(143,255,87),item.Heading*180/MathF.PI);
                Draw(box,item.X,item.Height+.7f,item.Z,.3f,.3f,1.5f,Color32.White,item.Heading*180/MathF.PI);
            }
        }
    }
    internal void Present(KartGame game,KartNetwork network,bool connected,int best=0)
    {
        float fx=MathF.Sin(game.Heading),fz=MathF.Cos(game.Heading);
        camera.LookAt(new Vector3(game.X-fx*11,game.Height+6,game.Z-fz*11),
            new Vector3(game.X+fx*10,game.Height+1.3f,game.Z+fz*10),new Vector3(0,1,0));
        device.BeginFrame(new Color32(114,199,239));renderer.Begin3D(camera);
        Draw(box,0,-1.3f,0,400,.5f,400,new Color32(28,146,193));
        Draw(box,0,-.5f,0,136,.6f,136,new Color32(106,180,99));
        Draw(track,0,0,0,1,1,1,Color32.White);
        for(int i=0;i<48;i++)
        {
            float a=i*2*MathF.PI/48,c=MathF.Cos(a),s=MathF.Sin(a);
            var color=i%2==0?new Color32(255,235,202):new Color32(228,91,83);
            Draw(box,c*44.3f,.15f,s*44.3f,1.1f,.3f,5.6f,color,-a*180/MathF.PI);
            Draw(box,c*59.7f,.15f,s*59.7f,1.1f,.3f,7.6f,color,-a*180/MathF.PI);
            if(i%4==0)
            {
                Draw(box,c*33,2,s*33,1,4,1,new Color32(139,98,68));
                Draw(box,c*33,4.2f,s*33,4,3,4,new Color32(41,133,87));
            }
        }
        for(int i=0;i<2;i++)
        {
            float a=i==0?.76f:4.71f;
            Draw(box,MathF.Cos(a)*52,.06f,MathF.Sin(a)*52,5,.1f,5,new Color32(255,191,62),-a*180/MathF.PI);
        }
        for(int i=0;i<KartItems.BoxCount;i++)if(game.Items.Boxes[i]<=0)
        {
            float x=KartItems.BoxX(i),z=KartItems.BoxZ(i);
            Draw(box,x,1.2f,z,1.7f,1.7f,1.7f,new Color32(81,217,239),game.Time*70);
            Draw(box,x,2.2f,z,.7f,.3f,.7f,new Color32(255,233,107));
        }
        ItemObjects(game.Items.Objects);
        if(network.Peer.Connected)ItemObjects(network.Peer.Objects);
        // Start/finish gantry and checkerboard.
        for(int i=0;i<10;i++)Draw(box,45.7f+i*1.4f,.05f,0,1.4f,.08f,1,
            i%2==0?Color32.White:new Color32(23,36,54));
        Draw(box,43.5f,3,0,.5f,6,.5f,new Color32(255,235,202));
        Draw(box,60.5f,3,0,.5f,6,.5f,new Color32(255,235,202));
        Draw(box,52,6,0,18,.7f,1,new Color32(235,105,87));
        Draw(box,0,7,0,20,15,20,new Color32(100,149,121),25);
        Kart(game.X,game.Height,game.Z,game.Heading+game.Stun*12,game.Stun>0?new Color32(250,94,87):new Color32(244,170,68),game.Gliding);
        if(network.Peer.Connected)Kart(network.Peer.X,network.Peer.Height,network.Peer.Z,
            network.Peer.Heading+network.Peer.Stun*12,new Color32(69,211,230),network.Peer.Height>1);
        renderer.End3D();hud.Present(game,network,connected,best);device.EndFrame();
    }
    public void Dispose(){hud.Dispose();track.Dispose();box.Dispose();}
}
