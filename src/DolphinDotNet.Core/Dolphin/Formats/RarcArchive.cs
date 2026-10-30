using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Read-only, bounded RARC member index. Files are addressed by exact archive paths.</summary>
public sealed class RarcArchive
{
    public sealed class Entry
    {
        public string Path { get; }
        public int Length { get; }
        internal readonly int Offset;
        internal Entry(string path, int offset, int length) { Path = path; Offset = offset; Length = length; }
    }

    private readonly byte[] bytes;
    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<string, Entry> lookup = new Dictionary<string, Entry>(StringComparer.Ordinal);
    public IReadOnlyList<Entry> Entries => entries;

    private RarcArchive(byte[] bytes) { this.bytes = bytes; }

    public static async Task<RarcArchive> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        var bytes = await Dolphin.IO.File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(bytes);
    }

    public static RarcArchive Read(byte[] source)
    {
        var bytes = UnwrapYaz0(source);
        if (Ascii(bytes, 0, 4) != "RARC") throw new InvalidDataException("Not an RARC archive.");
        uint declared = U32(bytes, 4);
        if (declared > bytes.Length || declared < 0x40) throw new InvalidDataException("Invalid RARC length.");
        if (U32(bytes, 8) != 0x20) throw new InvalidDataException("Unsupported RARC header layout.");

        long dataBase = 0x20L + U32(bytes, 0xC);
        long metadata = 0x20;
        uint nodeCount = U32(bytes, metadata);
        long nodes = metadata + U32(bytes, metadata + 4);
        uint entryCount = U32(bytes, metadata + 8);
        long fileEntries = metadata + U32(bytes, metadata + 0xC);
        uint stringLength = U32(bytes, metadata + 0x10);
        long stringBase = metadata + U32(bytes, metadata + 0x14);
        if (nodeCount == 0 || nodeCount > 8192 || entryCount > 65535 || stringLength > MaxAssetBytes)
            throw new InvalidDataException("RARC directory count exceeds limits.");
        Check(bytes, nodes, nodeCount * 0x10L);
        Check(bytes, fileEntries, entryCount * 0x14L);
        Check(bytes, stringBase, stringLength);
        Check(bytes, dataBase, 0);

        var archive = new RarcArchive(bytes);
        var visiting = new bool[(int)nodeCount];
        var visited = new bool[(int)nodeCount];
        archive.Visit(0, "", nodes, nodeCount, fileEntries, entryCount, stringBase, stringLength, dataBase, visiting, visited, 0);
        return archive;
    }

    private void Visit(int node, string prefix, long nodes, uint nodeCount, long fileEntries,
        uint entryCount, long strings, uint stringLength, long dataBase, bool[] visiting, bool[] visited, int depth)
    {
        if (depth > 32 || node < 0 || node >= nodeCount || visiting[node] || visited[node])
            throw new InvalidDataException("RARC directory cycle or alias.");
        visiting[node] = true;
        long baseOffset = nodes + node * 0x10L;
        int count = U16(bytes, baseOffset + 0xA);
        long first = U32(bytes, baseOffset + 0xC);
        if (first + count > entryCount) throw new InvalidDataException("RARC node references invalid entries.");

        for (int i = 0; i < count; i++)
        {
            long e = fileEntries + (first + i) * 0x14L;
            uint attributeAndName = U32(bytes, e + 4);
            int flags = (int)(attributeAndName >> 24);
            long nameOff = attributeAndName & 0xFFFFFF;
            if (nameOff >= stringLength) throw new InvalidDataException("RARC filename offset is invalid.");
            string name = NullTerminated(bytes, strings + nameOff, strings + stringLength);
            if (name == "." || name == "..") continue;
            if (name.Length == 0 || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 ||
                name.IndexOf(':') >= 0 || name == "." || name == "..")
                throw new InvalidDataException("Invalid RARC filename.");
            string path = prefix.Length == 0 ? name : prefix + "/" + name;
            if ((flags & 2) != 0)
            {
                uint child = U32(bytes, e + 8);
                if (child >= nodeCount) throw new InvalidDataException("RARC child is out of range.");
                Visit((int)child, path, nodes, nodeCount, fileEntries, entryCount,
                    strings, stringLength, dataBase, visiting, visited, depth + 1);
            }
            else
            {
                long offset = dataBase + U32(bytes, e + 8);
                uint size = U32(bytes, e + 0xC);
                Check(bytes, offset, size);
                if (size > MaxAssetBytes || offset + size > U32(bytes, 4))
                    throw new InvalidDataException("RARC member exceeds declared archive bounds.");
                var entry = new Entry(path, (int)offset, (int)size);
                if (lookup.ContainsKey(path)) throw new InvalidDataException("Duplicate RARC member path.");
                lookup.Add(path, entry);
                entries.Add(entry);
            }
        }
        visiting[node] = false;
        visited[node] = true;
    }

    public bool TryGetEntry(string path, out Entry entry)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        return lookup.TryGetValue(path, out entry!);
    }

    /// <summary>Open a seekable, read-only view over a raw RARC member without
    /// allocating a second member-sized buffer. The stream retains the archive
    /// backing array and cannot expose or modify it. Compressed members remain
    /// compressed; use ReadFile(path, true) for bounded Yaz0 expansion.</summary>
    public Stream OpenMemberStream(string path)
    {
        if (!TryGetEntry(path, out var entry))
            throw new FileNotFoundException("RARC member not found.", path);
        return new MemoryStream(bytes, entry.Offset, entry.Length, false, false);
    }

    public byte[] ReadFile(string path, bool decompressYaz0 = false)
    {
        if (!TryGetEntry(path, out var entry)) throw new FileNotFoundException("RARC member not found.", path);
        var data = Slice(bytes, entry.Offset, entry.Length);
        return decompressYaz0 ? UnwrapYaz0(data) : data;
    }

    public byte[] ReadFile(Entry entry, bool decompressYaz0 = false)
    {
        if (entry == null || !lookup.TryGetValue(entry.Path, out var own) || !ReferenceEquals(entry, own))
            throw new ArgumentException("Entry does not belong to this archive.", nameof(entry));
        return ReadFile(entry.Path, decompressYaz0);
    }
}
