#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
typedef unsigned char u8;
typedef unsigned short u16;
typedef struct {u8 r,g,b,a;} GXColor;
enum {GX_DISABLE=0,GX_SRC_REG=1,GX_LIGHTNULL=2,GX_DF_NONE=3,GX_AF_NONE=4,
 GX_COLOR0=5,GX_ALPHA0=6,GX_COLOR0A0=7,GX_VA_POS=8,GX_VA_TEX0=9,GX_VA_TEX1=10,
 GX_INDEX16=11,GX_TRIANGLES=12,GX_VTXFMT0=13};
static int states,invalidations,cache_invalidations,begins,positions,texcoords,expected_texcoords;
static int descriptors[16],counts[4];static const void *arrays[16];static int strides[16];
static bool state_valid=true;static u16 last_index;static GXColor material_color;
static bool indexed_layout;
static uint32_t checksum;
static bool draw_state(const DndDrawTexture *t,const DndDrawMaterial *m,const float *w,void *out)
{(void)t;(void)m;(void)w;assert(!out);states++;return state_valid;}
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
static GXColor color(uint32_t c){return (GXColor){c>>24,c>>16,c>>8,c};}
static uint32_t tint(uint32_t a,uint32_t b){uint32_t c=0;for(int s=0;s<32;s+=8)c|=(((a>>s&255)*(b>>s&255)/255)<<s);return c;}
static void GX_InvVtxCache(void){cache_invalidations++;}
static void GX_SetChanCtrl(int channel,int enabled,int ambient,int material,int lights,int diffuse,int attenuation)
{assert(channel==GX_COLOR0||channel==GX_ALPHA0);assert(enabled==GX_DISABLE&&ambient==GX_SRC_REG&&material==GX_SRC_REG&&lights==GX_LIGHTNULL&&diffuse==GX_DF_NONE&&attenuation==GX_AF_NONE);}
static void GX_SetChanMatColor(int channel,GXColor c){assert(channel==GX_COLOR0A0);material_color=c;}
static void GX_ClearVtxDesc(void){memset(descriptors,0,sizeof descriptors);}
static void GX_SetVtxDesc(int attribute,int mode){descriptors[attribute]=mode;}
static void GX_SetArray(int attribute,void *data,int stride){arrays[attribute]=data;strides[attribute]=stride;}
static void GX_Begin(int primitive,int format,u16 count){assert(primitive==GX_TRIANGLES&&format==GX_VTXFMT0&&count%3==0);counts[begins++]=count;}
static void GX_Position1x16(u16 index){last_index=index;positions++;checksum=checksum*31+index;}
static void GX_TexCoord1x16(u16 index){assert(index==last_index);texcoords++;}
static void GX_End(void){assert(texcoords==positions*expected_texcoords);}
#include "dnd_indexed_mesh.inc"
int main(void) {
 DndDrawVertex vertices[3]={{0}};uint16_t indices[3]={2,0,1};float world[16]={0};
 DndDrawTexture texture={0};DndDrawMaterial material={0x80402080u,0,0,NULL,NULL};
 assert(dnd_draw_backend_mesh(vertices,indices,3,0x80c8ff80u,NULL,&material,world));
 assert(states==1&&begins==1&&positions==3&&cache_invalidations==1&&invalidations==0&&indexed_layout);
 assert(material_color.r==64&&material_color.g==50&&material_color.b==32&&material_color.a==64);
 assert(descriptors[GX_VA_POS]==GX_INDEX16&&descriptors[GX_VA_TEX0]==0);
 assert(arrays[GX_VA_POS]==&vertices[0].x&&strides[GX_VA_POS]==sizeof(DndDrawVertex));
 assert(checksum==(2u*31+0)*31+1);
 positions=texcoords=begins=0;expected_texcoords=1;
 assert(dnd_draw_backend_mesh(vertices,NULL,3,0xffffffffu,&texture,&material,world));
 assert(cache_invalidations==1&&descriptors[GX_VA_TEX0]==GX_INDEX16);
 assert(arrays[GX_VA_TEX0]==&vertices[0].u&&strides[GX_VA_TEX0]==sizeof(DndDrawVertex));
 dnd_draw_backend_mesh_changed();material.secondary=&texture;expected_texcoords=2;positions=texcoords=begins=0;
 assert(dnd_draw_backend_mesh(vertices,NULL,3,0xffffffffu,&texture,&material,world));
 assert(cache_invalidations==2&&descriptors[GX_VA_TEX1]==GX_INDEX16&&arrays[GX_VA_TEX1]==&vertices[0].u);
 for(int flags=32;flags<=128;flags*=2){int previous=states;material.flags=flags;assert(!dnd_draw_backend_mesh(vertices,NULL,3,0,NULL,&material,world));assert(states==previous);}
 material.flags=0;state_valid=false;int previous=begins;
 assert(dnd_draw_backend_mesh(vertices,NULL,3,0,NULL,&material,world));assert(begins==previous);
 static uint16_t large[65538];for(int i=0;i<65538;i++)large[i]=(uint16_t)(i%3);
 state_valid=true;begins=positions=texcoords=0;checksum=0;
 assert(dnd_draw_backend_mesh(vertices,large,65538,0,&texture,&material,world));
 assert(begins==2&&counts[0]==65535&&counts[1]==3&&positions==65538);
 puts("GX indexed meshes: descriptors, register tint/alpha, UV arrays, index order, cache invalidation, fallbacks and triangle splitting passed.");
}
