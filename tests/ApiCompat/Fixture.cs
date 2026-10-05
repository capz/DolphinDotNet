using System;
using System.Collections.Generic;

namespace ContractFixture;

#if IMPLEMENTATION
// Shift definition tokens without changing the public contract.
internal class MetadataNoise { }
#endif

public class Item { }
public class Other { }
internal class HiddenOuter { public class LeakedNested { } }

public class Surface
{
    private int HiddenProperty { get; set; }
    private event Action HiddenEvent { add { } remove { } }
    public Item Field = new();
    public Item Property { get; set; } = new();
    public event Action<Item> Updated { add { } remove { } }
    protected Item Protected(Item value) => value;
    public class Nested { public Item Echo(Item value) => value; }
    public Item Echo(Item value) => value;
    public T Identity<T>(T value) => value;
    public List<Item[]> Arrays(ref Item value, Item[,] grid) => new();
#if IMPLEMENTATION
    public Other Changed(Other value) => value;
    public static int Storage(int value) => value;
    public event Action<string> ChangedEvent { add { } remove { } }
#else
    public Item Changed(Item value) => value;
    public int Storage(int value) => value;
    public event Action<int> ChangedEvent { add { } remove { } }
#endif
}