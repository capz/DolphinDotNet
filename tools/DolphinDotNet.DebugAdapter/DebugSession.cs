using System.Text.Json;
using DolphinDotNet.Debugger;

namespace DolphinDotNet.DebugAdapter;

/// <summary>Single all-stop target. Only the execution task reads RSP while running.</summary>
public sealed class DebugSession(DapConnection connection) : IAsyncDisposable
{
    private RspClient? _client;
    private SourceAddressMap? _symbols;
    private SourcePathMapper? _paths;
    private BreakpointRegistry? _breakpoints;
    private RspStop? _stop;
    private Task? _execution;
    private volatile bool _running;
    private bool _initialized, _configured, _closed, _pauseRequested;
    private int _lineBase = 1, _columnBase = 1, _nextFrame;
    private bool _uriPaths;
    private sealed record FrameState(NativeStackFrame Frame, bool TopFrame, uint Lookup);
    private readonly Dictionary<int, FrameState> _frames = new();
    private readonly Dictionary<int, (FrameState Frame, DebugVariable[] Variables)> _variableScopes = new();
    private int _nextVariable;
    private readonly CancellationTokenSource _lifetime = new();

    public async Task RunAsync()
    {
        try
        {
            while (!_closed)
            {
                using var document = await connection.ReadAsync();
                if (document is null) break;
                var request = document.RootElement;
                if (request.GetProperty("type").GetString() != "request")
                    throw new InvalidDataException("Expected a DAP request.");
                try { await DispatchAsync(request); }
                catch (Exception error)
                {
                    await connection.RespondAsync(request, error: error.Message);
                    if (error is IOException or OperationCanceledException && _client is not null)
                    {
                        await _client.DisposeAsync();
                        _client = null;
                        await connection.EventAsync("terminated");
                    }
                }
            }
        }
        finally { await DisposeAsync(); }
    }

