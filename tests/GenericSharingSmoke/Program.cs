internal static class Program
{
    private static int Main()
    {
        _ = Shared<int>.Marker();
        _ = Shared<string>.Marker();
        _ = Identity(7);
        _ = Identity("reference");
        _ = Identity(9L);
        return 0;
    }

    private static T Identity<T>(T value) => value;
}

internal static class Shared<T>
{
    public static int Marker() => 42;
}
