using System;
using System.Collections.Generic;
using System.IO;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>Read-only J3D1 BTK/BRK/BTP/BVA non-skeletal animation tracks.
/// Sampling is independent of the model renderer and uses source frame units.</summary>
public sealed class J3dMaterialAnimation
{
    public string FileType { get; }
    public string ChunkType { get; }
    public int DurationFrames { get; }
    public int LoopMode { get; }
    public int TrackCount { get; }
    private readonly byte[] data;
    private readonly int chunk;
    private readonly int size;

    private J3dMaterialAnimation(byte[] data, string fileType, string chunkType, int chunk, int size,
        int frames, int loop, int tracks)
    {
        this.data = data; FileType = fileType; ChunkType = chunkType; this.chunk = chunk;
        this.size = size; DurationFrames = frames; LoopMode = loop; TrackCount = tracks;
    }

    public static J3dMaterialAnimation Read(byte[] input)
    {
        byte[] data = UnwrapYaz0(input);
        if (Ascii(data, 0, 4) != "J3D1") throw new InvalidDataException("Not a J3D1 animation.");
        string type = Ascii(data, 4, 4);
        string expected;
        switch (type)
        {
            case "btp1": expected = "TPT1"; break;
            case "bva1": expected = "VAF1"; break;
            case "btk1": expected = "TTK1"; break;
            case "brk1": expected = "TRK1"; break;
            default: throw new NotSupportedException("J3D material animation not supported: " + type);
        }
        uint declared = U32(data, 8), count = U32(data, 0xC);
        if (declared < 0x20 || declared > data.Length || count == 0 || count > 32)
            throw new InvalidDataException("Invalid J3D header.");
        int pos = 0x20;
        for (int i = 0; i < count; i++)
        {
            Check(data, pos, 8);
            string chunkType = Ascii(data, pos, 4);
            int size = checked((int)U32(data, pos + 4));
            if (size < 0x18 || (long)pos + size > declared) throw new InvalidDataException("Invalid J3D chunk size.");
            if (chunkType == expected)
            {
                int tracks = U16(data, pos + 0xC);
                if (type == "btk1") tracks /= 3;
                if (type == "brk1") tracks += U16(data, pos + 0xE);
                return new J3dMaterialAnimation(data, type, expected, pos, size,
                    U16(data, pos + 0xA), data[pos + 8], tracks);
            }
            pos = checked(pos + size);
        }
        throw new InvalidDataException("Expected J3D animation chunk is missing.");
    }

    /// <summary>Resolve an animation track's authored material name. BVA
    /// visibility references shapes instead of materials and has no name table.</summary>
    public string GetTrackMaterialName(int trackIndex, bool konst = false)
    {
        if (FileType == "bva1")
            throw new NotSupportedException("BVA shape visibility is not material-name indexed.");
        int count = TrackCount;
        uint namesOffset;
        if (FileType == "brk1")
        {
            int regular = U16(data, chunk + 0xC), konstCount = U16(data, chunk + 0xE);
            count = konst ? konstCount : regular;
            namesOffset = U32(data, chunk + (konst ? 0x34 : 0x30));
        }
        else namesOffset = U32(data, chunk + 0x1C);
        if (trackIndex < 0 || trackIndex >= count)
            throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int table = Relative(namesOffset, 4);
        int listed = U16(data, table);
        if (listed < count || 4L + listed * 4L > size - namesOffset)
            throw new InvalidDataException("J3D material-name table is truncated.");
        int nameOffset = U16(data, table + 6 + trackIndex * 4L);
        if (nameOffset < 4 + listed * 4 || nameOffset >= size - namesOffset)
            throw new InvalidDataException("J3D animation name offset invalid.");
        return NullTerminated(data, table + nameOffset, chunk + size, 64);
    }

