using System;
namespace Dolphin.Graphics;
[Flags] public enum SpriteFlip { None=0,Horizontal=1,Vertical=2 }
public readonly struct TextureRegion
{
    public Texture2D Texture {get;} public RectF Source {get;}
    public TextureRegion(Texture2D texture,RectF source)
    {if(texture==null)throw new ArgumentNullException("texture");texture.Check();if(!(source.X>=0&&source.Y>=0&&source.Width>=0&&source.Height>=0&&source.X+source.Width<=texture.Width&&source.Y+source.Height<=texture.Height))throw new ArgumentException("Invalid texture region.");Texture=texture;Source=source;}
}
public struct SpriteDraw
{
    public TextureRegion Region;public RectF Destination;public Color32 Color;
    public float RotationDegrees;public Vector2 Origin;public SpriteFlip Flip;
    public SpriteDraw(TextureRegion region,RectF destination,Color32 color){Region=region;Destination=destination;Color=color;RotationDegrees=0;Origin=default;Flip=SpriteFlip.None;}
}
