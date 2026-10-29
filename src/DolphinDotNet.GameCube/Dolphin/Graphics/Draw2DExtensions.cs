using System;
namespace Dolphin.Graphics;
public sealed partial class Draw2D
{
    private readonly float[] sampling=new float[20];
    public void SetSampler(SamplerState sampler,UvTransform transform=default)
    {Require();Flush();Material.WriteSampler(sampling,0,sampler,transform);Material.WriteSampler(sampling,10,default,default);GraphicsDevice.Check(NativeGraphics.RenderMaterial(sampling),"Invalid sampler.");}
    private bool cameraActive;
    private readonly float[] cameraMatrix=new float[6];
    private readonly int[] clipStack=new int[64],spriteTextures,spriteColors;
    private readonly float[] spriteVertices;
    private int clipDepth,clipX,clipY,clipW,clipH;
    /// <summary>Snapshots the camera at Begin. Clipping uses logical screen coordinates after camera transformation.</summary>
    public void Begin(Camera2D camera,Viewport viewport)
    {if(camera==null)throw new ArgumentNullException("camera");BeginCore(camera,viewport);}
    public void Begin(Camera2D camera)=>Begin(camera,new Viewport(0,0,device.Width,device.Height));
    public void PushClip(int x,int y,int width,int height)
    {
        Require();if(width<0||height<0||x<0||y<0||x>int.MaxValue-width||y>int.MaxValue-height)throw new ArgumentException("Invalid clip.");
        if(clipDepth==16)throw new InvalidOperationException("Clip stack is full.");
        int left=Math.Max(x,clipX),top=Math.Max(y,clipY),right=Math.Min(x+width,clipX+clipW),bottom=Math.Min(y+height,clipY+clipH);
        left=Math.Min(left,clipX+clipW);top=Math.Min(top,clipY+clipH);
        int at=clipDepth*4;clipStack[at]=clipX;clipStack[at+1]=clipY;clipStack[at+2]=clipW;clipStack[at+3]=clipH;
        SetClip(left,top,Math.Max(0,right-left),Math.Max(0,bottom-top));clipDepth++;
    }
    public void PopClip()
    {Require();if(clipDepth==0)throw new InvalidOperationException("Clip stack is empty.");int at=(clipDepth-1)*4;SetClip(clipStack[at],clipStack[at+1],clipStack[at+2],clipStack[at+3]);clipDepth--;}
    public void DrawSprite(SpriteDraw sprite)
    {ValidateSprite(sprite);var t=sprite.Region.Texture;var r=sprite.Region.Source;float u=r.X/t.Width,v=r.Y/t.Height,w=r.Width/t.Width,h=r.Height/t.Height;if((sprite.Flip&SpriteFlip.Horizontal)!=0){u+=w;w=-w;}if((sprite.Flip&SpriteFlip.Vertical)!=0){v+=h;h=-h;}Quad(t.Token,new RectF(u,v,w,h),sprite.Destination,sprite.Color,sprite.RotationDegrees,sprite.Origin);}
    /// <summary>Validates the complete range before submission; uses bounded reusable staging and preserves painter order.</summary>
    public void DrawSprites(SpriteDraw[] sprites,int offset,int count)
    {
        Require();if(sprites==null)throw new ArgumentNullException("sprites");if(offset<0||count<0||count>spriteTextures.Length||offset>sprites.Length-count)throw new ArgumentOutOfRangeException("count");
        for(int i=0;i<count;i++)ValidateSprite(sprites[offset+i]);
        for(int start=0;start<count;start+=128){int n=Math.Min(128,count-start);
            for(int i=0;i<n;i++){
                var d=sprites[offset+start+i];var t=d.Region.Texture;var r=d.Region.Source;
                float u=r.X/t.Width,v=r.Y/t.Height,du=r.Width/t.Width,dv=r.Height/t.Height;
                if((d.Flip&SpriteFlip.Horizontal)!=0){u+=du;du=-du;}if((d.Flip&SpriteFlip.Vertical)!=0){v+=dv;dv=-dv;}
                float c=1,s=0;if(d.RotationDegrees!=0){float a=d.RotationDegrees*.01745329252f;c=(float)Math.Cos(a);s=(float)Math.Sin(a);}
                float l=-d.Origin.X,top=-d.Origin.Y,right=l+d.Destination.Width,bottom=top+d.Destination.Height,x=d.Destination.X+d.Origin.X,y=d.Destination.Y+d.Origin.Y;
                SpriteVertex(i,0,x+l*c-top*s,y+l*s+top*c,u,v);SpriteVertex(i,1,x+right*c-top*s,y+right*s+top*c,u+du,v);SpriteVertex(i,2,x+right*c-bottom*s,y+right*s+bottom*c,u+du,v+dv);
                SpriteVertex(i,3,x+l*c-top*s,y+l*s+top*c,u,v);SpriteVertex(i,4,x+right*c-bottom*s,y+right*s+bottom*c,u+du,v+dv);SpriteVertex(i,5,x+l*c-bottom*s,y+l*s+bottom*c,u,v+dv);
                spriteTextures[i]=t.Token;spriteColors[i]=unchecked((int)d.Color.Packed);
            }
            if(effect!=null)effect.Check();GraphicsDevice.Check(NativeGraphics.CanvasSprites(token,spriteTextures,spriteVertices,spriteColors,n),"Invalid sprite range or effect.");
        }
    }
    private void SpriteVertex(int sprite,int index,float x,float y,float u,float v)
    {if(cameraActive){float px=x;x=cameraMatrix[0]*px+cameraMatrix[2]*y+cameraMatrix[4];y=cameraMatrix[1]*px+cameraMatrix[3]*y+cameraMatrix[5];}int at=sprite*24+index*4;spriteVertices[at]=x;spriteVertices[at+1]=y;spriteVertices[at+2]=u;spriteVertices[at+3]=v;}
    private static void ValidateSprite(SpriteDraw d)
    {if(d.Region.Texture==null)throw new ArgumentException("Missing texture region.");d.Region.Texture.Check();Rectangle(d.Destination);GraphicsMath.Finite(d.Destination.X);GraphicsMath.Finite(d.Destination.Y);GraphicsMath.Finite(d.Destination.Width);GraphicsMath.Finite(d.Destination.Height);GraphicsMath.Finite(d.Destination.X+d.Destination.Width);GraphicsMath.Finite(d.Destination.Y+d.Destination.Height);GraphicsMath.Finite(d.RotationDegrees);GraphicsMath.Finite(d.Origin.X);GraphicsMath.Finite(d.Origin.Y);if((int)d.Flip<0||(int)d.Flip>3)throw new ArgumentException("Invalid sprite flip.");}
}
