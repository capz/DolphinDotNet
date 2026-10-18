using System;
using Dolphin.Graphics;

internal static class KartGeometry
{
    private static VertexPositionColor V(float x,float y,float z,Color32 color)=>new VertexPositionColor(new Vector3(x,y,z),color);
    internal static Mesh Box()
    {
        var vertices=new VertexPositionColor[36];
        var points=new float[] {-1,-1,-1, 1,-1,-1, 1,1,-1, -1,1,-1,
                                -1,-1,1, 1,-1,1, 1,1,1, -1,1,1};
        var indices=new int[] {0,2,1,0,3,2, 4,5,6,4,6,7, 0,4,7,0,7,3,
                               1,2,6,1,6,5, 3,7,6,3,6,2, 0,1,5,0,5,4};
        for(int i=0;i<36;i++)
        {
            int p=indices[i]*3;
            byte shade=(byte)(i<12?190:i<24?150:235);
            vertices[i]=V(points[p]*.5f,points[p+1]*.5f,points[p+2]*.5f,new Color32(shade,shade,shade));
        }
        return Mesh.Create(vertices);
    }
    internal static Mesh Track()
    {
        const int segments=128;
        var vertices=new VertexPositionColor[segments*6];
        for(int i=0;i<segments;i++)
        {
            float a=i*2*MathF.PI/segments,b=(i+1)*2*MathF.PI/segments;
            float inner=KartGame.Radius-KartGame.HalfWidth,outer=KartGame.Radius+KartGame.HalfWidth;
            var c=i%2==0?new Color32(74,86,106):new Color32(80,92,112);
            int p=i*6;
            vertices[p]=V(MathF.Cos(a)*inner,0,MathF.Sin(a)*inner,c);
            vertices[p+1]=V(MathF.Cos(a)*outer,0,MathF.Sin(a)*outer,c);
            vertices[p+2]=V(MathF.Cos(b)*outer,0,MathF.Sin(b)*outer,c);
            vertices[p+3]=vertices[p];vertices[p+4]=vertices[p+2];
            vertices[p+5]=V(MathF.Cos(b)*inner,0,MathF.Sin(b)*inner,c);
        }
        return Mesh.Create(vertices);
    }
}
