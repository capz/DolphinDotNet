using System;
namespace Dolphin.Graphics;

public sealed class Draw2DOptions
{
    private CanvasScaleMode scaleMode=CanvasScaleMode.Display;
    internal bool ScaleModeSpecified;
    public CanvasScaleMode ScaleMode {get=>scaleMode;set{scaleMode=value;ScaleModeSpecified=true;}}
    public int VertexCapacity {get;set;}=4096;
    /// <summary>0 disables bulk sprite staging; 1–128 reserves capacity during construction.</summary>
    public int SpriteCapacity {get;set;}
    /// <summary>Zero derives a display-correct logical size. Nonzero dimensions default to stretching unless Fit is selected.</summary>
    public int Width {get;set;}
    public int Height {get;set;}
}
/// <summary>Ordered, preallocated batch for textures, shapes and prebuilt bitmap fonts.</summary>
public sealed partial class Draw2D : IDisposable
{
    // TODO(throughput-validation): Retire this legacy A/B switch only after
    // repeated Dolphin/hardware validation establishes the performant path.
    // Preserve historical benchmark results when removing the old Quad path.
    internal static bool DirectRectanglePath=true;
    private readonly int token,width,height;
    private readonly GraphicsDevice device;
    private readonly CanvasScaleMode scaleMode;
    private Vector2 logicalSize;
    private readonly float[] scratch=new float[24];
    private bool active,disposed;
    private GxEffect? effect;
    public Draw2D(GraphicsDevice device,Draw2DOptions? options=null)
    {
        if(device==null)throw new ArgumentNullException("device");this.device=device;
        options=options ?? new Draw2DOptions();
        if(options.Width<0||options.Height<0||(options.Width==0)!=(options.Height==0))throw new ArgumentException("Set both logical dimensions or neither.");
        scaleMode=options.ScaleModeSpecified?options.ScaleMode:options.Width==0?CanvasScaleMode.Display:CanvasScaleMode.Stretch;
        if((int)scaleMode<0||(int)scaleMode>2||scaleMode==CanvasScaleMode.Fit&&options.Width==0||scaleMode==CanvasScaleMode.Display&&options.Width!=0)throw new ArgumentException("Display scaling derives its dimensions; Fit requires a fixed logical size.");
        if(options.SpriteCapacity<0||options.SpriteCapacity>128)throw new ArgumentOutOfRangeException("SpriteCapacity");
        spriteTextures=new int[options.SpriteCapacity];spriteColors=new int[options.SpriteCapacity];spriteVertices=new float[options.SpriteCapacity*24];
        token=NativeGraphics.CanvasCreate(options.VertexCapacity);
        if(token==0)throw new ArgumentException("Invalid batch capacity or native memory budget.");
        width=options.Width;height=options.Height;
    }
    public int DrawCalls {get{Check();return NativeGraphics.CanvasStats(token,0);}}
    public int Flushes {get{Check();return NativeGraphics.CanvasStats(token,1);}}
    public Vector2 LogicalSize {get{Check();if(active)return logicalSize;Viewport ignored;return Layout(new Viewport(0,0,device.Width,device.Height),out ignored);}}
    private Vector2 Layout(Viewport requested,out Viewport actual)
    {
        actual=requested;
        float pixelAspect=device.Display.AspectRatio*device.Height/device.Width;
        if(scaleMode==CanvasScaleMode.Display)return new Vector2(requested.Width*pixelAspect,requested.Height);
        int w=width==0?device.Width:width,h=height==0?device.Height:height;
        if(scaleMode==CanvasScaleMode.Fit){
            float aspect=(float)w/h;
            int vw=requested.Width,vh=requested.Height;
            if(vw*pixelAspect/vh>aspect)vw=Math.Max(1,Math.Min(vw,(int)(vh*aspect/pixelAspect)));
            else vh=Math.Max(1,Math.Min(vh,(int)(vw*pixelAspect/aspect)));
            actual=new Viewport(requested.X+(requested.Width-vw)/2,requested.Y+(requested.Height-vh)/2,vw,vh);
        }
        return new Vector2(w,h);
    }
    public void Begin()=>BeginCore(null,new Viewport(0,0,device.Width,device.Height));
    private void BeginCore(Camera2D? camera,Viewport viewport)
    {
        Check();if(active)throw new InvalidOperationException("Batch already active.");
        if(viewport.Width<=0||viewport.Height<=0||viewport.X>device.Width-viewport.Width||viewport.Y>device.Height-viewport.Height)throw new ArgumentException("Viewport outside framebuffer.");
        Viewport target;var size=Layout(viewport,out target);
        if(!(size.X>=1&&size.Y>=1&&size.X<=16384&&size.Y<=16384))throw new ArgumentException("Logical canvas is outside native limits.");
        if(camera!=null)camera.Matrix(cameraMatrix);
        GraphicsDevice.Check(NativeGraphics.CanvasBegin(token,(int)Math.Ceiling(size.X),(int)Math.Ceiling(size.Y)),"Begin a device frame and end other drawing passes first.");active=true;
        try {
            GraphicsDevice.Check(NativeGraphics.CanvasLayout(token,size.X,size.Y),"Invalid logical canvas.");
            GraphicsDevice.Check(NativeGraphics.CanvasViewport(token,target.X,target.Y,target.Width,target.Height),"Invalid viewport.");
            GraphicsDevice.Check(NativeGraphics.RenderScene(null),"Cannot reset render state.");
            logicalSize=size;effect=null;cameraActive=camera!=null&&(cameraMatrix[0]!=1||cameraMatrix[1]!=0||cameraMatrix[2]!=0||cameraMatrix[3]!=1||cameraMatrix[4]!=0||cameraMatrix[5]!=0);clipDepth=0;clipX=clipY=0;clipW=(int)Math.Ceiling(size.X);clipH=(int)Math.Ceiling(size.Y);
        }catch{NativeGraphics.CanvasEnd(token);active=false;throw;}
    }
    /// <summary>Flush queued vertices before changing effects. Null restores the default pipeline.</summary>
    public void SetEffect(GxEffect? value)
    {
        Require();if(value!=null)value.Check();
        GraphicsDevice.Check(NativeGraphics.CanvasEffect(token,value==null?0:value.Token),"Cannot bind this effect.");effect=value;
    }
    public void End(){Require();GraphicsDevice.Check(NativeGraphics.CanvasEnd(token),"Batch could not finish.");active=false;}
    public void Flush(){Require();GraphicsDevice.Check(NativeGraphics.CanvasFlush(token),"Batch could not flush.");}
    public void SetClip(int x,int y,int width,int height){Require();GraphicsDevice.Check(NativeGraphics.CanvasClip(token,x,y,width,height),"Invalid clipping rectangle.");clipX=x;clipY=y;clipW=width;clipH=height;}
    public void ResetClip(){Require();SetClip(0,0,(int)Math.Ceiling(logicalSize.X),(int)Math.Ceiling(logicalSize.Y));}
    public void SetBlend(BlendMode blend){Require();GraphicsDevice.Check(NativeGraphics.CanvasBlend(token,(int)blend),"Invalid blend mode.");}
    private static float Min(float a,float b)=>a<b?a:b;
    private void Check(){if(disposed)throw new ObjectDisposedException("Draw2D");}
    private void Require(){Check();if(!active)throw new InvalidOperationException("Begin the batch first.");}
    private void Vertex(int index,float x,float y,float u,float v){if(cameraActive){float px=x; x=cameraMatrix[0]*px+cameraMatrix[2]*y+cameraMatrix[4];y=cameraMatrix[1]*px+cameraMatrix[3]*y+cameraMatrix[5];}int at=index*4;scratch[at]=x;scratch[at+1]=y;scratch[at+2]=u;scratch[at+3]=v;}
    private void Submit(int texture,int count,Color32 color){if(effect!=null)effect.Check();GraphicsDevice.Check(NativeGraphics.CanvasVertices(token,texture,scratch,count,color.Packed),"Invalid drawing coordinates, texture or exhausted batch capacity.");}
    private static void Rectangle(RectF rectangle){if(!(rectangle.Width>=0&&rectangle.Height>=0))throw new ArgumentException("Invalid rectangle.");}
    private void Quad(int texture,RectF source,RectF destination,Color32 color,float rotation,Vector2 origin)
    {
        Require();Rectangle(destination);if(destination.Width==0||destination.Height==0)return;
        float c=1,s=0;
        if(rotation!=0){float radians=rotation*0.01745329252f;c=NativeGraphics.DrawCos(radians);s=NativeGraphics.DrawSin(radians);}
        float x=destination.X+origin.X,y=destination.Y+origin.Y,l=-origin.X,t=-origin.Y,r=l+destination.Width,b=t+destination.Height;
        float x0=x+l*c-t*s,y0=y+l*s+t*c,x1=x+r*c-t*s,y1=y+r*s+t*c,x2=x+r*c-b*s,y2=y+r*s+b*c,x3=x+l*c-b*s,y3=y+l*s+b*c;
        Vertex(0,x0,y0,source.X,source.Y);Vertex(1,x1,y1,source.X+source.Width,source.Y);Vertex(2,x2,y2,source.X+source.Width,source.Y+source.Height);
        Vertex(3,x0,y0,source.X,source.Y);Vertex(4,x2,y2,source.X+source.Width,source.Y+source.Height);Vertex(5,x3,y3,source.X,source.Y+source.Height);Submit(texture,6,color);
    }
    public void FillRectangle(RectF destination,Color32 color)
    {
        if(!DirectRectanglePath||cameraActive){Quad(0,default,destination,color,0,default);return;}
        Require();Rectangle(destination);if(destination.Width==0||destination.Height==0)return;
        if(effect!=null)effect.Check();
        GraphicsDevice.Check(NativeGraphics.CanvasRectangle(token,destination.X,destination.Y,destination.Width,destination.Height,color.Packed),
            "Invalid drawing coordinates, effect or exhausted batch capacity.");
    }
    public void DrawTexture(Texture2D texture,RectF destination,Color32 color)
    {
        if(texture==null)throw new ArgumentNullException("texture");texture.Check();Quad(texture.Token,new RectF(0,0,1,1),destination,color,0,default);
    }
    /// <summary>Source rectangle is in texels; rotation is degrees; origin is a pixel offset within the destination.</summary>
    public void DrawTexture(Texture2D texture,RectF source,RectF destination,Color32 color,float rotationDegrees=0,Vector2 origin=default)
    {
        if(texture==null)throw new ArgumentNullException("texture");texture.Check();Rectangle(source);
        if(!(source.X>=0&&source.Y>=0&&source.X+source.Width<=texture.Width&&source.Y+source.Height<=texture.Height))throw new ArgumentException("Source rectangle is outside the texture.");
        Quad(texture.Token,new RectF(source.X/texture.Width,source.Y/texture.Height,source.Width/texture.Width,source.Height/texture.Height),destination,color,rotationDegrees,origin);
    }
    public void FillTriangle(Vector2 a,Vector2 b,Vector2 c,Color32 color){Require();Vertex(0,a.X,a.Y,0,0);Vertex(1,b.X,b.Y,0,0);Vertex(2,c.X,c.Y,0,0);Submit(0,3,color);}
    public void DrawLine(Vector2 start,Vector2 end,Color32 color,float thickness=1)
    {
        Require();if(!(thickness>0))throw new ArgumentOutOfRangeException("thickness");float dx=end.X-start.X,dy=end.Y-start.Y;
        float length=NativeGraphics.DrawSqrt(dx*dx+dy*dy);if(length==0){FillRectangle(new RectF(start.X-thickness/2,start.Y-thickness/2,thickness,thickness),color);return;}
        float x=-dy/length*thickness/2,y=dx/length*thickness/2;
        Vertex(0,start.X+x,start.Y+y,0,0);Vertex(1,end.X+x,end.Y+y,0,0);Vertex(2,end.X-x,end.Y-y,0,0);
        Vertex(3,start.X+x,start.Y+y,0,0);Vertex(4,end.X-x,end.Y-y,0,0);Vertex(5,start.X-x,start.Y-y,0,0);Submit(0,6,color);
    }
    public void DrawRectangle(RectF rectangle,Color32 color,float thickness=1)
    {
        Rectangle(rectangle);if(!(thickness>0))throw new ArgumentOutOfRangeException("thickness");
        float t=Min(thickness,Min(rectangle.Width/2,rectangle.Height/2));
        FillRectangle(new RectF(rectangle.X,rectangle.Y,rectangle.Width,t),color);
        FillRectangle(new RectF(rectangle.X,rectangle.Y+rectangle.Height-t,rectangle.Width,t),color);
        FillRectangle(new RectF(rectangle.X,rectangle.Y+t,t,rectangle.Height-2*t),color);
        FillRectangle(new RectF(rectangle.X+rectangle.Width-t,rectangle.Y+t,t,rectangle.Height-2*t),color);
    }
    public void DrawText(BitmapFont font,string text,Vector2 position,Color32 color,float scale=1)
    {
        Require();if(font==null)throw new ArgumentNullException("font");if(text==null)throw new ArgumentNullException("text");if(!(scale>0&&scale<=1024))throw new ArgumentOutOfRangeException("scale");
        float x=position.X,y=position.Y,w=font.CellWidth*scale,h=font.CellHeight*scale;
        for(int i=0;i<text.Length;i++) {
            char ch=text[i];if(ch=='\r')continue;if(ch=='\n'){x=position.X;y+=h;continue;}if(ch=='\t'){x+=w*4;continue;}
            var source=font.Glyph(ch);if(source.Width>0)DrawTexture(font.Texture,source,new RectF(x,y,w,h),color);x+=w;
        }
    }
    public void DrawNineSlice(Texture2D texture,RectF source,RectF destination,Border border,Color32 color)
    {
        Require();Rectangle(source);Rectangle(destination);
        if(!(border.Left>=0&&border.Top>=0&&border.Right>=0&&border.Bottom>=0&&border.Left+border.Right<=source.Width&&border.Top+border.Bottom<=source.Height))throw new ArgumentException("Invalid slice border.");
        float xs=border.Left+border.Right,ys=border.Top+border.Bottom;
        float sx=xs>destination.Width?destination.Width/xs:1,sy=ys>destination.Height?destination.Height/ys:1;
        for(int y=0;y<3;y++)for(int x=0;x<3;x++) {
            float sl=x==0?0:x==1?border.Left:source.Width-border.Right,st=y==0?0:y==1?border.Top:source.Height-border.Bottom;
            float sw=x==0?border.Left:x==1?source.Width-border.Left-border.Right:border.Right,sh=y==0?border.Top:y==1?source.Height-border.Top-border.Bottom:border.Bottom;
            float dl=x==0?0:x==1?border.Left*sx:destination.Width-border.Right*sx,dt=y==0?0:y==1?border.Top*sy:destination.Height-border.Bottom*sy;
            float dw=x==0?border.Left*sx:x==1?destination.Width-xs*sx:border.Right*sx,dh=y==0?border.Top*sy:y==1?destination.Height-ys*sy:border.Bottom*sy;
            if(sw>0&&sh>0&&dw>0&&dh>0)DrawTexture(texture,new RectF(source.X+sl,source.Y+st,sw,sh),new RectF(destination.X+dl,destination.Y+dt,dw,dh),color);
        }
    }
    public void Dispose(){if(disposed)return;if(active)throw new InvalidOperationException("End the batch before disposal.");GraphicsDevice.Check(NativeGraphics.DrawRelease(token),"Cannot dispose batch during a raw stream.");disposed=true;}
}
