/* Static uniform-color models can point GX directly at immutable big-endian
 * DNMD attributes (80-byte stride). No expanded copy or per-frame allocation.
 * Evaluated skin buffers provide native position/normal pairs at stride 24.
 * Keep the direct path for animated UV/color channels, nonuniform
 * colors, non-unit normals and lit nonuniform/sheared transforms. */
static bool indexed_model(const DndMesh *mesh,const DndMaterial *material,
                          const float normal[16],bool custom_scene,bool animated,const float *skin,unsigned bindings) {
#if !defined(DND_INDEXED_MODELS) || DND_INDEXED_MODELS
  bool lit=!(material->mode&2);
  if(!indexed_models_enabled||(mesh->skin>=0&&!skin)||mesh->vertices>65536||!mesh->uniform_color||animated||custom_scene)return false;
  float scale=1;
  if(lit){
    if(!skin&&!mesh->unit_normals)return false; /* Evaluated skin normals are normalized. */
    /* Rows of inverse(modelview) are columns of its normal matrix. Normalize
     * one common scale once, preserving mirrors. GX does not normalize input
     * normals for us. Nonuniform/sheared transforms retain exact CPU shading. */
    float length[3]={0};
    for(int r=0;r<3;r++)for(int c=0;c<3;c++)length[r]+=normal[r*4+c]*normal[r*4+c];
    if(!(length[0]>0)||!isfinite(length[0]))return false;
    float tolerance=length[0]*0.00001f;
    for(int r=1;r<3;r++)if(fabsf(length[r]-length[0])>tolerance)return false;
    for(int a=0;a<3;a++)for(int b=a+1;b<3;b++){
      float dot=0;for(int c=0;c<3;c++)dot+=normal[a*4+c]*normal[b*4+c];
      if(fabsf(dot)>tolerance)return false;
    }
    scale=1/sqrtf(length[0]);
  }
  GXColor tint;
  // The ordinary mesh backend must not reuse cache assumptions across models.
  dnd_draw_backend_mesh_changed();
  u8 *channels=(u8*)&tint;
  for(int c=0;c<4;c++)channels[c]=(u8)(mesh->color[c]*material->color[c]*255);
  GX_SetChanMatColor(GX_COLOR0A0,tint);
  if(lit){
    Mtx matrix;
    for(int r=0;r<3;r++)for(int c=0;c<4;c++)matrix[r][c]=c<3?normal[r*4+c]*scale:0;
    GX_LoadNrmMtxImm(matrix,GX_PNMTX0);
    /* Equivalent camera-space diffuse headlight, within GX's 8-bit lighting
     * precision. RGB ambient/diffuse sum to 255; alpha is never lit. */
    if(!model_batch_active||!model_batch_light_ready){
    GXLightObj light={0};GX_InitLightPos(&light,0,0,1000000);
    GX_InitLightColor(&light,(GXColor){191,191,191,255});
    GX_InitLightAttn(&light,1,0,0,1,0,0);GX_LoadLightObj(&light,GX_LIGHT0);
    GX_SetChanAmbColor(GX_COLOR0A0,(GXColor){64,64,64,255});
    model_batch_light_ready=true;
    }
  }
  GX_SetChanCtrl(GX_COLOR0,lit?GX_ENABLE:GX_DISABLE,GX_SRC_REG,GX_SRC_REG,
                 lit?GX_LIGHT0:GX_LIGHTNULL,lit?GX_DF_CLAMP:GX_DF_NONE,GX_AF_NONE);
  GX_SetChanCtrl(GX_ALPHA0,GX_DISABLE,GX_SRC_REG,GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
  GX_ClearVtxDesc();if(!model_batch_active||!model_batch_vertex_ready||batch_mesh!=mesh||batch_skin!=skin){GX_InvVtxCache();model_batch_vertex_ready=true;batch_mesh=mesh;batch_skin=skin;}
  GX_SetVtxDesc(GX_VA_POS,GX_INDEX16);GX_SetArray(GX_VA_POS,skin?(void*)skin:(void*)mesh->data,skin?24:80);
  if(lit){GX_SetVtxDesc(GX_VA_NRM,GX_INDEX16);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_NRM,GX_NRM_XYZ,GX_F32,0);GX_SetArray(GX_VA_NRM,skin?(void*)(skin+3):(void*)(mesh->data+12),skin?24:80);}
  bool textured=material->texture>=0||material->combiner;
  if(textured){GX_SetVtxDesc(GX_VA_TEX0,GX_INDEX16);GX_SetArray(GX_VA_TEX0,(void*)(mesh->data+24),80);}
  for(unsigned first=0;first<mesh->indices;){
    unsigned count=mesh->indices-first;if(count>65535)count=65535;
    GX_Begin(GX_TRIANGLES,GX_VTXFMT0,(u16)count);
    dnd_performance_add_draw(first==0?bindings:0);
    for(unsigned j=0;j<count;j++){
      u16 index=(u16)dnd_model_u32(mesh->index+(first+j)*4);
      GX_Position1x16(index);if(lit)GX_Normal1x16(index);if(textured)GX_TexCoord1x16(index);
    }
    GX_End();first+=count;
  }
  return true;
#else
  (void)mesh;(void)material;(void)normal;(void)custom_scene;(void)animated;(void)skin;(void)bindings;return false;
#endif
}
