using System.Text.Json;

// A fake external toolchain lets the real build driver exercise preflight,
// owned-directory cleanup and publication without Docker or devkitPPC.
if (Environment.GetEnvironmentVariable("DND_PROBE_TOOLCHAIN") == "true")
{
    if (args.Length > 0 && args[0] == "info") { Console.WriteLine("linux"); return 0; }
    if (args.Length > 2 && args[0] == "--bundle")
    {
        Directory.CreateDirectory(args[2]);
        File.WriteAllBytes(Path.Combine(args[2], "Game.dol"), new byte[256]);
        File.WriteAllText(Path.Combine(args[2], "Game.elf"), "probe ELF");
        File.WriteAllText(Path.Combine(args[2], "dnd-il-debug.json"), "{}");
        File.WriteAllText(Path.Combine(args[2], "Game.dnd-addresses.json"), "{}");
        return 0;
    }
    return args.Length > 0 && args[0] == "run" ? 0 : 1;
}

// A real child executable for launcher integration tests; never emulates a DOL.
File.WriteAllText(Environment.GetEnvironmentVariable("DND_PROBE_OUTPUT")!,
    JsonSerializer.Serialize(new { Arguments = args, Pid = Environment.ProcessId }));
if (Environment.GetEnvironmentVariable("DND_PROBE_WAIT") == "true")
    await Task.Delay(TimeSpan.FromMinutes(1));
return int.Parse(Environment.GetEnvironmentVariable("DND_PROBE_EXIT") ?? "0");
