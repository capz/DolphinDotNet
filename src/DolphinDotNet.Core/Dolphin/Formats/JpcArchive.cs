using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static Dolphin.Formats.GameCubeBinary;

namespace Dolphin.Formats;

/// <summary>JPAC1-00 / JPAC2-10 read-only particle archives. Unknown effect chunks are retained.</summary>
public sealed class JpcArchive
{
    public sealed class Chunk
    {
        public string Type { get; }
        public int Length { get; }
        internal readonly int Offset;
        internal Chunk(string type, int offset, int length) { Type = type; Offset = offset; Length = length; }
    }

    public sealed class Particle
    {
        public ushort Id { get; }
        public IReadOnlyList<Chunk> Chunks => chunks;
        public IReadOnlyList<ushort> TextureIds => textureIds;
        private readonly List<Chunk> chunks = new List<Chunk>();
        private readonly List<ushort> textureIds = new List<ushort>();
        internal Particle(ushort id) { Id = id; }
        internal void Add(Chunk chunk) => chunks.Add(chunk);
        internal void AddTexture(ushort id) => textureIds.Add(id);
        public bool HasChunk(string kind)
        {
            foreach (var chunk in chunks) if (chunk.Type == kind) return true;
            return false;
        }
    }

    public sealed class Texture
    {
        public string Name { get; }
        public byte[] BtiData { get; }
        internal Texture(string name, byte[] bti) { Name = name; BtiData = bti; }
        public BtiTexture Decode() => BtiTexture.Read(BtiData);
    }

    private readonly byte[] source;
    private readonly List<Particle> particles = new List<Particle>();
    private readonly List<Texture> textures = new List<Texture>();
    public IReadOnlyList<Particle> Particles => particles;
    public IReadOnlyList<Texture> Textures => textures;
    public string Version { get; }
    private JpcArchive(byte[] source, string version) { this.source = source; Version = version; }

    public static async Task<JpcArchive> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        byte[] source = await Dolphin.IO.File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(source);
    }

    public static JpcArchive Read(byte[] input)
    {
        var data = UnwrapYaz0(input);
        Check(data, 0, 0x10);
        string version = Ascii(data, 0, 8);
        bool v1 = version == "JPAC1-00", v2 = version == "JPAC2-10";
        if (!v1 && !v2) throw new InvalidDataException("Unsupported JPAC version.");
        int count = U16(data, 8), textureCount = U16(data, 0xA);
        if (count > 4096 || textureCount > 4096) throw new InvalidDataException("JPAC count limit exceeded.");
        int offset = v1 ? 0x20 : 0x10;
        long texOffset = v2 ? U32(data, 0xC) : 0;
        var archive = new JpcArchive(data, version);
        var particleIds = new HashSet<ushort>();
        for (int i = 0; i < count; i++)
        {
            Check(data, offset, v1 ? 0x20 : 8);
            int chunks;
            int usedTextures;
            ushort id;
            if (v1)
            {
                if (Ascii(data, offset, 8) != "JEFFjpa1") throw new InvalidDataException("Invalid JPAC1 particle.");
                id = U16(data, offset + 0x18);
                uint rawChunks = U32(data, offset + 0xC);
                if(rawChunks > 1024) throw new InvalidDataException("JPAC chunk count exceeds limits.");
                chunks = (int)rawChunks;
                usedTextures = data[offset + 0x16];
                offset += 0x20;
            }
            else
            {
                id = U16(data, offset);
                chunks = U16(data, offset + 2);
                usedTextures = data[offset + 6];
                offset += 8;
            }
            if (!particleIds.Add(id) || chunks > 1024)
                throw new InvalidDataException("Duplicate particle ID or excessive chunks.");
            var particle = new Particle(id);
            int tdbOffset = -1, tdbLength = 0;
            for (int j = 0; j < chunks; j++)
            {
                Check(data, offset, 8);
                uint rawSize = U32(data, offset + 4);
                if(rawSize > data.Length) throw new InvalidDataException("JPAC chunk size exceeds asset bounds.");
                int size = (int)rawSize;
                if (size < 8) throw new InvalidDataException("Zero-length JPAC chunk.");
                Check(data, offset, size);
                string type = Ascii(data, offset, 4);
                particle.Add(new Chunk(type, offset, size));
                if (type == "TDB1")
                {
                    if (tdbOffset >= 0) throw new InvalidDataException("Duplicate TDB1.");
                    tdbOffset = offset; tdbLength = size;
                }
                offset += size; /* Validated by Check(data,offset,size) above. */
            }
            if (usedTextures > 0 && tdbOffset < 0) throw new InvalidDataException("Missing TDB1 references.");
            if (tdbOffset >= 0)
            {
                int tdbHeader = v1 ? 0xC : 8;
                if (tdbHeader + usedTextures * 2 > tdbLength)
                    throw new InvalidDataException("Truncated TDB1.");
                for (int j = 0; j < usedTextures; j++)
                {
                    ushort index = U16(data, tdbOffset + tdbHeader + j * 2);
                    if (index >= textureCount) throw new InvalidDataException("Invalid JPAC texture index.");
                    particle.AddTexture(index);
                }
            }
            archive.particles.Add(particle);
        }
        if (v2)
        {
            if (texOffset < offset || texOffset > data.Length) throw new InvalidDataException("JPAC texture offset overlaps particles.");
            offset = (int)texOffset;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < textureCount; i++)
        {
            Check(data, offset, 0x40);
            if (Ascii(data, offset, 4) != "TEX1") throw new InvalidDataException("Invalid JPAC texture chunk.");
            uint rawSize = U32(data, offset + 4);
                if(rawSize > data.Length) throw new InvalidDataException("JPAC chunk size exceeds asset bounds.");
                int size = (int)rawSize;
            if (size < 0x40) throw new InvalidDataException("Invalid TEX1 length.");
            Check(data, offset, size);
            string name = NullTerminated(data, offset + 0xC, offset + 0x20, 0x14);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate JPAC texture name.");
            archive.textures.Add(new Texture(name, Slice(data, offset + 0x20, size - 0x20)));
            offset += size; /* Validated by Check(data,offset,size) above. */
        }
        return archive;
    }

    public byte[] CopyChunk(Chunk chunk)
    {
        if (chunk == null) throw new ArgumentNullException(nameof(chunk));
        bool found = false;
        foreach (var p in particles)
            foreach (var c in p.Chunks)
                if (ReferenceEquals(c, chunk)) found = true;
        if (!found) throw new ArgumentException("Chunk belongs to a different archive.", nameof(chunk));
        return Slice(source, chunk.Offset, chunk.Length);
    }
}
