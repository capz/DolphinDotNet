using System;
using Dolphin.Graphics;

internal sealed class FoxRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Renderer renderer;
    private readonly Camera camera;
    private readonly Mesh ship,box,ring;
    private readonly Transform transform=new Transform();
    private readonly Material material=new Material { Cull=CullMode.None };
    private readonly FoxHud hud;
    private int frame;
    private Model? model;
    private ModelInstance? instance;
    private readonly Dolphin.Timing.FrameClock modelClock=new Dolphin.Timing.FrameClock {MaximumDeltaSeconds=.05f};
    internal FoxRenderer(GraphicsDevice device)
    {
        this.device=device;renderer=new Renderer(device);camera=new Camera(55,4f/3,1,160);
        camera.LookAt(new Vector3(0,3,12),new Vector3(0,0,-42),new Vector3(0,1,0));
        ship=FoxGeometry.Ship();
        try { box=FoxGeometry.Box(); }catch { ship.Dispose();throw; }
        try { ring=FoxGeometry.Ring(); }catch { box.Dispose();ship.Dispose();throw; }
        try { hud=new FoxHud(device); }catch { ring.Dispose();box.Dispose();ship.Dispose();throw; }
        try
        {
            model=Model.FromPackage(ShipModel.Package);instance=model.CreateInstance();
            instance.Animator.Play("Hover",true);
        }
        catch(Exception)
        {
            instance?.Dispose();instance=null;model?.Dispose();model=null;
            Dolphin.Diagnostics.DebugOverlay.WriteLine("MODEL UNAVAILABLE - PROCEDURAL SHIP FALLBACK");
        }
    }
    private void Draw(Mesh mesh,float x,float y,float z,float sx,float sy,float sz,Color32 tint,float pitch=0,float yaw=0,float roll=0)
    {
        transform.LocalPosition=new Vector3(x,y,z);transform.LocalScale=new Vector3(sx,sy,sz);
        transform.LocalRotation=Quaternion.Identity;
        if(pitch!=0 || yaw!=0 || roll!=0)transform.Rotate(pitch,yaw,roll);
        material.Color=tint;
        if(instance!=null&&mesh==ship&&tint.Packed==Color32.White.Packed)renderer.DrawModel(instance,transform);
        else renderer.DrawMesh(mesh,material,transform);
    }
    internal void Present(FoxGame game,bool connected)
    {
        float dt=modelClock.Tick();if(instance!=null&&game.State!=FoxState.Paused)instance.Update(dt);
        frame++;device.BeginFrame(new Color32(7,16,36));renderer.Begin3D(camera);
        Draw(box,0,-5.7f,-64,26,.3f,150,new Color32(28,53,65));
        for(int i=0;i<12;i++)
        {
            float z=4-i*12+game.SceneTravel;
            Draw(box,0,-5.5f,z,17,.04f,.12f,new Color32(50,130,133));
            Draw(box,-10,-3,z,2,5,2,new Color32(55,78,104));
            Draw(box,10,-3,z,2,5,2,new Color32(55,78,104));
        }
        for(int i=0;i<18;i++)
            Draw(box,(i*17%100)-50,10+i*7%24,-140,.55f,.55f,.55f,new Color32(110,145,186));
        for(int i=0;i<FoxGame.RingCount;i++)if(game.Rings[i])
            Draw(ring,game.RingX[i],game.RingY[i],game.RingZ[i],1,1,1,Color32.White);
        for(int i=0;i<FoxGame.EnemyCount;i++)if(game.Enemies[i])
            Draw(ship,game.EnemyX[i],game.EnemyY[i],game.EnemyZ[i],.65f,.65f,.65f,new Color32(255,132,112),0,180,game.EnemyDrift[i]*30);
        if(game.BossActive && game.BossHealth>0)
        {
            Draw(box,game.BossX,game.BossY,game.BossZ,5,1.5f,3,new Color32(155,105,180));
            Draw(ship,game.BossX-3,game.BossY,game.BossZ,1.5f,1.5f,1.5f,new Color32(230,128,150),0,180);
            Draw(ship,game.BossX+3,game.BossY,game.BossZ,1.5f,1.5f,1.5f,new Color32(230,128,150),0,180);
            Draw(box,game.BossX,game.BossY,game.BossZ+1.6f,1,.6f,.25f,new Color32(255,190,72));
        }
        for(int i=0;i<FoxGame.ShotCount;i++)if(game.Shots[i])
        {
            Draw(box,game.ShotX[i]-.4f,game.ShotY[i],game.ShotZ[i],.09f,.09f,2.5f,new Color32(104,255,179));
            Draw(box,game.ShotX[i]+.4f,game.ShotY[i],game.ShotZ[i],.09f,.09f,2.5f,new Color32(104,255,179));
        }
        for(int i=0;i<FoxGame.BoltCount;i++)if(game.Bolts[i])
            Draw(box,game.BoltX[i],game.BoltY[i],game.BoltZ[i],.25f,.25f,1,new Color32(255,137,72));
        if(game.State!=FoxState.GameOver && (game.Invulnerable<=0 || frame%10<5))
        {
            float roll=game.Bank;
            if(game.RollRemaining>0)roll+=game.RollDirection*(1-game.RollRemaining/FoxGame.RollDuration)*360;
            Draw(ship,game.PlayerX,game.PlayerY,0,.8f,.8f,.8f,Color32.White,0,0,roll);
            Draw(box,game.PlayerX,game.PlayerY,.7f,.22f,.22f,game.Speed>20?1.4f:.7f,new Color32(119,202,255));
        }
        renderer.End3D();hud.Present(game,connected);device.EndFrame();
    }
    public void Dispose() { instance?.Dispose();model?.Dispose();hud.Dispose();ring.Dispose();box.Dispose();ship.Dispose(); }
}
