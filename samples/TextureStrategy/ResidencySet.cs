using System;

// Ownership metadata only. The scene owns/disposes the corresponding GX textures.
// Wanted resources are protected; a full visible set falls back instead of thrashing.
internal sealed class ResidencySet
{
    internal const int Budget = 192 * 1024;
    internal readonly int[] Ids = new int[48], Touched = new int[48];
    internal int Capacity { get; private set; }
    internal int SlotBytes { get; private set; }
    internal int Count { get; private set; }
    internal int Bytes => Count * SlotBytes;
    internal ResidencySet(bool atlas) { Reset(atlas); }
    internal void Reset(bool atlas)
    {
        Capacity = atlas ? 3 : 48;
        SlotBytes = atlas ? 65536 : 4096;
        for (int i = 0; i < Ids.Length; i++) { Ids[i] = -1; Touched[i] = 0; }
        Count = 0;
    }
    internal int Find(int id)
    {
        for (int i = 0; i < Capacity; i++) if (Ids[i] == id) return i;
        return -1;
    }
    internal static bool Contains(int[] ids, int count, int id)
    {
        for (int i = 0; i < count; i++) if (ids[i] == id) return true;
        return false;
    }
    internal int Victim(int[] wanted, int count)
    {
        int victim = -1;
        for (int i = 0; i < Capacity; i++)
        {
            if (Ids[i] < 0) return i;
            if (!Contains(wanted, count, Ids[i]) && (victim < 0 || Touched[i] < Touched[victim]))
                victim = i;
        }
        return victim;
    }
    internal void Remove(int slot)
    {
        if (slot < 0 || slot >= Capacity) throw new ArgumentOutOfRangeException("slot");
        if (Ids[slot] >= 0) { Ids[slot] = -1; Count--; }
    }
    internal void Commit(int slot, int id, int frame)
    {
        if (slot < 0 || slot >= Capacity || id < 0 || Ids[slot] >= 0 || Find(id) >= 0)
            throw new InvalidOperationException("Invalid residency ownership transition.");
        Ids[slot] = id; Touched[slot] = frame; Count++;
        if (Bytes > Budget) throw new InvalidOperationException("Detail cache exceeded its byte budget.");
    }
}
