using Dolphin.Graphics;

/// <summary>Original colored polygon models built in memory, without imported assets.</summary>
internal static class FoxGeometry
{
    private static VertexPositionColor V(float x,float y,float z,Color32 tint)=>new VertexPositionColor(new Vector3(x,y,z),tint);
    internal static Mesh Ship()
    {
        var top=new Color32(220,235,248);var blue=new Color32(78,130,183);var dark=new Color32(38,61,93);
        return Mesh.Create(new VertexPositionColor[] {
            V(0,.25f,-1.8f,top),V(-.25f,0,.6f,blue),V(.25f,0,.6f,blue),
            V(0,.25f,-1.8f,top),V(0,.55f,.2f,top),V(-.25f,0,.6f,blue),
            V(0,.25f,-1.8f,top),V(.25f,0,.6f,blue),V(0,.55f,.2f,top),
            V(-.2f,0,-.6f,top),V(-1.65f,-.12f,.65f,blue),V(-.15f,0,.45f,top),
            V(.2f,0,-.6f,top),V(.15f,0,.45f,top),V(1.65f,-.12f,.65f,blue),
            V(-.2f,0,.3f,dark),V(-1.65f,-.12f,.65f,blue),V(-.2f,-.2f,.6f,dark),
            V(.2f,0,.3f,dark),V(.2f,-.2f,.6f,dark),V(1.65f,-.12f,.65f,blue),
            V(0,.5f,.1f,top),V(0,1,.8f,blue),V(0,0,.65f,blue)
        });
    }
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
    internal static Mesh Ring()
    {
        var x=new float[] {1,.7071f,0,-.7071f,-1,-.7071f,0,.7071f};
        var y=new float[] {0,.7071f,1,.7071f,0,-.7071f,-1,-.7071f};
        var vertices=new VertexPositionColor[48];var gold=new Color32(255,211,86);
        for(int i=0;i<8;i++)
        {
            int n=(i+1)%8,p=i*6;
            vertices[p]=V(x[i]*1.7f,y[i]*1.7f,0,gold);
            vertices[p+1]=V(x[n]*1.7f,y[n]*1.7f,0,gold);
            vertices[p+2]=V(x[i]*1.35f,y[i]*1.35f,0,gold);
            vertices[p+3]=V(x[n]*1.7f,y[n]*1.7f,0,gold);
            vertices[p+4]=V(x[n]*1.35f,y[n]*1.35f,0,gold);
            vertices[p+5]=V(x[i]*1.35f,y[i]*1.35f,0,gold);
        }
        return Mesh.Create(vertices);
    }
}