    private async Task DispatchAsync(JsonElement request)
    {
        var command = request.GetProperty("command").GetString();
        var args = request.TryGetProperty("arguments", out var arguments) ? arguments : JsonSerializer.SerializeToElement(new { });
        if (command == "initialize")
        {
            if (_initialized) throw new InvalidOperationException("Already initialized.");
            var format = args.TryGetProperty("pathFormat", out var f) ? f.GetString() : "path";
            if (format is not ("path" or "uri")) throw new ArgumentException("Unsupported pathFormat.");
            _uriPaths = format == "uri";
            _lineBase = args.TryGetProperty("linesStartAt1", out var lines) && !lines.GetBoolean() ? 0 : 1;
            _columnBase = args.TryGetProperty("columnsStartAt1", out var cols) && !cols.GetBoolean() ? 0 : 1;
            _initialized = true;
            await connection.RespondAsync(request, new
            {
                supportsConfigurationDoneRequest = true,
                supportsTerminateDebuggee = false, supportsSuspendDebuggee = false,
                exceptionBreakpointFilters = Array.Empty<object>()
            });
            return;
        }
        if (!_initialized) throw new InvalidOperationException("Initialize first.");
        if (command == "disconnect")
        {
            await CloseTargetAsync();
            _closed = true;
            await connection.RespondAsync(request);
            await connection.EventAsync("terminated");
            return;
        }
        if (command == "attach")
        {
            if (_client is not null) throw new InvalidOperationException("Already attached.");
            _configured = false;
            _frames.Clear();
            _variableScopes.Clear();
            _symbols = SourceAddressMap.Load(Required(args, "elf"), Required(args, "sourceMap"), Required(args, "addressMap"));
            _paths = new SourcePathMapper(args);
            var transport = AttachTransport.Parse(args);
            using var timeout = new CancellationTokenSource(transport.Timeout);
            _client = transport.Kind == "usbGecko"
                ? RspClient.ConnectUsbGecko(transport.SerialPort!, transport.BaudRate, transport.Timeout)
                : await RspClient.ConnectTcpAsync(transport.Host!, transport.Port, transport.Timeout, timeout.Token);
            try
            {
                _stop = await new SourceStepper(_client, _symbols).ReadProgramCounterAsync(await _client.QueryStopAsync());
                if (_stop.Exited) throw new InvalidOperationException("Target has exited.");
                _breakpoints = new BreakpointRegistry(_client);
            }
            catch { await _client.DisposeAsync(); _client = null; throw; }
            await connection.RespondAsync(request);
            await connection.EventAsync("initialized");
            return;
        }
        if (_client is null || _symbols is null || _breakpoints is null)
            throw new InvalidOperationException("Attach first.");
        if (command == "pause")
        {
            ThreadId(args);
            if (!_running) throw new InvalidOperationException("Target is already stopped.");
            _pauseRequested = true;
            await _client.InterruptAsync();
            await connection.RespondAsync(request);
            return;
        }
        if (_running) throw new InvalidOperationException("Target is running; pause before inspecting or changing breakpoints.");
        switch (command)
        {
            case "configurationDone":
                if (_configured) throw new InvalidOperationException("Already configured.");
                _configured = true;
                await connection.RespondAsync(request);
                await StoppedAsync("entry", "Attached; target is paused. Press Resume to run to a source breakpoint. Native startup code may not have a managed stack yet.");
                break;
            case "setBreakpoints":
                var path = Required(args.GetProperty("source"), "path");
                if (_uriPaths) path = new Uri(path).LocalPath;
                path = _paths!.ToBuild(path);
                var points = args.TryGetProperty("breakpoints", out var bps) ? bps.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
                if (points.Any(p => p.TryGetProperty("condition", out _) || p.TryGetProperty("hitCondition", out _) || p.TryGetProperty("logMessage", out _)))
                    throw new NotSupportedException("Conditional breakpoints and logpoints are not supported.");
                var entries = await _breakpoints.ReplaceAsync(path, points.Select(p => checked(p.GetProperty("line").GetInt32() + 1 - _lineBase)).ToArray(), _symbols);
                await connection.RespondAsync(request, new { breakpoints = entries.Select(e => new { id = e.Id, verified = e.Error is null, line = e.Line - 1 + _lineBase, message = e.Error }).ToArray() });
                break;
            case "setExceptionBreakpoints":
                if (new[] { "filters", "filterOptions", "exceptionOptions" }.Any(name =>
                    args.TryGetProperty(name, out var options) && options.GetArrayLength() != 0))
                    throw new NotSupportedException("Managed exception breakpoints are not supported.");
                await connection.RespondAsync(request, new { breakpoints = Array.Empty<object>() });
                break;
            case "threads":
                await connection.RespondAsync(request, new { threads = new[] { new { id = 1, name = "PowerPC target (all-stop)" } } });
                break;
            case "stackTrace":
                ThreadId(args);
                var trace = await new PowerPcUnwinder(_client, _symbols).ReadStackAsync(_stop!.ProgramCounter!.Value);
                var start = args.TryGetProperty("startFrame", out var sf) ? sf.GetInt32() : 0;
                var levels = args.TryGetProperty("levels", out var lv) ? lv.GetInt32() : 0;
                if (start < 0 || levels < 0) throw new ArgumentException("Invalid stack pagination.");
                var frames = trace.Frames.Skip(start).Take(levels == 0 ? int.MaxValue : levels).Select((frame, index) =>
                {
                    var id = ++_nextFrame;
                    var top = start + index == 0;
                    _frames.Add(id, new(frame, top, top ? frame.ProgramCounter : frame.ProgramCounter >= 4 ? frame.ProgramCounter - 4 : 0));
                    var source = frame.SourceLocations.Count == 1 ? frame.SourceLocations[0] : null;
                    var local = source is null ? null : _paths!.ToLocal(source.Document);
                    return new
                    {
                        id, name = frame.Method?.Name ?? $"native 0x{frame.ProgramCounter:x8}",
                        source = local is null ? null : new { name = Path.GetFileName(local), path = _uriPaths ? new Uri(Path.GetFullPath(local)).AbsoluteUri : local },
                        line = source is null ? 0 : source.Line - 1 + _lineBase, column = source is null ? 0 : _columnBase,
                        instructionPointerReference = $"0x{frame.ProgramCounter:x8}"
                    };
                }).ToArray();
                await connection.RespondAsync(request, new { stackFrames = frames, totalFrames = trace.Frames.Count });
                if (trace.Reason != UnwindStopReason.EndOfStack)
                    await connection.EventAsync("output", new { category = "console", output = $"Partial stack: {trace.Reason}\n" });
                break;
            case "scopes":
                if (!_frames.TryGetValue(args.GetProperty("frameId").GetInt32(), out var frameState)) throw new ArgumentException("Unknown or stale frameId.");
                var scopes = _symbols.FindVariables(frameState.Lookup).GroupBy(v => v.Group).Select(group =>
                {
                    var id = ++_nextVariable;
                    var variables = group.ToArray();
                    _variableScopes.Add(id, (frameState, variables));
                    return new { name = group.Key, variablesReference = id, namedVariables = variables.Length, expensive = false,
                        presentationHint = group.Key == "Arguments" ? "arguments" : "locals" };
                }).ToArray();
                await connection.RespondAsync(request, new { scopes });
                break;
            case "variables":
                if (!_variableScopes.TryGetValue(args.GetProperty("variablesReference").GetInt32(), out var scope))
                    throw new ArgumentException("Unknown or stale variablesReference.");
                var first = args.TryGetProperty("start", out var begin) ? begin.GetInt32() : 0;
                var count = args.TryGetProperty("count", out var amount) ? amount.GetInt32() : 0;
                if (first < 0 || count < 0) throw new ArgumentException("Invalid variable pagination.");
                var selected = args.TryGetProperty("filter", out var filter) && filter.GetString() == "indexed"
                    ? Array.Empty<DebugVariable>() : scope.Variables.Skip(first).Take(count == 0 ? int.MaxValue : count).ToArray();
                var values = new List<object>();
                foreach (var variable in selected)
                {
                    var value = await new PrimitiveVariableReader(_client).ReadAsync(variable, scope.Frame.Frame, scope.Frame.TopFrame, _lifetime.Token);
                    values.Add(new { name = value.Name, type = value.Type, value = value.Value, variablesReference = 0,
                        presentationHint = new { attributes = new[] { "readOnly" }, kind = "data" } });
                }
                await connection.RespondAsync(request, new { variables = values });
                break;
            case "continue":
            case "stepIn":
            case "next":
            case "stepOut":
                ThreadId(args);
                if (!_configured) throw new InvalidOperationException("Finish configuration first.");
                _frames.Clear();
                _variableScopes.Clear();
                _running = true;
                _pauseRequested = false;
                await connection.RespondAsync(request, command == "continue" ? new { allThreadsContinued = true } : null);
                await connection.EventAsync("continued", new { threadId = 1, allThreadsContinued = true });
                _execution = ExecuteAsync(command);
                break;
            default:
                throw new NotSupportedException($"Unsupported DAP request: {command}. Only read-only primitive inspection is supported; launch and evaluation are not implemented.");
        }
    }

