/* Color views own only RGBA8 data and hold the original geometry allocation.
 * The owner may be disposed, but its held storage survives until the last view. */
static void stream_colors(Resource *r,DndArray *a) {
    const unsigned char *p=a->data;uint32_t *out=(uint32_t*)r->data;
    for(int i=0;i<r->vertices;i++,p+=4)out[i]=(uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];
    r->mesh_color=out[0];r->uniform_color=true;
    for(int i=1;i<r->vertices;i++)if(out[i]!=r->mesh_color){r->uniform_color=false;break;}
    dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();
}
int dnd_mesh_color_view(int token,DndArray *colors) {
    Resource *base=find(token,MESH);
    if(dnd_device_active()||!base||base->geometry||base->holds==INT_MAX||!colors||colors->elements_are_references||colors->element_size!=4||colors->length!=(unsigned)base->vertices)return 0;
    int id=create(MESH,base->vertices*4,32);Resource *r=find(id,MESH);if(!r)return 0;
    r->geometry=token;r->vertices=base->vertices;r->indices=base->indices;r->format=base->format;base->holds++;
    stream_colors(r,colors);return id;
}
int dnd_mesh_color_update(int token,DndArray *colors) {
    Resource *r=find(token,MESH);
    if(dnd_device_active()||!r||!r->geometry||!mesh_geometry(r)||!colors||colors->elements_are_references||colors->element_size!=4||colors->length!=(unsigned)r->vertices)return -1;
    stream_colors(r,colors);return 0;
}
/* One cached POS/TEX0 index stream, built outside frames for repeated passes.
 * Dynamic positions/UVs may change; topology and this command list do not. */
int dnd_mesh_prepare_passes(int token) {
    Resource *r=find(token,MESH);
    if(dnd_device_active()||!r||r->geometry)return -1;
    if(r->commands)return 0;
    int count=r->indices?r->indices:r->vertices;
    if(count<3||count>65535||count%3)return -1;
    int bytes=(3+count*4+31)&~31;if(bytes>BUDGET-total_bytes)return -1;
    void *allocation=malloc((unsigned)bytes+31);if(!allocation)return -1;
    unsigned char *p=(unsigned char*)(((uintptr_t)allocation+31)&~(uintptr_t)31);memset(p,0,(size_t)bytes);
    p[0]=0x90;p[1]=(unsigned)count>>8;p[2]=count;int at=3;
    const uint16_t *index=(const uint16_t*)(r->data+r->vertices*sizeof(DndDrawVertex));
    for(int i=0;i<count;i++){unsigned n=r->indices?index[i]:(unsigned)i;p[at++]=n>>8;p[at++]=n;p[at++]=n>>8;p[at++]=n;}
    r->command_allocation=allocation;r->commands=p;r->extra_bytes=bytes;total_bytes+=bytes;dnd_draw_backend_flush(p,bytes,0);return 0;
}
