using Dolphin.Diagnostics;
using Dolphin.Threading.Tasks;

internal static class BenchmarkOptions
{
#if DND_TEXTURE_OPT_12 || DND_TEXTURE_OPT_13 || DND_TEXTURE_OPT_14 || DND_TEXTURE_OPT_15
    internal const bool Matrix=true;
#if DND_TEXTURE_OPT_13 || DND_TEXTURE_OPT_14 || DND_TEXTURE_OPT_15
    internal const int MatrixBudget=384;
#else
    internal const int MatrixBudget=256;
#endif
    internal static int Mode;
#else
    internal const bool Matrix=false;
    internal const int MatrixBudget=256;
#if DND_TEXTURE_OPT_1
    internal const int Mode=1;
#elif DND_TEXTURE_OPT_2
    internal const int Mode=2;
#elif DND_TEXTURE_OPT_3
    internal const int Mode=3;
#elif DND_TEXTURE_OPT_4
    internal const int Mode=4;
#elif DND_TEXTURE_OPT_5
    internal const int Mode=5;
#elif DND_TEXTURE_OPT_6
    internal const int Mode=6;
#elif DND_TEXTURE_OPT_7
    internal const int Mode=7;
#elif DND_TEXTURE_OPT_8
    internal const int Mode=8;
#elif DND_TEXTURE_OPT_9
    internal const int Mode=9;
#elif DND_TEXTURE_OPT_10
    internal const int Mode=10;
#elif DND_TEXTURE_OPT_11
    internal const int Mode=11;
#elif DND_TEXTURE_OPT_18
    internal const int Mode=18;
#elif DND_TEXTURE_OPT_19
    internal const int Mode=19;
#elif DND_TEXTURE_OPT_25
    internal const int Mode=25;
#elif DND_TEXTURE_OPT_26
    internal const int Mode=26;
#elif DND_TEXTURE_OPT_27
    internal const int Mode=27;
#elif DND_TEXTURE_OPT_28
    internal const int Mode=28;
#elif DND_TEXTURE_OPT_29
    internal const int Mode=29;
#elif DND_TEXTURE_OPT_31
    internal const int Mode=31;
#elif DND_TEXTURE_OPT_32
    internal const int Mode=32;
#elif DND_TEXTURE_OPT_33
    internal const int Mode=33;
#elif DND_TEXTURE_OPT_34
    internal const int Mode=34;
#elif DND_TEXTURE_OPT_35
    internal const int Mode=35;
#elif DND_TEXTURE_OPT_36
    internal const int Mode=36;
#elif DND_TEXTURE_OPT_37
    internal const int Mode=37;
#else
    internal const int Mode=0;
#endif
#endif
    internal static void SelectCase(int index)
    {
#if DND_TEXTURE_OPT_12 || DND_TEXTURE_OPT_13 || DND_TEXTURE_OPT_14 || DND_TEXTURE_OPT_15
#if DND_TEXTURE_OPT_15
        Mode=index==0?0:index==1?31:index==2?32:index==3?33:index==4?34:index==5?35:index==6?36:index==7?37:34;
#elif DND_TEXTURE_OPT_14
        Mode=index==0?0:index==1?19:index==2?25:index==3?18:index==4?26:index==5?27:index==6?28:index==7?29:26;
#else
        Mode=index==0?0:index==1?14:index==2?2:index==3?15:index==4?16:index==5?17:index==6?7:index==7?9:11;
#endif
        Configure();
#endif
    }
    internal static bool FastRows=>Mode==9||Mode==11||Mode==18||Mode==26||Mode==27||Mode==28||Mode==29||Mode==33||Mode==34||Mode==35||Mode==36;
    internal static int Depth=>Mode==29||Mode==35?5:Mode==5||Mode==8||Mode==11||Mode==18||Mode==26||Mode==28||Mode==34||Mode==36||Mode==37?4:Mode==3||Mode==4||Mode==6||Mode==7||Mode==9||Mode==10||Mode==16||Mode==17||Mode==27||Mode==33?2:1;
    internal static int Publications=>Mode==8||Mode==11||Mode==18||Mode==26||Mode==28||Mode==29||Mode==34||Mode==35||Mode==36||Mode==37?4:Mode==4||Mode==5||Mode==6||Mode==7||Mode==9||Mode==10||Mode==17||Mode==27||Mode==33?2:1;
    internal static bool Reuse=>Mode>=33||Mode==6||Mode==7||Mode==8||Mode==9||Mode==10||Mode==11||Mode==18||Mode==26||Mode==27||Mode==28||Mode==29;
    internal static bool ReuseClear=>Mode>=33||Mode==2||Mode==7||Mode==8||Mode==9||Mode==10||Mode==11||Mode==15||Mode==16||Mode==17||Mode==18||Mode==26||Mode==27||Mode==28||Mode==29;
    internal static int PublishBudgetUs=>Mode==10||Mode==28||Mode==36?750:1500;
    internal static void Configure()
    {
        GpuProfiler.UseNative2DRectangles=Mode>=31;
        GpuProfiler.OverlayGlyphs=Mode>=32||Mode==25||Mode==26||Mode==27||Mode==28||Mode==29?OverlayGlyphMode.Rectangles:Mode==18||Mode==19?OverlayGlyphMode.HorizontalRuns:OverlayGlyphMode.Pixels;
        Scheduler.ProductiveWaits=Mode>=33||Mode==14||Mode==15||Mode==16||Mode==17||Mode==7||Mode==8||Mode==9||Mode==10||Mode==11||Mode==18||Mode==26||Mode==27||Mode==28||Mode==29;
        Scheduler.ApplicationTimers=Scheduler.CompletionAwareWaits=Mode!=1;
        Scheduler.DeadlineWaits=Mode!=1;
        GpuProfiler.ReuseFrameClear=ReuseClear;
    }
}
