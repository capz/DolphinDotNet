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
        GraphicsDevice.Check(NativeGraphics.MeshDrawMaterial(mesh.Token,material.TextureToken,material.SecondaryTextureToken,transform.WorldMatrix,material.Color.Packed,material.Flags,material.AlphaCutoff,material.EffectToken),"Invalid mesh, material, transform or 3D pass.");
    }
    /// <summary>Replaces all imported material state for this draw only. Geometry, animation and UV0 remain authored.</summary>
    public void DrawModelWithMaterial(ModelInstance instance,Transform transform,Material material)
    {
        if(transform==null)throw new ArgumentNullException("transform");
        DrawModelWithMaterial(instance,transform.WorldMatrix,material);
    }
    public void DrawModelWithMaterial(ModelInstance instance,float[] world,Material material)
    {
        if(instance==null)throw new ArgumentNullException("instance");
        if(material==null)throw new ArgumentNullException("material");
        instance.Check();material.Check();
        GraphicsDevice.Check(NativeModel.ModelDrawMaterial(instance.Token,world,material.TextureToken,material.SecondaryTextureToken,material.Color.Packed,material.Flags,material.AlphaCutoff,material.EffectToken),"Invalid model material, cold model, transform or 3D pass.");
    }
    public void DrawWithMaterial(GameObject gameObject,Material material)
    {
        if(gameObject==null)throw new ArgumentNullException("gameObject");
        gameObject.Check();if(gameObject.Model!=null)DrawModelWithMaterial(gameObject.Model,gameObject.Transform,material);
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
        => Draw(gameObject, null);
    /// <summary>Draws this object's model with a borrowed RGB effect for this draw only.</summary>
    public void Draw(GameObject gameObject, GxEffect? effectOverride)
    {
        if (gameObject == null) throw new ArgumentNullException("gameObject");
        gameObject.Check();
        var model = gameObject.Model;
        if (model != null) DrawModel(model, gameObject.Transform.WorldMatrix, effectOverride);
    }
    public void DrawModel(ModelInstance instance, Transform transform)
        => DrawModel(instance, transform, null);
    /// <summary>Replaces imported RGB combination without changing geometry, alpha or material state.</summary>
    public void DrawModel(ModelInstance instance, Transform transform, GxEffect? effectOverride)
    {
        if (transform == null) throw new ArgumentNullException("transform");
        DrawModel(instance, transform.WorldMatrix, effectOverride);
    }

    public void BeginFrame(float[] viewMatrix, float[] projectionMatrix)
    {
        if (device.IsFrameActive) throw new InvalidOperationException("Frame already active; use Begin3D inside a device frame.");
        if (NativeModel.ModelFrameBegin(viewMatrix, projectionMatrix) < 0)
            throw new InvalidOperationException("Frame already active or invalid camera matrices.");
        ownsFrame=true;active=true;
    }

    public void DrawModel(ModelInstance instance, float[] transform)
        => DrawModel(instance, transform, null);
    /// <summary>Overrides every mesh for this submission; null restores imported rendering.</summary>
    public void DrawModel(ModelInstance instance, float[] transform, GxEffect? effectOverride)
    {
        if (instance == null) throw new ArgumentNullException("instance");
        instance.Check();
        if (effectOverride != null) {effectOverride.Check();if(effectOverride.RequiresSecondaryTexture)throw new ArgumentException("Use DrawModelWithMaterial for two-texture effects.","effectOverride");}
        var result = NativeModel.ModelDrawEffect(instance.Token, transform, effectOverride == null ? 0 : effectOverride.Token);
        if (result == -3)
            throw new ArgumentException("effectOverride requires a texture on every submitted mesh.", "effectOverride");
        if (result == -2)
            throw new InvalidOperationException("The effect override is invalid or pending disposal.");
        if (result < 0)
            throw new InvalidOperationException("Model is cold, frame is inactive or transform is invalid.");
    }

    /// <summary>Submits up to 128 objects / 4096 total meshes. All opaque meshes
    /// precede a stable global back-to-front translucent submesh pass. Caller
    /// scratch holds one token and 16 floats per object. No per-frame allocations.
    /// This does not sort individual triangles or resolve intersecting surfaces.</summary>
    public void DrawModels(GameObject[] models,int[] instanceScratch,float[] worldScratch)
    {
        if(!active)throw new InvalidOperationException("Begin a model frame first.");
        if(models==null)throw new ArgumentNullException("models");
        if(models.Length>128)throw new ArgumentException("At most 128 model objects per batch.","models");
        if(instanceScratch==null||instanceScratch.Length<models.Length)throw new ArgumentException("One token per object is required.","instanceScratch");
        if(worldScratch==null||worldScratch.Length<models.Length*16)throw new ArgumentException("16 matrix floats per object are required.","worldScratch");
        for(int i=0;i<models.Length;i++)
        {
            if(models[i]==null)throw new ArgumentException("Null model object.","models");
            models[i].Check();var instance=models[i].Model;
            if(instance!=null)instance.Check();
            instanceScratch[i]=instance==null?0:instance.Token;
            var matrix=models[i].Transform.WorldMatrix;
            for(int j=0;j<16;j++)worldScratch[i*16+j]=matrix[j];
        }
        GraphicsDevice.Check(NativeModel.ModelDrawModels(instanceScratch,worldScratch,models.Length),"Invalid/cold model, transform, inactive pass or batch exceeding 4096 meshes.");
    }

    private static float ModelDrawDepth(Camera camera,GameObject obj)
    {
        var matrix=obj.Transform.WorldMatrix;
        return camera.View[2]*matrix[12]+camera.View[6]*matrix[13]+
               camera.View[10]*matrix[14]+camera.View[14];
    }

    /// <summary>
    /// Draws a set of model instances from farthest to nearest camera-space
    /// origins, using caller-owned order scratch (no frame-time allocations).
    /// Intended for separate translucent models. Within each model, native
    /// mesh-level sorting remains active. Overlapping meshes in different
    /// models may still need manual draw grouping or further mesh splitting.
    /// </summary>
    public void DrawModelsBackToFront(Camera camera, GameObject[] models, int[] order)
    {
        if(!active)throw new InvalidOperationException("Begin a model frame first.");
        if(camera==null)throw new ArgumentNullException("camera");
        if(models==null)throw new ArgumentNullException("models");
        if(order==null||order.Length<models.Length)throw new ArgumentException("Order scratch must hold each model index.", "order");
        for(int i=0;i<models.Length;i++)
        {
            if(models[i]==null)throw new ArgumentException("Null model object.", "models");
            models[i].Check();
            if(models[i].Model!=null)models[i].Model!.Check();
            _=models[i].Transform.WorldMatrix;
            order[i]=i;
        }
        // Stable insertion order prevents equal-depth objects from flickering.
        for(int i=1;i<models.Length;i++)
        {
            int entry=order[i];
            float depth=ModelDrawDepth(camera,models[entry]);
            int j=i;
            while(j>0&&ModelDrawDepth(camera,models[order[j-1]])>depth)
            {
                order[j]=order[j-1];
                j--;
            }
            order[j]=entry;
        }
        for(int i=0;i<models.Length;i++)Draw(models[order[i]]);
    }

    public void EndFrame()
    {
        if(!ownsFrame)throw new InvalidOperationException("Use End3D for an externally owned device frame.");
        if (NativeModel.ModelFrameEnd() < 0)
            throw new InvalidOperationException("No active model frame.");
        ownsFrame=false;active=false;
        device.CollectRetired();
    }

}
