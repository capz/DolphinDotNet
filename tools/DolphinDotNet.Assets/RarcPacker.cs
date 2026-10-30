using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dolphin.Formats;

namespace DolphinDotNet.Assets;

/// <summary>Desktop-only RARC writer for bounded, uncompressed archive creation.</summary>
internal static class RarcPacker
{
    private sealed class Node
    {
        internal string Name = "";
        internal int Parent;
        internal List<Entry> Entries = new();
    }
    private sealed class Entry
    {
        internal string Name = "";
        internal int Child = -1;
        internal byte[]? Data;
        internal int Offset;
    }

    private static int Align(int value) => checked((value + 31) & ~31);
    private static ushort Hash(string s)
    {
        int hash = 0;
        foreach (char c in s) hash = (hash * 3 + c) & 0xffff;
        return (ushort)hash;
    }
    private static void W16(byte[] b, int at, int x) { b[at] = (byte)(x >> 8); b[at + 1] = (byte)x; }
    private static void W32(byte[] b, int at, int x)
    { b[at] = (byte)(x >> 24); b[at + 1] = (byte)(x >> 16); b[at + 2] = (byte)(x >> 8); b[at + 3] = (byte)x; }
    private static void WAscii(byte[] b, int at, string s, int size)
    {
        var v = Encoding.ASCII.GetBytes(s);
        if (v.Length > size) throw new InvalidDataException("RARC fixed string too long.");
        Buffer.BlockCopy(v, 0, b, at, v.Length);
    }

    public static byte[] PackFolder(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var nodes = new List<Node>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void AddDirectory(string path, string name, int parent, int depth)
        {
            if (depth > 32 || nodes.Count >= 4096) throw new InvalidDataException("Too many RARC directories.");
            string canonical = Path.GetFullPath(path);
            if (!visiting.Add(canonical)) throw new InvalidDataException("RARC directory cycle.");
            int index = nodes.Count;
            var node = new Node { Name = name, Parent = parent };
            nodes.Add(node);
            node.Entries.Add(new Entry { Name = ".", Child = index });
            node.Entries.Add(new Entry { Name = "..", Child = parent });
            foreach (string child in Directory.GetDirectories(path).OrderBy(x => x, StringComparer.Ordinal))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Cannot pack directory links.");
                string childName = Path.GetFileName(child);
                CheckName(childName);
                int childIndex = nodes.Count;
                node.Entries.Add(new Entry { Name = childName, Child = childIndex });
                AddDirectory(child, childName, index, depth + 1);
            }
            foreach (string file in Directory.GetFiles(path).OrderBy(x => x, StringComparer.Ordinal))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Cannot pack symlinked files.");
                string childName = Path.GetFileName(file);
                CheckName(childName);
                var info = new FileInfo(file);
                if (info.Length > GameCubeBinary.MaxAssetBytes) throw new InvalidDataException("Oversized RARC file.");
                node.Entries.Add(new Entry { Name = childName, Data = File.ReadAllBytes(file) });
            }
            visiting.Remove(canonical);
        }
        AddDirectory(root, "archive", -1, 0);
        var allEntries = nodes.SelectMany(n => n.Entries).ToList();
        if (allEntries.Count > 65535) throw new InvalidDataException("Too many RARC files.");

        var stringOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        using var strings = new MemoryStream();
        int Intern(string name)
        {
            if (stringOffsets.TryGetValue(name, out int existing)) return existing;
            int offset = checked((int)strings.Length);
            var bytes = Encoding.ASCII.GetBytes(name);
            strings.Write(bytes, 0, bytes.Length);
            strings.WriteByte(0);
            stringOffsets.Add(name, offset);
            return offset;
        }
        foreach (var n in nodes) Intern(n.Name);
        foreach (var e in allEntries) Intern(e.Name);

        int nodesStart = 0x40;
        int entriesStart = Align(checked(nodesStart + nodes.Count * 0x10));
        int stringsStart = Align(checked(entriesStart + allEntries.Count * 0x14));
        int dataStart = Align(checked(stringsStart + (int)strings.Length));
        int cursor = 0;
        foreach (var e in allEntries)
        {
            if (e.Data == null) continue;
            e.Offset = cursor;
            cursor = Align(checked(cursor + e.Data.Length));
        }
        int size = checked(dataStart + cursor);
        if (size > GameCubeBinary.MaxAssetBytes)
            throw new InvalidDataException("RARC exceeds 16 MiB memory budget.");
        var output = new byte[size];
        WAscii(output, 0, "RARC", 4);
        W32(output, 4, size);
        W32(output, 8, 0x20);
        W32(output, 0xC, dataStart - 0x20);
        W32(output, 0x10, cursor);
        W32(output, 0x14, cursor);
        W32(output, 0x20, nodes.Count);
        W32(output, 0x24, nodesStart - 0x20);
        W32(output, 0x28, allEntries.Count);
        W32(output, 0x2C, entriesStart - 0x20);
        W32(output, 0x30, checked((int)strings.Length));
        W32(output, 0x34, stringsStart - 0x20);
        W16(output, 0x38, allEntries.Count);
        output[0x3A] = 1;

        int entryIdx = 0, fileId = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            int o = nodesStart + i * 0x10;
            WAscii(output, o, i == 0 ? "ROOT" : "DIR ", 4);
            W32(output, o + 4, Intern(node.Name));
            W16(output, o + 8, Hash(node.Name));
            W16(output, o + 0xA, node.Entries.Count);
            W32(output, o + 0xC, entryIdx);
            foreach (var e in node.Entries)
            {
                o = entriesStart + entryIdx++ * 0x14;
                bool isDir = e.Data == null;
                W16(output, o, isDir ? 0xFFFF : fileId++);
                W16(output, o + 2, Hash(e.Name));
                W32(output, o + 4, ((isDir ? 0x02 : 0x11) << 24) | Intern(e.Name));
                W32(output, o + 8, isDir ? e.Child : e.Offset);
                W32(output, o + 0xC, isDir ? 0x10 : e.Data!.Length);
                if (!isDir) Buffer.BlockCopy(e.Data!, 0, output, dataStart + e.Offset, e.Data!.Length);
            }
        }
        Buffer.BlockCopy(strings.ToArray(), 0, output, stringsStart, (int)strings.Length);
        // Verify the full directory tree and member offsets against the shared reader.
        RarcArchive.Read(output);
        return output;
    }
    private static void CheckName(string name)
    {
        if (name.Length == 0 || name.Length > 255 || name == "." || name == ".." ||
            name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 ||
            name.Any(c => c > 127 || c == 0))
            throw new InvalidDataException("Unsupported RARC archive filename: " + name);
    }
}
