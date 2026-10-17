using DolphinDotNet.Debugger;

namespace DolphinDotNet.DebugAdapter;

/// <summary>Desired source sets and actual native writes are tracked independently.</summary>
public sealed class BreakpointRegistry(RspClient client)
{
    public sealed record Entry(int Id, int Line, uint[] Addresses, string? Error);
    private readonly Dictionary<string, Entry[]> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _installed = new();
    private int _nextId;
    private Dictionary<uint, int[]> _wanted = new();
    public int VerifiedCount => _sources.Values.SelectMany(entries => entries).Count(entry => entry.Error is null);
    public int NativeAddressCount => _wanted.Count;
    public bool Contains(uint pc) => _wanted.ContainsKey(pc);
    public int[] HitIds(uint pc) => _wanted.TryGetValue(pc, out var ids) ? ids.ToArray() : Array.Empty<int>();

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
        _wanted = _sources.Values.SelectMany(e => e).SelectMany(e => e.Addresses.Select(address => (address, e.Id)))
            .GroupBy(p => p.address).ToDictionary(g => g.Key, g => g.Select(p => p.Id).Distinct().ToArray());
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
        var wanted = _wanted.Keys.ToHashSet();
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
