using System.Globalization;

namespace DolphinDotNet.Debugger;

/// <summary>A remote target's stop, exit or termination reply.</summary>
/// <remarks>The raw stop reply is retained because Dolphin and libogc2 have different extensions.</remarks>
public sealed record RspStop(int Signal, uint? ProgramCounter, bool Exited, string Raw)
{
    public static RspStop Parse(string reply)
    {
        if (reply.Length < 3 || reply[0] is not ('T' or 'S' or 'W' or 'X') ||
            !int.TryParse(reply.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var signal))
            throw new InvalidDataException("Not a GDB stop/exit packet: " + reply);
        if (reply[0] is 'W' or 'X')
            return new RspStop(signal, null, true, reply);
        if (reply[0] == 'S')
            return new RspStop(signal, null, false, reply);

        uint? pc = null;
        // PowerPC GDB register 0x40 is the instruction address. Some stubs
        // respond with Txx40:xxxxxxxx; others send only Sxx.
        foreach (var field in reply[3..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!field.StartsWith("40:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!uint.TryParse(field.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("Invalid PowerPC stop PC: " + field);
            pc = value;
            break;
        }
        return new RspStop(signal, pc, false, reply);
    }
}
