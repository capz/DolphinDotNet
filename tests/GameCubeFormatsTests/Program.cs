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
        Console.WriteLine("GameCubeFormatsTests passed: " + assertionCount + " assertions");
    }
}
