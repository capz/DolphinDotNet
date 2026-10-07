Console.WriteLine("Hello from real C#!");
Console.WriteLine("Running through DolphinDotNet.");
Console.WriteLine(40 + 2);

// Both closed types intentionally resolve to one Shared<T> method body in AOT.
Shared<int>.WriteMarker();
Shared<string>.WriteMarker();

static class Shared<T>
{
    public static void WriteMarker() => Console.WriteLine("Shared generic AOT body.");
}
