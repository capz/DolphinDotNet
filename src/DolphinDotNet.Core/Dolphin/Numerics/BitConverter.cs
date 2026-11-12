namespace Dolphin.Numerics;

/// <summary>Bit-preserving numeric reinterpretation supported by GameCube AOT.</summary>
public static class BitConverter
{
    public static int SingleToInt32Bits(float value) =>
        global::System.BitConverter.ToInt32(global::System.BitConverter.GetBytes(value), 0);
    public static float Int32BitsToSingle(int value) =>
        global::System.BitConverter.ToSingle(global::System.BitConverter.GetBytes(value), 0);
}
