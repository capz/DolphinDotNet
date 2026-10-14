namespace Dolphin.Input;

/// <summary>Per-port processing applied on the next update. Calibrate maximum travel for your controller.</summary>
public sealed class ControllerOptions
{
    public float StickDeadZone { get; set; } = 0.15f;
    public float CStickDeadZone { get; set; } = 0.15f;
    public float StickMaximum { get; set; } = 80;
    public float CStickMaximum { get; set; } = 65;
    public float TriggerMinimum { get; set; } = 30;
    public float TriggerMaximum { get; set; } = 180;
}
