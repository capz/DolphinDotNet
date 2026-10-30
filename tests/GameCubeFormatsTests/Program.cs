using System;
using System.IO;
using System.Text;
using Dolphin.Formats;

static class Program
{
    static int assertionCount;
    static void Check(bool condition, string what)
    {
        assertionCount++;
        if (!condition) throw new Exception("Format regression: " + what);
    }
    static void W16(byte[] data, int off, int value)
    { data[off] = (byte)(value >> 8); data[off + 1] = (byte)value; }
    static void W32(byte[] data, int off, int value)
    {
        data[off] = (byte)(value >> 24); data[off + 1] = (byte)(value >> 16);
        data[off + 2] = (byte)(value >> 8); data[off + 3] = (byte)value;
    }
    static void Str(byte[] data, int off, string s)
    {
        Encoding.ASCII.GetBytes(s).CopyTo(data, off);
    }
    static void Reject(Action action, string what)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, what);
    }
    static void Main()
    {
        var yaz = new byte[20];
        Str(yaz, 0, "Yaz0"); W32(yaz, 4, 3);
        yaz[16] = 0xE0; yaz[17] = (byte)'A'; yaz[18] = (byte)'B'; yaz[19] = (byte)'C';
        Check(Encoding.ASCII.GetString(GameCubeBinary.Yaz0Decode(yaz)) == "ABC", "Yaz0 literals");
        var bad = (byte[])yaz.Clone(); W32(bad, 4, GameCubeBinary.MaxAssetBytes + 1);
        Reject(() => GameCubeBinary.Yaz0Decode(bad), "Yaz0 declared budget");

        Check(ShiftJisCodec.Decode(new byte[]{0x82,0xA0})=="あ", "JIS0208 hiragana mapping");
        Check(ShiftJisCodec.Decode(new byte[]{0x83,0x65,0x83,0x58,0x83,0x67})=="テスト",
              "Shift-JIS multi-byte katakana");
        Check(ShiftJisCodec.Decode(new byte[]{0xA6})=="ｦ", "Shift-JIS halfwidth katakana");
        Check(ShiftJisCodec.Decode(new byte[]{0x41,0x82,0xA0})=="Aあ",
              "Shift-JIS mixed ASCII and DBCS text");
        Reject(() => ShiftJisCodec.Decode(new byte[]{0x82}), "Truncated Shift-JIS lead");
        Reject(() => ShiftJisCodec.Decode(new byte[]{0x82,0x7F}), "Invalid Shift-JIS trail");
        var bti = new byte[0x40];
        bti[0] = 4; W16(bti, 2, 4); W16(bti, 4, 4); bti[0x18] = 1;
        W32(bti, 0x1C, 0x20);
        for (int i = 0x20; i < 0x40; i += 2) W16(bti, i, 0xF800);
        var image = BtiTexture.Read(bti);
        Check(image.Width == 4 && image.Height == 4 && image.Rgba.Length == 64, "BTI dimensions");
        Check(image.Rgba[0] == 255 && image.Rgba[1] == 0 && image.Rgba[2] == 0, "RGB565 red");
        bad = (byte[])bti.Clone(); W32(bad, 0x1C, 0xFF);
        Reject(() => BtiTexture.Read(bad), "BTI offset outside file");

        var bmg = new byte[0x80];
        Str(bmg, 0, "MESGbmg1"); W32(bmg, 8, 0x80); W32(bmg, 0xC, 2);
        Str(bmg, 0x20, "INF1"); W32(bmg, 0x24, 0x40);
        W16(bmg, 0x28, 1); W16(bmg, 0x2A, 0x18);
        W16(bmg, 0x34, 7); W16(bmg, 0x38, 9);
        Str(bmg, 0x60, "DAT1"); W32(bmg, 0x64, 0x20);
        Str(bmg, 0x68, "Hi");
        var messages = BmgMessages.Read(bmg);
        Check(messages.Messages.Count == 1 && messages.Messages[0].Id == 7, "BMG IDs");
        Check(messages.Messages[0].EscapedText == "Hi", "BMG text");
        bad = (byte[])bmg.Clone(); W32(bad, 0x30, 0x100);
        Reject(() => BmgMessages.Read(bad), "BMG invalid text offset");

        var cp1252Bmg = (byte[])bmg.Clone();
        cp1252Bmg[0x10] = 1; cp1252Bmg[0x68] = 0x80; cp1252Bmg[0x69] = 0;
        Check(BmgCatalog.Read(cp1252Bmg).Entries[0].EscapedText == "€",
            "Generic BMG Windows-1252 extended characters");
        var utf8Bmg = (byte[])bmg.Clone();
        utf8Bmg[0x10] = 4;
        utf8Bmg[0x68] = 0xE9; utf8Bmg[0x69] = 0x9B;
        utf8Bmg[0x6A] = 0xAA; utf8Bmg[0x6B] = 0;
        Check(BmgCatalog.Read(utf8Bmg).Entries[0].EscapedText == "雪",
            "Generic BMG UTF-8 multibyte decoding");
        bad = (byte[])utf8Bmg.Clone(); bad[0x69] = 0xFF;
        Reject(() => BmgCatalog.Read(bad), "Generic BMG invalid UTF-8 rejected");

        var shiftBmg = (byte[])bmg.Clone();
        shiftBmg[0x10] = 3;
        shiftBmg[0x68] = 0x82; shiftBmg[0x69] = 0xA0; shiftBmg[0x6A] = 0;
        Check(BmgCatalog.Read(shiftBmg).Entries[0].EscapedText=="あ",
            "BMG Shift-JIS independent of platform codepages");
        var genericBmg = new byte[0xA0];
        Str(genericBmg, 0, "MESGbmg1"); W32(genericBmg, 8, 0xA0);
        W32(genericBmg, 0xC, 3); genericBmg[0x10] = 2; // UTF-16BE
        Str(genericBmg, 0x20, "INF1"); W32(genericBmg, 0x24, 0x20);
        W16(genericBmg, 0x28, 2); W16(genericBmg, 0x2A, 4);
        W32(genericBmg, 0x30, 0); W32(genericBmg, 0x34, 0x10);
        Str(genericBmg, 0x40, "DAT1"); W32(genericBmg, 0x44, 0x40);
        W16(genericBmg, 0x48, 0x0048); W16(genericBmg, 0x4A, 0x00E9);
        W16(genericBmg, 0x4C, 0); // "Hé"
        W16(genericBmg, 0x58, 0x0041); W16(genericBmg, 0x5A, 0x001A);
        genericBmg[0x5C] = 5; genericBmg[0x5D] = 0xFF;
        W16(genericBmg, 0x5E, 1); W16(genericBmg, 0x60, 0);
        Str(genericBmg, 0x80, "MID1"); W32(genericBmg, 0x84, 0x20);
        W16(genericBmg, 0x88, 2);
        W32(genericBmg, 0x90, 0x1234); W32(genericBmg, 0x94, unchecked((int)0x87654321));
        var catalog = BmgCatalog.Read(genericBmg);
        Check(catalog.EncodingCode == 2 && catalog.Entries.Count == 2,
            "Generic BMG UTF-16BE and MID1 enumeration");
        Check(catalog.Entries[0].Id == 0x1234 && catalog.Entries[0].EscapedText == "Hé",
            "Generic BMG UTF-16BE decoded text");
        Check(catalog.Entries[1].Id == 0x87654321 && catalog.Entries[1].Tags.Count == 1 &&
              catalog.Entries[1].Tags[0].Group == 0xFF && catalog.Entries[1].Tags[0].Number == 1,
            "Generic BMG MID1 IDs and preserved format tags");
        bad = (byte[])genericBmg.Clone(); W16(bad, 0x88, 1);
        Reject(() => BmgCatalog.Read(bad), "Generic BMG mismatched MID1 counts");
        bad = (byte[])genericBmg.Clone(); bad[0x5C] = 250;
        Reject(() => BmgCatalog.Read(bad), "Generic BMG truncated tag");
        bad = (byte[])genericBmg.Clone(); W16(bad, 0x4A, 0xD800);
        Reject(() => BmgCatalog.Read(bad), "Generic BMG invalid UTF-16 surrogate");

        var bmgFlow = new byte[0xE0];
        Buffer.BlockCopy(genericBmg, 0, bmgFlow, 0, genericBmg.Length);
        W32(bmgFlow, 8, bmgFlow.Length); W32(bmgFlow, 0xC, 5);
        Str(bmgFlow, 0xA0, "FLW1"); W32(bmgFlow, 0xA4, 0x20);
        W16(bmgFlow, 0xA8, 1); W16(bmgFlow, 0xAA, 1);
        bmgFlow[0xB0] = 1; W16(bmgFlow, 0xB2, 0);
        W16(bmgFlow, 0xB4, 0xFFFF); W16(bmgFlow, 0xB8, 0xFFFF);
        Str(bmgFlow, 0xC0, "FLI1"); W32(bmgFlow, 0xC4, 0x20);
        W16(bmgFlow, 0xC8, 1); W16(bmgFlow, 0xD0, 0x55);
        W16(bmgFlow, 0xD4, 0);
        var flows = BmgFlowTables.Read(bmgFlow);
        Check(flows.Nodes.Count == 1 && flows.Nodes[0].Kind == 1 &&
              flows.Nodes[0].ValueB == 0xFFFF, "BMG FLW1 continuation and termination");
        Check(flows.Indirections.Count == 1 && flows.Indirections[0] == 0xFFFF &&
              flows.FlowIds[0x55] == 0, "BMG flow ID and indirection resolution");
        bad = (byte[])bmgFlow.Clone(); W16(bad, 0xB4, 2);
        Reject(() => BmgFlowTables.Read(bad), "BMG out-of-range flow node");
        bad = (byte[])bmgFlow.Clone(); W16(bad, 0xB8, 3);
        Reject(() => BmgFlowTables.Read(bad), "BMG out-of-range flow indirection");
        bad = (byte[])bmgFlow.Clone(); W16(bad, 0xD4, 8);
        Reject(() => BmgFlowTables.Read(bad), "BMG out-of-range named flow");

        var jpc = new byte[0x28];
        Str(jpc, 0, "JPAC2-10"); W16(jpc, 8, 1); W32(jpc, 0xC, 0x28);
        W16(jpc, 0x10, 0x1234); W16(jpc, 0x12, 2);
        Str(jpc, 0x18, "BEM1"); W32(jpc, 0x1C, 8);
        Str(jpc, 0x20, "BSP1"); W32(jpc, 0x24, 8);
        var particles = JpcArchive.Read(jpc);
        Check(particles.Particles.Count == 1 && particles.Particles[0].Id == 0x1234, "JPAC IDs");
        Check(particles.Particles[0].HasChunk("BEM1"), "JPAC chunks");
        bad = (byte[])jpc.Clone(); W32(bad, 0x1C, int.MaxValue);
        Reject(() => JpcArchive.Read(bad), "JPAC oversized chunk");

        var bva = new byte[0x60];
        Str(bva, 0, "J3D1"); Str(bva, 4, "bva1"); W32(bva, 8, 0x60); W32(bva, 0xC, 1);
        Str(bva, 0x20, "VAF1"); W32(bva, 0x24, 0x40);
        bva[0x28] = 2; W16(bva, 0x2A, 2); W16(bva, 0x2C, 1);
        W16(bva, 0x2E, 2); W32(bva, 0x30, 0x20); W32(bva, 0x34, 0x24);
        W16(bva, 0x40, 2); W16(bva, 0x42, 0);
        bva[0x44] = 1; bva[0x45] = 0;
        var anim = J3dMaterialAnimation.Read(bva);
        Check(anim.TrackCount == 1 && anim.SampleVisibility(0, 0), "BVA first frame");
        Check(!anim.SampleVisibility(0, 1) && anim.SampleVisibility(0, 2), "BVA repeat");
        bad = (byte[])bva.Clone(); W32(bad, 0x34, 0xFFFF);
        Reject(() => J3dMaterialAnimation.Read(bad).SampleVisibility(0, 0), "BVA table bounds");

        var rarc = new byte[0xC3];
        Str(rarc, 0, "RARC"); W32(rarc, 4, rarc.Length);
        W32(rarc, 8, 0x20); W32(rarc, 0xC, 0xA0);
        W32(rarc, 0x10, 3); W32(rarc, 0x14, 3);
        W32(rarc, 0x20, 1); W32(rarc, 0x24, 0x20);
        W32(rarc, 0x28, 3); W32(rarc, 0x2C, 0x40);
        W32(rarc, 0x30, 20); W32(rarc, 0x34, 0x80);
        Str(rarc, 0x40, "ROOT"); W16(rarc, 0x4A, 3);
        // "." and ".." are directory links, and must not be followed.
        W16(rarc, 0x60, 0xFFFF); W32(rarc, 0x64, 0x02000008); W32(rarc, 0x68, 0);
        W16(rarc, 0x74, 0xFFFF); W32(rarc, 0x78, 0x0200000A); W32(rarc, 0x7C, -1);
        W16(rarc, 0x88, 1); W32(rarc, 0x8C, 0x1100000D);
        W32(rarc, 0x90, 0); W32(rarc, 0x94, 3);
        Str(rarc, 0xA0, "archive\0.\0..\0hi.txt\0");
        Str(rarc, 0xC0, "ABC");
        var archive = RarcArchive.Read(rarc);
        Check(archive.Entries.Count == 1 && archive.Entries[0].Path == "hi.txt", "RARC paths");
        Check(Encoding.ASCII.GetString(archive.ReadFile("hi.txt")) == "ABC", "RARC bytes");
        bad = (byte[])rarc.Clone(); W32(bad, 0x90, 0xFFFF);
        Reject(() => RarcArchive.Read(bad), "RARC member offset bounds");
        using (var memberStream = archive.OpenMemberStream("hi.txt"))
        {
            Check(memberStream.CanRead && memberStream.CanSeek && !memberStream.CanWrite,
                "RARC member stream is read-only and seekable");
            Check(memberStream.Length == 3 && memberStream.ReadByte() == (byte)'A',
                "RARC member stream exposes bounded member bytes");
            memberStream.Seek(2, SeekOrigin.Begin);
            Check(memberStream.ReadByte() == (byte)'C' && memberStream.ReadByte() == -1,
                "RARC member stream enforces EOF");
        }
        var bfn = new byte[0x40];
        Str(bfn, 0, "FONTbfn1"); W32(bfn, 8, 0x40); W32(bfn, 0xC, 1);
        Str(bfn, 0x20, "INF1"); W32(bfn, 0x24, 0x20);
        W16(bfn, 0x2A, 10); W16(bfn, 0x2C, 2); W16(bfn, 0x2E, 8);
        W16(bfn, 0x30, 12); W16(bfn, 0x32, 0xAB);
        var font = BfnFont.Read(bfn);
        Check(font.Ascent == 10 && font.LineHeight == 12, "BFN metrics");
        Check(font.GlyphForEncodedCharacter(0x41) == 0xAB && font.WidthForGlyph(3) == 8,
            "BFN default glyph and width");
        var glyphBfn = new byte[0x80];
        Str(glyphBfn, 0, "FONTbfn1"); W32(glyphBfn, 8, 0x80); W32(glyphBfn, 0xC, 2);
        Str(glyphBfn, 0x20, "INF1"); W32(glyphBfn, 0x24, 0x20);
        Str(glyphBfn, 0x40, "GLY1"); W32(glyphBfn, 0x44, 0x40);
        W16(glyphBfn, 0x48, 65); W16(glyphBfn, 0x4A, 65);
        W16(glyphBfn, 0x4C, 8); W16(glyphBfn, 0x4E, 8);
        W32(glyphBfn, 0x50, 32);
        glyphBfn[0x55] = 0; // GX I4
        W16(glyphBfn, 0x56, 1); W16(glyphBfn, 0x58, 1);
        W16(glyphBfn, 0x5A, 8); W16(glyphBfn, 0x5C, 8);
        for (int i = 0x60; i < 0x80; i++) glyphBfn[i] = 0xFF;
        var sheets = BfnGlyphSheets.Read(glyphBfn);
        Check(sheets.Sheets.Count == 1 && sheets.TryGetGlyph(65, out var glyph) &&
              glyph.X == 0 && glyph.Y == 0 && glyph.Width == 8,
              "BFN glyph rect and sheet index");
        byte[] glyphPixels = sheets.CopyGlyph(65);
        Check(glyphPixels.Length == 8 * 8 * 4 && glyphPixels[0] == 255 &&
              glyphPixels[1] == 255 && glyphPixels[2] == 255,
              "BFN GLY1 native I4 tile conversion");
        Check(!sheets.TryGetGlyph(66, out _), "BFN missing glyph");
        bad = (byte[])glyphBfn.Clone(); W32(bad, 0x50, 64);
        Reject(() => BfnGlyphSheets.Read(bad), "BFN sheet length validation");
        Check(GameCubeFormatDetector.Detect(bmg) == GameCubeFileFormat.Bmg, "BMG detection");
        Check(GameCubeFormatDetector.Detect(jpc) == GameCubeFileFormat.Jpc, "JPAC detection");
        Check(GameCubeFormatDetector.Detect(bti, "texture.bti") == GameCubeFileFormat.Bti,
            "Hinted BTI detection");
        var effect = new byte[0x138];
        Str(effect, 0, "JPAC2-10"); W16(effect, 8, 1); W16(effect, 0xA, 1);
        W32(effect, 0xC, 0xD8);
        W16(effect, 0x10, 0xCAFE); W16(effect, 0x12, 3); effect[0x16] = 1;
        Str(effect, 0x18, "BEM1"); W32(effect, 0x1C, 0x80);
        W32(effect, 0x20, 4 << 8); // point volume, bit positions 8-10
        W32(effect, 0x18 + 0x4C, 0x3F800000); // one particle/frame
        W32(effect, 0x18 + 0x34, 0x3C23D70A); // velocity
        W32(effect, 0x18 + 0x60, 0x3F800000); // momentum retention
        W32(effect, 0x18 + 0x28 + 4, 0x3F800000); // direction +Y
        W16(effect, 0x18 + 0x72, 30); W16(effect, 0x18 + 0x6E, 10);
        Str(effect, 0x98, "BSP1"); W32(effect, 0x9C, 0x34);
        W32(effect, 0xA0, 2); // billboard, low shape bits
        W32(effect, 0x98 + 0x10, 0x3F800000);
        W32(effect, 0x98 + 0x14, 0x3F800000);
        effect[0x98 + 0x26] = 255; effect[0x98 + 0x27] = 200;
        effect[0x98 + 0x28] = 100; effect[0x98 + 0x29] = 255;
        Str(effect, 0xCC, "TDB1"); W32(effect, 0xD0, 0x0A);
        Str(effect, 0xD8, "TEX1"); W32(effect, 0xDC, 0x60);
        Str(effect, 0xE4, "sprite"); Buffer.BlockCopy(bti, 0, effect, 0xF8, bti.Length);
        var effectArchive = JpcArchive.Read(effect);
        var definition = JpcEmitterDefinition.Read(effectArchive, 0xCAFE);
        Check(definition.SpawnRate == 1 && definition.ParticleLifetimeFrames == 30,
            "JPA emitter configuration");
        Check(definition.TextureIndex == 0 && definition.SizeX == 1, "JPA texture and shape");
        var sim = new JpcParticleSimulator(definition, 16, 123);
        sim.Update(3f / 60f);
        Check(sim.ActiveCount == 3, "JPA deterministic spawn");
        Check(sim.GetParticle(0).LifetimeFrames == 30, "JPA lifetime");
        for (int i = 0; i < 20; i++) sim.Update(1f / 60f);
        Check(!sim.IsEmitting && sim.ActiveCount == 10, "JPA finite emitter lifetime");
        sim.Stop();
        for (int i = 0; i < 31; i++) sim.Update(1f / 60f);
        Check(sim.ActiveCount == 0, "JPA expiry without survivors");
        Reject(() => JpcEmitterDefinition.Read(JpcArchive.Read(jpc), 0x1234),
            "JPA incomplete emitter profile");
        var cubeEffect = (byte[])effect.Clone();
        W32(cubeEffect, 0x20, 0); // JPAC cube volume
        W16(cubeEffect, 0x18 + 0x74, 5);
        W32(cubeEffect, 0x18 + 0x10, 0x3F800000); // volume scale X
        W32(cubeEffect, 0x18 + 0x14, 0x3F800000); // volume scale Y
        W32(cubeEffect, 0x18 + 0x18, 0x3F800000); // volume scale Z
        var cubeDef = JpcEmitterDefinition.Read(JpcArchive.Read(cubeEffect), 0xCAFE);
        Check(cubeDef.Volume == JpcEmitterVolume.Cube && cubeDef.VolumeSize == 5,
              "JPA bounded volume metadata");
        var cubeSim = new JpcParticleSimulator(cubeDef, 16, 123);
        cubeSim.Update(1f / 60f);
        var spawn = cubeSim.GetParticle(0);
        Check(cubeSim.ActiveCount == 1 && Math.Abs(spawn.X) <= 5 &&
              Math.Abs(spawn.Y) <= 5 && Math.Abs(spawn.Z) <= 5,
              "JPA cube spawn bounds");
        var cubeSim2 = new JpcParticleSimulator(cubeDef, 16, 123);
        cubeSim2.Update(1f / 60f);
        var repeat = cubeSim2.GetParticle(0);
        Check(repeat.X == spawn.X && repeat.Y == spawn.Y && repeat.Z == spawn.Z,
              "JPA deterministic volume sampling");
        W32(cubeEffect, 0x20, 1 << 8); // JPAC sphere volume
        var sphereDef = JpcEmitterDefinition.Read(JpcArchive.Read(cubeEffect), 0xCAFE);
        var sphereSim = new JpcParticleSimulator(sphereDef, 16, 123);
        sphereSim.Update(1f / 60f);
        var sphere = sphereSim.GetParticle(0);
        Check(sphere.X * sphere.X + sphere.Y * sphere.Y + sphere.Z * sphere.Z <= 25.01f,
              "JPA sphere emission volume");
        W32(cubeEffect, 0x20, 3 << 8); // torus remains unsupported
        bool torusRejected = false;
        try { JpcEmitterDefinition.Read(JpcArchive.Read(cubeEffect), 0xCAFE); }
        catch (NotSupportedException) { torusRejected = true; }
        Check(torusRejected, "JPA unsupported torus fails explicitly");
        var extraEffect = new byte[effect.Length + 0x60];
        Buffer.BlockCopy(effect,0,extraEffect,0,0x98);
        Buffer.BlockCopy(effect,0x98,extraEffect,0xF8,effect.Length-0x98);
        W32(extraEffect,0xC,0x138); W16(extraEffect,0x12,4);
        Str(extraEffect,0x98,"ESP1"); W32(extraEffect,0x9C,0x60);
        W32(extraEffect,0xA0,0x10001); // enable alpha and XY scale
        W32(extraEffect,0x98+16,0x3F800000); // scale out @ 1
        W32(extraEffect,0x98+20,0x3F800000); // scale start x
        W32(extraEffect,0x98+24,0x40000000); // scale end x
        W32(extraEffect,0x98+28,0x3F800000); // scale start y
        W32(extraEffect,0x98+32,0x40000000); // scale end y
        W32(extraEffect,0x98+44,0x3E4CCCCD); // alpha in 0.2
        W32(extraEffect,0x98+48,0x3F4CCCCD); // alpha out 0.8
        W32(extraEffect,0x98+56,0x3F800000); // alpha base 1
        var extraArchive = JpcArchive.Read(extraEffect);
        var extraDefinition = JpcEmitterDefinition.Read(extraArchive,0xCAFE);
        var extraProfile = JpcExtraShapeProfile.Read(extraArchive,0xCAFE)!;
        extraProfile.Sample(0.5f,out float sx,out float sy,out float sa);
        Check(extraDefinition.TextureIndex==0 && sx>1 && sy>1 && sa==1,
            "JPA ESP1 scale/alpha sample");
        extraProfile.Sample(0.9f,out sx,out sy,out sa);
        Check(sa<1 && sa>0, "JPA ESP1 alpha fade-out");
        var profileBytes = (byte[])extraEffect.Clone();
        W32(profileBytes,0xA0,0x10001 | (1 << 24)); // rotation unsupported
        bool extraRejected=false;
        try { JpcExtraShapeProfile.Read(JpcArchive.Read(profileBytes),0xCAFE); }
        catch(NotSupportedException) { extraRejected=true; }
        Check(extraRejected,"JPA unsupported ESP1 flags are rejected");

        var childEffect = new byte[extraEffect.Length+0x48];
        Buffer.BlockCopy(extraEffect,0,childEffect,0,0xF8);
        Buffer.BlockCopy(extraEffect,0xF8,childEffect,0x140,extraEffect.Length-0xF8);
        W32(childEffect,0xC,0x180); W16(childEffect,0x12,5);
        Str(childEffect,0xF8,"SSP1"); W32(childEffect,0xFC,0x48);
        W32(childEffect,0x100,2); // billboard child
        W32(childEffect,0xF8+32,0x3F800000);
        W32(childEffect,0xF8+36,0x3F800000);
        W32(childEffect,0xF8+60,0x3F000000); // child after 50% life
        W16(childEffect,0xF8+64,5);
        W16(childEffect,0xF8+66,1);
        var childArchive = JpcArchive.Read(childEffect);
        var childConfig = JpcChildShapeProfile.Read(childArchive,0xCAFE)!;
        Check(childConfig.LifetimeFrames==5 && childConfig.ChildrenPerParent==1,
            "JPAC2 SSP1 child emitter profile");
        var childSimulator = new JpcParticleSimulator(
            JpcEmitterDefinition.Read(childArchive,0xCAFE),128,33) { ChildShape=childConfig };
        for(int i=0;i<16;i++)childSimulator.Update(1f/60f);
        bool hasChild=false;
        for(int i=0;i<childSimulator.ActiveCount;i++)hasChild|=childSimulator.GetParticle(i).IsChild;
        Check(hasChild,"JPA SSP1 deterministic child spawning");

        var force = new JpcParticleField(JpcForceKind.Directional,0,-1,0,0.1f);
        force.Sample(0,0,0,out float fx,out float fy,out float fz);
        Check(fx==0 && fy==-0.1f && fz==0,"JPA directional FLD-style force");
        var fieldSimulator=new JpcParticleSimulator(definition,16,123);
        fieldSimulator.AddField(force);fieldSimulator.Update(2f/60f);
        Check(fieldSimulator.GetParticle(0).Vy < 0.1f,
              "JPA external field influences particle velocity");
        var mipBti = new byte[0x60];
        Buffer.BlockCopy(bti, 0, mipBti, 0, bti.Length);
        mipBti[0x18] = 2;
        W16(mipBti, 2, 4); W16(mipBti, 4, 4);
        var gxPackage = BtiTexture.CreateNativePackage(mipBti)!;
        Check(gxPackage.Length == 32 + 64, "GX BTI two-level native mip payload");
        Check(gxPackage[0] == (byte)'D' && gxPackage[24] == 2, "GX DNTX v2 mip descriptor");
        var indexedBti = new byte[0x80];
        Buffer.BlockCopy(bti, 0, indexedBti, 0, bti.Length);
        indexedBti[0] = 8; indexedBti[9] = 1; W16(indexedBti, 0xA, 1);
        W32(indexedBti, 0xC, 0x20);
        W32(indexedBti, 0x1C, 0x40);
        var indexedPackage = BtiTexture.CreateNativePackage(indexedBti)!;
        Check(indexedPackage.Length == 32 + 64 && indexedPackage[8] == 6,
            "CI4 BTI expanded as native RGBA8 texture package");

        var indexedMipBti = new byte[0x80];
        indexedMipBti[0] = 8; W16(indexedMipBti, 2, 8); W16(indexedMipBti, 4, 8);
        indexedMipBti[9] = 1; W16(indexedMipBti, 0xA, 1);
        W32(indexedMipBti, 0xC, 0x20); W16(indexedMipBti, 0x20, 0xF800);
        indexedMipBti[0x18] = 2; W32(indexedMipBti, 0x1C, 0x40);
        var indexedMips = BtiTexture.CreateNativePackage(indexedMipBti)!;
        Check(indexedMips.Length == 32 + 256 + 64 && indexedMips[24] == 2 &&
              indexedMips[33] == 255 && indexedMips[32] == 255 &&
              indexedMips[289] == 255,
              "CI4 two-level mip chain preserves color in GX RGBA8 tiles");
        bad = (byte[])indexedMipBti.Clone(); W32(bad, 0x1C, 0x60);
        Reject(() => BtiTexture.CreateNativePackage(bad), "Indexed BTI truncated mips");
        var oddBti = (byte[])bti.Clone(); W16(oddBti, 2, 3); W16(oddBti, 4, 3);
        Check(BtiTexture.Read(oddBti).Width == 3 &&
              BtiTexture.CreateNativePackage(oddBti) == null,
              "Non-power-of-two BTI decodes with RGBA fallback");

        var j3dChannels = new Dolphin.Graphics.J3dMaterialChannels(2);
        var state = new float[12];
        j3dChannels.SetVisibility(0, false);
        j3dChannels.SetColor(0, new Dolphin.Graphics.Color32(255, 128, 0, 64));
        j3dChannels.SetTextureIndex(0, 1);
        j3dChannels.CopyMaterialState(0, state);
        Check(state[0] == 0 && state[1] == 1 && state[3] == 0, "J3D material visibility/color");
        Check(state[11] == 1 && state[4] > 0 && state[4] < 1, "J3D texture channel");
        j3dChannels.Apply(anim, 0, 0, 1f);
        j3dChannels.CopyMaterialState(0, state);
        Check(state[0] == 0, "J3D BVA applies material visibility");
        j3dChannels.Reset();
        j3dChannels.CopyMaterialState(0, state);
        Check(state[0] == 1 && state[11] == -1 && state[5] == 1 && state[9] == 1,
            "J3D reset source defaults");

        var btk = new byte[0x120];
        Str(btk, 0, "J3D1"); Str(btk, 4, "btk1");
        W32(btk, 8, btk.Length); W32(btk, 0xC, 1);
        Str(btk, 0x20, "TTK1"); W32(btk, 0x24, 0x100);
        W16(btk, 0x2A, 30); W16(btk, 0x2C, 3);
        W16(btk, 0x2E, 2); W16(btk, 0x30, 1); W16(btk, 0x32, 3);
        W32(btk, 0x34, 0x60); W32(btk, 0x44, 0xA0);
        W32(btk, 0x48, 0xB0); W32(btk, 0x4C, 0xB8); W32(btk, 0x50, 0xC0);
        for (int k = 0; k < 9; k++)
        {
            int rec = 0x20 + 0x60 + k * 6;
            W16(btk, rec, 1);
            int index = k == 3 ? 1 : k == 5 ? 1 : k == 8 ? 2 : 0;
            W16(btk, rec + 2, index);
        }
        W32(btk, 0x20 + 0xB0, 0x3F800000);
        W32(btk, 0x20 + 0xB4, 0x3F800000);
        W32(btk, 0x20 + 0xC0, 0x3E800000); // S +0.25
        W32(btk, 0x20 + 0xC4, unchecked((int)0xBE000000)); // T -0.125
        W32(btk, 0x3C, 0xD0); // TTK1 material name table
        W16(btk, 0xF0, 1); W16(btk, 0xF6, 8);
        Str(btk, 0xF8, "Body");
        var uvAnim = J3dMaterialAnimation.Read(btk);
        j3dChannels.Apply(uvAnim, 1, 0, 2f);
        j3dChannels.CopyMaterialState(1, state);
        Check(Math.Abs(state[7] - 0.25f) < 0.0001f, "BTK S translation");
        Check(Math.Abs(state[10] + 0.125f) < 0.0001f, "BTK T translation");
        Check(state[5] == 1 && state[9] == 1, "BTK affine identity scales");
        Check(uvAnim.GetTrackMaterialName(0) == "Body", "Named BTK material track");
        var j3dModel = new byte[0x80];
        Str(j3dModel, 0, "J3D2"); Str(j3dModel, 4, "bmd3");
        W32(j3dModel, 8, j3dModel.Length); W32(j3dModel, 0xC, 1);
        Str(j3dModel, 0x20, "MAT3"); W32(j3dModel, 0x24, 0x60);
        W16(j3dModel, 0x28, 1); W32(j3dModel, 0x34, 0x20);
        W16(j3dModel, 0x40, 1); W16(j3dModel, 0x46, 8);
        Str(j3dModel, 0x48, "Body");
        var materialNames = J3dMaterialNames.Read(j3dModel);
        Check(materialNames.Length == 1 && materialNames[0] == "Body",
            "MAT3 source-name extraction");
        Console.WriteLine("GameCubeFormatsTests passed: " + assertionCount + " assertions");
    }
}
