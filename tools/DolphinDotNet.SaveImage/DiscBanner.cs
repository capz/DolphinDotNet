using System.Text;

namespace DolphinDotNet.SaveImage;

public static class DiscBanner
{
    // Layout verified against Dolphin's DiscIO/VolumeGC.h GCBanner structure.
    public static byte[] Encode(byte[] image, bool pal, string title, string maker,
        string longTitle, string longMaker, string description)
    {
        if (image.Length != 6144) throw new ArgumentException("Disc artwork must be 96x32 RGB5A3.");
        var output = new byte[pal ? 0x1fa0 : 0x1960];
        Encoding.ASCII.GetBytes(pal ? "BNR2" : "BNR1").CopyTo(output, 0);
        image.CopyTo(output, 0x20);
        for (int language = 0; language < (pal ? 6 : 1); language++)
        {
            int offset = 0x1820 + language * 0x140;
            Put(title, offset, 32);
            Put(maker, offset + 32, 32);
            Put(longTitle.Length == 0 ? title : longTitle, offset + 64, 64);
            Put(longMaker.Length == 0 ? maker : longMaker, offset + 128, 64);
            Put(description, offset + 192, 128);
        }
        return output;

        void Put(string value, int offset, int capacity)
        {
            if (value.Length >= capacity || value.Any(c => c == 0 || c > 255))
                throw new ArgumentException($"Disc metadata must be Latin-1, without NUL, and fit {capacity - 1} bytes.");
            Encoding.Latin1.GetBytes(value).CopyTo(output, offset);
        }
    }
}
