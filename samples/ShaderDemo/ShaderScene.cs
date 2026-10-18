using System;
using Dolphin.Graphics;
using Dolphin.Graphics.Raw;
using static Dolphin.Graphics.Raw.GXConstants;

/// <summary>Three real GX pixel-combiner programs over identical raster input.
/// CPU work supplies geometry and a few uniform registers; TEV shades fragments.</summary>
internal sealed class ShaderScene : IDisposable
{
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly NativeBuffer projection=NativeBuffer.Allocate(64),model=NativeBuffer.Allocate(48);
    private readonly ShaderHud hud;

    internal ShaderScene()
    {
        GU.guOrtho(projection.Address,0,480,0,640,-1,1);
        GU.guMtxIdentity(model.Address);
        projection.Flush();model.Flush();
        hud=new ShaderHud(device);
    }
    internal void Present(float wave,bool paused)
    {
        device.BeginFrame(new Color32(8,13,26));
        device.BeginRaw();
        GX.GX_LoadProjectionMtx(projection.Address,GX_ORTHOGRAPHIC);
        GX.GX_LoadPosMtxImm(model.Address,GX_PNMTX0);GX.GX_SetCurrentMtx(GX_PNMTX0);
        GX.GX_SetViewport(0,0,device.Width,device.Height,0,1);
        GX.GX_SetScissor(0,0,(uint)device.Width,(uint)device.Height);
        GX.GX_SetCullMode(GX_CULL_NONE);GX.GX_SetZMode(GX_FALSE,GX_ALWAYS,GX_FALSE);
        GX.GX_SetBlendMode(GX_BM_NONE,GX_BL_ONE,GX_BL_ZERO,GX_LO_COPY);
        GX.GX_SetColorUpdate(GX_TRUE);GX.GX_SetAlphaUpdate(GX_TRUE);GX.GX_SetDither(GX_FALSE);
        GX.GX_SetAlphaCompare(GX_ALWAYS,0,GX_AOP_AND,GX_ALWAYS,0);
        GX.GX_SetFog(GX_FOG_NONE,0,1,0,1,new GXColor(0,0,0,0));
        GX.GX_SetNumChans(1);GX.GX_SetNumTexGens(0);GX.GX_SetNumIndStages(0);
        GX.GX_SetChanCtrl(GX_COLOR0A0,GX_DISABLE,GX_SRC_REG,GX_SRC_VTX,0,GX_DF_NONE,GX_AF_NONE);
        GX.GX_ClearVtxDesc();
        GX.GX_SetVtxDesc(GX_VA_POS,GX_DIRECT);GX.GX_SetVtxDesc(GX_VA_CLR0,GX_DIRECT);
        GX.GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_POS,GX_POS_XYZ,GX_F32,0);
        GX.GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_CLR0,GX_CLR_RGBA,GX_RGBA8,0);
        GX.GX_SetTevSwapModeTable(GX_TEV_SWAP0,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_ALPHA);
        for(byte stage=0;stage<2;stage++)
        {
            GX.GX_SetTevDirect(stage);GX.GX_SetTevSwapMode(stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
            GX.GX_SetTevOrder(stage,GX_TEXCOORDNULL,GX_TEXMAP_NULL,GX_COLOR0A0);
            GX.GX_SetTevAlphaIn(stage,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO,GX_CA_RASA);
            GX.GX_SetTevAlphaOp(stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        }
        Reference();Shape(112,wave);
        Ramp(wave);Shape(320,wave);
        Toon(wave);Shape(528,wave);
        device.EndRaw();
        // Managed drawing reestablishes its own TEV/pass state after raw GX.
        hud.Present(paused);
        device.EndFrame();
    }
    private static void ColorOp(byte stage,byte op)
        =>GX.GX_SetTevColorOp(stage,op,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    private static void Reference()
    {
        GX.GX_SetNumTevStages(1);
        GX.GX_SetTevColorIn(0,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_RASC);
        ColorOp(0,GX_TEV_ADD);
    }
    private static void Ramp(float wave)
    {
        // result = dark * (1 - raster.rgb) + bright * raster.rgb
        GX.GX_SetNumTevStages(1);
        GX.GX_SetTevColor(GX_TEVREG0,new GXColor(12,18,58,255));
        GX.GX_SetTevColor(GX_TEVREG1,new GXColor((byte)(60+80*wave),(byte)(180+50*wave),240,255));
        GX.GX_SetTevColorIn(0,GX_CC_C0,GX_CC_C1,GX_CC_RASC,GX_CC_ZERO);
        ColorOp(0,GX_TEV_ADD);
    }
    private static void Toon(float wave)
    {
        // Comparison stages implement base + step(t1,I)*low + step(t2,I)*high.
        // The R8 comparison is evaluated per fragment, after raster interpolation.
        GX.GX_SetNumTevStages(2);
        byte low=(byte)(65+40*wave),high=(byte)(165+40*wave);
        GX.GX_SetTevColor(GX_TEVREG0,new GXColor(low,low,low,255));
        GX.GX_SetTevColor(GX_TEVREG1,new GXColor(12,24,64,255));
        GX.GX_SetTevColor(GX_TEVREG2,new GXColor(high,high,high,255));
        GX.GX_SetTevKColor(GX_KCOLOR0,new GXColor(18,66,94,255));
        GX.GX_SetTevKColor(GX_KCOLOR1,new GXColor(40,130,90,255));
        GX.GX_SetTevKColorSel(0,GX_TEV_KCSEL_K0);
        GX.GX_SetTevColorIn(0,GX_CC_RASC,GX_CC_C0,GX_CC_KONST,GX_CC_C1);
        ColorOp(0,GX_TEV_COMP_R8_GT);
        GX.GX_SetTevKColorSel(1,GX_TEV_KCSEL_K1);
        GX.GX_SetTevColorIn(1,GX_CC_RASC,GX_CC_C2,GX_CC_KONST,GX_CC_CPREV);
        ColorOp(1,GX_TEV_COMP_R8_GT);
    }
    private static void Vertex(float x,float y,byte intensity)
    {
        GX.GX_Position3f32(x,y,0);GX.GX_Color4u8(intensity,intensity,intensity,255);
    }
    private static void Shape(float center,float wave)
    {
        // Identical 32-triangle grayscale disks and ramps in each panel.
        // This is a shading illustration, not a physically lit sphere.
        float x=0,y=-78;
        GX.GX_Begin(GX_TRIANGLES,GX_VTXFMT0,102);
        for(int i=0;i<32;i++)
        {
            float nextX=x*0.98078528f-y*0.19509032f,nextY=x*0.19509032f+y*0.98078528f;
            Vertex(center-20+40*wave,240,255);
            Vertex(center+x,252+y,0);Vertex(center+nextX,252+nextY,0);
            x=nextX;y=nextY;
        }
        Vertex(center-78,340,0);Vertex(center+78,340,255);Vertex(center+78,354,255);
        Vertex(center-78,340,0);Vertex(center+78,354,255);Vertex(center-78,354,0);
        GX.GX_End();
    }
    public void Dispose(){hud.Dispose();model.Dispose();projection.Dispose();}
}
