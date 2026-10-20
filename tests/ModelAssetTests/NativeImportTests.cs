using System.Text;
using SkiaSharp;

// Fixture generation only: original files are consumed by the native runtime tests.
internal static class NativeImportTests
{
    internal static void Run(string output)
    {
        string sd=Path.Combine(output,"sd");
        File.Copy(Path.Combine(output,"texture.png"),Path.Combine(sd,"texture.png"),true);
        BinaryFbxFixture.Write(Path.Combine(sd,"embedded-binary.fbx"),File.ReadAllBytes(Path.Combine(sd,"texture.png")));
        File.WriteAllText(Path.Combine(sd,"concave.mtl"),"newmtl Surface\nKd 0.8 0.6 0.4\nmap_Kd texture.png\n");
        File.WriteAllText(Path.Combine(sd,"concave.obj"),"mtllib concave.mtl\no Concave\nv 0 0 0\nv 2 0 0\nv 2 2 0\nv 1 1 0\nv 0 2 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0.5 0.5\nvt 0 1\ns 1\nusemtl Surface\nf -5/-5 -4/-4 -3/-3 -2/-2 -1/-1\n");
        File.WriteAllText(Path.Combine(sd,"direct.fbx"),AnimatedFbx);
        string skinSource=AnimatedFbx.Replace("Objects: {","Objects: {\n"+SkinObjects,StringComparison.Ordinal)
            .Replace("Connections: {","Connections: {\n"+SkinConnections,StringComparison.Ordinal)
            .Replace("C: \"OP\",302,101,","C: \"OP\",302,102,",StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(sd,"skinned.fbx"),skinSource);
        string textured=AnimatedFbx.Replace("Objects: {","Objects: {\n"+TextureObjects,StringComparison.Ordinal)
            .Replace("Connections: {","Connections: {\n"+TextureConnections,StringComparison.Ordinal)
            .Replace("PolygonVertexIndex: *3 { a: 0,1,-3 }","PolygonVertexIndex: *3 { a: 0,1,-3 }\n"+UvLayer,StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(sd,"textured.fbx"),textured);
        string translucent=textured.Replace(
            "P: \"DiffuseColor\", \"Color\", \"\", \"A\",1,1,1",
            "P: \"DiffuseColor\", \"Color\", \"\", \"A\",1,1,1\n            P: \"Opacity\", \"double\", \"Number\", \"A\",0.45",
            StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(sd,"translucent.fbx"),translucent);
        File.WriteAllText(Path.Combine(sd,"invalid.fbx"),"not an FBX file");
        File.WriteAllText(Path.Combine(sd,"invalid.obj"),"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 999\n");
        // Native image codec fixtures, independent of the desktop model importer.
        byte[] tga=new byte[18+64];tga[2]=2;tga[12]=4;tga[14]=4;tga[16]=32;tga[17]=0x28;
        for(int i=0;i<16;i++){tga[18+i*4]=50;tga[19+i*4]=40;tga[20+i*4]=30;tga[21+i*4]=255;}
        File.WriteAllBytes(Path.Combine(sd,"texture.tga"),tga);
        byte[] bmp=new byte[54+64];
        using(var w=new BinaryWriter(new MemoryStream(bmp))){w.Write((ushort)0x4d42);w.Write(bmp.Length);w.Write(0);w.Write(54);w.Write(40);w.Write(4);w.Write(4);w.Write((ushort)1);w.Write((ushort)32);w.Write(0);w.Write(64);}
        for(int i=0;i<16;i++){bmp[54+i*4]=50;bmp[55+i*4]=40;bmp[56+i*4]=30;bmp[57+i*4]=255;}
        File.WriteAllBytes(Path.Combine(sd,"texture.bmp"),bmp);
        using(var bitmap=SKBitmap.Decode(Path.Combine(sd,"texture.png")))
        using(var image=SKImage.FromBitmap(bitmap))
        using(var encoded=image.Encode(SKEncodedImageFormat.Jpeg,95))
        using(var file=File.Create(Path.Combine(sd,"texture.jpg")))encoded.SaveTo(file);
        foreach(string extension in new[]{"tga","bmp","jpg"}) {
            File.WriteAllText(Path.Combine(sd,"image-"+extension+".mtl"),"newmtl Surface\nKd 1 1 1\nmap_Kd texture."+extension+"\n");
            File.WriteAllText(Path.Combine(sd,"image-"+extension+".obj"),File.ReadAllText(Path.Combine(sd,"concave.obj")).Replace("concave.mtl","image-"+extension+".mtl",StringComparison.Ordinal));
        }
        Console.WriteLine("Generated original FBX, skinned FBX, OBJ/MTL and image fixtures for on-device tests.");
    }
    private const string UvLayer="""
        LayerElementUV: 0 {
            Version: 101
            Name: "UVMap"
            MappingInformationType: "ByPolygonVertex"
            ReferenceInformationType: "IndexToDirect"
            UV: *6 { a: 0,0,1,0,0,1 }
            UVIndex: *3 { a: 0,1,2 }
        }
        Layer: 0 { Version: 100
            LayerElement: { Type: "LayerElementUV"
                TypedIndex: 0
            }
        }
        """;
    private const string TextureObjects="""
        Material: 600, "Material::Surface", "" {
            Version: 102
            ShadingModel: "lambert"
            Properties70: { P: "DiffuseColor", "Color", "", "A",1,1,1 }
        }
        Texture: 601, "Texture::Texture", "TextureVideoClip" {
            Type: "TextureVideoClip"
            Version: 202
            TextureName: "Texture::Texture"
            Media: "Video::Video"
            FileName: "texture.png"
            RelativeFilename: "texture.png"
            ModelUVTranslation: 0,0
            ModelUVScaling: 1,1
        }
        Video: 602, "Video::Video", "Clip" {
            Type: "Clip"
            Filename: "texture.png"
            RelativeFilename: "texture.png"
        }
        """;
    private const string TextureConnections="""
        C: "OO",600,101
        C: "OP",601,600,"DiffuseColor"
        C: "OO",602,601
        """;
    private const string SkinObjects="""
        Model: 102, "Model::Bone", "LimbNode" {
            Version: 232
            Properties70: {
                P: "Lcl Translation", "Lcl Translation", "", "A",0,0,0
                P: "Lcl Rotation", "Lcl Rotation", "", "A",0,0,0
                P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1
            }
        }
        Deformer: 200, "Deformer::Skin", "Skin" { Version: 101
            Link_DeformAcuracy: 50
        }
        Deformer: 201, "SubDeformer::Cluster", "Cluster" {
            Version: 100
            UserData: "", ""
            Indexes: *3 { a: 0,1,2 }
            Weights: *3 { a: 1,1,1 }
            Transform: *16 { a: 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1 }
            TransformLink: *16 { a: 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1 }
        }
        """;
    private const string SkinConnections="""
        C: "OO",102,0
        C: "OO",200,100
        C: "OO",201,200
        C: "OO",102,201
        """;
    private const string AnimatedFbx="""
        ; FBX 7.4.0 generated regression fixture, one triangle and a one-second translation.
        FBXHeaderExtension: { FBXHeaderVersion: 1003
            FBXVersion: 7400
        }
        GlobalSettings: { Version: 1000
            Properties70: {
                P: "UpAxis", "int", "Integer", "",1
                P: "UpAxisSign", "int", "Integer", "",1
                P: "FrontAxis", "int", "Integer", "",2
                P: "FrontAxisSign", "int", "Integer", "",-1
                P: "CoordAxis", "int", "Integer", "",0
                P: "CoordAxisSign", "int", "Integer", "",1
                P: "UnitScaleFactor", "double", "Number", "",100
            }
        }
        Objects: {
            Geometry: 100, "Geometry::Triangle", "Mesh" {
                Vertices: *9 { a: 0,0,0,1,0,0,0,1,0 }
                PolygonVertexIndex: *3 { a: 0,1,-3 }
            }
            Model: 101, "Model::Triangle", "Mesh" {
                Version: 232
                Properties70: {
                    P: "Lcl Translation", "Lcl Translation", "", "A",0,0,0
                    P: "Lcl Rotation", "Lcl Rotation", "", "A",0,0,0
                    P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1
                }
            }
            AnimationStack: 300, "AnimStack::Move", "" {
                Properties70: {
                    P: "LocalStart", "KTime", "Time", "",0
                    P: "LocalStop", "KTime", "Time", "",46186158000
                    P: "ReferenceStart", "KTime", "Time", "",0
                    P: "ReferenceStop", "KTime", "Time", "",46186158000
                }
            }
            AnimationLayer: 301, "AnimLayer::Base", "" { }
            AnimationCurveNode: 302, "AnimCurveNode::T", "" {
                Properties70: {
                    P: "d|X", "Number", "", "A",0
                    P: "d|Y", "Number", "", "A",0
                    P: "d|Z", "Number", "", "A",0
                }
            }
            AnimationCurve: 400, "AnimCurve::X", "" {
                Default: 0
                KeyVer: 4008
                KeyTime: *2 { a: 0,46186158000 }
                KeyValueFloat: *2 { a: 0,2 }
                KeyAttrFlags: *1 { a: 4 }
                KeyAttrDataFloat: *4 { a: 0,0,0,0 }
                KeyAttrRefCount: *1 { a: 2 }
            }
        }
        Connections: {
            C: "OO",100,101
            C: "OO",101,0
            C: "OO",301,300
            C: "OO",302,301
            C: "OP",302,101,"Lcl Translation"
            C: "OP",400,302,"d|X"
        }
        """;
}
