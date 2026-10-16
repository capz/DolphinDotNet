using System.Text.Json;

// A real child executable for launcher integration tests; never emulates a DOL.
File.WriteAllText(Environment.GetEnvironmentVariable("DND_PROBE_OUTPUT")!,
    JsonSerializer.Serialize(new { Arguments = args, Pid = Environment.ProcessId }));
if (Environment.GetEnvironmentVariable("DND_PROBE_WAIT") == "true")
    await Task.Delay(TimeSpan.FromMinutes(1));
return int.Parse(Environment.GetEnvironmentVariable("DND_PROBE_EXIT") ?? "0");
