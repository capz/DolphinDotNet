#include <assert.h>
#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
typedef uint8_t u8;typedef uint32_t u32;typedef float Mtx[3][4];
typedef struct{u8 r,g,b,a;} GXColor;
typedef struct{float pos[3],dir[3],attn[6];GXColor color;} GXLightObj;
typedef struct{int u,v,filter,mag,clamp,edge,aniso;float lo,hi,bias;} GXTexObj;
enum{GX_LIGHT0=1,GX_DA_MEDIUM=1,GX_SP_COS=1,GX_COLOR0A0=1,GX_COLOR0=2,GX_ALPHA0=3,GX_ENABLE=1,GX_DISABLE=0,GX_SRC_REG=1,GX_SRC_VTX=0,GX_DF_CLAMP=1,GX_AF_SPOT=1,GX_LIGHTNULL=0,GX_DF_NONE=0,GX_AF_NONE=0,GX_FOG_PERSP_LIN=2,GX_FOG_PERSP_EXP=4,GX_FOG_PERSP_EXP2=5,GX_FOG_NONE=0,GX_LINEAR=1,GX_NEAR=0,GX_LIN_MIP_LIN=5,GX_NEAR_MIP_NEAR=2,GX_FALSE=0,GX_ANISO_1=0,GX_TEXMTX0=30,GX_TEXMTX1=33,GX_MTX2x4=1,GX_TEXCOORD0=0,GX_TEXCOORD1=1,GX_TG_MTX2x4=1,GX_TG_TEX0=4,GX_TG_TEX1=5,GX_MTX3x4=0,GX_TG_MTX3x4=0,GX_TG_POS=0};
static int pass=1,invalidations,loaded,fog_kind,tex_source,tex_matrix,light_mask,tex_projection;static GXLightObj lights[8];static GXColor ambient;static Mtx matrix;
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
static void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float lo,float hi,float bias,int clamp,int edge,int aniso){t->filter=min;t->mag=mag;t->lo=lo;t->hi=hi;t->bias=bias;t->clamp=clamp;t->edge=edge;t->aniso=aniso;}
static void GX_LoadTexMtxImm(Mtx m,unsigned id,int kind){assert(kind==GX_MTX2x4||kind==GX_MTX3x4);memcpy(matrix,m,sizeof matrix);tex_matrix=(int)id;tex_projection=kind;}
static void GX_SetTexCoordGen(int slot,int kind,int source,unsigned id){assert(slot==0||slot==1);assert((kind==GX_TG_MTX2x4||kind==GX_TG_MTX3x4)&&id==(unsigned)tex_matrix);tex_source=source;}
#include "dnd_render_state.h"
#include "dnd_render_gx.inc"
static void projected(float x,float y,float z,float *s,float *t){
 float q=matrix[2][0]*x+matrix[2][1]*y+matrix[2][2]*z+matrix[2][3];
 *s=(matrix[0][0]*x+matrix[0][1]*y+matrix[0][2]*z+matrix[0][3])/q;
 *t=(matrix[1][0]*x+matrix[1][1]*y+matrix[1][2]*z+matrix[1][3])/q;
}
static void screen_projection_tests(void){
 // Perspective clip matrix: W=-Z, X=2X, Y=3Y. Projected center/corners
 // depend on clip position, not the object's authored texture coordinates.
 float clip[16]={2,0,0,0,0,3,0,0,0,0,-1,-1,0,0,-.2f,0},s,t;
 dnd_draw_backend_options(NULL,0);managed_screen_projection(clip);
 assert(tex_projection==GX_MTX3x4&&tex_source==GX_TG_POS&&tex_matrix==GX_TEXMTX0);
 projected(0,0,-4,&s,&t);assert(s==.5f&&t==.5f);
 projected(2,4.f/3.f,-4,&s,&t);assert(fabsf(s-1)<.000001f&&fabsf(t)<.000001f);
 // A rigid world scroll changes MVP even while material options stay cached.
 clip[12]=2;managed_screen_projection(clip);projected(0,0,-4,&s,&t);assert(s==.75f&&t==.5f);
 // Camera translation/zoom similarly changes projection without editing UVs.
 clip[12]=-2;clip[0]=4;managed_screen_projection(clip);projected(1,0,-4,&s,&t);assert(s==.75f&&t==.5f);
 float uv[40]={0};uv[4]=2;uv[7]=3;uv[8]=.1f;uv[9]=.2f;uv[24]=uv[27]=1;
 dnd_draw_backend_options(uv,0);managed_screen_projection(clip);projected(1,0,-4,&s,&t);
 assert(fabsf(s-1.6f)<.000001f&&fabsf(t-1.7f)<.000001f);
 managed_uv(1);assert(tex_projection==GX_MTX2x4&&tex_source==GX_TG_TEX1);
 // A subsequent ordinary material restores UV0's 2x4 texgen.
 managed_uv(0);assert(tex_projection==GX_MTX2x4&&tex_source==GX_TG_TEX0);
 dnd_draw_backend_options(NULL,0);
}
int main(void){
 screen_projection_tests();
 float p[136]={1,20,30,40,1,1,50,60,70,80,.1f,100,0,3};
 for(int i=0;i<3;i++){float *l=p+16+i*15;l[0]=(float)i;l[1]=l[2]=l[3]=255;l[6]=4;l[9]=-1;l[10]=10;l[11]=45;}
 assert(dnd_render_scene_valid(p));p[13]=9;assert(!dnd_render_scene_valid(p));p[13]=3;p[16+9]=0;assert(!dnd_render_scene_valid(p));p[16+9]=-1;
 float view[16]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,-5,1};
 dnd_draw_backend_options(p,1);assert(dnd_draw_backend_scene_active());dnd_draw_backend_scene(1,view);
 assert(loaded==3&&light_mask==7&&ambient.r==20&&fog_kind==2);assert(lights[0].pos[2]==1000000&&lights[0].attn[0]==1&&lights[0].attn[3]==1);assert(lights[1].pos[2]==-1&&lights[2].dir[2]==-1);
 int previous=invalidations;dnd_draw_backend_options(p,1);assert(invalidations==previous);loaded=0;p[12]=1;dnd_draw_backend_options(p,1);dnd_draw_backend_scene(1,view);assert(fog_kind==10);
 float style[40]={2,2,3,0,2,3,4,5,6,7};style[20]=style[21]=style[22]=style[24]=style[27]=1;assert(dnd_render_style_valid(style));style[1]=4;assert(!dnd_render_style_valid(style));style[1]=2;
 dnd_draw_backend_options(style,0);GXTexObj t={0};managed_sampler(&t,0,1,false,0,0);assert(t.u==1&&t.v==2&&t.filter==GX_LINEAR);managed_uv(0);assert(matrix[0][0]==2&&matrix[0][1]==4&&matrix[0][3]==6&&matrix[1][3]==7);managed_uv(1);assert(tex_source==GX_TG_TEX1&&tex_matrix==GX_TEXMTX1);
 managed_sampler(&t,0,3,false,0,0);assert(t.filter==GX_LIN_MIP_LIN);
 style[0]=1;dnd_draw_backend_options(style,0);managed_sampler(&t,0,3,true,0,0);assert(t.filter==GX_NEAR_MIP_NEAR);
 style[3]=1;style[10]=3;style[11]=0;style[12]=1;style[13]=8;style[14]=-1.25f;style[15]=style[16]=1;style[17]=2;
 assert(dnd_render_style_valid(style));dnd_draw_backend_options(style,0);managed_sampler(&t,0,3,true,0,0);
 assert(t.filter==3&&t.mag==0&&t.lo==1&&t.hi==2&&t.bias==-1.25f&&t.clamp&&t.edge&&t.aniso==2);
 managed_sampler(&t,0,1,true,0,0);assert(t.filter==GX_LINEAR&&t.lo==0&&t.hi==0);
 for(int filter=0;filter<6;filter++){
  style[10]=(float)filter;dnd_draw_backend_options(style,0);managed_sampler(&t,0,3,true,0,0);assert(t.filter==filter);
  managed_sampler(&t,0,1,true,0,0);assert(t.filter==(filter<2?filter:(filter&1)));
 }
 style[10]=3;
 style[14]=NAN;assert(!dnd_render_style_valid(style));style[14]=-1.25f;
 style[17]=3;assert(!dnd_render_style_valid(style));style[17]=2;
 dnd_draw_backend_options(style,2);dnd_draw_backend_options(NULL,0);managed_sampler(&t,0,3,false,0,0);assert(t.filter==3&&t.aniso==2);
 float local[40]={1};dnd_draw_backend_options(local,0);managed_sampler(&t,0,3,true,0,0);assert(t.filter==GX_NEAR_MIP_NEAR&&t.aniso==0);
 dnd_draw_backend_imported_sampler(&t,0,3,0,0,0);assert(t.filter==3&&t.aniso==2);
 dnd_draw_backend_options(NULL,1);managed_sampler(&t,0,3,false,0,0);assert(t.filter==3);dnd_draw_backend_options(NULL,2);
 float wrap_only[40]={0,2};dnd_draw_backend_options(wrap_only,0);t.filter=4;t.bias=1.5f;
 managed_sampler(&t,0,3,true,0,0);assert(t.u==1&&t.filter==4&&t.bias==1.5f);
 pass=0;t.u=8;managed_sampler(&t,0,1,false,0,0);assert(t.u==8&&!dnd_draw_backend_scene_active());pass=1;
 loaded=0;p[13]=8;for(int i=3;i<8;i++)memcpy(p+16+i*15,p+16,(size_t)15*sizeof(float));assert(dnd_render_scene_valid(p));dnd_draw_backend_options(p,1);p[1]=99;dnd_draw_backend_scene(1,view);assert(loaded==8&&light_mask==255&&ambient.r==20);
 loaded=0;p[13]=0;p[4]=0;dnd_draw_backend_options(p,1);dnd_draw_backend_scene(1,view);assert(loaded==0&&light_mask==0&&fog_kind==GX_FOG_NONE);
 dnd_draw_backend_options(NULL,1);assert(!dnd_draw_backend_scene_active()&&!style_options_active);puts("Production GX lights, camera-space conversion, fog, sampler, UV matrices and pass isolation passed.");
}
