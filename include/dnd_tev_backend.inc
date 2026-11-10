/* Included by the real GX backend and the recording GX test backend. Keep this
 * combiner independent of texture binding, geometry and material render state. */
void dnd_draw_backend_tev(const DndDrawEffect *effect,bool texture_bound) {
    /* Raw passes may leave indirect texturing enabled. Every shared combiner
     * invocation, including imported-model draws, owns this global state. */
    bool warp=effect&&effect->indirect_strength!=0;
    GX_SetNumIndStages(warp?1:0);
    if(warp){
        // DNEF v3 uses TEXMAP1 as a signed, scrolling 2D displacement field.
        // The two main color layers both sample TEXMAP0 via independent texgens.
        f32 gain=(f32)effect->indirect_strength/255.f*.35f;
        f32 offsets[2][3]={{gain,0,0},{0,gain,0}};
        GX_SetIndTexOrder(GX_INDTEXSTAGE0,GX_TEXCOORD1,GX_TEXMAP1);
        GX_SetIndTexMatrix(GX_ITM_0,offsets,-1);
    }
    GX_SetTevSwapModeTable(GX_TEV_SWAP0,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_ALPHA);
    /* DNEF rejects reads of uninitialized RGB registers. Clear them anyway so
     * every draw also starts with deterministic alpha/register/konst state. */
    for(int i=0;i<4;i++) {
        GX_SetTevColor((u8)i,(GXColor){0,0,0,0});
        unsigned packed=effect&&i<effect->uniforms?effect->colors[i]:0;
        GX_SetTevKColor((u8)i,(GXColor){(u8)(packed>>24),(u8)(packed>>16),(u8)(packed>>8),(u8)packed});
    }
    int count=effect?effect->count:1;
    GX_SetNumTevStages((u8)count);
    for(int i=0;i<count;i++) {
        u8 stage=(u8)i;
        const DndTevStage *s=effect?&effect->stages[i]:NULL;
        bool secondary=s&&s->reserved==1,alt=s&&s->reserved==2;
        bool sampled=s&&(s->a==GX_CC_TEXC||s->b==GX_CC_TEXC||s->c==GX_CC_TEXC||s->d==GX_CC_TEXC);
        // The final v3 epilogue samples unwarped TEX0 alpha, preserving
        // transparent outlines independently of the displaced RGB samples.
        if(warp&&sampled)
            GX_SetTevIndWarp(stage,GX_INDTEXSTAGE0,GX_TRUE,GX_FALSE,GX_ITM_0);
        else GX_SetTevDirect(stage);
        GX_SetTevSwapMode(stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
        GX_SetTevOrder(stage,secondary||alt?GX_TEXCOORD1:texture_bound?GX_TEXCOORD0:GX_TEXCOORDNULL,
                       secondary?GX_TEXMAP1:texture_bound?GX_TEXMAP0:GX_TEXMAP_NULL,GX_COLOR0A0);
        GX_SetTevKColorSel(stage,effect?effect->stages[i].konst:GX_TEV_KCSEL_1);
        GX_SetTevKAlphaSel(stage,GX_TEV_KASEL_1);
        if(effect) {
            GX_SetTevColorIn(stage,s->a,s->b,s->c,s->d);
            GX_SetTevColorOp(stage,s->op,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,s->dest);
        } else {
            GX_SetTevColorIn(stage,GX_CC_ZERO,texture_bound?GX_CC_TEXC:GX_CC_ZERO,
                             texture_bound?GX_CC_RASC:GX_CC_ZERO,texture_bound?GX_CC_ZERO:GX_CC_RASC);
            GX_SetTevColorOp(stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        }
        /* Preserve authored opacity even when the RGB expression ignores TEX0. */
        GX_SetTevAlphaIn(stage,GX_CA_ZERO,texture_bound?GX_CA_TEXA:GX_CA_ZERO,
                         texture_bound?GX_CA_RASA:GX_CA_ZERO,texture_bound?GX_CA_ZERO:GX_CA_RASA);
        GX_SetTevAlphaOp(stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    }
}
