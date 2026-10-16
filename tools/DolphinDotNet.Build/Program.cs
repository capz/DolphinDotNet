using System.Diagnostics;
using System.Text.Json;

return await Driver.MainAsync(args);

internal static class Driver
{
    // Same toolchain as CI, pinned by digest rather than a mutable tag.
    const string Image = "ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85";
    static Process? child;
    static string? container;
    static volatile bool cancelled;
    sealed record RunConfig(string Dol, string Dolphin, string UserDirectory);

    internal static async Task<int> MainAsync(string[] args)
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled = true; StopChild(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopChild();
        try
        {
            if (args.Length == 0 || args[0] == "--help")
            {
                Console.WriteLine("DolphinDotNet build/run driver\n" +
                    "  doctor [--backend Auto|Native|Docker] [--dolphin PATH] [--build-only true]\n" +
                    "  build --assembly DLL --compiler DLL --native-directory DIR --dol DOL --elf ELF --run-config JSON\n" +
                    "        [--backend Auto|Native|Docker] [--dolphin PATH] [--user-directory DIR] [--jobs N]\n" +
                     "        [--debug-symbols true|false] [--gdb-transport Off|Tcp] [--gdb-ip IPv4] [--gdb-netmask IPv4] [--gdb-gateway IPv4] [--gdb-port PORT]\n" +
                    "  run --config JSON [--dolphin PATH]");
                return 0;
            }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length ||
                    !options.TryAdd(args[i][2..], args[i + 1]))
                    throw new ArgumentException("Options require unique --name VALUE pairs.");
            }
            string Get(string key, string fallback = "") => options.GetValueOrDefault(key, fallback);
            string Required(string key) => Get(key) is { Length: > 0 } value ? Path.GetFullPath(value) :
                throw new ArgumentException("Missing --" + key + ".");
            if (args[0] == "run")
            {
                Allow(options, "config", "dolphin");
                var config = JsonSerializer.Deserialize<RunConfig>(File.ReadAllText(Required("config")))
                    ?? throw new InvalidDataException("Invalid Dolphin run configuration. Build the project first.");
                if (!File.Exists(config.Dol) || new FileInfo(config.Dol).Length < 256)
                    throw new FileNotFoundException("DOL missing or truncated. Build the project before running.", config.Dol);
                var dolphin = FindDolphin(Get("dolphin", config.Dolphin));
                Directory.CreateDirectory(Path.Combine(config.UserDirectory, "Config"));
                var ini = Path.Combine(config.UserDirectory, "Config", "Dolphin.ini");
                if (!File.Exists(ini))
                    File.WriteAllText(ini, "[Core]\nSIDevice0 = 6\nSIDevice1 = 6\n");
                Console.WriteLine("Launching " + config.Dol + "\nDolphin user directory: " + config.UserDirectory);
                return await Run(dolphin, ["--user", config.UserDirectory, "--exec", config.Dol], Path.GetDirectoryName(config.Dol)!);
            }
            if (args[0] is not ("build" or "doctor")) throw new ArgumentException("Unknown command: " + args[0]);
            Allow(options, args[0] == "doctor" ? ["backend", "dolphin", "build-only"] :
                ["assembly", "compiler", "native-directory", "dol", "elf", "run-config", "backend", "dolphin", "user-directory", "jobs",
                  "debug-symbols", "gdb-transport", "gdb-ip", "gdb-netmask", "gdb-gateway", "gdb-port"]);
            string? native = null, dol = null, elf = null, runConfig = null, assembly = null, compiler = null;
            if (args[0] == "build")
            {
                assembly = Required("assembly"); compiler = Required("compiler");
                native = Required("native-directory"); dol = Required("dol"); elf = Required("elf");
                runConfig = Required("run-config");
                if (Path.GetExtension(dol) != ".dol" || Path.GetExtension(elf) != ".elf")
                    throw new ArgumentException("Native outputs must use .dol and .elf extensions.");
                foreach (var path in new[] { assembly, compiler, dol, elf, runConfig,
                    Path.ChangeExtension(elf, ".dnd-debug.json"), Path.ChangeExtension(elf, ".dnd-addresses.json"),
                    AppContext.BaseDirectory })
                    if (Within(native, path)) throw new ArgumentException("The native work directory must not contain inputs, tools or published outputs.");
                // Never allow a failed native rebuild to leave an old runnable artifact.
                foreach (var path in new[] { dol, elf, runConfig,
                    Path.ChangeExtension(elf, ".dnd-debug.json"), Path.ChangeExtension(elf, ".dnd-addresses.json") })
                    try { File.Delete(path); } catch (DirectoryNotFoundException) { }
                if (!File.Exists(assembly) || !File.Exists(compiler))
                    throw new FileNotFoundException("Managed assembly or compiler missing. Build through the sample project/solution.");
            }
            string gdbTransport = Get("gdb-transport", "Off");
            bool gdbTcp = gdbTransport.Equals("Tcp", StringComparison.OrdinalIgnoreCase);
            if (!gdbTcp && !gdbTransport.Equals("Off", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("--gdb-transport must be Off or Tcp.");
            if (gdbTcp && new[] { "gdb-ip", "gdb-netmask", "gdb-gateway" }.Any(key => string.IsNullOrEmpty(Get(key))))
                throw new ArgumentException("TCP debugging requires --gdb-ip, --gdb-netmask and --gdb-gateway.");
            if (!gdbTcp && new[] { "gdb-ip", "gdb-netmask", "gdb-gateway", "gdb-port" }.Any(options.ContainsKey))
                throw new ArgumentException("GDB TCP settings require --gdb-transport Tcp.");
            if (!bool.TryParse(Get("debug-symbols", "false"), out bool debugSymbols))
                throw new ArgumentException("--debug-symbols must be true or false.");
            debugSymbols |= gdbTcp;
            string backend = Get("backend", "Auto");
            if (backend.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                backend = !OperatingSystem.IsWindows() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVKITPRO")) ? "Native" : "Docker";
            if (backend.Equals("Native", StringComparison.OrdinalIgnoreCase))
            {
                if (OperatingSystem.IsWindows())
                    throw new InvalidOperationException("Use DndBuildBackend=Docker on Windows (Docker Desktop with Linux containers).");
                string sdk = Environment.GetEnvironmentVariable("DEVKITPRO") ?? "";
                string ppc = Environment.GetEnvironmentVariable("DEVKITPPC") ?? "";
                if (!File.Exists(Path.Combine(sdk, "libogc2/gamecube/include/gccore.h")) ||
                    !File.Exists(Path.Combine(sdk, "libogc2/gamecube/include/fat.h")) ||
                    new[] { "ogc", "fat", "dvm", "iso9660", "bba", "aesnd" }.Any(name =>
                        !File.Exists(Path.Combine(sdk, "libogc2/gamecube/lib/lib" + name + ".a"))) ||
                    !File.Exists(Path.Combine(ppc, "bin/powerpc-eabi-gcc")))
                    throw new InvalidOperationException("Native toolchain missing. Install devkitPPC and libogc2; set DEVKITPRO and DEVKITPPC, or select DndBuildBackend=Docker.");
                Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator,
                    Path.Combine(ppc, "bin"), Path.Combine(sdk, "tools/bin"), Environment.GetEnvironmentVariable("PATH")));
                foreach (var tool in new[] { "make", "bash", "git", "python3", "elf2dol" }) RequireExecutable(tool);
                if ((native ?? "").Any(char.IsWhiteSpace) || sdk.Any(char.IsWhiteSpace) || ppc.Any(char.IsWhiteSpace))
                    throw new InvalidOperationException("The native Make toolchain needs paths without whitespace. Select DndBuildBackend=Docker for a checkout or SDK path containing spaces.");
                Console.WriteLine("Native toolchain: " + ppc);
            }
            else if (backend.Equals("Docker", StringComparison.OrdinalIgnoreCase))
            {
                RequireExecutable("docker");
                string dockerOs;
                try { dockerOs = await Capture("docker", "info", "--format", "{{.OSType}}"); }
                catch (IOException) { throw new InvalidOperationException("Docker daemon unavailable. Start Docker Desktop/Engine with Linux containers, or select the Native backend."); }
                if (dockerOs != "linux")
                    throw new InvalidOperationException("Switch Docker Desktop to Linux containers for the console toolchain.");
                Console.WriteLine("Container toolchain: " + Image);
            }
            else throw new ArgumentException("DndBuildBackend must be Auto, Native or Docker.");
            if (args[0] == "doctor")
            {
                if (!Get("build-only").Equals("true", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("Dolphin: " + FindDolphin(Get("dolphin")));
                return 0;
            }
            if (!int.TryParse(Get("jobs", "2"), out int jobs) || jobs < 1 || jobs > 64)
                throw new ArgumentException("--jobs must be between 1 and 64.");
            var marker = Path.Combine(native!, ".dnd-build-directory");
            if (Directory.Exists(native))
            {
                if (!File.Exists(marker)) throw new InvalidOperationException("Refusing to remove an unowned native work directory: " + native);
                Directory.Delete(native, true);
            }
            Directory.CreateDirectory(native!);
            File.WriteAllText(marker, "DolphinDotNet generated build directory\n");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var compilerArgs = new List<string> { compiler!, "--bundle", assembly!, native! };
            if (debugSymbols) compilerArgs.Add("--debug-symbols");
            if (gdbTcp)
                compilerArgs.AddRange(["--gdb-tcp", Get("gdb-ip"), "--gdb-netmask", Get("gdb-netmask"),
                    "--gdb-gateway", Get("gdb-gateway"), "--gdb-port", Get("gdb-port", "2828")]);
            int result = await Run(dotnet, compilerArgs, Directory.GetCurrentDirectory());
            if (result != 0) return result;
            if (backend.Equals("Native", StringComparison.OrdinalIgnoreCase))
                result = await Run("make", ["-j" + jobs], native!);
            else
            {
                container = "dnd-build-" + Guid.NewGuid().ToString("N");
                var docker = new List<string> { "run", "--rm", "--init", "--name", container, "--platform", "linux/amd64" };
                if (!OperatingSystem.IsWindows())
                {
                    docker.AddRange(["--user", (await Capture("id", "-u")) + ":" + (await Capture("id", "-g"))]);
                }
                foreach (string variable in new[] { "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy" })
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable))) docker.AddRange(["--env", variable]);
                // Honor the host's explicitly configured trust bundle without
                // disabling TLS verification or copying certificates into outputs.
                var ca = new[] { "GIT_SSL_CAINFO", "SSL_CERT_FILE", "CURL_CA_BUNDLE" }
                    .Select(Environment.GetEnvironmentVariable).FirstOrDefault(path => !string.IsNullOrEmpty(path) && File.Exists(path));
                if (ca != null) docker.AddRange(["--volume", Path.GetFullPath(ca) + ":/tmp/dnd-ca-bundle.pem:ro",
                    "--env", "GIT_SSL_CAINFO=/tmp/dnd-ca-bundle.pem", "--env", "SSL_CERT_FILE=/tmp/dnd-ca-bundle.pem"]);
                docker.AddRange(["--volume", native! + ":/work", "--workdir", "/work", Image, "bash", "-lc", "make -j" + jobs]);
                try { result = await Run("docker", docker, Directory.GetCurrentDirectory(), native); }
                finally { RemoveContainer(); }
            }
            if (result != 0) return result;
            if (cancelled) throw new OperationCanceledException();
            if (!File.Exists(Path.Combine(native!, "Game.dol")) || !File.Exists(Path.Combine(native!, "Game.elf")))
                throw new FileNotFoundException("Native build did not produce both Game.dol and Game.elf.");
            var bundleDebugMap = Path.Combine(native!, "dnd-il-debug.json");
            var bundleAddresses = Path.Combine(native!, "Game.dnd-addresses.json");
            if (debugSymbols && !File.Exists(bundleDebugMap))
                throw new FileNotFoundException("Debug native build did not produce source mapping metadata.", bundleDebugMap);
            if (debugSymbols && !File.Exists(bundleAddresses))
                throw new FileNotFoundException("Debug native build did not produce ELF address metadata.", bundleAddresses);
            foreach (var pair in new[] { ("Game.dol", dol!), ("Game.elf", elf!) })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(pair.Item2)!);
                File.Copy(Path.Combine(native!, pair.Item1), pair.Item2, true);
            }
            if (debugSymbols)
            {
                var publishedDebugMap = Path.ChangeExtension(elf!, ".dnd-debug.json");
                File.Copy(bundleDebugMap, publishedDebugMap, true);
                var publishedAddresses = Path.ChangeExtension(elf!, ".dnd-addresses.json");
                File.Copy(bundleAddresses, publishedAddresses, true);
                Console.WriteLine("Managed debug map: " + publishedDebugMap +
                    "\nPowerPC breakpoint addresses: " + publishedAddresses);
            }
            var user = Path.GetFullPath(Get("user-directory", Path.Combine(Path.GetDirectoryName(native!)!, "dolphin-user")));
            Directory.CreateDirectory(Path.GetDirectoryName(runConfig!)!);
            File.WriteAllText(runConfig!, JsonSerializer.Serialize(new RunConfig(dol!, Get("dolphin"), user), new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("DOL: " + dol + "\nSymbols: " + elf);
            return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.Error.WriteLine("error DND001: " + e.Message);
            return 1;
        }
    }

    static bool Within(string directory, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
        string.Equals(Path.TrimEndingDirectorySeparator(directory), Path.TrimEndingDirectorySeparator(path),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static void Allow(Dictionary<string, string> options, params string[] names)
    {
        foreach (var name in options.Keys)
            if (!names.Contains(name)) throw new ArgumentException("Unknown option: --" + name);
    }

    static string? Locate(string name)
    {
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(name) ? Path.GetFullPath(name) : null;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            foreach (var suffix in OperatingSystem.IsWindows() ? new[] { "", ".exe" } : new[] { "" })
            {
                if (directory.Length == 0) continue;
                string path = Path.Combine(directory.Trim('"'), name + suffix);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    static string RequireExecutable(string name) => Locate(name) ??
        throw new FileNotFoundException("Required tool '" + name + "' not found on PATH. See docs/ide-workflow.md.");

    static string FindDolphin(string configured)
    {
        configured = configured.Length > 0 ? configured : Environment.GetEnvironmentVariable("DOLPHIN_PATH") ?? "";
        if (configured.Length > 0) return Locate(configured) ?? throw new FileNotFoundException("Dolphin executable does not exist: " + configured);
        var candidates = new List<string> { "dolphin-emu", "dolphin-emu-qt2", "Dolphin.exe", "/usr/games/dolphin-emu", "/Applications/Dolphin.app/Contents/MacOS/Dolphin" };
        if (OperatingSystem.IsWindows()) candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dolphin", "Dolphin.exe"));
        foreach (var candidate in candidates) if (Locate(candidate) is { } found) return found;
        throw new FileNotFoundException("Dolphin not found. Install Dolphin and set DOLPHIN_PATH or DolphinPath in DolphinDotNet.local.props to its executable.");
    }

    static async Task<int> Run(string file, IEnumerable<string> arguments, string directory, string? mappedDirectory = null)
    {
        if (cancelled) throw new OperationCanceledException();
        var info = new ProcessStartInfo(RequireExecutable(file)) { WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = mappedDirectory != null, RedirectStandardError = mappedDirectory != null };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start " + file);
        child = process;
        if (cancelled) StopChild();
        try
        {
            Task output = mappedDirectory == null ? Task.CompletedTask : Forward(process.StandardOutput, Console.Out, mappedDirectory);
            Task error = mappedDirectory == null ? Task.CompletedTask : Forward(process.StandardError, Console.Error, mappedDirectory);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            return process.ExitCode;
        }
        finally { child = null; }
    }

    static async Task Forward(StreamReader input, TextWriter output, string directory)
    {
        while (await input.ReadLineAsync() is { } line)
            await output.WriteLineAsync(line.Replace("/work/", Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal));
    }

    static async Task<string> Capture(string file, params string[] arguments)
    {
        if (cancelled) throw new OperationCanceledException();
        var info = new ProcessStartInfo(RequireExecutable(file)) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        child = process;
        try
        {
            if (cancelled) StopChild();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Command failed: " + file);
            return output.Trim();
        }
        finally { child = null; }
    }

    static void StopChild()
    {
        try { if (child is { HasExited: false } process) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { RemoveContainer(); }
    }

    static void RemoveContainer()
    {
        var name = Interlocked.Exchange(ref container, null);
        if (name == null) return;
        try
        {
            var info = new ProcessStartInfo(RequireExecutable("docker"))
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "rm", "--force", name }) info.ArgumentList.Add(arg);
            using var cleanup = Process.Start(info)!;
            if (!cleanup.WaitForExit(5000)) cleanup.Kill(entireProcessTree: true);
        }
        catch (Exception) { /* Daemon shutdown must not hide the build result. */ }
    }
}
