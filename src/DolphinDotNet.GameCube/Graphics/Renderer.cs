using System;

namespace Dolphin.Graphics;

/// <summary>GameCube graphics entry points.</summary>
public sealed class Renderer
{
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
        if (NativeModel.ModelFrameBegin(viewMatrix, projectionMatrix) < 0)
            throw new InvalidOperationException("Frame already active or invalid camera matrices.");
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
        if (NativeModel.ModelFrameEnd() < 0)
            throw new InvalidOperationException("No active model frame.");
    }

    /// <summary>Draws and presents the built-in rotating demonstration scene.</summary>
    public static void PresentDemoFrame(int rotationDegrees) =>
        throw new PlatformNotSupportedException("Renderer.PresentDemoFrame requires DolphinDotNet AOT.");
}
