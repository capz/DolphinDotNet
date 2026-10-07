internal static class Program
{
    private static int Main()
    {
        _ = Shared<int>.Marker();
        _ = Shared<string>.Marker();
        return 0;
    }
}

internal static class Shared<T>
{
    public static int Marker() => 42;
}
