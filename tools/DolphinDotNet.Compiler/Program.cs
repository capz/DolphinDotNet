using DolphinDotNet.Compiler;

var bundleMode = args.Length >= 1 && args[0] == "--bundle";
if (bundleMode)
{
    try
    {
        var platform = "gamecube";
        var positional = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--platform")
            {
                if (++i >= args.Length) throw new ArgumentException("--platform requires gamecube, wii or wiiu");
                platform = args[i].ToLowerInvariant();
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Unknown bundle option: " + args[i]);
            else positional.Add(args[i]);
        }
        if (positional.Count != 2 || platform is not ("gamecube" or "wii" or "wiiu"))
            throw new ArgumentException("Usage: dndc --bundle <assembly.dll> <output-directory> [--platform gamecube|wii|wiiu]");
        var compiled = AotCompiler.Compile(positional[0]);
        GameCubeBundle.Emit(positional[0], positional[1], compiled, platform);
        Console.WriteLine($"Generated {platform} build bundle: {positional[1]}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine("dndc: " + exception.Message);
        return 1;
    }
}

var explicitAot = args.Length > 0 && args[0] is "--aot" or "--value-aot";
var positionalArgs = explicitAot ? args[1..] : args;
if (positionalArgs.Length is < 1 or > 2 || positionalArgs.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("Usage: dndc [--aot|--value-aot] <assembly.dll> [output.c]");
    return 2;
}
var input = positionalArgs[0];
var output = positionalArgs.Length == 2 ? positionalArgs[1] : "generated_program.c";
try
{
    var compiled = AotCompiler.Compile(input);
    File.WriteAllText(output, ValueCBackend.EmitProgram(compiled.ValueMethods, compiled.Entry, compiled.Model, compiled.Graph));
    Console.WriteLine($"AOT compiled {input} -> {output} ({compiled.ValueMethods.Count} reachable methods, {compiled.Graph.Types.Count} types)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"dndc: {ex.Message}");
    return 1;
}
