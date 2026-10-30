using System;
using System.Collections.Generic;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Read-only generic FLW1/FLI1 BMG conversation-flow tables.
/// Flow execution depends on game-defined query/event callbacks and is
/// intentionally not provided by the asset format layer.</summary>
public sealed class BmgFlowTables
{
    public readonly struct Node
    {
        public readonly byte Kind, Parameter;
        public readonly ushort ValueA, ValueB, ValueC;
        public Node(byte kind, byte parameter, ushort a, ushort b, ushort c)
        { Kind = kind; Parameter = parameter; ValueA = a; ValueB = b; ValueC = c; }
    }

    private readonly List<Node> nodes = new List<Node>();
    private readonly List<ushort> links = new List<ushort>();
    private readonly Dictionary<ushort, ushort> flowIds = new Dictionary<ushort, ushort>();
    public IReadOnlyList<Node> Nodes => nodes;
    public IReadOnlyList<ushort> Indirections => links;
    public IReadOnlyDictionary<ushort, ushort> FlowIds => flowIds;

    public static BmgFlowTables Read(byte[] input)
    {
        byte[] data = UnwrapYaz0(input);
        if (Ascii(data, 0, 8) != "MESGbmg1")
            throw new InvalidDataException("Not a big-endian BMG.");
        uint count = U32(data, 0xC);
        if (count < 2 || count > 32) throw new InvalidDataException("Invalid BMG section count.");
        long offset = 0x20, flow = -1, indices = -1;
        long flowLength = 0, indicesLength = 0;
        for (int i = 0; i < count; i++)
        {
            Check(data, offset, 8);
            int length = checked((int)U32(data, offset + 4));
            if (length < 8 || length > data.Length - offset)
                throw new InvalidDataException("BMG section exceeds data.");
            string name = Ascii(data, offset, 4);
            if (name == "FLW1")
            {
                if (flow >= 0) throw new InvalidDataException("Duplicate FLW1.");
                flow = offset; flowLength = length;
            }
            else if (name == "FLI1")
            {
                if (indices >= 0) throw new InvalidDataException("Duplicate FLI1.");
                indices = offset; indicesLength = length;
            }
            offset += length;
        }
        var result = new BmgFlowTables();
        if (flow >= 0)
        {
            if (flowLength < 0x10) throw new InvalidDataException("Truncated FLW1.");
            int nodeCount = U16(data, flow + 8), linkCount = U16(data, flow + 0xA);
            if (0x10L + (long)nodeCount * 8 + linkCount * 2L > flowLength)
                throw new InvalidDataException("FLW1 node or link table outside section.");
            for (int i = 0; i < nodeCount; i++)
            {
                long at = flow + 0x10L + i * 8L;
                byte kind = data[(int)at], param = data[(int)at + 1];
                ushort a = U16(data, at + 2), b = U16(data, at + 4), c = U16(data, at + 6);
                if (kind > 3) throw new InvalidDataException("Unknown BMG flow node kind.");
                // Continue nodes reference their successor directly. Event
                // nodes reference the indirection list instead.
                if (kind == 1 && b != 0xFFFF && b >= nodeCount)
                    throw new InvalidDataException("FLW1 continuation leaves node table.");
                if (kind == 3 && a >= linkCount)
                    throw new InvalidDataException("FLW1 event leaves indirection table.");
                result.nodes.Add(new Node(kind, param, a, b, c));
            }
            long linkStart = flow + 0x10L + nodeCount * 8L;
            for (int i = 0; i < linkCount; i++)
            {
                ushort link = U16(data, linkStart + i * 2L);
                if (link != 0xFFFF && link >= nodeCount)
                    throw new InvalidDataException("Invalid BMG FLW1 indirection.");
                result.links.Add(link);
            }
        }
        if (indices >= 0)
        {
            if (flow < 0 || indicesLength < 0x10)
                throw new InvalidDataException("FLI1 without a valid FLW1.");
            int num = U16(data, indices + 8);
            if (0x10L + num * 8L > indicesLength)
                throw new InvalidDataException("Truncated FLI1 flow ID entries.");
            for (int i = 0; i < num; i++)
            {
                long at = indices + 0x10L + i * 8L;
                ushort id = U16(data, at), target = U16(data, at + 4);
                if (target >= result.nodes.Count || result.flowIds.ContainsKey(id))
                    throw new InvalidDataException("Invalid or duplicate BMG flow ID.");
                result.flowIds.Add(id, target);
            }
        }
        return result;
    }
}
