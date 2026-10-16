using DolphinDotNet.DebugAdapter;

if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: dotnet DolphinDotNet.DebugAdapter.dll (DAP over stdin/stdout)");
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
