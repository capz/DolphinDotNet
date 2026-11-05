/* Compile the production model GX path and production combiner against a recording
 * GX API. No host-only successful-return stub can satisfy these assertions. */
#include "dnd_model_internal.h"
#include "dnd_draw.h"
#include "dnd_performance.h"
#include <gccore.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "dnd_tev_backend.inc"
#include "dnd_material_math.h"
#include "dnd_material_channels.inc"
static int channel_test,channel_calls;
static bool custom_scene_active;static int scene_lights,scene_normals;
bool dnd_draw_backend_scene_active(void){return custom_scene_active;}
void dnd_draw_backend_scene(int lit,const float *view){assert(view);if(lit)scene_lights++;}
static int indirect_resets,full_draws,full_skin;
void GX_SetNumIndStages(int count){assert(count==0);indirect_resets++;}
/* Wrappers catch transient allocations that stable budgets would miss. The
 * portable CI build redirects production calls at compile time, leaving this
 * fixture's libc calls intact; older GNU builds can still use --wrap. */
static size_t allocations;
#ifdef DND_MODEL_ALLOC_COMPILE_TEST
#define __real_malloc malloc
#define __real_calloc calloc
#define __real_realloc realloc
void *__wrap_aligned_alloc(size_t alignment,size_t size){allocations++;return aligned_alloc(alignment,size);}
#endif
void *__real_malloc(size_t size);
void *__real_calloc(size_t count,size_t size);
void *__real_realloc(void *data,size_t size);
void *__wrap_malloc(size_t size){allocations++;return __real_malloc(size);}
void *__wrap_calloc(size_t count,size_t size){allocations++;return __real_calloc(count,size);}
void *__wrap_realloc(void *data,size_t size){allocations++;return __real_realloc(data,size);}
typedef struct {int a,b,c,d,op,dest,coord,tex,raster,konst,alpha[4],alpha_clamp,direct,swap;} Stage;
typedef struct {
  int count,texgens,cull,zwrite,blend,src,dst,compare,cutoff;
  Stage stages[16]; GXColor colors[4],konst[4]; GXTexObj texture,secondary;
  float position[9],uv[6],world_x; GXColor vertex[3];
} Draw;
static Draw state,record[16];static int draws,restores,position_index,color_index,uv_index,texture_loads,light_loads,vertex_invalidations;
static bool authored_test;
static GXColorS10 authored_colors[4];
static int authored_rgb_op[5],authored_alpha_op[5],authored_depth_location,authored_dither;
static int authored_tex_source,authored_tex_id;static Mtx authored_tex_matrix;
static const unsigned char *indexed_arrays[32];static int indexed_stride[32],indexed_positions,indexed_normals,indexed_uvs,skin_normal_reads;
static GXColor indexed_tint;static Mtx indexed_normal;
void GX_SetChanMatColor(int channel,GXColor c){assert(channel==GX_COLOR0A0);indexed_tint=c;}
void GX_SetChanAmbColor(int channel,GXColor c){assert(channel==GX_COLOR0A0);if(!authored_test)assert(c.r==64&&c.g==64&&c.b==64);}
void GX_InitLightPos(GXLightObj *p,float x,float y,float z){(void)p;assert(x==0&&y==0&&z==1000000);}
void GX_InitLightColor(GXLightObj *p,GXColor c){(void)p;if(!authored_test)assert(c.r==191&&c.g==191&&c.b==191);}
void GX_InitLightAttn(GXLightObj *p,float a,float b,float c,float d,float e,float f){(void)p;assert(a==1&&b==0&&c==0&&d==1&&e==0&&f==0);}
void GX_LoadLightObj(GXLightObj *p,int slot){(void)p;assert(slot==GX_LIGHT0);light_loads++;}
void GX_SetArray(int attribute,void *p,int stride){assert(attribute>=0&&attribute<32&&(stride==80||stride==24));indexed_arrays[attribute]=p;indexed_stride[attribute]=stride;}
static float indexed_float(int attribute,u16 index,int component){const unsigned char *p=indexed_arrays[attribute]+index*indexed_stride[attribute]+component*4;float value;if(indexed_stride[attribute]==24){memcpy(&value,p,4);return value;}return dnd_model_f32(p);}
void GX_InvVtxCache(void){vertex_invalidations++;}
void GX_Position1x16(u16 index){indexed_positions++;GX_Position3f32(indexed_float(GX_VA_POS,index,0),indexed_float(GX_VA_POS,index,1),indexed_float(GX_VA_POS,index,2));state.vertex[color_index++]=indexed_tint;}
void GX_Normal1x16(u16 index){indexed_normals++;if(indexed_stride[GX_VA_NRM]==24)skin_normal_reads++;float normal[3];for(int i=0;i<3;i++)normal[i]=indexed_float(GX_VA_NRM,index,i);float nz=0;for(int i=0;i<3;i++)nz+=indexed_normal[2][i]*normal[i];float shade=(64+191*fmaxf(0,nz))/255;GXColor *c=&state.vertex[color_index-1];c->r=(u8)fminf(255,c->r*shade);c->g=(u8)fminf(255,c->g*shade);c->b=(u8)fminf(255,c->b*shade);}
void GX_TexCoord1x16(u16 index){const unsigned char *p=indexed_arrays[GX_VA_TEX0]+index*indexed_stride[GX_VA_TEX0];indexed_uvs++;GX_TexCoord2f32(dnd_model_f32(p),dnd_model_f32(p+4));}
void dnd_draw_backend_restore(void){memset(&state,0,sizeof(state));memset(state.colors,0x5a,sizeof(state.colors));memset(state.konst,0x5a,sizeof(state.konst));state.count=-1;restores++;}
void GX_SetNumTevStages(int n){assert(n>0&&n<=16);state.count=n;}
void GX_SetTevDirect(int s){state.stages[s].direct=1;}
void GX_SetTevSwapMode(int s,int r,int t){assert(r==0&&t==0);state.stages[s].swap=1;}
void GX_SetTevSwapModeTable(int t,int r,int g,int b,int a){assert(t==0&&r==0&&g==1&&b==2&&a==3);}
void GX_SetTevColor(int s,GXColor c){state.colors[s]=c;}
void GX_SetTevColorS10(int s,GXColorS10 c){assert(s>=0&&s<4);authored_colors[s]=c;}
void GX_SetZCompLoc(int location){authored_depth_location=location;}
void GX_SetDither(int enabled){authored_dither=enabled;}
void GX_LoadTexMtxImm(Mtx matrix,int slot,int projection){assert(slot==30&&projection==1);memcpy(authored_tex_matrix,matrix,sizeof(Mtx));}
void GX_SetTevKColor(int s,GXColor c){state.konst[s]=c;}
void GX_SetTevOrder(int s,int c,int t,int r){state.stages[s].coord=c;state.stages[s].tex=t;state.stages[s].raster=r;}
void GX_SetTevKColorSel(int s,int k){state.stages[s].konst=k;}
void GX_SetTevKAlphaSel(int s,int k){(void)s;assert(k==GX_TEV_KASEL_1||(authored_test&&s>=1&&s<=2&&k==GX_TEV_KASEL_K3_A));}
void GX_SetTevColorIn(int s,int a,int b,int c,int d){Stage *p=&state.stages[s];p->a=a;p->b=b;p->c=c;p->d=d;}
void GX_SetTevColorOp(int s,int op,int bias,int scale,int clamp,int dest){if(!authored_test)assert(!bias&&!scale&&clamp);state.stages[s].op=op;state.stages[s].dest=dest;int values[]={op,bias,scale,clamp,dest};memcpy(authored_rgb_op,values,sizeof(values));}
void GX_SetTevAlphaIn(int s,int a,int b,int c,int d){int *p=state.stages[s].alpha;p[0]=a;p[1]=b;p[2]=c;p[3]=d;}
void GX_SetTevAlphaOp(int s,int op,int bias,int scale,int clamp,int dest){state.stages[s].alpha_clamp=clamp;if(!authored_test)assert(!op&&!bias&&!scale&&clamp&&!dest);int values[]={op,bias,scale,clamp,dest};memcpy(authored_alpha_op,values,sizeof(values));}
void GX_SetNumTexGens(int n){state.texgens=n;}
void GX_InitTexObj(GXTexObj *t,void *p,u16 w,u16 h,int f,int s,int v,int m){
 /* Host ABI padding is not GX state. Vary it to catch raw-struct comparisons. */
 static unsigned char padding_pattern;memset(t,++padding_pattern,sizeof(*t));
 t->data=p;t->width=w;t->height=h;t->format=f;t->wrap_s=s;t->wrap_t=v;t->mips=m;
 t->min_filter=t->mag_filter=0;t->max_lod=0;
}
void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float lo,float hi,float bias,int clamp,int edge,int aniso){assert(!lo&&!bias&&!clamp&&!edge&&!aniso);t->min_filter=min;t->mag_filter=mag;t->max_lod=hi;}
void GX_LoadTexObj(GXTexObj *t,int slot){assert((slot==0||slot==1)&&state.count==-1);texture_loads++;if(slot==0)state.texture=*t;else state.secondary=*t;}
void GX_SetCullMode(int c){state.cull=c;}
void GX_SetZMode(int e,int c,int w){assert(e&&c==GX_LEQUAL);state.zwrite=w;}
void GX_SetBlendMode(int m,int s,int d,int op){if(!authored_test)assert(!op);state.blend=m;state.src=s;state.dst=d;}
void GX_SetAlphaCompare(int c,int cutoff,int op,int other,int ref){if(!authored_test)assert(!op&&other==GX_ALWAYS&&!ref);state.compare=c;state.cutoff=cutoff;}
void GX_Begin(int p,int f,u16 n){assert(p==GX_TRIANGLES&&f==0&&n==3&&state.count>0);position_index=color_index=uv_index=0;
 for(int i=0;i<state.count;i++){Stage *s=&state.stages[i];assert(s->direct==1&&s->swap==1&&(s->raster==GX_COLOR0A0||(authored_test&&i>0&&s->raster==GX_COLORNULL)));
 if(authored_test&&i>0){assert(s->coord==GX_TEXCOORDNULL&&s->tex==GX_TEXMAP_NULL);}
 else if(s->tex==GX_TEXMAP1)assert(state.texgens==2&&s->coord==GX_TEXCOORD1);else {assert(s->coord==(state.texture.data?GX_TEXCOORD0:GX_TEXCOORDNULL));assert(s->tex==(state.texture.data?GX_TEXMAP0:GX_TEXMAP_NULL));}
 if(!authored_test){assert(s->alpha[0]==GX_CA_ZERO&&s->alpha[1]==(state.texture.data?GX_CA_TEXA:GX_CA_ZERO));
 assert(s->alpha[2]==(state.texture.data?GX_CA_RASA:GX_CA_ZERO)&&s->alpha[3]==(state.texture.data?GX_CA_ZERO:GX_CA_RASA));}}}
