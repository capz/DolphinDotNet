using DolphinDotNet.Imaging;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DolphinDotNet.Assets;

internal sealed partial class GltfImport
{
    private bool studioBaked;
    private static Vector3 Unit(Vector3 v)
    {
        float length=v.Length();
        if(!float.IsFinite(length)||length<1e-12f)throw new InvalidDataException("Studio bake requires finite, nonzero normals/tangents.");
        return v/length;
    }
    private sealed class StudioMap : IDisposable
    {
        internal readonly RasterImage Image;
        private readonly (int set,float[] offset,float[] scale,float rotation) uv;
        private readonly int wrapS,wrapT;
        private readonly bool linear,srgb;
        internal StudioMap(GltfImport owner,JsonElement info,bool srgb)
        {
            uv=TextureUv(info);this.srgb=srgb;
            int index=Int(info,"index",-1);var sampler=owner.SamplerOf(index);
            wrapS=sampler.wrapS;wrapT=sampler.wrapT;linear=sampler.linear!=0;
            Image=owner.DecodeSourceTexture(index);
        }
        internal Vector2 Coordinates(Vector2 uv0,Vector2? uv1)
        {
            Vector2 p=uv.set==0?uv0:uv1??throw new InvalidDataException("Studio material requires missing TEXCOORD_1.");
            float c=MathF.Cos(uv.rotation),s=MathF.Sin(uv.rotation);
            return new(uv.offset[0]+c*p.X*uv.scale[0]-s*p.Y*uv.scale[1],uv.offset[1]+s*p.X*uv.scale[0]+c*p.Y*uv.scale[1]);
        }
        internal bool AuthoredTangent=>uv.set==0&&uv.rotation==0&&uv.scale[0]==1&&uv.scale[1]==1;
        private static float WrapUv(float x,int wrap)=>wrap==0?Math.Clamp(x,0,1):wrap==1?x-MathF.Floor(x):1-MathF.Abs((x%2+2)%2-1);
        private static int Pixel(int i,int size,int wrap)
        {
            if(wrap==0)return Math.Clamp(i,0,size-1);
            int n=i%(wrap==1?size:size*2);if(n<0)n+=wrap==1?size:size*2;
            return n<size?n:size*2-1-n;
        }
        private Vector4 Read(int x,int y)
        {
            var p=Image[Pixel(x,Image.Width,wrapS),Pixel(y,Image.Height,wrapT)];
            return new(srgb?NeutralStudio.Linear(p.R/255f):p.R/255f,srgb?NeutralStudio.Linear(p.G/255f):p.G/255f,srgb?NeutralStudio.Linear(p.B/255f):p.B/255f,p.A/255f);
        }
        internal Vector4 Sample(Vector2 uv0,Vector2? uv1)
        {
            var p=Coordinates(uv0,uv1);float x=WrapUv(p.X,wrapS)*Image.Width-.5f,y=WrapUv(p.Y,wrapT)*Image.Height-.5f;
            if(!linear)return Read((int)MathF.Floor(x+.5f),(int)MathF.Floor(y+.5f));
            int ix=(int)MathF.Floor(x),iy=(int)MathF.Floor(y);float fx=x-ix,fy=y-iy;
            return Vector4.Lerp(Vector4.Lerp(Read(ix,iy),Read(ix+1,iy),fx),Vector4.Lerp(Read(ix,iy+1),Read(ix+1,iy+1),fx),fy);
        }
        public void Dispose()=>Image.Dispose();
    }
    private sealed class StudioSurface : IDisposable
    {
        internal StudioMap? Base,Normal,MetalRough,Emission,Occlusion;
        internal Vector4 Factor=Vector4.One;
        internal Vector3 Emissive;
        internal float Metal=1,Rough=1,NormalScale=1,Strength=1;
        internal bool Unlit,Two;
        internal string Alpha="OPAQUE";
        internal float Cutoff=.5f;
        internal StudioSurface(GltfImport owner,JsonElement m)
        {
            if(m.ValueKind==JsonValueKind.Undefined)return;
            try
            {
                if(m.TryGetProperty("extensions",out var ex))
                {
                    foreach(var e in ex.EnumerateObject())if(e.Name is not ("KHR_materials_unlit" or "KHR_materials_emissive_strength"))
                        throw new InvalidDataException("Studio bake does not support material extension "+e.Name);
                    Unlit=ex.TryGetProperty("KHR_materials_unlit",out _);
                }
                Two=m.TryGetProperty("doubleSided",out var two)&&two.GetBoolean();Alpha=Text(m,"alphaMode","OPAQUE");Cutoff=Float(m,"alphaCutoff",.5f);
                if(Alpha is not ("OPAQUE" or "MASK" or "BLEND")||!float.IsFinite(Cutoff)||Cutoff<0||Cutoff>1)throw new InvalidDataException("Studio alpha mode/cutoff.");
                if(m.TryGetProperty("pbrMetallicRoughness",out var pbr))
                {
                    var f=Values(pbr,"baseColorFactor",[1,1,1,1]);if(f.Length!=4||f.Any(x=>!float.IsFinite(x)||x<0||x>1))throw new InvalidDataException("Studio base factor.");
                    Factor=new(f[0],f[1],f[2],f[3]);Metal=Float(pbr,"metallicFactor",1);Rough=Float(pbr,"roughnessFactor",1);
                    if(!float.IsFinite(Metal)||Metal<0||Metal>1||!float.IsFinite(Rough)||Rough<0||Rough>1)throw new InvalidDataException("Studio metallic/roughness range.");
                    if(pbr.TryGetProperty("baseColorTexture",out var b))Base=new(owner,b,true);
                    if(!Unlit&&pbr.TryGetProperty("metallicRoughnessTexture",out var mr))MetalRough=new(owner,mr,false);
                }
                if(!Unlit)
                {
                    var ef=Values(m,"emissiveFactor",[0,0,0]);float es=1;
                    if(ex.ValueKind!=JsonValueKind.Undefined&&ex.TryGetProperty("KHR_materials_emissive_strength",out var emissionStrength))es=Float(emissionStrength,"emissiveStrength",1);
                    if(ef.Length!=3||ef.Any(x=>!float.IsFinite(x)||x<0||x>1)||!float.IsFinite(es)||es<0||es>65504)throw new InvalidDataException("Studio emission range.");
                    Emissive=new Vector3(ef[0],ef[1],ef[2])*es;
                    if(m.TryGetProperty("normalTexture",out var n)){Normal=new(owner,n,false);NormalScale=Float(n,"scale",1);if(!float.IsFinite(NormalScale)||MathF.Abs(NormalScale)>10000)throw new InvalidDataException("Studio normal scale.");}
                    if(m.TryGetProperty("emissiveTexture",out var e))Emission=new(owner,e,true);
                    if(m.TryGetProperty("occlusionTexture",out var o)){Occlusion=new(owner,o,false);Strength=Float(o,"strength",1);if(!float.IsFinite(Strength)||Strength<0||Strength>1)throw new InvalidDataException("Studio occlusion strength.");}
                }
                long pixels=new[]{Base,Normal,MetalRough,Emission,Occlusion}.Sum(x=>x==null?0L:(long)x.Image.Width*x.Image.Height);
                if(pixels>16*1024*1024)throw new InvalidDataException("Studio material exceeds the 64-MiB decoded-map budget.");
            }
            catch{Dispose();throw;}
        }
        public void Dispose(){Base?.Dispose();Normal?.Dispose();MetalRough?.Dispose();Emission?.Dispose();Occlusion?.Dispose();}
    }
    private void BakeStudioScene()
    {
        if(Items("skins").Length!=0||Items("animations").Length!=0)throw new InvalidDataException("Studio baking requires a static snapshot; export the chosen pose without skins/animation first.");
        var ns=Items("nodes");var meshes=Items("meshes");var mats=Items("materials");
        if(ns.Length==0||ns.Length>1024)throw new InvalidDataException("Studio node limit.");
        var parents=Enumerable.Repeat(-1,ns.Length).ToArray();
        for(int i=0;i<ns.Length;i++)foreach(var child in Array(ns[i],"children")){int c=child.GetInt32();if((uint)c>=ns.Length||parents[c]!=-1||c==i)throw new InvalidDataException("Studio node graph.");parents[c]=i;}
        var world=new Matrix4x4[ns.Length];var state=new byte[ns.Length];
        Matrix4x4 World(int i,int depth=0)
        {
            if(depth>=128||state[i]==1)throw new InvalidDataException("Studio node cycle/depth.");if(state[i]==2)return world[i];state[i]=1;
            Matrix4x4 local;
            if(ns[i].TryGetProperty("matrix",out var a))
            {var f=a.EnumerateArray().Select(x=>x.GetSingle()).ToArray();if(f.Length!=16||f.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Studio node matrix.");local=new(f[0],f[1],f[2],f[3],f[4],f[5],f[6],f[7],f[8],f[9],f[10],f[11],f[12],f[13],f[14],f[15]);}
            else
            {var t=Values(ns[i],"translation",[0,0,0]);var s=Values(ns[i],"scale",[1,1,1]);var q=Values(ns[i],"rotation",[0,0,0,1]);if(t.Length!=3||s.Length!=3||q.Length!=4||t.Concat(s).Concat(q).Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Studio node TRS.");var rotation=new Quaternion(q[0],q[1],q[2],q[3]);if(rotation.LengthSquared()<1e-12f)throw new InvalidDataException("Studio rotation.");local=Matrix4x4.CreateScale(s[0],s[1],s[2])*Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation))*Matrix4x4.CreateTranslation(t[0],t[1],t[2]);}
            world[i]=parents[i]<0?local:local*World(parents[i],depth+1);state[i]=2;return world[i];
        }
        var active=new HashSet<int>();
        void Visit(int i){if((uint)i>=ns.Length)throw new InvalidDataException("Studio scene node.");if(!active.Add(i))return;foreach(var c in Array(ns[i],"children"))Visit(c.GetInt32());}
        var scenes=Items("scenes");if(scenes.Length!=0){int selected=Int(root,"scene");if((uint)selected>=scenes.Length)throw new InvalidDataException("Studio scene index.");foreach(var n in Array(scenes[selected],"nodes"))Visit(n.GetInt32());}else for(int i=0;i<ns.Length;i++)if(parents[i]<0)Visit(i);
        var output=JsonNode.Parse(root.GetRawText())!.AsObject();var outputMeshes=new JsonArray();var outputMaterials=new JsonArray();var outputImages=new JsonArray();var outputTextures=new JsonArray();
        foreach(var n in output["nodes"]!.AsArray())n!.AsObject().Remove("mesh");
        int baked=0;long pixelBytes=0;
        foreach(int node in active.Order())
        {
            if(!ns[node].TryGetProperty("mesh",out var meshId))continue;int mesh=meshId.GetInt32();if((uint)mesh>=meshes.Length||ns[node].TryGetProperty("weights",out _)||meshes[mesh].TryGetProperty("weights",out _))throw new InvalidDataException("Studio mesh/morph weights.");
            var outputPrimitives=new JsonArray();Matrix4x4 matrix=World(node);if(!Matrix4x4.Invert(matrix,out var inverse))throw new InvalidDataException("Studio singular node transform.");
            foreach(var primitive in Array(meshes[mesh],"primitives"))
            {
                if(++baked>4096||(pixelBytes+=(long)options.TextureLimit*options.TextureLimit*4)>8*1024*1024)throw new InvalidDataException("Studio bake exceeds 4096 primitives or 8 MiB of output base-level pixels; lower texture limit or split the scene.");
                int material=Int(primitive,"material",-1);if(material< -1||material>=mats.Length)throw new InvalidDataException("Studio material reference.");
                using var surface=new StudioSurface(this,material<0?default:mats[material]);
                using var image=BakeStudioPrimitive(primitive,matrix,Matrix4x4.Transpose(inverse),surface);
                using var png=new MemoryStream();image.SaveAsPng(png);int texture=outputTextures.Count;
                outputImages.Add(new JsonObject{["uri"]="data:image/png;base64,"+Convert.ToBase64String(png.ToArray())});
                outputTextures.Add(new JsonObject{["source"]=texture,["sampler"]=0});
                var mapped=new JsonObject{["extensions"]=new JsonObject{["KHR_materials_unlit"]=new JsonObject()},["pbrMetallicRoughness"]=new JsonObject{["baseColorTexture"]=new JsonObject{["index"]=texture},["metallicFactor"]=0},["alphaMode"]=surface.Alpha,["alphaCutoff"]=surface.Cutoff,["doubleSided"]=surface.Two};
                int mappedId=outputMaterials.Count;outputMaterials.Add(mapped);
                var p=JsonNode.Parse(primitive.GetRawText())!.AsObject();p["material"]=mappedId;
                p["attributes"]!.AsObject().Remove("COLOR_0");p["attributes"]!.AsObject().Remove("TANGENT");outputPrimitives.Add(p);
            }
            output["nodes"]![node]!["mesh"]=outputMeshes.Count;outputMeshes.Add(new JsonObject{["primitives"]=outputPrimitives});
        }
        if(baked==0)throw new InvalidDataException("Studio scene contains no mesh primitives.");
        output["meshes"]=outputMeshes;output["materials"]=outputMaterials;output["images"]=outputImages;output["textures"]=outputTextures;
        output["samplers"]=new JsonArray(new JsonObject{["wrapS"]=33071,["wrapT"]=33071,["magFilter"]=9729,["minFilter"]=9987});
        output["extensionsUsed"]=new JsonArray("KHR_materials_unlit");output["extensionsRequired"]=new JsonArray("KHR_materials_unlit");
        root=JsonDocument.Parse(output.ToJsonString()).RootElement.Clone();studioBaked=true;
        Console.Error.WriteLine($"Baked {baked} static primitives using neutral-studio-v1 (fixed +Z orthographic view, no shadows/specular IBL). See output .studio.json.");
    }
    private RasterImage BakeStudioPrimitive(JsonElement primitive,Matrix4x4 world,Matrix4x4 normalWorld,StudioSurface surface)
    {
        if(primitive.TryGetProperty("targets",out _))throw new InvalidDataException("Studio morph targets require a static exported snapshot.");
        var attributes=primitive.GetProperty("attributes");
        foreach(var a in attributes.EnumerateObject())if(a.Name is not ("POSITION" or "NORMAL" or "TEXCOORD_0" or "TEXCOORD_1" or "COLOR_0" or "TANGENT"))throw new InvalidDataException("Unsupported studio attribute "+a.Name);
        var positions=Access(Int(attributes,"POSITION",-1),"VEC3");var uv=Access(Int(attributes,"TEXCOORD_0",-1),"VEC2");
        float[][]? Get(string key,string shape)=>attributes.TryGetProperty(key,out var a)?Access(a.GetInt32(),shape):null;
        var normals=Get("NORMAL","VEC3");var uv1=Get("TEXCOORD_1","VEC2");var tangents=Get("TANGENT","VEC4");float[][]? colors=null;
        if(attributes.TryGetProperty("COLOR_0",out var color)){string shape=Text(Items("accessors")[color.GetInt32()],"type");if(shape is not ("VEC3" or "VEC4"))throw new InvalidDataException("Studio color shape.");colors=Access(color.GetInt32(),shape);}
        foreach(var a in new[]{uv,normals,uv1,tangents,colors})if(a!=null&&a.Length!=positions.Length)throw new InvalidDataException("Studio attribute counts.");
        if(uv.Any(v=>v.Any(x=>x<0||x>1)))throw new InvalidDataException("Studio output UV0 must be unwrapped into 0..1; source material maps may wrap independently.");
        int mode=Int(primitive,"mode",4);if(mode is not (4 or 5 or 6))throw new InvalidDataException("Studio requires triangles, strips or fans.");
        var indices=primitive.TryGetProperty("indices",out var idx)?Access(idx.GetInt32(),"SCALAR").Select(v=>checked((int)v[0])).ToArray():Enumerable.Range(0,positions.Length).ToArray();
        if(indices.Length<3||indices.Length>196605||indices.Any(i=>(uint)i>=positions.Length))throw new InvalidDataException("Studio triangle indices.");indices=Triangulate(indices,mode);
        int size=options.TextureLimit;var image=new RasterImage(size,size);var covered=new bool[size*size];
        Vector3 P(int i)=>new(positions[i][0],positions[i][1],positions[i][2]);Vector2 U(int i)=>new(uv[i][0],uv[i][1]);Vector2? U1(int i)=>uv1==null?null:new(uv1[i][0],uv1[i][1]);
        Vector3 N(int i,Vector3 fallback)=>normals==null?fallback:new(normals[i][0],normals[i][1],normals[i][2]);
        Vector4 C(int i)=>colors==null?Vector4.One:new(colors[i][0],colors[i][1],colors[i][2],colors[i].Length==4?colors[i][3]:1);
        try
        {
            for(int t=0;t<indices.Length;t+=3)
            {
                int a=indices[t],b=indices[t+1],c=indices[t+2];Vector2 ua=U(a),ub=U(b),uc=U(c);
                float area=(ub.X-ua.X)*(uc.Y-ua.Y)-(ub.Y-ua.Y)*(uc.X-ua.X);if(MathF.Abs(area)<1e-10f)throw new InvalidDataException("Studio UV0 has a degenerate triangle.");
                Vector3 face=Unit(Vector3.Cross(P(b)-P(a),P(c)-P(a)));bool back=Vector3.Dot(Unit(Vector3.TransformNormal(face,normalWorld)),NeutralStudio.View)<0;
                Vector3 tangent=Vector3.Zero,bitangent=Vector3.Zero;
                if(surface.Normal!=null)
                {
                    var na=surface.Normal.Coordinates(ua,U1(a));var nb=surface.Normal.Coordinates(ub,U1(b));var nc=surface.Normal.Coordinates(uc,U1(c));
                    Vector2 d1=nb-na,d2=nc-na;float determinant=d1.X*d2.Y-d1.Y*d2.X;if(MathF.Abs(determinant)<1e-10f)throw new InvalidDataException("Studio normal-map UVs are degenerate.");
                    Vector3 e1=Vector3.TransformNormal(P(b)-P(a),world),e2=Vector3.TransformNormal(P(c)-P(a),world);
                    tangent=(e1*d2.Y-e2*d1.Y)/determinant;bitangent=(e2*d1.X-e1*d2.X)/determinant;
                }
                int minX=Math.Clamp((int)MathF.Floor(MathF.Min(ua.X,MathF.Min(ub.X,uc.X))*size),0,size-1),maxX=Math.Clamp((int)MathF.Ceiling(MathF.Max(ua.X,MathF.Max(ub.X,uc.X))*size),0,size-1);
                int minY=Math.Clamp((int)MathF.Floor(MathF.Min(ua.Y,MathF.Min(ub.Y,uc.Y))*size),0,size-1),maxY=Math.Clamp((int)MathF.Ceiling(MathF.Max(ua.Y,MathF.Max(ub.Y,uc.Y))*size),0,size-1),written=0;
                for(int y=minY;y<=maxY;y++)for(int x=minX;x<=maxX;x++)
                {
                    float px=(x+.5f)/size,py=(y+.5f)/size;
                    float wb=((px-ua.X)*(uc.Y-ua.Y)-(py-ua.Y)*(uc.X-ua.X))/area;
                    float wc=((ub.X-ua.X)*(py-ua.Y)-(ub.Y-ua.Y)*(px-ua.X))/area,wa=1-wb-wc;
                    if(wa< -1e-6f||wb< -1e-6f||wc< -1e-6f)continue;written++;
                    Vector2 u=ua*wa+ub*wb+uc*wc;Vector2? v=uv1==null?null:U1(a)!.Value*wa+U1(b)!.Value*wb+U1(c)!.Value*wc;
                    Vector4 baseColor=(surface.Base?.Sample(u,v)??Vector4.One)*surface.Factor*(C(a)*wa+C(b)*wb+C(c)*wc);
                    if(baseColor.X<0||baseColor.X>1||baseColor.Y<0||baseColor.Y>1||baseColor.Z<0||baseColor.Z>1||baseColor.W<0||baseColor.W>1)throw new InvalidDataException("Studio vertex/base color outside 0..1.");
                    Vector3 normal=Unit(Vector3.TransformNormal(N(a,face)*wa+N(b,face)*wb+N(c,face)*wc,normalWorld));
                    if(surface.Normal!=null)
                    {
                        Vector3 tx=tangent;float sign=Vector3.Dot(Vector3.Cross(normal,tangent),bitangent)<0?-1:1;
                        if(tangents!=null&&surface.Normal.AuthoredTangent){tx=Vector3.TransformNormal(new Vector3(tangents[a][0],tangents[a][1],tangents[a][2])*wa+new Vector3(tangents[b][0],tangents[b][1],tangents[b][2])*wb+new Vector3(tangents[c][0],tangents[c][1],tangents[c][2])*wc,world);sign=tangents[a][3]*(world.GetDeterminant()<0?-1:1);if(sign is not (-1 or 1))throw new InvalidDataException("Studio tangent handedness.");}
                        tx=Unit(tx-normal*Vector3.Dot(normal,tx));Vector3 by=Vector3.Cross(normal,tx)*sign;var sampled=surface.Normal.Sample(u,v);
                        Vector3 map=Unit(new((sampled.X*2-1)*surface.NormalScale,(sampled.Y*2-1)*surface.NormalScale,sampled.Z*2-1));normal=Unit(tx*map.X+by*map.Y+normal*map.Z);
                    }
                    if(back&&surface.Two)normal=-normal;
                    var mr=surface.MetalRough?.Sample(u,v)??Vector4.One;float ao=surface.Occlusion==null?1:1-surface.Strength+surface.Strength*surface.Occlusion.Sample(u,v).X;
                    var em=surface.Emission?.Sample(u,v)??Vector4.One;Vector3 emission=surface.Emissive*new Vector3(em.X,em.Y,em.Z);
                    var rgb=NeutralStudio.Evaluate(new(baseColor.X,baseColor.Y,baseColor.Z),surface.Metal*mr.Z,surface.Rough*mr.Y,normal,ao,emission,surface.Unlit);
                    var result=new Rgba32(NeutralStudio.Display(rgb.X,!surface.Unlit),NeutralStudio.Display(rgb.Y,!surface.Unlit),NeutralStudio.Display(rgb.Z,!surface.Unlit),surface.Alpha=="OPAQUE"?(byte)255:Clamp255(baseColor.W));
                    int at=y*size+x;if(covered[at]){var old=image[x,y];if(Math.Abs(old.R-result.R)>3||Math.Abs(old.G-result.G)>3||Math.Abs(old.B-result.B)>3||Math.Abs(old.A-result.A)>1)throw new InvalidDataException("Studio UV0 overlaps with conflicting baked values; provide a non-overlapping atlas.");}
                    image[x,y]=result;covered[at]=true;
                }
                if(written==0)throw new InvalidDataException("Studio triangle covers no texels; increase --texture-limit or improve the UV atlas.");
            }
            for(int border=0;border<4;border++)
            {
                var previous=(bool[])covered.Clone();for(int y=0;y<size;y++)for(int x=0;x<size;x++)if(!previous[y*size+x])
                {foreach(var d in new[]{(-1,0),(1,0),(0,-1),(0,1)}){int nx=x+d.Item1,ny=y+d.Item2;if(nx>=0&&nx<size&&ny>=0&&ny<size&&previous[ny*size+nx]){image[x,y]=image[nx,ny];covered[y*size+x]=true;break;}}}
            }
            return image;
        }
        catch{image.Dispose();throw;}
    }
}
