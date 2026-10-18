using System;
using Dolphin.Graphics;

/// <summary>Managed effects, including a build-time compiled material graph.</summary>
internal sealed class ShaderScene : IDisposable
{
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Renderer renderer=new Renderer();
    private readonly Camera camera=new Camera(90,640f/480,0.1f,10);
    private readonly ShaderHud hud;
    private readonly VertexPositionColor[] diskVertices=new VertexPositionColor[96];
    private readonly DynamicMesh disk;
    private readonly Mesh strip;
    private readonly GxEffect ramp=GxEffect.ColorRamp(new Color32(12,18,58),new Color32(60,180,240));
    private readonly GxEffect toon=GxEffect.ToonBands(65,165,new Color32(12,24,64),new Color32(30,90,158),new Color32(70,220,248));
    private readonly GxEffect compiled=GxEffect.FromPackage(CompiledEffect.Package);
    private readonly Material reference=new Material{Cull=CullMode.None,DepthTest=false,DepthWrite=false};
    private readonly Material rampMaterial,toonMaterial;
    private readonly Draw2D draw;
    private readonly Transform left=new Transform(),middle=new Transform(),right=new Transform();
    private readonly int brightSlot,thresholdSlot,graphThresholdSlot;
    internal ShaderScene()
    {
        camera.LookAt(new Vector3(0,0,1),new Vector3(0,0,0),new Vector3(0,1,0));
        left.LocalPosition=new Vector3(-208f/240,0,0);right.LocalPosition=new Vector3(208f/240,0,0);
        float x=0,y=-78;
        for(int i=0;i<32;i++){
            float nx=x*0.98078528f-y*0.19509032f,ny=x*0.19509032f+y*0.98078528f;
            diskVertices[i*3]=Vertex(0,240,255);
            diskVertices[i*3+1]=Vertex(x,252+y,0);diskVertices[i*3+2]=Vertex(nx,252+ny,0);x=nx;y=ny;
        }
        disk=DynamicMesh.Create(diskVertices);
        strip=Mesh.Create(new[]{Vertex(-78,340,0),Vertex(78,340,255),Vertex(78,354,255),Vertex(-78,340,0),Vertex(78,354,255),Vertex(-78,354,0)});
        rampMaterial=new Material{Effect=ramp,Cull=CullMode.None,DepthTest=false,DepthWrite=false};
        toonMaterial=new Material{Effect=toon,Cull=CullMode.None,DepthTest=false,DepthWrite=false};
        draw=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,VertexCapacity=1024});
        brightSlot=ramp.GetUniformSlot("Bright");thresholdSlot=toon.GetUniformSlot("Thresholds");graphThresholdSlot=compiled.GetUniformSlot("Thresholds");
        hud=new ShaderHud(device);
    }
    private static VertexPositionColor Vertex(float x,float y,byte intensity)
        =>new VertexPositionColor(new Vector3(x/240,(240-y)/240,0),new Color32(intensity,intensity,intensity));
    internal void Present(float wave,bool paused)
    {
        ramp.SetColor(brightSlot,new Color32((byte)(60+80*wave),(byte)(180+50*wave),240));
        var thresholds=new Color32((byte)(65+40*wave),(byte)(165+40*wave),0);
        toon.SetColor(thresholdSlot,thresholds);compiled.SetColor(graphThresholdSlot,thresholds);
        for(int i=0;i<32;i++)diskVertices[i*3]=Vertex(-20+40*wave,240,255);
        disk.SetVertices(diskVertices);
        device.BeginFrame(new Color32(8,13,26));renderer.Begin3D(camera);
        renderer.DrawMesh(disk,reference,left);renderer.DrawMesh(strip,reference,left);
        renderer.DrawMesh(disk,rampMaterial,middle);renderer.DrawMesh(strip,rampMaterial,middle);
        renderer.DrawMesh(disk,toonMaterial,right);
        renderer.End3D();
        draw.Begin();draw.SetEffect(compiled);
        for(int x=0;x<156;x++){
            byte intensity=(byte)(x*255/155);
            draw.FillRectangle(new RectF(450+x,340,1,14),new Color32(intensity,intensity,intensity));
        }
        draw.SetEffect(null);draw.End();
        hud.Present(paused);device.EndFrame();
    }
    public void Dispose(){hud.Dispose();draw.Dispose();strip.Dispose();disk.Dispose();compiled.Dispose();toon.Dispose();ramp.Dispose();}
}
