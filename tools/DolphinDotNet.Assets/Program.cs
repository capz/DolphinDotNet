using DolphinDotNet.Assets;
try
{
    if (args.Length == 0 || args[0] == "--help")
    {
        Console.WriteLine("dnd-assets model INPUT.{glb,gltf,fbx,obj} OUTPUT.dndmodel [--texture-limit N] [--assimp PATH] [--allow-material-approximation]\nFBX/OBJ require Assimp CLI 5.3.x. Outputs include a dependency manifest.");
        return 0;
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
