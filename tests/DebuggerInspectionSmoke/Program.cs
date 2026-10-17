internal class Counter
{
    public int Count;
    public byte Flag;
}

internal sealed class Box : Counter
{
    public string Name = "";
    public Box? Next;
    public double Fraction;
    public short Delta;
    public int Read() => Count + Name.Length + (Next == null ? 0 : Next.Count);
}

internal static class Program
{
    private static int _result;
    private static int Inspect(Box box, string text)
    {
        Box local = box;
        string caption = text;
        return local.Read() + caption.Length;
    }

    private static void Main()
    {
        Box box = new Box { Count = 7, Flag = 1, Name = "Snake", Fraction = 1.5, Delta = -2 };
        box.Next = box;
        _result = Inspect(box, "hello");
        while (true) _result = (_result + 1) & 1023;
    }
}