void GX_Position3f32(float x,float y,float z){assert(position_index<9);state.position[position_index++]=x;state.position[position_index++]=y;state.position[position_index++]=z;}
void GX_Color4u8(u8 r,u8 g,u8 b,u8 a){assert(color_index<3);state.vertex[color_index++]=(GXColor){r,g,b,a};}
void GX_TexCoord2f32(float u,float v){assert(uv_index<6);state.uv[uv_index++]=u;state.uv[uv_index++]=v;}
void GX_End(void){assert(position_index==9&&color_index==3&&uv_index==6);record[draws++%16]=state;}
void dnd_draw_set_camera(const float *v,const float *p){(void)v;(void)p;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int p,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){assert(n==3&&p==0x90&&t&&m->secondary&&m->flags==65&&m->color==0x12345678&&w[0]==-1);assert(v[0].nz==-1&&v[0].color==0xcc99667f);if(v[0].x==10)full_skin++;full_draws++;}
void dnd_draw_backend_camera(const float *v,const float *p){(void)v;(void)p;}
void dnd_draw_backend_2d(int w,int h){(void)w;(void)h;}
void dnd_draw_backend_viewport(int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_scissor(int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_copy(const DndDrawTexture *t,int x,int y,int w,int h){(void)t;(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_abort(void){}
void dnd_draw_backend_external(void){}
void dnd_draw_backend_flush(void *p,int n,int i){(void)p;(void)n;(void)i;}
bool dnd_draw_backend_immediate_ready(void){return true;}
void GX_LoadProjectionMtx(Mtx44 p,int kind){(void)p;(void)kind;}
void GX_LoadPosMtxImm(Mtx m,int slot){state.world_x=m[0][3];(void)slot;}
void GX_SetCurrentMtx(int slot){(void)slot;}
void GX_ClearVtxDesc(void){}
void GX_SetVtxDesc(int attribute,int mode){(void)attribute;(void)mode;}
void GX_SetVtxAttrFmt(int format,int attribute,int components,int type,int frac){(void)format;(void)attribute;(void)components;(void)type;(void)frac;}
void GX_SetNumChans(int count){(void)count;}
void GX_SetChanCtrl(int channel,int enable,int ambient,int material,int lights,int diffuse,int attenuation){
 if(channel_test){
  assert(ambient==GX_SRC_REG&&material==GX_SRC_VTX&&attenuation==GX_AF_NONE);
  if(channel_calls++%2==0){assert(channel==GX_COLOR0&&enable==(channel_test==1)&&lights==(channel_test==1?GX_LIGHT0:GX_LIGHTNULL)&&diffuse==(channel_test==1?GX_DF_CLAMP:GX_DF_NONE));}
  else assert(channel==GX_ALPHA0&&!enable&&lights==GX_LIGHTNULL&&diffuse==GX_DF_NONE);
 }
}
void GX_SetTexCoordGen(int coordinate,int kind,int source,int matrix){(void)coordinate;(void)kind;authored_tex_source=source;authored_tex_id=matrix;}
static int effect_create(int count,int textured) {
 DndArray *a=calloc(1,sizeof(*a)+176+count*8);assert(a);a->element_size=1;a->length=176+count*8;
 unsigned char *p=a->data;memcpy(p,"DNEF",4);p[4]=1;p[8]=count;p[12]=textured;p[16]=1;
 memcpy(p+32,"Tint",4);p[64]=17;p[65]=34;p[66]=51;p[67]=255;
 for(int i=0;i<count;i++){unsigned char *s=p+176+i*8;s[0]=s[1]=s[2]=15;s[3]=textured?8:14;s[6]=12;}
 int token=dnd_effect_create(a);free(a);assert(token>0);return token;
}
static DndArray *identity(void) {
 DndArray *a=calloc(1,sizeof(*a)+64);assert(a);a->element_size=4;a->length=16;
 float *m=(float*)a->data;m[0]=m[5]=m[10]=m[15]=1;return a;
}
/* Compare recorded GX values, never unspecified C ABI padding. */
static void assert_texture_equal(GXTexObj a,GXTexObj b) {
 assert(a.data==b.data&&a.width==b.width&&a.height==b.height&&a.format==b.format);
 assert(a.wrap_s==b.wrap_s&&a.wrap_t==b.wrap_t&&a.mips==b.mips);
 assert(a.min_filter==b.min_filter&&a.mag_filter==b.mag_filter);
 assert(!memcmp(&a.max_lod,&b.max_lod,sizeof(a.max_lod)));
}
static void assert_stage_equal(Stage a,Stage b) {
 assert(a.a==b.a&&a.b==b.b&&a.c==b.c&&a.d==b.d&&a.op==b.op&&a.dest==b.dest);
 assert(a.coord==b.coord&&a.tex==b.tex&&a.raster==b.raster&&a.konst==b.konst);
 assert(!memcmp(a.alpha,b.alpha,sizeof(a.alpha))&&a.direct==b.direct&&a.swap==b.swap);
}
static void assert_color_equal(GXColor a,GXColor b) {
 assert(a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a);
}
static void assert_draw_equal(Draw a,Draw b) {
 assert(a.count==b.count&&a.texgens==b.texgens&&a.cull==b.cull&&a.zwrite==b.zwrite);
 assert(a.blend==b.blend&&a.src==b.src&&a.dst==b.dst&&a.compare==b.compare&&a.cutoff==b.cutoff);
 for(int i=0;i<16;i++)assert_stage_equal(a.stages[i],b.stages[i]);
 for(int i=0;i<4;i++){assert_color_equal(a.colors[i],b.colors[i]);assert_color_equal(a.konst[i],b.konst[i]);}
 assert_texture_equal(a.texture,b.texture);assert_texture_equal(a.secondary,b.secondary);
 assert(!memcmp(a.position,b.position,sizeof(a.position))&&!memcmp(a.uv,b.uv,sizeof(a.uv)));
 assert(!memcmp(&a.world_x,&b.world_x,sizeof(a.world_x)));
 for(int i=0;i<3;i++)assert_color_equal(a.vertex[i],b.vertex[i]);
}
static void assert_geometry_equal(Draw a,Draw b) {
 /* Compare every non-combiner property including the actual emitted vertices. */
 a.count=b.count=0;memset(a.stages,0,sizeof(a.stages));memset(b.stages,0,sizeof(b.stages));
 memset(a.colors,0,sizeof(a.colors));memset(b.colors,0,sizeof(b.colors));
 memset(a.konst,0,sizeof(a.konst));memset(b.konst,0,sizeof(b.konst));
 assert_draw_equal(a,b);
}
int main(void) {
 assert(dnd_model_indexed_path(-1)==1&&dnd_model_indexed_path(0)==0&&dnd_model_indexed_path(1)==1&&dnd_model_indexed_path(2)<0);
 DndDrawEffect dual={.count=3,.texture=3,.stages={{15,15,15,10,0,0,0,0},{15,15,15,8,0,0,0,1},{15,15,15,0,0,0,0,0}}};
 dnd_draw_backend_tev(&dual,true);assert(state.stages[0].tex==GX_TEXMAP0&&state.stages[1].tex==GX_TEXMAP1&&state.stages[1].coord==GX_TEXCOORD1&&state.stages[2].tex==GX_TEXMAP0);
 channel_test=1;dnd_material_channels(true);channel_test=2;dnd_material_channels(false);
 assert(channel_calls==4);channel_test=0;
 /* Normal projection: unit directions, rotation, nonuniform scale and zero. */
 float n[3][4]={{1,0,0,0},{0,1,0,0},{0,0,1,0}},u,v;
 dnd_material_view_normal(n,1,0,0,&u,&v);assert(u==1&&v==.5f);
 dnd_material_view_normal(n,0,1,0,&u,&v);assert(u==.5f&&v==0);
 dnd_material_view_normal(n,0,0,0,&u,&v);assert(u==.5f&&v==.5f);
 n[0][0]=.5f;dnd_material_view_normal(n,2,0,0,&u,&v);assert(u==1&&v==.5f);
 n[0][0]=0;n[0][2]=1;n[2][0]=-1;n[2][2]=0;
 dnd_material_view_normal(n,0,0,1,&u,&v);assert(u==1&&v==.5f);
 float unit[3][4]={{1,0,0,0},{0,1,0,0},{0,0,1,0}};
 dnd_material_world_reflection(unit,unit,0,0,1,0,0,-1,&u,&v);assert(fabsf(u-.5f)<.0001f&&fabsf(v-.5f)<.0001f);
 dnd_material_world_reflection(unit,unit,0,0,1,1,0,-1,&u,&v);assert(fabsf(u-.625f)<.0001f);
 dnd_material_world_reflection(unit,unit,0,0,1,-1,0,-1,&u,&v);assert(fabsf(u-.375f)<.0001f);
 float rotated[3][4]={{0,0,1,0},{0,1,0,0},{-1,0,0,0}};
 dnd_material_world_reflection(unit,rotated,0,0,1,0,0,-1,&u,&v);assert(fabsf(u-.75f)<.0001f);
 dnd_material_world_reflection(unit,unit,0,0,0,0,0,-1,&u,&v);assert(u==.5f&&v==.5f);
 unsigned char data[3*80]={0},indices[12]={0},pixels[128]={0};float skinned[18]={0};
 for(int i=0;i<3;i++){
  float values[20]={0};float *v=values;v[0]=(float)i;v[5]=-1;v[6]=i*.25f;v[7]=i*.5f;
  v[8]=.8f;v[9]=.6f;v[10]=.4f;v[11]=.5f;indices[i*4+3]=(unsigned char)i;
  for(int c=0;c<20;c++){uint32_t bits;memcpy(&bits,v+c,4);for(int b=0;b<4;b++)data[i*80+c*4+b]=(unsigned char)(bits>>(24-b*8));}
  memcpy(skinned+i*6,v,24);skinned[i*6]+=10; // proves the skinned buffer, not bind pose, was used
 }
 DndMesh meshes[3]={0};DndMaterial materials[4]={0};DndTexture texture={.width=4,.height=4,.levels=2,.wrap_s=1,.wrap_t=2,.linear=1,.data=pixels};
 for(int i=0;i<3;i++){
  meshes[i]=(DndMesh){.node=0,.skin=i==1?0:-1,.material=i,.vertices=3,.indices=3,.data=data,.index=indices,.center={0,0,-(float)i}};
  materials[i]=(DndMaterial){.color={.5f,.75f,1,.8f},.cutoff=.5f,.texture=0,.mode=2};
 }
 materials[0].mode|=1;materials[1].two=1;
 materials[2].mode=(materials[2].mode&~2)|4|16|(1<<5)|(5<<8);materials[3].texture=-1; // unused, must not reject textured effect
 DndMaterial original_materials[4];memcpy(original_materials,materials,sizeof(materials));
 DndModel model={.ready=true,.meshes=3,.materials=4,.textures=1,.mesh=meshes,.material=materials,.texture=&texture};
 DndArray *world=identity(),*camera=identity();float *wm=(float*)world->data;wm[0]=-1;
 unsigned order[3]={99,98,97};float depths[3]={0};float *vertices[3]={NULL,skinned,NULL};
 DndInstance instance={.token=123,.model=&model,.global=(float*)camera->data,.vertices=vertices,.draw_order=order,.draw_depth=depths};
 DndInstance second=instance;second.token=124;dnd_instances[0]=&instance;dnd_instances[1]=&second;
 DndArray *rgba=calloc(1,sizeof(*rgba)+64);assert(rgba);rgba->element_size=1;rgba->length=64;memset(rgba->data,255,64);
 int override_texture=dnd_texture_create(4,4,rgba,0);assert(override_texture>0);free(rgba);
 int short_effect=effect_create(2,0),long_effect=effect_create(7,0),textured=effect_create(1,1);
 int wrong=dnd_buffer_create(32,32);assert(wrong>0);
 assert(dnd_model_draw_effect(123,world,short_effect)==-1);
 assert(dnd_model_frame_begin(camera,camera)==0);
 assert(dnd_model_indexed_path(0)<0&&dnd_model_indexed_path(-1)==1);
 assert(dnd_model_draw(123,world)==0);assert(draws==3&&restores==3);
 Draw baseline[3];memcpy(baseline,record,sizeof(baseline));
 for(int i=0;i<4;i++){assert(baseline[0].colors[i].r==0&&baseline[0].konst[i].r==0);}
 assert(baseline[0].cull==GX_CULL_FRONT&&baseline[0].compare==GX_GEQUAL&&baseline[0].cutoff==127);
 assert(baseline[1].cull==GX_CULL_NONE&&baseline[1].position[0]==10);
 assert(baseline[0].vertex[0].r==102&&baseline[0].vertex[0].a==102);
 assert(baseline[2].vertex[0].r==25); // original diffuse shade remains before custom RGB
 assert(baseline[2].blend==GX_BM_BLEND&&baseline[2].src==1&&baseline[2].dst==5&&!baseline[2].zwrite);
 assert(baseline[0].texture.wrap_s==1&&baseline[0].texture.wrap_t==2&&baseline[0].texture.max_lod==1);
 assert(baseline[0].texture.min_filter==GX_LIN_MIP_LIN&&baseline[0].texture.mips);
 assert(dnd_model_draw_effect(123,world,0)==0);
 for(int i=0;i<3;i++)assert_draw_equal(baseline[i],record[3+i]);
 int before=draws;
 assert(dnd_model_draw_effect(124,world,long_effect)==0);
 for(int i=0;i<3;i++){Draw *r=&record[(before+i)%16];assert(r->count==7);assert_geometry_equal(baseline[i],*r);assert(r->konst[0].r==17);}
 before=draws;assert(dnd_model_draw(123,world)==0);
 for(int i=0;i<3;i++)assert_draw_equal(baseline[i],record[(before+i)%16]);
 before=draws;assert(dnd_model_draw_effect(123,world,short_effect)==0);
 for(int i=0;i<3;i++){assert(record[(before+i)%16].count==2);assert_geometry_equal(baseline[i],record[(before+i)%16]);}
 assert(dnd_model_draw_effect(123,world,textured)==0); // ignores unused untextured material
 /* Preflight must not sort, count triangles, reset GX, or submit even the first mesh. */
 materials[1].texture=-1;order[0]=99;order[1]=98;order[2]=97;
 before=draws;int restore_before=restores;uint32_t triangles=dnd_performance_stats()->triangles;
 assert(dnd_model_draw_effect(123,world,textured)==-3);
 assert(draws==before&&restores==restore_before&&order[0]==99&&order[1]==98&&order[2]==97&&dnd_performance_stats()->triangles==triangles);
 assert(dnd_model_draw_effect(123,world,short_effect)==0); // raster-only mixed model
 assert(!record[(before+1)%16].texgens);materials[1].texture=0;
 before=draws;restore_before=restores;
 assert(dnd_model_draw_effect(123,world,wrong)==-2);
 assert(dnd_model_draw_effect(123,world,-1)==-2);
 assert(draws==before&&restores==restore_before);
 const DndDrawEffect *borrowed=NULL;assert(dnd_effect_resolve(long_effect,&borrowed)==0&&borrowed&&borrowed->count==7);
 int bytes=dnd_draw_total_bytes();assert(dnd_draw_release(long_effect)==0);
 assert(dnd_draw_total_bytes()==bytes&&borrowed->count==7); // pending retains storage through encoded draws
 assert(dnd_model_draw_effect(123,world,long_effect)==-2);
 assert(dnd_effect_color(short_effect,0,0x123456ff)<0); // immutable during device frame
 // Exercise actual translucent depth ordering, with an opaque mesh between them in source order.
 materials[0].mode|=4;before=draws;
 assert(dnd_model_draw_effect(123,world,short_effect)==0);
 assert(order[0]==1&&order[1]==2&&order[2]==0);
 Draw sorted[3];for(int i=0;i<3;i++)sorted[i]=record[(before+i)%16];
 before=draws;assert(dnd_model_draw(123,world)==0);
 for(int i=0;i<3;i++)assert_geometry_equal(sorted[i],record[(before+i)%16]);
 materials[0]=original_materials[0];
 // Imported v2 materials select TEX1 for RGB and return to base alpha.
 materials[0].secondary=0;materials[0].combiner=1;before=draws;
 assert(dnd_model_draw(123,world)==0);
 assert(record[before%16].count==3&&record[before%16].secondary.data==pixels);
 assert(record[before%16].stages[1].tex==GX_TEXMAP1&&record[before%16].stages[1].c==GX_CC_TEXC);
 materials[0].combiner=2;before=draws;assert(dnd_model_draw(123,world)==0);
 assert(record[before%16].stages[1].dest==1&&record[before%16].stages[2].d==GX_CC_C0);
 assert(record[before%16].stages[2].tex==GX_TEXMAP0);
 materials[0].texture=-1;before=draws;assert(dnd_model_draw(123,world)==0);
 assert(record[before%16].texgens==2&&record[before%16].stages[2].tex==GX_TEXMAP_NULL);
 assert(record[before%16].stages[2].alpha[3]==GX_CA_RASA);
 // A per-draw override supersedes the imported combiner for this draw only.
 before=draws;assert(dnd_model_draw_effect(123,world,short_effect)==0);
 assert(record[before%16].count==2&&!record[before%16].secondary.data);
 materials[0]=original_materials[0];
 // Global submesh sorting: distinct model origins are insufficient when mesh
 // depths interleave. The recording backend verifies actual submission order.
 DndArray *ids=calloc(1,sizeof(*ids)+8),*worlds=calloc(1,sizeof(*worlds)+128);assert(ids&&worlds);
 ids->element_size=worlds->element_size=4;ids->length=2;worlds->length=32;
 ((int*)ids->data)[0]=123;((int*)ids->data)[1]=124;
 memcpy(worlds->data,world->data,64);memcpy(worlds->data+64,world->data,64);
 ((float*)worlds->data)[16+12]=100;((float*)worlds->data)[16+14]=-1;
 materials[0].mode=4;materials[2].mode=4;
 float z0=meshes[0].center[2],z2=meshes[2].center[2];meshes[0].center[2]=-10;meshes[2].center[2]=-2;
 before=draws;size_t batch_allocations=allocations;
 assert(dnd_model_draw_models(ids,worlds,2)==0&&draws==before+6&&allocations==batch_allocations);
 for(int i=0;i<2;i++)assert(record[(before+i)%16].blend==GX_BM_NONE);
 assert(record[(before+2)%16].world_x==100); // far mesh from object 1
 assert(record[(before+3)%16].world_x==0);   // far mesh from object 0
 assert(record[(before+4)%16].world_x==100); // near mesh from object 1
 assert(record[(before+5)%16].world_x==0);
 // Equal depths retain input order; invalid later objects cannot partially draw.
 ((float*)worlds->data)[16+14]=0;before=draws;
 assert(dnd_model_draw_models(ids,worlds,2)==0);
 assert(record[(before+2)%16].world_x==0&&record[(before+3)%16].world_x==100);
 ((int*)ids->data)[1]=-1;before=draws;triangles=dnd_performance_stats()->triangles;
 assert(dnd_model_draw_models(ids,worlds,2)<0&&draws==before&&dnd_performance_stats()->triangles==triangles);
 ((int*)ids->data)[1]=124;((float*)worlds->data)[16]=0;
 assert(dnd_model_draw_models(ids,worlds,2)<0&&draws==before);
 assert(dnd_model_draw_models(ids,worlds,129)<0&&draws==before);
 free(ids);free(worlds);materials[0]=original_materials[0];materials[2]=original_materials[2];
 meshes[0].center[2]=z0;meshes[2].center[2]=z2;
 int live=dnd_draw_live();bytes=dnd_draw_total_bytes();size_t allocation_baseline=allocations;
 for(int i=0;i<2000;i++)assert(dnd_model_draw_effect(123,world,i%2?short_effect:0)==0);
 assert(allocations==allocation_baseline&&dnd_draw_live()==live&&dnd_draw_total_bytes()==bytes&&!memcmp(original_materials,materials,sizeof(materials)));
 assert(dnd_model_frame_end()==0);assert(dnd_effect_resolve(long_effect,&borrowed)==-2&&borrowed==NULL);
 assert(dnd_effect_color(short_effect,0,0x123456ff)==0);
 int replacement=effect_create(1,0);assert(replacement!=long_effect); // generation rejects stale resource
 assert(dnd_model_frame_begin(camera,camera)==0);before=draws;
 assert(dnd_model_draw_effect(123,world,long_effect)==-2&&draws==before);
 model.ready=false;assert(dnd_model_draw_effect(123,world,short_effect)==-1);model.ready=true;
 assert(dnd_model_draw_effect(123,world,short_effect)==0);assert(record[before%16].konst[0].r==0x12);
 assert(allocations>0); /* Prove the production allocation hooks are active. */
 size_t full_allocations=allocations;
 assert(dnd_model_draw_material(123,world,override_texture,override_texture,0x12345678,65,.25f,textured)==0);
 assert(full_draws==3&&full_skin==1&&order[0]==2&&order[1]==1&&order[2]==0&&allocations==full_allocations);
 assert(!memcmp(original_materials,materials,sizeof(materials)));
 assert(dnd_model_draw_material(123,world,wrong,override_texture,0x12345678,65,.25f,textured)<0&&full_draws==3);
 wm[0]=0;assert(dnd_model_draw_material(123,world,override_texture,override_texture,0x12345678,65,.25f,textured)<0&&full_draws==3);wm[0]=-1;
 assert(dnd_draw_release(override_texture)==0);
 assert(dnd_model_draw_material(123,world,override_texture,override_texture,0x12345678,65,.25f,textured)<0&&full_draws==3);
 custom_scene_active=true;int previous_normals=scene_normals;assert(dnd_model_draw(123,world)==0&&scene_normals>previous_normals&&scene_lights>0);custom_scene_active=false;
 // The real indexed production path preserves geometry, UV, authored alpha,
 // culling and imported material state. Skins use evaluated poses, not bind geometry.
 for(int i=0;i<3;i++){meshes[i].uniform_color=meshes[i].unit_normals=true;float color[]={.8f,.6f,.4f,.5f};memcpy(meshes[i].color,color,sizeof(color));}
 before=draws;int old_positions=indexed_positions,old_normals=indexed_normals,old_uvs=indexed_uvs;size_t old_allocations=allocations;
 assert(dnd_model_draw(123,world)==0);
 assert(indexed_positions==old_positions+9&&indexed_normals==old_normals+3&&indexed_uvs==old_uvs+9&&allocations==old_allocations);
 for(int i=0;i<3;i++)assert_geometry_equal(baseline[i],record[(before+i)%16]);
 // A lit skin fetches normalized evaluated normals at stride 24, independent
 // of raw bind-pose normal metadata. Positions still include the pose offset.
 materials[1].mode&=~2;meshes[1].unit_normals=false;int old_skin_reads=skin_normal_reads;before=draws;
 assert(dnd_model_draw(123,world)==0&&skin_normal_reads==old_skin_reads+3);
 assert(record[(before+1)%16].vertex[0].r==25&&record[(before+1)%16].position[0]==10);
 materials[1].mode=original_materials[1].mode;meshes[1].unit_normals=true;
 // Exported zero normals receive ambient-only shading, just like the original
 // zero-length CPU normal case, without disabling the entire mesh fast path.
 memset(data+12,0,12);before=draws;old_positions=indexed_positions;
 assert(dnd_model_draw(123,world)==0&&indexed_positions==old_positions+9);
 assert(record[(before+2)%16].vertex[0].r==25);data[20]=0xbf;data[21]=0x80;
 // Normalize uniform scale once, including mirrored transforms.
 wm[0]=-4;wm[5]=wm[10]=4;old_positions=indexed_positions;assert(dnd_model_draw(123,world)==0&&indexed_positions==old_positions+9);
 assert(fabsf(indexed_normal[0][0]+1)<.00001f&&fabsf(indexed_normal[1][1]-1)<.00001f&&fabsf(indexed_normal[2][2]-1)<.00001f);
 // Nonuniform/sheared lit transforms, invalid normal lengths, nonuniform
 // colors, oversized indices and scene options must retain the direct path.
 wm[5]=2;old_positions=indexed_positions;assert(dnd_model_draw(123,world)==0&&indexed_positions==old_positions+6);
 wm[0]=-1;wm[5]=wm[10]=1;meshes[2].unit_normals=false;old_positions=indexed_positions;
 assert(dnd_model_draw(123,world)==0&&indexed_positions==old_positions+6);meshes[2].unit_normals=true;
 meshes[0].uniform_color=meshes[1].uniform_color=false;meshes[2].vertices=65537;old_positions=indexed_positions;
 assert(dnd_model_draw(123,world)==0&&indexed_positions==old_positions);meshes[0].uniform_color=meshes[1].uniform_color=true;meshes[2].vertices=3;
 custom_scene_active=true;old_positions=indexed_positions;assert(dnd_model_draw(123,world)==0&&indexed_positions==old_positions);custom_scene_active=false;
 assert(dnd_model_frame_end()==0);
 // Compare the production opaque batch against the original object order.
 // Every emitted geometry/material/alpha value must survive grouping, while
 // actual GX loads and primitive counters report the reduced work exactly.
 assert(dnd_model_state_cache(-1)==1&&dnd_model_state_cache(2)<0&&dnd_model_state_cache(0)==0);
 memcpy(materials,original_materials,sizeof(materials));for(int i=0;i<3;i++)materials[i].mode&=~4;
 DndArray *batch_ids=calloc(1,sizeof(*batch_ids)+8),*batch_worlds=calloc(1,sizeof(*batch_worlds)+128);
 assert(batch_ids&&batch_worlds);batch_ids->element_size=batch_worlds->element_size=4;batch_ids->length=2;batch_worlds->length=32;
 ((int*)batch_ids->data)[0]=123;((int*)batch_ids->data)[1]=124;
 memcpy(batch_worlds->data,world->data,64);memcpy(batch_worlds->data+64,world->data,64);((float*)batch_worlds->data)[28]=100;
 Draw legacy_batch[6];assert(dnd_model_frame_begin(camera,camera)==0);before=draws;
 int original_loads=texture_loads;assert(dnd_model_draw_models(batch_ids,batch_worlds,2)==0);
 for(int i=0;i<6;i++)legacy_batch[i]=record[(before+i)%16];
 assert(texture_loads-original_loads==6);
 assert(dnd_model_frame_end()==0&&dnd_model_state_cache(1)==1);
 assert(dnd_model_frame_begin(camera,camera)==0&&dnd_model_state_cache(0)<0);
 before=draws;restore_before=restores;original_loads=texture_loads;
 int old_lights=light_loads,old_invalidations=vertex_invalidations;old_allocations=allocations;
 uint32_t old_submissions=dnd_performance_stats()->draw_submissions,old_bindings=dnd_performance_stats()->texture_binds;
 assert(dnd_model_draw_models(batch_ids,batch_worlds,2)==0&&draws==before+6&&allocations==old_allocations);
 int permutation[]={0,3,1,4,2,5};for(int i=0;i<6;i++)assert_geometry_equal(legacy_batch[permutation[i]],record[(before+i)%16]);
 assert(restores-restore_before==3&&texture_loads-original_loads==3&&light_loads-old_lights==1&&vertex_invalidations-old_invalidations==3);
 assert(dnd_performance_stats()->draw_submissions-old_submissions==6&&dnd_performance_stats()->texture_binds-old_bindings==3);
 assert(dnd_model_frame_end()==0);
 // Reused two-texture multiply/emission materials retain their TEV and alpha.
 for(int i=0;i<2;i++){materials[i].combiner=i+1;materials[i].secondary=0;}
 assert(dnd_model_state_cache(0)==0&&dnd_model_frame_begin(camera,camera)==0);before=draws;
 assert(dnd_model_draw_models(batch_ids,batch_worlds,2)==0);for(int i=0;i<6;i++)legacy_batch[i]=record[(before+i)%16];
 assert(dnd_model_frame_end()==0&&dnd_model_state_cache(1)==1&&dnd_model_frame_begin(camera,camera)==0);
 before=draws;original_loads=texture_loads;old_bindings=dnd_performance_stats()->texture_binds;
 uint32_t old_api=dnd_performance_stats()->drawing_api_calls,old_changes=dnd_performance_stats()->material_changes;
 assert(dnd_model_draw_models(batch_ids,batch_worlds,2)==0);
 for(int i=0;i<6;i++){Draw a=legacy_batch[permutation[i]],b=record[(before+i)%16];assert_geometry_equal(a,b);assert(a.count==b.count);for(int j=0;j<16;j++)assert_stage_equal(a.stages[j],b.stages[j]);}
 assert(texture_loads-original_loads==5&&dnd_performance_stats()->texture_binds-old_bindings==5);
 assert(dnd_performance_stats()->drawing_api_calls-old_api==1&&dnd_performance_stats()->material_changes-old_changes==3);
 assert(dnd_model_frame_end()==0);free(batch_ids);free(batch_worlds);memcpy(materials,original_materials,sizeof(materials));
 assert(dnd_draw_release(short_effect)==0&&dnd_draw_release(textured)==0&&dnd_draw_release(replacement)==0&&dnd_draw_release(wrong)==0);
 assert(dnd_draw_total_bytes()==0&&dnd_draw_live()==0);
 assert(indirect_resets>=restores);
 dnd_instances[0]=dnd_instances[1]=NULL;free(world);free(camera);
 puts("Model GX effect selection, alpha, geometry, preflight and lifetime passed.");return 0;
}

void GX_LoadNrmMtxImm(Mtx m,u32 id){memcpy(indexed_normal,m,sizeof(indexed_normal));(void)id;}
void GX_Normal3f32(float x,float y,float z){assert(isfinite(x)&&isfinite(y)&&isfinite(z));scene_normals++;}
