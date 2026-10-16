using DolphinDotNet.Compiler;

var bundleMode = args.Length >= 1 && args[0] == "--bundle";
if (bundleMode)
{
    try
    {
        var platform = "gamecube";
        var positional = new List<string>();
        string? gdbIp = null, gdbNetmask = null, gdbGateway = null;
        var gdbPort = "2828";
        bool gdbPortSpecified = false, debugSymbols = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--platform")
            {
                if (++i >= args.Length) throw new ArgumentException("--platform requires gamecube, wii or wiiu");
                platform = args[i].ToLowerInvariant();
            }
            else if (args[i] == "--debug-symbols")
            {
                if (debugSymbols) throw new ArgumentException("Duplicate --debug-symbols.");
                debugSymbols = true;
            }
            else if (args[i] is "--gdb-tcp" or "--gdb-netmask" or "--gdb-gateway" or "--gdb-port")
            {
                var option = args[i];
                if (++i >= args.Length) throw new ArgumentException(option + " requires a value.");
                switch (option)
                {
                    case "--gdb-tcp":
                        if (gdbIp is not null) throw new ArgumentException("Duplicate --gdb-tcp option.");
                        gdbIp = args[i];
                        break;
                    case "--gdb-netmask":
                        if (gdbNetmask is not null) throw new ArgumentException("Duplicate --gdb-netmask option.");
                        gdbNetmask = args[i];
                        break;
                    case "--gdb-gateway":
                        if (gdbGateway is not null) throw new ArgumentException("Duplicate --gdb-gateway option.");
                        gdbGateway = args[i];
                        break;
                    case "--gdb-port":
                        if (gdbPortSpecified) throw new ArgumentException("Duplicate --gdb-port option.");
                        gdbPort = args[i];
                        gdbPortSpecified = true;
                        break;
                }
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Unknown bundle option: " + args[i]);
            else positional.Add(args[i]);
        }
        if (positional.Count != 2 || platform is not ("gamecube" or "wii" or "wiiu"))
            throw new ArgumentException("Usage: dndc --bundle <assembly.dll> <output-directory> [--platform gamecube|wii|wiiu] [--debug-symbols] [--gdb-tcp IPv4 --gdb-netmask IPv4 --gdb-gateway IPv4 [--gdb-port PORT]]");
        if (gdbIp is null && (gdbNetmask is not null || gdbGateway is not null || gdbPortSpecified))
            throw new ArgumentException("GDB TCP settings require --gdb-tcp.");
        if (gdbIp is not null && (gdbNetmask is null || gdbGateway is null))
            throw new ArgumentException("--gdb-tcp also requires --gdb-netmask and --gdb-gateway.");
        var gdb = gdbIp is null ? null : GdbTcpOptions.Parse(gdbIp, gdbNetmask!, gdbGateway!, gdbPort);
        if (gdb is not null && platform != "gamecube")
            throw new ArgumentException("GDB TCP support currently requires --platform gamecube.");
        var compiled = AotCompiler.Compile(positional[0]);
        GameCubeBundle.Emit(positional[0], positional[1], compiled, platform, gdb, debugSymbols);
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
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, ValueCBackend.EmitProgram(compiled.ValueMethods, compiled.Entry, compiled.Model, compiled.Graph));
    Console.WriteLine($"AOT compiled {input} -> {output} ({compiled.ValueMethods.Count} reachable methods, {compiled.Graph.Types.Count} types)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"dndc: {ex.Message}");
    return 1;
}
