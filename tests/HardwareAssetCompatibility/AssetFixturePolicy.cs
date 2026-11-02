using System;

internal static class AssetFixturePolicy
{
    public const int MaxAssets=32, MaxImages=12, MaxScannedEntries=2048,
        MaxArchiveReads=16, MaxSourceBytes=96*1024, MaxModelBytes=8*1024*1024, MaxNestedDepth=6;
    public const int Texture=1, Particle=2, Model=3;
    public static bool DiscImage(string path)
        => path.EndsWith(".iso",StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".gcm",StringComparison.OrdinalIgnoreCase);
    public static bool Archive(string path)
        => path.EndsWith(".arc",StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".szs",StringComparison.OrdinalIgnoreCase);
    public static int Kind(string path)
    {
        if(path.EndsWith(".bti",StringComparison.OrdinalIgnoreCase))return Texture;
        if(path.EndsWith(".jpc",StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpa",StringComparison.OrdinalIgnoreCase))return Particle;
        if(path.EndsWith(".bmd",StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bdl",StringComparison.OrdinalIgnoreCase))return Model;
        return 0;
    }
    public static bool ValidHeader(int kind, byte[] first)
    {
        if(first==null||first.Length<8)return false;
        if(kind==Texture)return first.Length>=0x20;
        if(kind==Particle)return Begins(first,"JPAC1-00")||Begins(first,"JPAC2-10");
        if(kind==Model)return Begins(first,"J3D1")||Begins(first,"J3D2");
        return false;
    }
    private static bool Begins(byte[] bytes,string text)
    {
        if(bytes.Length<text.Length)return false;
        for(int i=0;i<text.Length;i++)if(bytes[i]!=(byte)text[i])return false;
        return true;
    }
}
