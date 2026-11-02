namespace Dolphin.Numerics;

/// <summary>Bit-preserving numeric reinterpretation supported by GameCube AOT.</summary>
public static class BitConverter
{
    public static float Int32BitsToSingle(int value) =>
        System.BitConverter.ToSingle(System.BitConverter.GetBytes(value), 0);
}
