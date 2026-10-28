static bool vertex_cache_dirty=true;
void dnd_draw_backend_mesh_changed(void){vertex_cache_dirty=true;}
/* Uniform-color, unlit meshes can use GX indexed arrays directly. The CPU
 * sends indices; GX fetches the existing immutable vertex allocation. No
 * expanded staging copy or additional persistent geometry allocation. */
bool dnd_draw_backend_mesh(const DndDrawVertex *vertices,const uint16_t *indices,int count,
                           uint32_t uniform_color,const DndDrawTexture *texture,
                           const DndDrawMaterial *material,const float *world) {
#if !defined(DND_INDEXED_MESHES) || DND_INDEXED_MESHES
    if(material->flags&224)return false; /* Keep lit and generated-UV paths unchanged. */
    if(!draw_state(texture,material,world,NULL))return true;
    if(vertex_cache_dirty){GX_InvVtxCache();vertex_cache_dirty=false;}
    GX_SetChanCtrl(GX_COLOR0,GX_DISABLE,GX_SRC_REG,GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_SetChanCtrl(GX_ALPHA0,GX_DISABLE,GX_SRC_REG,GX_SRC_REG,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    GX_SetChanMatColor(GX_COLOR0A0,color(tint(uniform_color,material->color)));
    GX_ClearVtxDesc();GX_SetVtxDesc(GX_VA_POS,GX_INDEX16);
    GX_SetArray(GX_VA_POS,(void*)&vertices[0].x,sizeof(DndDrawVertex));
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
        for(int j=0;j<n;j++) {
            u16 index=indices?indices[start+j]:(u16)(start+j);
            GX_Position1x16(index);
            if(texture||material->secondary)GX_TexCoord1x16(index);
            if(material->secondary)GX_TexCoord1x16(index);
        }
        GX_End();start+=n;
    }
    /* Preserve material state across indexed draws. The direct backend and
     * cached console explicitly restore descriptors when switching layouts. */
    indexed_layout=true;return true;
#else
    (void)vertices;(void)indices;(void)count;(void)uniform_color;(void)texture;(void)material;(void)world;
    return false;
#endif
}
