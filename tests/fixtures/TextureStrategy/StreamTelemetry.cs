using Dolphin.Graphics;

// A tiny frame-local binary strip gives CI actual guest counters without OCR,
// a debugger stub, or accepting a blank/error screen. It is drawn after the world.
internal sealed class StreamTelemetry
{
    internal const int Fields = 32;
    internal readonly int[] Values = new int[Fields];
    private readonly Draw2D draw = new Draw2D(GraphicsDevice.Default,
        new Draw2DOptions { Width = 640, Height = 480, VertexCapacity = 4096 });
    internal readonly int[] TimingValues = new int[16];
    internal bool TimingEnabled;
    internal void Present()
    {
        draw.Begin();
        for (int field = 0; field < Fields; field++)
        {
            int value = Values[field];
            if (value < 0) value = 0;
            if (value > 65535) value = 65535;
            for (int bit = 0; bit < 16; bit++)
            {
                int index = field * 16 + bit;
                byte shade = (byte)((value & (1 << bit)) != 0 ? 240 : 16);
                draw.FillRectangle(new RectF((index % 64) * 10, 448 + (index / 64) * 4, 10, 4),
                    new Color32(shade, shade, shade));
            }
        }
        if(TimingEnabled) for(int field=0;field<16;field++)
        {
            int value=TimingValues[field];if(value<0)value=0;if(value>65535)value=65535;
            for(int bit=0;bit<16;bit++)
            {
                int index=field*16+bit;byte shade=(byte)((value&(1<<bit))!=0?240:16);
                draw.FillRectangle(new RectF((index%64)*10,432+(index/64)*4,10,4),new Color32(shade,shade,shade));
            }
        }
        draw.End();
    }
    internal void Dispose() { draw.Dispose(); }
}
