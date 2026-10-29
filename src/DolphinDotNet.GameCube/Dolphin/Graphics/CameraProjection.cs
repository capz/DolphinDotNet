using System;
namespace Dolphin.Graphics;
public sealed partial class Camera
{
    private bool orthographic;
    private float orthoWidth,orthoHeight;
    private readonly float[] combined=new float[16],inverseCamera=new float[16],inverseScratch=new float[32];
    private bool matrixDirty=true,inverseDirty=true;
    public float NearPlane=>settings[11];
    public float FarPlane=>settings[12];
    public bool IsOrthographic=>orthographic;
    public void SetPerspective(float fieldOfViewDegrees,float aspectRatio,float nearPlane,float farPlane)
    {
        ValidateProjection(fieldOfViewDegrees,aspectRatio,nearPlane,farPlane);
        AspectRatioMode=AspectRatioMode.Explicit;
        settings[9]=fieldOfViewDegrees;settings[10]=aspectRatio;settings[11]=nearPlane;settings[12]=farPlane;orthographic=false;Refresh();
    }
    public void SetOrthographic(float width,float height,float nearPlane,float farPlane)
    {
        GraphicsMath.Finite(width);GraphicsMath.Finite(height);if(width<=0||height<=0)throw new ArgumentOutOfRangeException("width");
        ValidateProjection(60,width/height,nearPlane,farPlane);
        AspectRatioMode=AspectRatioMode.Explicit;
        orthoWidth=width;orthoHeight=height;settings[11]=nearPlane;settings[12]=farPlane;orthographic=true;Refresh();
    }
    private static void ValidateProjection(float fov,float aspect,float near,float far)
    {GraphicsMath.Finite(fov);GraphicsMath.Finite(aspect);GraphicsMath.Finite(near);GraphicsMath.Finite(far);if(fov<=0||fov>=179||aspect<=0||near<=0||far<=near)throw new ArgumentException("Invalid projection.");}
    private void SetOrthographicMatrix()
    {for(int i=0;i<16;i++)Projection[i]=0;Projection[0]=2/orthoWidth;Projection[5]=2/orthoHeight;Projection[10]=-1/(settings[12]-settings[11]);Projection[14]=-settings[12]/(settings[12]-settings[11]);Projection[15]=1;}
    private void Combine()
    {PrepareDisplay(GraphicsDevice.Default);if(!matrixDirty)return;for(int c=0;c<4;c++)for(int r=0;r<4;r++){float v=0;for(int k=0;k<4;k++)v+=Projection[k*4+r]*View[c*4+k];combined[c*4+r]=v;}matrixDirty=false;}
    /// <summary>Pixel coordinates in viewport; depth is 0 at near and 1 at far.</summary>
    public Vector3 Project(Vector3 world,Viewport viewport)
    {
        CheckViewport(viewport);Combine();var v=Apply(combined,world);
        return new Vector3(viewport.X+(v.X+1)*viewport.Width*.5f,viewport.Y+(1-v.Y)*viewport.Height*.5f,v.Z+1);
    }
    public Vector3 Unproject(Vector3 screen,Viewport viewport)
    {
        CheckViewport(viewport);Combine();Invert();
        return Apply(inverseCamera,new Vector3((screen.X-viewport.X)*2/viewport.Width-1,1-(screen.Y-viewport.Y)*2/viewport.Height,screen.Z-1));
    }
    public Ray CreatePickingRay(Vector2 screen,Viewport viewport)
    {var a=Unproject(new Vector3(screen.X,screen.Y,0),viewport);var b=Unproject(new Vector3(screen.X,screen.Y,1),viewport);var d=b-a;float n=(float)Math.Sqrt(GraphicsMath.Dot(d,d));if(!(n>0))throw new InvalidOperationException("Degenerate ray.");return new Ray(a,d*(1/n));}
    private static void CheckViewport(Viewport v){if(v.Width<=0||v.Height<=0)throw new ArgumentException("Empty viewport.");}
    private static Vector3 Apply(float[] m,Vector3 p)
    {GraphicsMath.Finite(p.X);GraphicsMath.Finite(p.Y);GraphicsMath.Finite(p.Z);float w=m[3]*p.X+m[7]*p.Y+m[11]*p.Z+m[15];if(w==0||float.IsNaN(w)||float.IsInfinity(w))throw new ArgumentException("Point has no finite projection.");return new Vector3((m[0]*p.X+m[4]*p.Y+m[8]*p.Z+m[12])/w,(m[1]*p.X+m[5]*p.Y+m[9]*p.Z+m[13])/w,(m[2]*p.X+m[6]*p.Y+m[10]*p.Z+m[14])/w);}
    private void Invert()
    {
        if(!inverseDirty)return;
        for(int r=0;r<4;r++)for(int c=0;c<8;c++)inverseScratch[r*8+c]=c<4?combined[c*4+r]:c-4==r?1:0;
        for(int c=0;c<4;c++){
            int pivot=c;for(int r=c+1;r<4;r++)if(Math.Abs(inverseScratch[r*8+c])>Math.Abs(inverseScratch[pivot*8+c]))pivot=r;
            float scale=inverseScratch[pivot*8+c];if(Math.Abs(scale)<1e-20f)throw new InvalidOperationException("Singular camera.");
            for(int k=0;k<8;k++){float t=inverseScratch[c*8+k];inverseScratch[c*8+k]=inverseScratch[pivot*8+k];inverseScratch[pivot*8+k]=t;}
            for(int k=0;k<8;k++)inverseScratch[c*8+k]/=scale;
            for(int r=0;r<4;r++)if(r!=c){float f=inverseScratch[r*8+c];for(int k=0;k<8;k++)inverseScratch[r*8+k]-=f*inverseScratch[c*8+k];}
        }
        for(int c=0;c<4;c++)for(int r=0;r<4;r++)inverseCamera[c*4+r]=inverseScratch[r*8+c+4];
        inverseDirty=false;
    }
    public bool Intersects(BoundingSphere sphere){Combine();return TestBounds(sphere.Center,new Vector3(sphere.Radius,sphere.Radius,sphere.Radius),true);}
    public bool Intersects(BoundingBox box){Combine();return TestBounds((box.Min+box.Max)*.5f,(box.Max-box.Min)*.5f,false);}
    private bool TestBounds(Vector3 center,Vector3 extent,bool sphere)
    {
        for(int plane=0;plane<6;plane++){
            int axis=plane/2;float sign=(plane&1)==0?1:-1;
            float a,b,c,d;
            if(plane==5){a=-combined[2];b=-combined[6];c=-combined[10];d=-combined[14];}
            else {a=combined[3]+sign*combined[axis];b=combined[7]+sign*combined[axis+4];c=combined[11]+sign*combined[axis+8];d=combined[15]+sign*combined[axis+12];}
            float radius=sphere?extent.X*(float)Math.Sqrt(a*a+b*b+c*c):Math.Abs(a)*extent.X+Math.Abs(b)*extent.Y+Math.Abs(c)*extent.Z;
            if(a*center.X+b*center.Y+c*center.Z+d < -radius)return false;
        }return true;
    }
}
