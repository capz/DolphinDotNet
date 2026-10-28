using System;

internal static class BenchmarkSource
{
    internal static void Fill(byte[] pixels, int id, int group, int offset=0)
    {
        int width = group * 32, ox = (id % (48 / group)) * group, oy = (id / (48 / group)) * group;
        for (int y = 0; y < width; y++) for (int x = 0; x < width; x++)
        {
            int cx = ox + x / 32, cy = oy + y / 32, lx = x % 32, ly = y % 32;
            bool bright = (((lx / 8) ^ (ly / 8) ^ (cx * 3 + cy)) & 1) != 0;
            int at = offset+(y * width + x) * 4;
            pixels[at] = (byte)TextureSource.Red(cx, bright);
            pixels[at + 1] = (byte)TextureSource.Green(cy, bright);
            pixels[at + 2] = (byte)(lx == 0 || ly == 0 ? 245 : bright ? 184 : 84);
            pixels[at + 3] = 255;
        }
    }
    // Same texels and output allocation as Fill. Generate the three distinct
    // rows of each cell, then use the SDK's checked native Array.Copy path.
    internal static void FillRows(byte[] pixels, int id, int group, int offset=0)
    {
        int width=group*32, ox=(id%(48/group))*group, oy=(id/(48/group))*group;
        for(int cy=0;cy<group;cy++)for(int cx=0;cx<group;cx++)
        {
            int origin=offset+(cy*32*width+cx*32)*4;
            for(int kind=0;kind<3;kind++)
            {
                int y=kind==0?0:kind==1?1:8;
                for(int x=0;x<32;x++)
                {
                    bool bright=(((x/8)^(y/8)^((ox+cx)*3+oy+cy))&1)!=0;
                    int at=origin+(y*width+x)*4;
                    pixels[at]=(byte)TextureSource.Red(ox+cx,bright);
                    pixels[at+1]=(byte)TextureSource.Green(oy+cy,bright);
                    pixels[at+2]=(byte)(x==0||y==0?245:bright?184:84);
                    pixels[at+3]=255;
                }
            }
            for(int y=2;y<32;y++)if(y!=8)
                Array.Copy(pixels,origin+(((y/8)&1)==0?1:8)*width*4,pixels,origin+y*width*4,128);
        }
    }
}
