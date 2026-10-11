namespace Dolphin.Graphics;

/// <summary>GameCube graphics entry points.</summary>
public sealed class Renderer
{
    public void BeginFrame(Camera camera){if(camera==null)throw new System.ArgumentNullException("camera");BeginFrame(camera.View,camera.Projection);}
    public void Draw(GameObject gameObject){if(gameObject==null)throw new System.ArgumentNullException("gameObject");DrawModel(gameObject.Model,gameObject.Transform.WorldMatrix);}
    public void DrawModel(ModelInstance instance,Transform transform){if(transform==null)throw new System.ArgumentNullException("transform");DrawModel(instance,transform.WorldMatrix);}
    public void BeginFrame(float[] viewMatrix,float[] projectionMatrix)
    {if(NativeModel.ModelFrameBegin(viewMatrix,projectionMatrix)<0)throw new System.InvalidOperationException("Frame already active or invalid camera matrices.");}
    public void DrawModel(ModelInstance instance,float[] transform)
    {if(instance==null)throw new System.ArgumentNullException("instance");instance.Check();if(NativeModel.ModelDraw(instance.Token,transform)<0)throw new System.InvalidOperationException("Model is cold, frame is inactive or transform is invalid.");}
    public void EndFrame()
    {if(NativeModel.ModelFrameEnd()<0)throw new System.InvalidOperationException("No active model frame.");}
    /// <summary>Draws and presents the built-in rotating demonstration scene.</summary>
    public static void PresentDemoFrame(int rotationDegrees) =>
        throw new System.PlatformNotSupportedException("Renderer.PresentDemoFrame requires DolphinDotNet AOT.");
}
