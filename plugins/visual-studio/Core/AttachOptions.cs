using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DolphinDotNet.VisualStudio;

/// <summary>IDE-independent target/artifact configuration, also exercised by host tests.</summary>
public sealed class AttachOptions
{
    public string BuildFolder { get; set; } = "";
    public string Transport { get; set; } = "tcp";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2166;
    public string SerialPort { get; set; } = "COM3";
    public int BaudRate { get; set; } = 115200;
    public int TimeoutSeconds { get; set; } = 10;
    public string SourceFileMapJson { get; set; } = "{}";
    public string HeartbeatHost { get; set; } = "";
    public int HeartbeatPort { get; set; } = 2167;

    public JObject CreateLaunch(string adapter)
    {
        if (!File.Exists(adapter)) throw new FileNotFoundException("The extension is missing its debug adapter. Reinstall the VSIX.", adapter);
        if (TimeoutSeconds < 1 || TimeoutSeconds > 120) throw new ArgumentException("Timeout must be between 1 and 120 seconds.");
        if (!Directory.Exists(BuildFolder)) throw new ArgumentException("Choose an existing Debug build folder.");
        var files = Directory.GetFiles(BuildFolder, "*.elf");
        if (files.Length != 1) throw new ArgumentException("The Debug build folder must contain exactly one ELF file.");
        var elf = Path.GetFullPath(files[0]);
        var stem = Path.GetFileNameWithoutExtension(elf);
        var folder = Path.GetDirectoryName(elf)!;
        var source = Path.Combine(folder, stem + ".dnd-debug.json");
        if (!File.Exists(source)) source = Path.Combine(folder, "dnd-il-debug.json");
        var addresses = Path.Combine(folder, stem + ".dnd-addresses.json");
        if (!File.Exists(source) || !File.Exists(addresses))
            throw new ArgumentException("Rebuild Debug: the ELF needs its matching .dnd-debug.json (or dnd-il-debug.json) and " + stem + ".dnd-addresses.json.");
        var mappings = JObject.Parse(SourceFileMapJson);
        foreach (var mapping in mappings.Properties())
            if (string.IsNullOrWhiteSpace(mapping.Name) || mapping.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)mapping.Value))
                throw new ArgumentException("Source mappings must be nonempty build-prefix to local-prefix strings.");
        var result = new JObject {
            ["$adapter"] = Path.GetFullPath(adapter),
            ["$localsScopeName"] = "Locals",
            ["$suppressModulesRequestOnAttach"] = true,
            ["$languageMappings"] = new JObject { ["C#"] = new JObject {
                ["languageId"] = "3F5162F8-07C6-11D3-9053-00C04FA302A1", ["extensions"] = new JArray("cs") } },
            ["type"] = "dolphindotnet", ["request"] = "attach", ["name"] = "DolphinDotNet",
            ["elf"] = elf, ["sourceMap"] = source, ["addressMap"] = addresses,
            ["sourceFileMap"] = mappings, ["transport"] = Transport, ["timeoutSeconds"] = TimeoutSeconds
        };
        if (Transport == "tcp")
        {
            if (string.IsNullOrWhiteSpace(Host) || Port < 1 || Port > 65535) throw new ArgumentException("Specify a host and TCP port between 1 and 65535.");
            result["host"] = Host.Trim();result["port"] = Port;
        }
        else if (Transport == "usbGecko")
        {
            if (string.IsNullOrWhiteSpace(SerialPort) || BaudRate <= 0) throw new ArgumentException("Specify a serial port and positive driver baud rate.");
            result["serialPort"] = SerialPort.Trim();result["baudRate"] = BaudRate;
        }
        else throw new ArgumentException("Choose TCP or USB Gecko.");
        if (!string.IsNullOrWhiteSpace(HeartbeatHost))
        {
            var parts = HeartbeatHost.Trim().Split('.');
            if (parts.Length != 4 || parts.Any(p => !byte.TryParse(p, out _)) ||
                byte.Parse(parts[0]) == 0 || byte.Parse(parts[0]) >= 224 || HeartbeatPort < 0 || HeartbeatPort > 65535)
                throw new ArgumentException("Live status needs this computer's unicast IPv4 address and a UDP port from 0 to 65535.");
            result["heartbeatHost"] = HeartbeatHost.Trim();result["heartbeatPort"] = HeartbeatPort;
        }
        return result;
    }

    public static AttachOptions Load(string path) => File.Exists(path)
        ? JsonConvert.DeserializeObject<AttachOptions>(File.ReadAllText(path)) ?? new AttachOptions() : new AttachOptions();
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
