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
        var bfn = new byte[0x40];
        Str(bfn, 0, "FONTbfn1"); W32(bfn, 8, 0x40); W32(bfn, 0xC, 1);
        Str(bfn, 0x20, "INF1"); W32(bfn, 0x24, 0x20);
        W16(bfn, 0x2A, 10); W16(bfn, 0x2C, 2); W16(bfn, 0x2E, 8);
        W16(bfn, 0x30, 12); W16(bfn, 0x32, 0xAB);
        var font = BfnFont.Read(bfn);
        Check(font.Ascent == 10 && font.LineHeight == 12, "BFN metrics");
        Check(font.GlyphForEncodedCharacter(0x41) == 0xAB && font.WidthForGlyph(3) == 8,
            "BFN default glyph and width");
        Check(GameCubeFormatDetector.Detect(bmg) == GameCubeFileFormat.Bmg, "BMG detection");
        Check(GameCubeFormatDetector.Detect(jpc) == GameCubeFileFormat.Jpc, "JPAC detection");
        Check(GameCubeFormatDetector.Detect(bti, "texture.bti") == GameCubeFileFormat.Bti,
            "Hinted BTI detection");
        var effect = new byte[0x138];
        Str(effect, 0, "JPAC2-10"); W16(effect, 8, 1); W16(effect, 0xA, 1);
        W32(effect, 0xC, 0xD8);
        W16(effect, 0x10, 0xCAFE); W16(effect, 0x12, 3); effect[0x16] = 1;
        Str(effect, 0x18, "BEM1"); W32(effect, 0x1C, 0x80);
        W32(effect, 0x20, 4 << 21); // point volume
        W32(effect, 0x18 + 0x4C, 0x3F800000); // one particle/frame
        W32(effect, 0x18 + 0x34, 0x3C23D70A); // velocity
        W32(effect, 0x18 + 0x60, 0x3F800000); // momentum retention
        W32(effect, 0x18 + 0x28 + 4, 0x3F800000); // direction +Y
        W16(effect, 0x18 + 0x72, 30); W16(effect, 0x18 + 0x6E, 10);
        Str(effect, 0x98, "BSP1"); W32(effect, 0x9C, 0x34);
        W32(effect, 0xA0, unchecked((int)(2u << 28))); // billboard
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
        Check(BtiTexture.CreateNativePackage(indexedBti) == null, "Indexed BTI requires palette expansion");
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
