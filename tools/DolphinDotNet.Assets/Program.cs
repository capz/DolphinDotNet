using DolphinDotNet.Assets;
try
{
    if (args.Length == 0 || args[0] == "--help")
    {
        Console.WriteLine("dnd-assets model INPUT.{glb,gltf,fbx,obj} OUTPUT.dndmodel [--texture-limit N] [--assimp PATH] [--allow-material-approximation]\ndnd-assets texture INPUT.png OUTPUT.dndtex\ndnd-assets font ATLAS.png OUTPUT.dndfont CELL_WIDTH CELL_HEIGHT [FIRST_CHARACTER] [CHARACTERS]\nFBX/OBJ require Assimp CLI 5.3.x. Outputs include a dependency manifest.");
        return 0;
    }
    if(args.Length>=3&&(args[0]=="texture"||args[0]=="font"))
    {
        using var image=DolphinDotNet.Imaging.RasterImage.Load(File.ReadAllBytes(args[1]));
        byte[] package;
        if(args[0]=="texture") {if(args.Length!=3)throw new ArgumentException("texture INPUT OUTPUT.dndtex");package=DrawingPackages.Texture(image);}
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
            default: throw new ArgumentException("Unknown option " + args[i]);
        }
    ModelConverter.Convert(args[1], args[2], options);
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("dnd-assets: " + error.Message); return 1; }
