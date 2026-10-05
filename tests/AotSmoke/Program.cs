namespace AotSmoke;
internal sealed class Counter
{
    private int value;
    public Counter(int initial) { value = initial; }
    public int Increment(int amount) { value = value + amount; return value; }
}
internal static class Program
{
    private static int Main()
    {
        var counter = new Counter(4);
        return counter.Increment(3);
    }
}
