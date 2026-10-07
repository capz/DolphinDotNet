_ = Shared<int>.Marker();
_ = Shared<string>.Marker();

static class Shared<T>
{
    public static int Marker() => 42;
}
