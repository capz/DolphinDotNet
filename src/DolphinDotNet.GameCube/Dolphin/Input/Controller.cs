using System;

namespace Dolphin.Input;

/// <summary>Cached GameCube input. Update once per game tick, then read snapshots freely.</summary>
public static class Controller
{
    public const int PortCount = 4;
    public const int ButtonStart = 0x1000;
    public static void Update() => NativeInput.InputUpdate();
    public static ControllerState GetState(int port=0)
    {
        ValidatePort(port);
        return new ControllerState(NativeInput.InputFlags(port), NativeInput.InputButtons(port,0),
            NativeInput.InputButtons(port,1), NativeInput.InputButtons(port,2),
            new StickState(NativeInput.InputAxis(port,0), NativeInput.InputAxis(port,1)),
            new StickState(NativeInput.InputAxis(port,2), NativeInput.InputAxis(port,3)),
            NativeInput.InputAxis(port,4), NativeInput.InputAxis(port,5));
    }
    public static void Configure(int port, ControllerOptions options)
    {
        ValidatePort(port);
        if (options==null) throw new ArgumentNullException("options");
        if (!(options.StickDeadZone>=0 && options.StickDeadZone<1 && options.CStickDeadZone>=0 && options.CStickDeadZone<1
            && options.StickMaximum>0 && options.StickMaximum<=127 && options.CStickMaximum>0 && options.CStickMaximum<=127
            && options.TriggerMinimum>=0 && options.TriggerMinimum<options.TriggerMaximum && options.TriggerMaximum<=255))
            throw new ArgumentOutOfRangeException("options");
        NativeInput.InputConfigure(port,options.StickDeadZone,options.CStickDeadZone,options.StickMaximum,
            options.CStickMaximum,options.TriggerMinimum,options.TriggerMaximum);
    }
    /// <summary>Starts or replaces a timed rumble. Returns false if unavailable. Native alarms stop it even during an await.</summary>
    public static bool Rumble(int port, float durationSeconds)
    {
        ValidatePort(port);
        if (!(durationSeconds>0 && durationSeconds<=60)) throw new ArgumentOutOfRangeException("durationSeconds");
        return NativeInput.InputRumble(port,durationSeconds);
    }
    public static void StopRumble(int port=0) { ValidatePort(port); NativeInput.InputStopRumble(port); }
    internal static void ValidatePort(int port)
    { if (port<0 || port>=PortCount) throw new ArgumentOutOfRangeException("port"); }
    /// <summary>Compatibility polling call. Prefer Update followed by GetState for shared snapshots.</summary>
    public static int ReadButtonsDown(int port=0) => throw new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
}
