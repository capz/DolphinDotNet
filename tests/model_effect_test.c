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
/* Linker wrappers catch transient allocations that stable budgets would miss. */
static size_t allocations;
void *__real_malloc(size_t size);
void *__real_calloc(size_t count,size_t size);
void *__real_realloc(void *data,size_t size);
void *__wrap_malloc(size_t size){allocations++;return __real_malloc(size);}
void *__wrap_calloc(size_t count,size_t size){allocations++;return __real_calloc(count,size);}
void *__wrap_realloc(void *data,size_t size){allocations++;return __real_realloc(data,size);}
typedef struct {int a,b,c,d,op,dest,coord,tex,raster,konst,alpha[4],direct,swap;} Stage;
typedef struct {
  int count,texgens,cull,zwrite,blend,src,dst,compare,cutoff;
  Stage stages[16]; GXColor colors[4],konst[4]; GXTexObj texture;
  float position[9],uv[6]; GXColor vertex[3];
} Draw;
static Draw state,record[16];static int draws,restores,position_index,color_index,uv_index;
void dnd_draw_backend_restore(void){memset(&state,0,sizeof(state));memset(state.colors,0x5a,sizeof(state.colors));memset(state.konst,0x5a,sizeof(state.konst));state.count=-1;restores++;}
void GX_SetNumTevStages(int n){assert(n>0&&n<=16);state.count=n;}
void GX_SetTevDirect(int s){state.stages[s].direct=1;}
void GX_SetTevSwapMode(int s,int r,int t){assert(r==0&&t==0);state.stages[s].swap=1;}
void GX_SetTevSwapModeTable(int t,int r,int g,int b,int a){assert(t==0&&r==0&&g==1&&b==2&&a==3);}
void GX_SetTevColor(int s,GXColor c){state.colors[s]=c;}
void GX_SetTevKColor(int s,GXColor c){state.konst[s]=c;}
void GX_SetTevOrder(int s,int c,int t,int r){state.stages[s].coord=c;state.stages[s].tex=t;state.stages[s].raster=r;}
void GX_SetTevKColorSel(int s,int k){state.stages[s].konst=k;}
void GX_SetTevKAlphaSel(int s,int k){(void)s;assert(k==GX_TEV_KASEL_1);}
void GX_SetTevColorIn(int s,int a,int b,int c,int d){Stage *p=&state.stages[s];p->a=a;p->b=b;p->c=c;p->d=d;}
void GX_SetTevColorOp(int s,int op,int bias,int scale,int clamp,int dest){assert(!bias&&!scale&&clamp);state.stages[s].op=op;state.stages[s].dest=dest;}
void GX_SetTevAlphaIn(int s,int a,int b,int c,int d){int *p=state.stages[s].alpha;p[0]=a;p[1]=b;p[2]=c;p[3]=d;}
void GX_SetTevAlphaOp(int s,int op,int bias,int scale,int clamp,int dest){(void)s;assert(!op&&!bias&&!scale&&clamp&&!dest);}
void GX_SetNumTexGens(int n){state.texgens=n;}
void GX_InitTexObj(GXTexObj *t,void *p,u16 w,u16 h,int f,int s,int v,int m){*t=(GXTexObj){.data=p,.width=w,.height=h,.format=f,.wrap_s=s,.wrap_t=v,.mips=m};}
void GX_InitTexObjLOD(GXTexObj *t,int min,int mag,float lo,float hi,float bias,int clamp,int edge,int aniso){assert(!lo&&!bias&&!clamp&&!edge&&!aniso);t->min_filter=min;t->mag_filter=mag;t->max_lod=hi;}
void GX_LoadTexObj(GXTexObj *t,int slot){assert(slot==0&&state.count==-1);state.texture=*t;}
void GX_SetCullMode(int c){state.cull=c;}
void GX_SetZMode(int e,int c,int w){assert(e&&c==GX_LEQUAL);state.zwrite=w;}
void GX_SetBlendMode(int m,int s,int d,int op){assert(!op);state.blend=m;state.src=s;state.dst=d;}
void GX_SetAlphaCompare(int c,int cutoff,int op,int other,int ref){assert(!op&&other==GX_ALWAYS&&!ref);state.compare=c;state.cutoff=cutoff;}
void GX_Begin(int p,int f,u16 n){assert(p==GX_TRIANGLES&&f==0&&n==3&&state.count>0);position_index=color_index=uv_index=0;
 for(int i=0;i<state.count;i++){Stage *s=&state.stages[i];assert(s->direct==1&&s->swap==1&&s->raster==GX_COLOR0A0);
 assert(s->coord==(state.texgens?GX_TEXCOORD0:GX_TEXCOORDNULL));assert(s->tex==(state.texgens?GX_TEXMAP0:GX_TEXMAP_NULL));
 assert(s->alpha[0]==GX_CA_ZERO&&s->alpha[1]==(state.texgens?GX_CA_TEXA:GX_CA_ZERO));
 assert(s->alpha[2]==(state.texgens?GX_CA_RASA:GX_CA_ZERO)&&s->alpha[3]==(state.texgens?GX_CA_ZERO:GX_CA_RASA));}}
