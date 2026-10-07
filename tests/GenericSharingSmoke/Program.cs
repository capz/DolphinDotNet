Shared<int>.WriteMarker();
Shared<string>.WriteMarker();

static class Shared<T>
{
    public static void WriteMarker() => Console.WriteLine("Shared generic AOT body.");
}
