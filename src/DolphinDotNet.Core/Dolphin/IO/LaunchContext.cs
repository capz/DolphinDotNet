using System;
namespace Dolphin.IO;

/// <summary>Incoming libogc launcher arguments captured before managed startup.
/// Available on GameCube AOT. Missing or invalid argument blocks produce an
/// empty context; the runtime never infers a path from the working directory.
/// The snapshot is bounded to 64 arguments and 4096 UTF-8 bytes.</summary>
public static class LaunchContext
{
    public static int ArgumentCount=>NativeDol.LaunchArgumentCount();
    /// <summary>argv[0], or an empty string when no executable path was supplied.
    /// This is a launcher claim, not proof that a file or mount is trustworthy.</summary>
    public static string ExecutablePath=>ArgumentCount==0?"":GetArgument(0);
    public static string GetArgument(int index)
    {
        if((uint)index>=ArgumentCount)throw new ArgumentOutOfRangeException("index");
        return NativeDol.LaunchArgument(index);
    }
    public static string[] GetArguments()
    {
        var values=new string[ArgumentCount];
        for(int i=0;i<values.Length;i++)values[i]=GetArgument(i);
        return values;
    }
}
