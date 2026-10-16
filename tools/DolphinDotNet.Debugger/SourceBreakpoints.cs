namespace DolphinDotNet.Debugger;

/// <summary>Owns a deduplicated set of native breakpoints. Use only while the target is stopped.</summary>
public sealed class SourceBreakpoints(RspClient client, SourceAddressMap symbols)
{
    private readonly HashSet<uint> _installed = new();
    public IReadOnlyCollection<uint> Installed => _installed.ToArray();

    public async Task<IReadOnlyList<uint>> AddAsync(string document, int line, CancellationToken token = default)
    {
        var addresses = symbols.Resolve(document, line);
        foreach (var address in addresses)
        {
            if (_installed.Contains(address)) continue;
            await client.SetSoftwareBreakpointAsync(address, token).ConfigureAwait(false);
            // Keep successful writes tracked even if a later target operation fails.
            _installed.Add(address);
        }
        return addresses;
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        foreach (var address in _installed.ToArray())
        {
            await client.ClearSoftwareBreakpointAsync(address, token).ConfigureAwait(false);
            _installed.Remove(address);
        }
    }
}