    private async Task ExecuteAsync(string command)
    {
        string reason = "step";
        string? description = null;
        try
        {
            var stepper = new SourceStepper(_client!, _symbols!, _breakpoints!.Contains);
            await _breakpoints.SuspendAsync();
            if (command == "continue")
            {
                // Step off an installed breakpoint before resuming so it cannot retrigger.
                var steppedOff = _stop!.ProgramCounter is uint pc && _breakpoints.Contains(pc);
                if (steppedOff)
                    _stop = await stepper.ReadProgramCounterAsync(await _client!.StepInstructionAsync(_lifetime.Token), _lifetime.Token);
                await _breakpoints.RestoreAsync();
                if (!_stop!.Exited && (!steppedOff || _stop.Signal == 5))
                    _stop = await stepper.ReadProgramCounterAsync(await _client!.ContinueAsync(_lifetime.Token), _lifetime.Token);
                reason = _pauseRequested ? "pause" : _stop.ProgramCounter is uint stoppedPc && _breakpoints.Contains(stoppedPc) ? "breakpoint" : "exception";
            }
            else
            {
                var result = command switch
                {
                    "stepIn" => await stepper.StepIntoAsync(_stop!, token: _lifetime.Token),
                    "next" => await stepper.StepOverAsync(_stop!, token: _lifetime.Token),
                    _ => await stepper.StepOutAsync(_stop!, token: _lifetime.Token)
                };
                _stop = result.Stop;
                reason = result.Reason == SourceStepReason.UserBreakpoint ? "breakpoint" : _pauseRequested ? "pause" : result.Reason == SourceStepReason.Signal ? "exception" : "step";
                description = result.Reason.ToString();
                if (!_stop.Exited) await _breakpoints.RestoreAsync();
            }
            _running = false;
            if (_stop!.Exited)
            {
                _configured = false;
                await connection.EventAsync("terminated");
            }
            else await StoppedAsync(reason, description);
        }
        catch (Exception error)
        {
            // A timed-out/corrupt RSP operation may leave an unread reply. Never
            // advertise a usable stopped session or issue new commands on it.
            await _client!.DisposeAsync();
            _client = null;
            _running = false;
            await connection.EventAsync("output", new { category = "stderr", output = $"Debug session ended: {error.Message}\n" });
            await connection.EventAsync("terminated");
        }
    }

    private Task StoppedAsync(string reason, string? description = null)
        => connection.EventAsync("stopped", new
        {
            reason, description, threadId = 1, allThreadsStopped = true,
            hitBreakpointIds = reason == "breakpoint" && _stop?.ProgramCounter is uint pc ? _breakpoints!.HitIds(pc) : Array.Empty<int>()
        });

    private async Task CloseTargetAsync()
    {
        try
        {
            if (_running && _client is not null)
            {
                await _client.InterruptAsync();
                if (_execution is not null) await _execution.WaitAsync(TimeSpan.FromSeconds(3));
            }
            if (_client is not null && _stop is { Exited: false }) await _breakpoints!.SuspendAsync();
        }
        finally
        {
            _lifetime.Cancel();
            if (_client is not null) await _client.DisposeAsync();
            if (_execution is not null) { try { await _execution; } catch { } }
            _client = null;
        }
    }

    private static string Required(JsonElement args, string name)
        => args.TryGetProperty(name, out var value) && value.GetString() is string text && !string.IsNullOrWhiteSpace(text)
            ? text : throw new ArgumentException($"{name} is required.");
    private static void ThreadId(JsonElement args)
    { if (args.GetProperty("threadId").GetInt32() != 1) throw new ArgumentException("Only threadId 1 is supported."); }
    public async ValueTask DisposeAsync()
    {
        try { await CloseTargetAsync(); }
        catch (Exception error) { Console.Error.WriteLine("Debugger cleanup: " + error.Message); }
    }
}
