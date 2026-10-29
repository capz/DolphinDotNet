using System;

namespace Dolphin.Graphics;

/// <summary>Perspective/orthographic camera with explicit look-at view. Coordinates use right-handed, Y-up model space.</summary>
public sealed partial class Camera
{
    internal readonly float[] View = new float[16];
    internal readonly float[] Projection = new float[16];
    // Native layout: eye XYZ, target XYZ, up XYZ, field of view, aspect, near, far.
    private readonly float[] settings = new float[13];
    public AspectRatioMode AspectRatioMode {get;set;}=AspectRatioMode.Display;
    public float FieldOfViewDegrees {get=>settings[9];set{ValidateProjection(value,settings[10],settings[11],settings[12]);settings[9]=value;Refresh();}}
    /// <summary>Zero aspect selects automatic display aspect; positive values are explicit.</summary>
    public Camera(float fieldOfViewDegrees=60,float aspectRatio=0,float nearPlane=.1f,float farPlane=100)
    {
        AspectRatioMode=aspectRatio==0?AspectRatioMode.Display:AspectRatioMode.Explicit;
        if(aspectRatio==0)aspectRatio=4f/3f;
        settings[2] = 5;
        settings[7] = 1;
        settings[9] = fieldOfViewDegrees;
        settings[10] = aspectRatio;
        settings[11] = nearPlane;
        settings[12] = farPlane;
        Refresh();
    }

    public void LookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        settings[0] = eye.X;
        settings[1] = eye.Y;
        settings[2] = eye.Z;
        settings[3] = target.X;
        settings[4] = target.Y;
        settings[5] = target.Z;
        settings[6] = up.X;
        settings[7] = up.Y;
        settings[8] = up.Z;
        Refresh();
    }

    internal void PrepareDisplay(GraphicsDevice device)
    {
        if(AspectRatioMode!=AspectRatioMode.Display&&AspectRatioMode!=AspectRatioMode.Explicit)throw new ArgumentException("Invalid aspect mode.");
        if(AspectRatioMode==AspectRatioMode.Display){float aspect=device.Display.AspectRatio;if(orthographic){float w=orthoHeight*aspect;if(orthoWidth!=w){orthoWidth=w;Refresh();}}else if(settings[10]!=aspect){settings[10]=aspect;Refresh();}}
    }
    private void Refresh()
    {
        if (NativeModel.ModelCamera(settings, View, Projection) < 0)
            throw new ArgumentException("Invalid camera frustum or look-at vectors.");
        if(orthographic)SetOrthographicMatrix();
        matrixDirty=inverseDirty=true;
    }
}
