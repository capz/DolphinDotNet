#include <assert.h>
#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
typedef uint8_t u8;typedef uint32_t u32;typedef float Mtx[3][4];
typedef struct{u8 r,g,b,a;} GXColor;
typedef struct{float pos[3],dir[3],attn[6];GXColor color;} GXLightObj;
typedef struct{int u,v,filter;} GXTexObj;
enum{GX_LIGHT0=1,GX_DA_MEDIUM=1,GX_SP_COS=1,GX_COLOR0A0=1,GX_COLOR0=2,GX_ALPHA0=3,GX_ENABLE=1,GX_DISABLE=0,GX_SRC_REG=1,GX_SRC_VTX=0,GX_DF_CLAMP=1,GX_AF_SPOT=1,GX_LIGHTNULL=0,GX_DF_NONE=0,GX_AF_NONE=0,GX_FOG_PERSP_LIN=2,GX_FOG_PERSP_EXP=4,GX_FOG_PERSP_EXP2=5,GX_FOG_NONE=0,GX_LINEAR=1,GX_NEAR=0,GX_FALSE=0,GX_ANISO_1=0,GX_TEXMTX0=30,GX_TEXMTX1=33,GX_MTX2x4=1,GX_TEXCOORD0=0,GX_TEXCOORD1=1,GX_TG_MTX2x4=1,GX_TG_TEX0=4,GX_TG_TEX1=5};
static int pass=1,invalidations,loaded,fog_kind,tex_source,tex_matrix,light_mask;static GXLightObj lights[8];static GXColor ambient;static Mtx matrix;
int dnd_device_pass(void){return pass;}
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
static void GX_InitLightAttn(GXLightObj *l,float a,float b,float c,float d,float e,float f){l->attn[0]=a;l->attn[1]=b;l->attn[2]=c;l->attn[3]=d;l->attn[4]=e;l->attn[5]=f;}
static void GX_InitLightPos(GXLightObj *l,float x,float y,float z){l->pos[0]=x;l->pos[1]=y;l->pos[2]=z;}
static void GX_InitLightColor(GXLightObj *l,GXColor c){l->color=c;}
static void GX_InitLightDistAttn(GXLightObj *l,float d,float b,int f){assert(d>0&&b==.5f&&f==GX_DA_MEDIUM);l->attn[4]=1/d;}
static void GX_InitLightDir(GXLightObj *l,float x,float y,float z){l->dir[0]=x;l->dir[1]=y;l->dir[2]=z;}
static void GX_InitLightSpot(GXLightObj *l,float cutoff,int f){assert(cutoff>0&&cutoff<=90&&f==GX_SP_COS);l->attn[1]=cutoff;}
static void GX_LoadLightObj(GXLightObj *l,unsigned mask){assert(mask==(1u<<loaded));lights[loaded++]=*l;}
static void GX_SetChanAmbColor(int channel,GXColor c){assert(channel==GX_COLOR0A0);ambient=c;}
static void GX_SetChanCtrl(int channel,int enabled,int a,int m,unsigned mask,int d,int f){(void)a;(void)m;(void)d;(void)f;if(channel==GX_COLOR0){assert(enabled);light_mask=(int)mask;}else assert(channel==GX_ALPHA0&&!enabled&&mask==0);}
static void GX_SetFog(int kind,float start,float end,float near,float far,GXColor c){(void)c;assert(end>start&&far>near);fog_kind=kind;}
static void GX_InitTexObjWrapMode(GXTexObj *t,int u,int v){t->u=u;t->v=v;}
static void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float lo,float hi,float bias,int clamp,int edge,int aniso){(void)lo;(void)hi;(void)bias;(void)clamp;(void)edge;(void)aniso;assert(min==mag);t->filter=min;}
static void GX_LoadTexMtxImm(Mtx m,unsigned id,int kind){assert(kind==GX_MTX2x4);memcpy(matrix,m,sizeof matrix);tex_matrix=(int)id;}
static void GX_SetTexCoordGen(int slot,int kind,int source,unsigned id){assert(slot==0||slot==1);assert(kind==GX_TG_MTX2x4&&id==(unsigned)tex_matrix);tex_source=source;}
#include "dnd_render_state.h"
#include "dnd_render_gx.inc"
int main(void){
 float p[136]={1,20,30,40,1,1,50,60,70,80,.1f,100,0,3};
 for(int i=0;i<3;i++){float *l=p+16+i*15;l[0]=(float)i;l[1]=l[2]=l[3]=255;l[6]=4;l[9]=-1;l[10]=10;l[11]=45;}
 assert(dnd_render_scene_valid(p));p[13]=9;assert(!dnd_render_scene_valid(p));p[13]=3;p[16+9]=0;assert(!dnd_render_scene_valid(p));p[16+9]=-1;
 float view[16]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,-5,1};
 dnd_draw_backend_options(p,1);assert(dnd_draw_backend_scene_active());dnd_draw_backend_scene(1,view);
 assert(loaded==3&&light_mask==7&&ambient.r==20&&fog_kind==2);assert(lights[0].pos[2]==1000000&&lights[0].attn[0]==1&&lights[0].attn[3]==1);assert(lights[1].pos[2]==-1&&lights[2].dir[2]==-1);
 int previous=invalidations;dnd_draw_backend_options(p,1);assert(invalidations==previous);loaded=0;p[12]=1;dnd_draw_backend_options(p,1);dnd_draw_backend_scene(1,view);assert(fog_kind==10);
 float style[20]={2,2,3,0,2,3,4,5,6,7,1,1,1,0,1,0,0,1,0,0};assert(dnd_render_style_valid(style));style[1]=4;assert(!dnd_render_style_valid(style));style[1]=2;
 dnd_draw_backend_options(style,0);GXTexObj t={0};managed_sampler(&t,0,false,0,0);assert(t.u==1&&t.v==2&&t.filter==GX_LINEAR);managed_uv(0);assert(matrix[0][0]==2&&matrix[0][1]==4&&matrix[0][3]==6&&matrix[1][3]==7);managed_uv(1);assert(tex_source==GX_TG_TEX1&&tex_matrix==GX_TEXMTX1);
 pass=0;t.u=8;managed_sampler(&t,0,false,0,0);assert(t.u==8&&!dnd_draw_backend_scene_active());pass=1;
 loaded=0;p[13]=8;for(int i=3;i<8;i++)memcpy(p+16+i*15,p+16,(size_t)15*sizeof(float));assert(dnd_render_scene_valid(p));dnd_draw_backend_options(p,1);p[1]=99;dnd_draw_backend_scene(1,view);assert(loaded==8&&light_mask==255&&ambient.r==20);
 loaded=0;p[13]=0;p[4]=0;dnd_draw_backend_options(p,1);dnd_draw_backend_scene(1,view);assert(loaded==0&&light_mask==0&&fog_kind==GX_FOG_NONE);
 dnd_draw_backend_options(NULL,1);assert(!dnd_draw_backend_scene_active()&&!style_options_active);puts("Production GX lights, camera-space conversion, fog, sampler, UV matrices and pass isolation passed.");
}
