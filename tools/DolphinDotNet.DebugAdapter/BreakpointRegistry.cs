using DolphinDotNet.Debugger;

namespace DolphinDotNet.DebugAdapter;

/// <summary>Desired source sets and actual native writes are tracked independently.</summary>
public sealed class BreakpointRegistry(RspClient client)
{
    public sealed record Entry(int Id, int Line, uint[] Addresses, string? Error);
    private readonly Dictionary<string, Entry[]> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _installed = new();
    private int _nextId;
    public bool Contains(uint pc) => _sources.Values.SelectMany(e => e).Any(e => e.Addresses.Contains(pc));
    public int[] HitIds(uint pc) => _sources.Values.SelectMany(e => e).Where(e => e.Addresses.Contains(pc)).Select(e => e.Id).ToArray();

    public async Task<Entry[]> ReplaceAsync(string source, int[] lines, SourceAddressMap symbols)
    {
        _sources.TryGetValue(source, out var old);
        var entries = lines.Select(line =>
        {
            var id = old?.FirstOrDefault(e => e.Line == line)?.Id ?? ++_nextId;
            try { return new Entry(id, line, symbols.Resolve(source, line).ToArray(), null); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException)
            { return new Entry(id, line, Array.Empty<uint>(), error.Message); }
        }).ToArray();
        _sources[source] = entries;
        await RestoreAsync();
        return entries;
    }

    public async Task SuspendAsync()
    {
        foreach (var address in _installed.ToArray())
        {
            await client.ClearSoftwareBreakpointAsync(address);
            _installed.Remove(address);
        }
    }

    public async Task RestoreAsync()
    {
        var wanted = _sources.Values.SelectMany(e => e).SelectMany(e => e.Addresses).ToHashSet();
        foreach (var address in _installed.Where(a => !wanted.Contains(a)).ToArray())
        {
            await client.ClearSoftwareBreakpointAsync(address);
            _installed.Remove(address);
        }
        foreach (var address in wanted.Order())
        {
            if (_installed.Contains(address)) continue;
            await client.SetSoftwareBreakpointAsync(address);
            _installed.Add(address);
        }
    }
}
