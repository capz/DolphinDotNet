namespace Dolphin.Input;

/// <summary>Physical GameCube buttons. L/R are digital clicks, not analog travel.</summary>
public enum ControllerButton
{
    DPadLeft=0x0001, DPadRight=0x0002, DPadDown=0x0004, DPadUp=0x0008,
    Z=0x0010, R=0x0020, L=0x0040, A=0x0100, B=0x0200, X=0x0400, Y=0x0800, Start=0x1000
}
