internal static class Program
{
    private static int _result;

    private static int Main()
    {
        _result = Outer(3);
        while (true) _result = (_result + 1) & 1023;
    }

    private static int Outer(int value)
    {
        int first = Inner(value);
        int second = Inner(first);
        return second + 1;
    }

    private static int Inner(int value)
    {
        return value + 1;
    }
}
