/* Included by the real GX backend and the recording GX test backend. Keep this
 * combiner independent of texture binding, geometry and material render state. */
void dnd_draw_backend_tev(const DndDrawEffect *effect,bool texture_bound) {
    /* Callers establish ordinary material alpha state on each material/pass
     * transition. A v4 program may override it without changing that material. */
    const DndEffectAlpha *alpha=effect?effect->alpha:NULL;
    if(alpha&&alpha->compare_enabled)
        GX_SetAlphaCompare(alpha->compare0,alpha->reference0,alpha->operation,alpha->compare1,alpha->reference1);
    if(alpha&&alpha->blend_enabled)
        GX_SetBlendMode(GX_BM_BLEND,alpha->blend_source,alpha->blend_destination,GX_LO_CLEAR);
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
        const DndTevAlphaStage *a=alpha?&alpha->stages[i]:NULL;
        bool explicit_alpha=a&&(a->control&128);
        bool secondary=s&&s->reserved==1,alt=s&&s->reserved==2;
        bool sampled=s&&(s->a==GX_CC_TEXC||s->a==9||s->b==GX_CC_TEXC||s->b==9||s->c==GX_CC_TEXC||s->c==9||s->d==GX_CC_TEXC||s->d==9);
        if(explicit_alpha&&(a->a==GX_CA_TEXA||a->b==GX_CA_TEXA||a->c==GX_CA_TEXA||a->d==GX_CA_TEXA))sampled=true;
        // The final v3 epilogue samples unwarped TEX0 alpha, preserving
        // transparent outlines independently of the displaced RGB samples.
        if(warp&&sampled)
            GX_SetTevIndWarp(stage,GX_INDTEXSTAGE0,GX_TRUE,GX_FALSE,GX_ITM_0);
        else GX_SetTevDirect(stage);
        GX_SetTevSwapMode(stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
        GX_SetTevOrder(stage,secondary||alt?GX_TEXCOORD1:texture_bound?GX_TEXCOORD0:GX_TEXCOORDNULL,
                       secondary?GX_TEXMAP1:texture_bound?GX_TEXMAP0:GX_TEXMAP_NULL,GX_COLOR0A0);
        GX_SetTevKColorSel(stage,effect?effect->stages[i].konst:GX_TEV_KCSEL_1);
        GX_SetTevKAlphaSel(stage,explicit_alpha?a->konst:GX_TEV_KASEL_1);
        if(effect) {
            GX_SetTevColorIn(stage,s->a,s->b,s->c,s->d);
            if(s->dest&128)
                GX_SetTevColorOp(stage,s->op,(s->dest>>2)&3,(s->dest>>4)&3,(s->dest&64)?GX_TRUE:GX_FALSE,s->dest&3);
            else GX_SetTevColorOp(stage,s->op,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,s->dest);
        } else {
            GX_SetTevColorIn(stage,GX_CC_ZERO,texture_bound?GX_CC_TEXC:GX_CC_ZERO,
                             texture_bound?GX_CC_RASC:GX_CC_ZERO,texture_bound?GX_CC_ZERO:GX_CC_RASC);
            GX_SetTevColorOp(stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        }
        if(explicit_alpha){
            GX_SetTevAlphaIn(stage,a->a,a->b,a->c,a->d);
            GX_SetTevAlphaOp(stage,a->op,(a->control>>2)&3,(a->control>>4)&3,(a->control&64)?GX_TRUE:GX_FALSE,a->control&3);
        }else{
        /* Preserve legacy opacity even when the RGB expression ignores TEX0. */
        GX_SetTevAlphaIn(stage,GX_CA_ZERO,texture_bound?GX_CA_TEXA:GX_CA_ZERO,
                         texture_bound?GX_CA_RASA:GX_CA_ZERO,texture_bound?GX_CA_ZERO:GX_CA_RASA);
        GX_SetTevAlphaOp(stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        }
    }
}
