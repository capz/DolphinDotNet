using System.IO;

static void Check(bool value, string label) { if (!value) throw new Exception(label); }
foreach (bool atlas in new[] { false, true })
{
    var cache = new ResidencySet(atlas);
    for (int i = 0; i < cache.Capacity; i++) cache.Commit(i, i, i);
    Check(cache.Bytes == ResidencySet.Budget, "Equal native-byte budgets");
    var wanted = Enumerable.Range(0, cache.Capacity).ToArray();
    Check(cache.Victim(wanted, wanted.Length) == -1, "Visible set must not thrash");
    wanted[0] = 1000;
    Check(cache.Victim(wanted, wanted.Length) == 0, "Evict the unneeded resource");
    cache.Remove(0); cache.Commit(0, 1000, 500);
    Check(cache.Bytes == ResidencySet.Budget && cache.Find(0) == -1 && cache.Find(1000) == 0, "Replacement ownership");
    cache.Reset(!atlas);
    Check(cache.Count == 0 && cache.Bytes == 0 && cache.Find(1000) == -1, "Mode change clears old IDs");
}
var page = new byte[4096];
var atlasPixels = new byte[65536];
// Independent layouts must contain identical cell pixels for every cell in a patch.
for (int tileY = 0; tileY < 8; tileY++) for (int tileX = 0; tileX < 12; tileX++)
{
    TextureSource.Fill(atlasPixels, tileY * 12 + tileX, true, false);
    Check(TextureSource.Matches(atlasPixels, tileY * 12 + tileX, true), "Atlas identity validation");
    for (int dy = 0; dy < 4; dy++) for (int dx = 0; dx < 4; dx++)
    {
        int id = (tileY * 4 + dy) * 48 + tileX * 4 + dx;
        TextureSource.Fill(page, id, false, false);
        Check(TextureSource.Matches(page, id, false), "Page identity validation");
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            for (int c = 0; c < 4; c++)
                Check(page[(y * 32 + x) * 4 + c] == atlasPixels[((dy * 32 + y) * 128 + dx * 32 + x) * 4 + c],
                    "Page/atlas image mismatch");
    }
}
try { TextureSource.Fill(page, 0, false, true); throw new Exception("Failure injection did not fail"); }
catch (IOException) { }
TextureSource.Fill(page, 0, false, false);
Check(page[3] == 255 && TextureSource.Coarse().Length == 48 * 32 * 4, "Retry and permanent coarse representation");
Console.WriteLine("Texture streaming residency and source tests: PASS");
