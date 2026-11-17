using System;
using Dolphin.IO;
internal static class Program
{
    private static int Main()
    {
        var args=LaunchContext.GetArguments();
        if(LaunchContext.ArgumentCount==0)return args.Length==0&&LaunchContext.ExecutablePath==""?0:1;
        if(args.Length!=2||LaunchContext.ExecutablePath!="cardb:/apps/example/default.dol"||args[0]!=LaunchContext.GetArgument(0)||args[1]!="caption=\u00fc")return 2;
        try{LaunchContext.GetArgument(2);return 3;}catch(ArgumentOutOfRangeException){}
        return 0;
    }
}
