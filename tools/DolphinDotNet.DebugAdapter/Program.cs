using DolphinDotNet.DebugAdapter;
using DolphinDotNet.Debugger;
using System.Text.Json;

if (args is ["--list-serial-ports"])
{
    Console.WriteLine(JsonSerializer.Serialize(RspClient.SerialPortNames().Order(StringComparer.Ordinal)));
    return 0;
}

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: dotnet DolphinDotNet.DebugAdapter.dll [--list-serial-ports] (otherwise DAP over stdin/stdout)");
    return 2;
}
try
{
    var connection = new DapConnection(Console.OpenStandardInput(), Console.OpenStandardOutput());
    await using var session = new DebugSession(connection);
    await session.RunAsync();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
