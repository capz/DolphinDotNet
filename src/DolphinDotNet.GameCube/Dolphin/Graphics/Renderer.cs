using System;

namespace Dolphin.Graphics;

/// <summary>GameCube graphics entry points.</summary>
public sealed class Renderer
{
    private readonly GraphicsDevice device;
    private bool ownsFrame, active;
    public Renderer() : this(GraphicsDevice.Default) { }
    public Renderer(GraphicsDevice device){this.device=device ?? throw new ArgumentNullException("device");}
    public void Begin3D(Camera camera)
    {
        if(active)throw new InvalidOperationException("Renderer already active.");
        if(camera==null)throw new ArgumentNullException("camera");
        if(!device.IsFrameActive)throw new InvalidOperationException("Begin a device frame first.");
        GraphicsDevice.Check(NativeModel.ModelFrameBegin(camera.View,camera.Projection),"Another render pass is active.");
        ownsFrame=false;active=true;
    }
    public void End3D(){if(!active||ownsFrame)throw new InvalidOperationException("No externally owned 3D pass.");GraphicsDevice.Check(NativeModel.ModelFrameEnd(),"No active 3D pass.");active=false;}
    public void DrawMesh(Mesh mesh,Material material,Transform transform)
    {
        if(mesh==null)throw new ArgumentNullException("mesh");if(material==null)throw new ArgumentNullException("material");if(transform==null)throw new ArgumentNullException("transform");
        mesh.Check();material.Check();
        GraphicsDevice.Check(NativeGraphics.MeshDraw(mesh.Token,material.TextureToken,transform.WorldMatrix,material.Color.Packed,material.Flags,material.AlphaCutoff),"Invalid mesh, material, transform or 3D pass.");
    }
    public void DrawPrimitives(VertexPositionColor[] vertices,int offset,int vertexCount,PrimitiveType primitive,Transform transform)
    {
        if(vertices==null)throw new ArgumentNullException("vertices");if(transform==null)throw new ArgumentNullException("transform");
        GraphicsDevice.Check(NativeGraphics.MeshPrimitives(vertices,offset,vertexCount,0,(int)primitive,transform.WorldMatrix,0xffffffffu,4),"Invalid primitive range or inactive 3D pass (maximum 768 vertices per submission).");
    }

    public void BeginFrame(Camera camera)
    {
        if (camera == null) throw new ArgumentNullException("camera");
        BeginFrame(camera.View, camera.Projection);
    }

    /// <summary>Draws the object's instance at its cached world transform. Empty objects submit no geometry.</summary>
    public void Draw(GameObject gameObject)
    {
        if (gameObject == null) throw new ArgumentNullException("gameObject");
        gameObject.Check();
        var model = gameObject.Model;
        if (model != null) DrawModel(model, gameObject.Transform.WorldMatrix);
    }
    public void DrawModel(ModelInstance instance, Transform transform)
    {
        if (transform == null) throw new ArgumentNullException("transform");
        DrawModel(instance, transform.WorldMatrix);
    }

    public void BeginFrame(float[] viewMatrix, float[] projectionMatrix)
    {
        if (device.IsFrameActive) throw new InvalidOperationException("Frame already active; use Begin3D inside a device frame.");
        if (NativeModel.ModelFrameBegin(viewMatrix, projectionMatrix) < 0)
            throw new InvalidOperationException("Frame already active or invalid camera matrices.");
        ownsFrame=true;active=true;
    }

    public void DrawModel(ModelInstance instance, float[] transform)
    {
        if (instance == null) throw new ArgumentNullException("instance");
        instance.Check();
        if (NativeModel.ModelDraw(instance.Token, transform) < 0)
            throw new InvalidOperationException("Model is cold, frame is inactive or transform is invalid.");
    }

    public void EndFrame()
    {
        if(!ownsFrame)throw new InvalidOperationException("Use End3D for an externally owned device frame.");
        if (NativeModel.ModelFrameEnd() < 0)
            throw new InvalidOperationException("No active model frame.");
        ownsFrame=false;active=false;
    }

}
