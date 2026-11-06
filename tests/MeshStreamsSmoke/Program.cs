using System;
using Dolphin.Graphics;
static class Program
{
 static int Main(){
  var v=new VertexPositionColorTexture[]{new(new Vector3(0,0,0),Color32.White,new Vector2(0,0)),new(new Vector3(1,0,0),Color32.White,new Vector2(1,0)),new(new Vector3(0,1,0),Color32.White,new Vector2(0,1))};
  using var mesh=DynamicMesh.Create(v);
  var colors=new Color32[]{new(255,0,0,255),new(0,255,0,255),new(0,0,255,255)};
  using var view=new MeshColorView(mesh,colors);if(view.NativeBytes!=12)return 1;
  view.SetColors(colors);mesh.SetVertices(v);
  var passes=new MeshPasses(mesh,2);if(mesh.NativeBytes!=140)return 2;
  passes.Set(0,new Color32(255,0,0,128),new UvTransform(new Vector2(.25f,0),new Vector2(1,1)));
  passes.Set(1,new Color32(0,255,0,64),new UvTransform(new Vector2(.75f,0),new Vector2(1,1)));
  using var tex=Texture2D.Create(4,4,new byte[64]);
  var material=new Material{Texture=tex,Color=new Color32(11,22,33,44)};
  var renderer=new Renderer();var camera=new Camera();var pose=new Transform();var device=GraphicsDevice.Default;
  device.BeginFrame(Color32.Black);
  try{passes.Set(0,Color32.White,default);return 3;}catch(InvalidOperationException){}
  renderer.Begin3D(camera);renderer.DrawMeshPasses(passes,material,pose);renderer.DrawMesh(mesh,material,pose);renderer.End3D();device.EndFrame();
  if(material.Color.Packed!=0x0b16212c)return 4;
  mesh.Dispose();device.BeginFrame(Color32.Black);renderer.Begin3D(camera);renderer.DrawMesh(view,new Material(),pose);renderer.End3D();device.EndFrame();
  return 0;
 }
}
