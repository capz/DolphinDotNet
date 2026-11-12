static bool vertex_cache_dirty=true;
static const DndDrawVertex *vertex_cache_source;
static int vertex_cache_layout=-1;
static const uint32_t *vertex_cache_colors;
void dnd_draw_backend_mesh_changed(void){vertex_cache_dirty=true;}
/* Unlit meshes can use GX indexed arrays directly. Varying colors require
 * identity material tint to preserve the direct path's integer tint semantics.
 * Uniform-color meshes continue to use the material register. The CPU
 * sends indices; GX fetches the existing immutable vertex allocation. No
 * expanded staging copy or additional persistent geometry allocation. */
static bool indexed_mesh(const DndDrawVertex *vertices,const uint16_t *indices,int count,
                           uint32_t uniform_color,const DndDrawTexture *texture,
                           const DndDrawMaterial *material,const float *world,bool vertex_colors,const uint32_t *colors) {
#if !defined(DND_INDEXED_MESHES) || DND_INDEXED_MESHES
    if((material->flags&224)||(vertex_colors&&material->color!=0xffffffffu))return false; /* Normal-based generated UVs retain the expanded path; screen projection uses indexed positions. */
    DndVertexState state;
    if(!draw_state(texture,material,world,&state))return true;
    int layout=(texture?1:0)|(material->secondary?2:0)|(vertex_colors?4:0);
    if(vertex_cache_dirty||vertex_cache_source!=vertices||vertex_cache_layout!=layout||vertex_cache_colors!=colors){GX_InvVtxCache();vertex_cache_dirty=false;vertex_cache_source=vertices;vertex_cache_layout=layout;vertex_cache_colors=colors;}
    GX_SetChanCtrl(GX_COLOR0,GX_DISABLE,GX_SRC_REG,vertex_colors?GX_SRC_VTX:GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_SetChanCtrl(GX_ALPHA0,GX_DISABLE,GX_SRC_REG,vertex_colors?GX_SRC_VTX:GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_SetChanMatColor(GX_COLOR0A0,color(tint(uniform_color,material->color)));
    GX_ClearVtxDesc();GX_SetVtxDesc(GX_VA_POS,GX_INDEX16);
    GX_SetArray(GX_VA_POS,(void*)&vertices[0].x,sizeof(DndDrawVertex));
    if(vertex_colors){GX_SetVtxDesc(GX_VA_CLR0,GX_INDEX16);GX_SetArray(GX_VA_CLR0,(void*)(colors?colors:&vertices[0].color),colors?4:sizeof(DndDrawVertex));}
    if(texture||material->secondary){
        GX_SetVtxDesc(GX_VA_TEX0,GX_INDEX16);
        GX_SetArray(GX_VA_TEX0,(void*)&vertices[0].u,sizeof(DndDrawVertex));
    }
    if(material->secondary){
        GX_SetVtxDesc(GX_VA_TEX1,GX_INDEX16);
        GX_SetArray(GX_VA_TEX1,(void*)&vertices[0].u,sizeof(DndDrawVertex));
    }
    for(int start=0;start<count;) {
        int n=count-start;if(n>65535)n=65535; /* Preserve whole triangles. */
        GX_Begin(GX_TRIANGLES,GX_VTXFMT0,(u16)n);
        dnd_performance_add_draw(start==0?state.bindings:0);
        for(int j=0;j<n;j++) {
            u16 index=indices?indices[start+j]:(u16)(start+j);
            GX_Position1x16(index);
            if(vertex_colors)GX_Color1x16(index);
            if(texture||material->secondary)GX_TexCoord1x16(index);
            if(material->secondary)GX_TexCoord1x16(index);
        }
        GX_End();start+=n;
    }
    /* Preserve material state across indexed draws. The direct backend and
     * cached console explicitly restore descriptors when switching layouts. */
    indexed_layout=true;return true;
#else
    (void)colors;(void)vertex_colors;(void)vertices;(void)indices;(void)count;(void)uniform_color;(void)texture;(void)material;(void)world;
    return false;
#endif
}

bool dnd_draw_backend_mesh(const DndDrawVertex *v,const uint16_t *i,int n,uint32_t c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w)
{return indexed_mesh(v,i,n,c,t,m,w,false,NULL);}
bool dnd_draw_backend_mesh_colors(const DndDrawVertex *v,const uint16_t *i,int n,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w)
{return indexed_mesh(v,i,n,0xffffffffu,t,m,w,true,NULL);}

bool dnd_draw_backend_mesh_color_stream(const DndDrawVertex *v,const uint16_t *i,int n,const uint32_t *c,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w)
{return indexed_mesh(v,i,n,0xffffffffu,t,m,w,true,c);}

/* Native ordered variants: load shared geometry/material once, replay the
 * immutable POS/TEX index list, updating only primary UV and register color.
 * Restore the managed style even though no caller material was mutated. */
bool dnd_draw_backend_mesh_passes(const DndDrawVertex *v,uint32_t vertex_color,const DndDrawTexture *texture,const DndDrawMaterial *material,const float *world,const unsigned char *commands,int command_bytes,int count,const unsigned char *colors,const float *uv,int passes) {
    bool saved_active=style_options_active;float saved[40];memcpy(saved,style_options,sizeof saved);
    if(!style_options_active){memset(style_options,0,sizeof style_options);style_options[4]=style_options[7]=style_options[24]=style_options[27]=1;style_options_active=true;}
    DndVertexState state;
    if(!draw_state(texture,material,world,&state)){memcpy(style_options,saved,sizeof saved);style_options_active=saved_active;dnd_draw_backend_invalidate_cache();return true;}
    GX_InvVtxCache();vertex_cache_dirty=true;
    GX_SetChanCtrl(GX_COLOR0,GX_DISABLE,GX_SRC_REG,GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_SetChanCtrl(GX_ALPHA0,GX_DISABLE,GX_SRC_REG,GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_ClearVtxDesc();GX_SetVtxDesc(GX_VA_POS,GX_INDEX16);GX_SetVtxDesc(GX_VA_TEX0,GX_INDEX16);
    GX_SetArray(GX_VA_POS,(void*)&v[0].x,sizeof(DndDrawVertex));GX_SetArray(GX_VA_TEX0,(void*)&v[0].u,sizeof(DndDrawVertex));
    for(int i=0;i<passes;i++) {
        memcpy(style_options+4,uv+i*6,6*sizeof(float));managed_uv(0);
        const unsigned char *p=colors+i*4;uint32_t rgba=(uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];
        GX_SetChanMatColor(GX_COLOR0A0,color(tint(vertex_color,rgba)));
        GX_CallDispList((void*)commands,(uint32_t)command_bytes);dnd_performance_add_draw(i==0?state.bindings:0);
    }
    (void)count;memcpy(style_options,saved,sizeof saved);style_options_active=saved_active;
    dnd_draw_backend_invalidate_cache();indexed_layout=true;return true;
}
