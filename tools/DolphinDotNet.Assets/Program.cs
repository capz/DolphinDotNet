using DolphinDotNet.Assets;
try
{
    if (args.Length > 0 && args[0] == "gcft") { GameCubeAssetCommands.Run(args); return 0; }
    if (args.Length == 0 || args[0] == "--help")
    {
        Console.WriteLine("dnd-assets gcft inspect INPUT | gcft extract ARCHIVE OUTPUT_DIR | gcft pack INPUT_DIR OUTPUT.arc | gcft bti INPUT.bti OUTPUT.png\ndnd-assets model INPUT.{glb,gltf,fbx,obj,dae,3ds,stl,ply,gj,xj} OUTPUT.dndmodel [--texture-limit N] [--assimp PATH] [--allow-material-approximation] [--compile-gx-materials] [--bake-neutral-studio]\ndnd-assets audio INPUT.mp3 OUTPUT.adx [--loops PATH] [--loop NAME] [--sample-rate HZ] [--ffmpeg PATH]\ndnd-assets texture INPUT.png OUTPUT.dndtex [--mipmaps]\ndnd-assets font ATLAS.png OUTPUT.dndfont CELL_WIDTH CELL_HEIGHT [FIRST_CHARACTER] [CHARACTERS]\nFBX/OBJ/DAE/3DS/STL/PLY require Assimp CLI 5.3.x. PSO GJ/XJ geometry uses built-in .NET parsers; textured PSO materials currently require --allow-material-approximation. Outputs include a dependency manifest.");
        return 0;
    }
    if (args.Length >= 3 && args[0] == "audio")
    {
        string? sidecar = null, loop = null;
        string ffmpeg = "ffmpeg";
        int rate = 32000;
        for (int i = 3; i < args.Length; i++)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Missing value for " + args[i]);
            switch (args[i])
            {
                case "--loops": sidecar = args[++i]; break;
                case "--loop": loop = args[++i]; break;
                case "--sample-rate": rate = int.Parse(args[++i]); break;
                case "--ffmpeg": ffmpeg = args[++i]; break;
                default: throw new ArgumentException("Unknown audio option " + args[i]);
            }
        }
        AudioConverter.Convert(args[1], args[2], sidecar, loop, rate, ffmpeg);
        return 0;
    }
    if(args.Length>=3&&(args[0]=="texture"||args[0]=="font"))
    {
        using var image=DolphinDotNet.Imaging.RasterImage.Load(File.ReadAllBytes(args[1]));
        byte[] package;
        if(args[0]=="texture") {if(args.Length!=3&&(args.Length!=4||args[3]!="--mipmaps"))throw new ArgumentException("texture INPUT OUTPUT.dndtex [--mipmaps]");package=DrawingPackages.Texture(image,args.Length==4);}
        else {if(args.Length<5||args.Length>7)throw new ArgumentException("font ATLAS OUTPUT.dndfont CELL_WIDTH CELL_HEIGHT [FIRST_CHARACTER] [CHARACTERS]");package=DrawingPackages.Font(image,int.Parse(args[3]),int.Parse(args[4]),args.Length>5?int.Parse(args[5]):32,args.Length>6?int.Parse(args[6]):96);}
        File.WriteAllBytes(args[2],package);return 0;
    }
    if (args.Length < 3 || args[0] != "model") throw new ArgumentException("Use dnd-assets --help.");
    var options = new ImportOptions();
    for (int i = 3; i < args.Length; i++)
        switch (args[i])
        {
            case "--texture-limit": options.TextureLimit = int.Parse(args[++i]); break;
            case "--assimp": options.Assimp = args[++i]; break;
            case "--allow-material-approximation": options.ApproximateMaterials = true; break;
            case "--compile-gx-materials": options.CompileGxMaterials = true; break;
            case "--bake-neutral-studio": options.BakeNeutralStudio = true; break;
            default: throw new ArgumentException("Unknown option " + args[i]);
        }
    ModelConverter.Convert(args[1], args[2], options);
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("dnd-assets: " + error.Message); return 1; }