    public float ResolveFrame(float frame)
    {
        if (float.IsNaN(frame) || float.IsInfinity(frame) || frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (DurationFrames <= 0) return 0;
        float duration = DurationFrames;
        if (LoopMode == 2) return frame % duration;
        if (LoopMode == 4)
        {
            float period = duration * 2;
            float t = frame % period;
            return t > duration ? period - t : t;
        }
        if (LoopMode == 1 && frame >= duration) return 0;
        if (LoopMode == 3 && frame >= duration * 2) return 0;
        if (LoopMode == 3 && frame > duration) return duration * 2 - frame;
        return Math.Min(frame, duration);
    }

    private int Relative(uint offset, long needed)
    {
        if (offset == 0 || offset >= size || needed < 0 || needed > size - offset)
            throw new InvalidDataException("Invalid J3D animation table offset.");
        return chunk + (int)offset;
    }

    private void RequireKind(string kind)
    {
        if (FileType != kind) throw new InvalidOperationException("Animation is not " + kind + ".");
    }

    /// <summary>Sample a BTP texture index track (material binding is handled by the caller).</summary>
    public ushort SampleTextureIndex(int trackIndex, float frame)
    {
        RequireKind("btp1");
        if (trackIndex < 0 || trackIndex >= TrackCount) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int records = Relative(U32(data, chunk + 0x10), (long)TrackCount * 4);
        int tableSize = U16(data, chunk + 0xE);
        int table = Relative(U32(data, chunk + 0x14), (long)tableSize * 2);
        int count = U16(data, records + trackIndex * 4), first = U16(data, records + trackIndex * 4 + 2);
        if (count == 0 || first + count > tableSize) throw new InvalidDataException("Invalid BTP key range.");
        int n = Math.Min((int)ResolveFrame(frame), count - 1);
        return U16(data, table + (first + n) * 2);
    }

    /// <summary>Sample BVA visibility for a shape/track index.</summary>
    public bool SampleVisibility(int trackIndex, float frame)
    {
        RequireKind("bva1");
        if (trackIndex < 0 || trackIndex >= TrackCount) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int records = Relative(U32(data, chunk + 0x10), TrackCount * 4L);
        int tableSize = U16(data, chunk + 0xE);
        int table = Relative(U32(data, chunk + 0x14), tableSize);
        int count = U16(data, records + trackIndex * 4), first = U16(data, records + trackIndex * 4 + 2);
        if (count == 0 || first + count > tableSize) throw new InvalidDataException("Invalid BVA visibility range.");
        int n = Math.Min((int)ResolveFrame(frame), count - 1);
        return data[table + first + n] != 0;
    }

    /// <summary>Return four raw RGBA channel values for one BRK register/konst track.
    /// Konst channel values typically require clamping to [0,255] before GX submission.</summary>
    public float[] SampleColor(int trackIndex, bool konst, float frame)
    {
        var values = new float[4];
        SampleColor(trackIndex, konst, frame, values);
        return values;
    }

    public void SampleColor(int trackIndex, bool konst, float frame, float[] values)
    {
        if (values == null || values.Length < 4) throw new ArgumentException("Four color channels required.", nameof(values));
        RequireKind("brk1");
        int regularCount = U16(data, chunk + 0xC), konstCount = U16(data, chunk + 0xE);
        int count = konst ? konstCount : regularCount;
        if (trackIndex < 0 || trackIndex >= count) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int anims = Relative(U32(data, chunk + (konst ? 0x24 : 0x20)), count * 0x1CL);
        int rec = anims + trackIndex * 0x1C;
        float time = ResolveFrame(frame);
        for (int component = 0; component < 4; component++)
        {
            int header = konst ? 0x18 : 0x10;
            int countValues = U16(data, chunk + header + component * 2);
            int tableOff = konst ? 0x48 : 0x38;
            int table = Relative(U32(data, chunk + tableOff + component * 4), countValues * 2L);
            values[component] = SampleTrack(rec + component * 6, table, countValues, 2, false, time);
        }
    }

    /// <summary>Sample BTK S/T/Q UV scale, rotation and translation tracks in order.
    /// Rotations are returned as raw signed 16-bit angular units, not degrees.</summary>
    public float[] SampleUv(int trackIndex, float frame)
    {
        var values = new float[9];
        SampleUv(trackIndex, frame, values);
        return values;
    }

    public int TextureRotationFraction => FileType == "btk1" ? data[chunk + 9] : 0;
    public bool IsMayaTextureMatrix => FileType == "btk1" && size >= 0x60 && U32(data, chunk + 0x5C) == 1;

    public void SampleUv(int trackIndex, float frame, float[] output)
    {
        if (output == null || output.Length < 9) throw new ArgumentException("Nine UV channels required.", nameof(output));
        RequireKind("btk1");
        if (trackIndex < 0 || trackIndex >= TrackCount) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int records = Relative(U32(data, chunk + 0x14), TrackCount * 0x36L);
        int scaleCount = U16(data, chunk + 0xE);
        int rotationCount = U16(data, chunk + 0x10);
        int translationCount = U16(data, chunk + 0x12);
         int row = records + trackIndex * 0x36;
        float time = ResolveFrame(frame);
        for (int k = 0; k < 9; k++)
        {
            int tableType = k % 3;
            int count = tableType == 0 ? scaleCount : tableType == 1 ? rotationCount : translationCount;
            bool floating = tableType != 1;
            int stride = floating ? 4 : 2;
            int tableOffset = tableType == 0 ? 0x28 : tableType == 1 ? 0x2C : 0x30;
            int table = Relative(U32(data, chunk + tableOffset), count * (long)stride);
            output[k] = SampleTrack(row + k * 6, table, count, stride, floating, time);
        }
    }

    public float[] GetUvCenter(int trackIndex)
    {
        var destination = new float[2];
        GetUvCenter(trackIndex, destination);
        return destination;
    }

    public void GetUvCenter(int trackIndex, float[] destination)
    {
        if (destination == null || destination.Length < 2)
            throw new ArgumentException("Two center coordinates required.", nameof(destination));
        RequireKind("btk1");
        if (trackIndex < 0 || trackIndex >= TrackCount) throw new ArgumentOutOfRangeException(nameof(trackIndex));
        int center = Relative(U32(data, chunk + 0x24), TrackCount * 12L);
        destination[0] = F32(data, center + trackIndex * 12);
        destination[1] = F32(data, center + trackIndex * 12 + 4);
    }

    private float SampleTrack(int record, int table, int tableCount, int stride, bool floating, float frame)
    {
        int count = U16(data, record), index = U16(data, record + 2), tangentMode = U16(data, record + 4);
        if (count == 0 || tangentMode > 1) throw new InvalidDataException("Invalid J3D animation track.");
        int step = count == 1 ? 1 : tangentMode == 0 ? 3 : 4;
        if ((long)index + (long)count * step > tableCount)
            throw new InvalidDataException("Animation key points outside table.");
        float Value(int at) => floating ? F32(data, table + at * stride) : S16(data, table + at * stride);
        if (count == 1) return Value(index);
        float t0 = Value(index), v0 = Value(index + 1);
        for (int i = 1; i < count; i++)
        {
            int next = index + i * step;
            float t1 = Value(next), v1 = Value(next + 1);
            if (t1 <= t0) throw new InvalidDataException("Non-monotonic J3D animation keys.");
            if (frame <= t1)
            {
                float u = Math.Max(0, Math.Min(1, (frame - t0) / (t1 - t0)));
                float outTan = Value(index + (i - 1) * step + (tangentMode == 0 ? 2 : 3));
                float inTan = Value(next + 2);
                float h00 = 2 * u * u * u - 3 * u * u + 1;
                float h10 = u * u * u - 2 * u * u + u;
                float h01 = -2 * u * u * u + 3 * u * u;
                float h11 = u * u * u - u * u;
                return h00 * v0 + h10 * outTan * (t1 - t0) +
                       h01 * v1 + h11 * inTan * (t1 - t0);
            }
            t0 = t1; v0 = v1;
        }
        return v0;
    }
}