void GX_Position3f32(float x,float y,float z){assert(position_index<9);state.position[position_index++]=x;state.position[position_index++]=y;state.position[position_index++]=z;}
void GX_Color4u8(u8 r,u8 g,u8 b,u8 a){assert(color_index<3);state.vertex[color_index++]=(GXColor){r,g,b,a};}
void GX_TexCoord2f32(float u,float v){assert(uv_index<6);state.uv[uv_index++]=u;state.uv[uv_index++]=v;}
void GX_End(void){assert(position_index==9&&color_index==3&&uv_index==6);record[draws++%16]=state;}
void dnd_draw_set_camera(const float *v,const float *p){(void)v;(void)p;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int p,const DndDrawTexture *t,const DndDrawMaterial *m,const float *w){(void)v;(void)n;(void)p;(void)t;(void)m;(void)w;assert(!"Models must retain their own geometry path");}
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
void GX_LoadPosMtxImm(Mtx m,int slot){(void)m;(void)slot;}
void GX_SetCurrentMtx(int slot){(void)slot;}
void GX_ClearVtxDesc(void){}
void GX_SetVtxDesc(int attribute,int mode){(void)attribute;(void)mode;}
void GX_SetVtxAttrFmt(int format,int attribute,int components,int type,int frac){(void)format;(void)attribute;(void)components;(void)type;(void)frac;}
void GX_SetNumChans(int count){(void)count;}
void GX_SetChanCtrl(int channel,int enable,int ambient,int material,int lights,int diffuse,int attenuation){(void)channel;(void)enable;(void)ambient;(void)material;(void)lights;(void)diffuse;(void)attenuation;}
void GX_SetTexCoordGen(int coordinate,int kind,int source,int matrix){(void)coordinate;(void)kind;(void)source;(void)matrix;}
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
static void assert_geometry_equal(Draw a,Draw b) {
 /* Compare every non-combiner property including the actual emitted vertices. */
 a.count=b.count=0;memset(a.stages,0,sizeof(a.stages));memset(b.stages,0,sizeof(b.stages));
 memset(a.colors,0,sizeof(a.colors));memset(b.colors,0,sizeof(b.colors));
 memset(a.konst,0,sizeof(a.konst));memset(b.konst,0,sizeof(b.konst));
 assert(!memcmp(&a,&b,sizeof(a)));
}
int main(void) {
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
 int short_effect=effect_create(2,0),long_effect=effect_create(7,0),textured=effect_create(1,1);
 int wrong=dnd_buffer_create(32,32);assert(wrong>0);
 assert(dnd_model_draw_effect(123,world,short_effect)==-1);
 assert(dnd_model_frame_begin(camera,camera)==0);
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
 for(int i=0;i<3;i++)assert(!memcmp(&baseline[i],&record[3+i],sizeof(Draw)));
 int before=draws;
 assert(dnd_model_draw_effect(124,world,long_effect)==0);
 for(int i=0;i<3;i++){Draw *r=&record[(before+i)%16];assert(r->count==7);assert_geometry_equal(baseline[i],*r);assert(r->konst[0].r==17);}
 before=draws;assert(dnd_model_draw(123,world)==0);
 for(int i=0;i<3;i++)assert(!memcmp(&baseline[i],&record[(before+i)%16],sizeof(Draw)));
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
 assert(dnd_model_frame_end()==0);
 assert(dnd_draw_release(short_effect)==0&&dnd_draw_release(textured)==0&&dnd_draw_release(replacement)==0&&dnd_draw_release(wrong)==0);
 assert(dnd_draw_total_bytes()==0&&dnd_draw_live()==0);
 dnd_instances[0]=dnd_instances[1]=NULL;free(world);free(camera);
 puts("Model GX effect selection, alpha, geometry, preflight and lifetime passed.");return 0;
}
