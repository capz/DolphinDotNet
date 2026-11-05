/* Authored material state inside the existing model draw path. Geometry still
 * comes from validated triangles; no source GX command stream is submitted. */
static void authored_model_material(const DndAuthoredMaterial *m,const DndMaterial *material,
                                    const float normal[16],bool custom_scene,const DndInstance *instance,unsigned material_index) {
  GX_SetCullMode(m->cull);
  GX_SetZMode(m->depth[0],m->depth[1],m->depth[2]);
  GX_SetZCompLoc(m->depth_location);GX_SetDither(m->dither);
  GX_SetBlendMode(m->blend[0],m->blend[1],m->blend[2],m->blend[3]);
  GX_SetAlphaCompare(m->alpha[0],m->alpha[1],m->alpha[2],m->alpha[3],m->alpha[4]);
  bool fading=instance->color_set&&(instance->color_set[material_index*4]&2)&&instance->colors[material_index*16+3]<255;
  if(fading) {
    GX_SetBlendMode(GX_BM_BLEND,GX_BL_SRCALPHA,GX_BL_INVSRCALPHA,0);
    GX_SetZMode(m->depth[0],m->depth[1],GX_FALSE);
    GX_SetAlphaCompare(GX_ALWAYS,0,0,GX_ALWAYS,0);
  }
  GXColor tint;
  u8 *channels=(u8*)&tint;
  for(unsigned c=0;c<4;c++)channels[c]=(u8)(fminf(1,fmaxf(0,material->color[c]))*255);
  GX_SetNumChans(1);GX_SetChanMatColor(GX_COLOR0A0,tint);
  if(!custom_scene) {
    GX_SetChanAmbColor(GX_COLOR0A0,(GXColor){0,0,0,0});
    if(m->channel[0][0]||m->channel[1][0]) {
      GXLightObj light={0};GX_InitLightPos(&light,0,0,1000000);
      GX_InitLightColor(&light,(GXColor){255,255,255,255});
      GX_InitLightAttn(&light,1,0,0,1,0,0);GX_LoadLightObj(&light,GX_LIGHT0);
    }
  }
  for(unsigned ch=0;ch<2;ch++) {
    const uint8_t *c=m->channel[ch];
    GX_SetChanCtrl(ch?GX_ALPHA0:GX_COLOR0,c[0],GX_SRC_REG,c[1],c[2],c[3],c[4]);
  }
  Mtx n;
  for(unsigned r=0;r<3;r++)for(unsigned c=0;c<4;c++)n[r][c]=c<3?normal[r*4+c]:0;
  GX_LoadNrmMtxImm(n,GX_PNMTX0);
  GX_SetVtxDesc(GX_VA_NRM,GX_DIRECT);
  GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_NRM,GX_NRM_XYZ,GX_F32,0);
  GX_SetNumTexGens(m->texgens);
  if(m->texgens) {
    if(m->texmatrix_active) {
      Mtx t;memcpy(t,m->texmatrix,sizeof(t));GX_LoadTexMtxImm(t,30,1);
    }
    GX_SetTexCoordGen(GX_TEXCOORD0,GX_TG_MTX2x4,m->texsource,m->texmatrix_active?30:GX_IDENTITY);
  }
  GX_SetTevOrder(0,m->texgens?m->order[0]:GX_TEXCOORDNULL,
                 material->texture>=0?m->order[1]:GX_TEXMAP_NULL,m->order[2]);
  for(unsigned reg=0;reg<3;reg++) {
    const int16_t *c=m->colors[reg];GXColorS10 color={c[0],c[1],c[2],c[3]};
    unsigned index=material_index*4+reg+1;
    if(instance->color_set&&instance->color_set[index]) {
      const float *v=instance->colors+index*4;color=(GXColorS10){(int16_t)v[0],(int16_t)v[1],(int16_t)v[2],(int16_t)v[3]};
    }
    GX_SetTevColorS10((u8)(reg+1),color);
  }
  const DndAuthoredTev *rgb=&m->rgb,*alpha=&m->opacity;
  GX_SetTevColorIn(0,rgb->input[0],rgb->input[1],rgb->input[2],rgb->input[3]);
  GX_SetTevColorOp(0,rgb->operation,rgb->bias,rgb->scale,rgb->clamp,rgb->output);
  GX_SetTevAlphaIn(0,alpha->input[0],alpha->input[1],alpha->input[2],alpha->input[3]);
  GX_SetTevAlphaOp(0,alpha->operation,alpha->bias,alpha->scale,alpha->clamp,alpha->output);
  if(fading) {
    /* Keep stage zero and every authored RGB/TEV register unchanged. Opaque
     * source alpha is immaterial; translucent sources retain their alpha mask. */
    GX_SetNumTevStages(2);GX_SetTevDirect(1);GX_SetTevSwapMode(1,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder(1,GX_TEXCOORDNULL,GX_TEXMAP_NULL,GX_COLORNULL);
    GX_SetTevKColor(GX_KCOLOR3,(GXColor){0,0,0,(u8)instance->colors[material_index*16+3]});
    GX_SetTevKAlphaSel(1,GX_TEV_KASEL_K3_A);
    GX_SetTevColorIn(1,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
    GX_SetTevColorOp(1,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevAlphaIn(1,GX_CA_ZERO,m->blend[0]?GX_CA_APREV:GX_CA_ZERO,
                     GX_CA_KONST,m->blend[0]?GX_CA_ZERO:GX_CA_KONST);
    GX_SetTevAlphaOp(1,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
  }

}
