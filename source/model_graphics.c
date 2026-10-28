#include "dnd_draw.h"
#include "dnd_graphics.h"
#include "dnd_model_internal.h"
#include "dnd_performance.h"
#include <math.h>
#include <string.h>
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
#include <gccore.h>
#endif
__attribute__((weak)) bool dnd_draw_backend_scene_active(void){return false;}
__attribute__((weak)) void dnd_draw_backend_scene(int lit,const float *view){(void)lit;(void)view;}
static float frame_view[16], frame_projection[16];
static bool owns_frame;
static bool matrix_array(DndArray *a, float out[16]) {
  if (!a || a->element_size != 4 || a->elements_are_references ||
      a->length != 16)
    return false;
  memcpy(out, a->data, 64);
  for (int i = 0; i < 16; i++)
    if (!isfinite(out[i]))
      return false;
  return true;
}
int dnd_model_frame_begin(DndArray *view, DndArray *projection) {
  if (dnd_model_frame_active || !matrix_array(view, frame_view) ||
      !matrix_array(projection, frame_projection))
    return -1;
  owns_frame = !dnd_device_active();
  if (owns_frame && dnd_device_begin(0.025f, 0.035f, 0.06f, 1) < 0)
    return -1;
  if (dnd_device_pass_begin(1) < 0)
    return -1;
  dnd_draw_set_camera(frame_view, frame_projection);
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
  Mtx44 p;
  for (int r = 0; r < 4; r++)
    for (int c = 0; c < 4; c++)
      p[r][c] = frame_projection[c * 4 + r];
  GX_LoadProjectionMtx(p, frame_projection[15]==1&&frame_projection[11]==0?GX_ORTHOGRAPHIC:GX_PERSPECTIVE);
#endif
  dnd_model_frame_active = true;
  return 0;
}
int dnd_model_frame_end(void) {
  if (!dnd_model_frame_active)
    return -1;
  if (dnd_device_pass_end(1) < 0)
    return -1;
  dnd_model_frame_active = false;
  if (owns_frame)
    return dnd_device_end();
  return 0;
}
/* Opaque geometry precedes depth-sorted translucency within each model draw.
 * Buffers belong to the instance and are budgeted once, never per frame. */
static bool draw_later(DndInstance *in, unsigned a, unsigned b) {
  return in->draw_depth[a] > in->draw_depth[b] ||
         (in->draw_depth[a] == in->draw_depth[b] && a > b);
}
static void draw_sift(DndInstance *in, unsigned *order, unsigned count,
                      unsigned root) {
  for (;;) {
    unsigned child = root * 2 + 1;
    if (child >= count)
      return;
    if (child + 1 < count && draw_later(in, order[child + 1], order[child]))
      child++;
    if (!draw_later(in, order[child], order[root]))
      return;
    unsigned swap = order[root];
    order[root] = order[child];
    order[child] = swap;
    root = child;
  }
}
static void sort_material_draws(DndInstance *in, const float world[16],
                          const float view[16],int override_blend) {
  if (!in->draw_order)
    return;
  DndModel *m = in->model;
  unsigned opaque = 0;
  for (unsigned i = 0; i < m->meshes; i++)
    if (!(override_blend<0?(m->material[m->mesh[i].material].mode & 4):override_blend))
      in->draw_order[opaque++] = i;
  unsigned count = 0;
  for (unsigned i = 0; i < m->meshes; i++) {
    DndMesh *mesh = &m->mesh[i];
    if (!(override_blend<0?(m->material[mesh->material].mode & 4):override_blend))
      continue;
    float object[16], mv[16];
    if (mesh->skin < 0)
      dnd_model_multiply(object, world, in->global + (unsigned)mesh->node * 16);
    else
      memcpy(object, world, 64);
    dnd_model_multiply(mv, view, object);
    in->draw_depth[i] = mv[2] * mesh->center[0] + mv[6] * mesh->center[1] +
                        mv[10] * mesh->center[2] + mv[14];
    in->draw_order[opaque + count++] = i;
  }
  unsigned *order = in->draw_order + opaque;
  for (unsigned i = count / 2; i > 0; i--)
    draw_sift(in, order, count, i - 1);
  for (unsigned end = count; end > 1; end--) {
    unsigned swap = order[0];
    order[0] = order[end - 1];
    order[end - 1] = swap;
    draw_sift(in, order, end - 1, 0);
  }
}
void dnd_model_sort_draws(DndInstance *in,const float world[16],const float view[16]){sort_material_draws(in,world,view,-1);}
int dnd_model_draw(int token, DndArray *transform) {
  return dnd_model_draw_effect(token, transform, 0);
}
/* Full override submission shares the managed mesh backend, so lighting,
 * coordinates, samplers, opacity and render state cannot diverge. Bounded
 * application-executor scratch storage; never allocate or mutate model assets. */
int dnd_model_draw_material(int token,DndArray *transform,int tex,int secondary,
    uint32_t color,int flags,float cutoff,int effect_token) {
  DndInstance *in=dnd_instance_find(token);float world[16];DndDrawTexture base,extra;
  const DndDrawEffect *effect;
  if(!in||!in->model->ready||!dnd_model_frame_active||!matrix_array(transform,world)||
     flags<0||flags>255||(flags&192)==192||!isfinite(cutoff)||cutoff<0||cutoff>1||
     dnd_texture_resolve(tex,&base)<0||dnd_texture_resolve(secondary,&extra)<0||
     dnd_effect_resolve(effect_token,&effect)<0)return -1;
  if((flags&192)&&!tex&&!secondary)return -1;
  if(effect&&(((effect->texture&1)&&!tex)||((effect->texture&2)&&!secondary)))return -1;
  DndModel *model=in->model;
  /* Validate every submitted transform/skinned buffer before sorting or GX. */
  for(unsigned i=0;i<model->meshes;i++){
    DndMesh *mesh=&model->mesh[i];float object[16],mv[16],inverse[16];
    if(mesh->skin<0)dnd_model_multiply(object,world,in->global+(unsigned)mesh->node*16);
    else {if(!in->vertices[i])return -1;memcpy(object,world,64);}
    dnd_model_multiply(mv,frame_view,object);if(!dnd_model_inverse(inverse,mv))return -1;
  }
  sort_material_draws(in,world,frame_view,(flags&3)!=0);
  static DndDrawVertex vertices[768];
  DndDrawMaterial material={color,flags,cutoff,effect,secondary?&extra:NULL};
  for(unsigned draw=0;draw<model->meshes;draw++){
    unsigned mi=in->draw_order?in->draw_order[draw]:draw;DndMesh *mesh=&model->mesh[mi];float object[16];
    if(mesh->skin<0)dnd_model_multiply(object,world,in->global+(unsigned)mesh->node*16);else memcpy(object,world,64);
    for(unsigned first=0;first<mesh->indices;){
      unsigned count=mesh->indices-first;if(count>768)count=768;
      for(unsigned j=0;j<count;j++){
        unsigned index=dnd_model_u32(mesh->index+(first+j)*4);const unsigned char *src=mesh->data+index*80;
        DndDrawVertex *v=&vertices[j];float values[6];
        if(mesh->skin>=0)memcpy(values,in->vertices[mi]+index*6,24);
        else for(int c=0;c<6;c++)values[c]=dnd_model_f32(src+c*4);
        v->x=values[0];v->y=values[1];v->z=values[2];v->nx=values[3];v->ny=values[4];v->nz=values[5];
        v->u=dnd_model_f32(src+24);v->v=dnd_model_f32(src+28);v->color=0;
        for(int c=0;c<4;c++)v->color=(v->color<<8)|(uint32_t)(fminf(1,fmaxf(0,dnd_model_f32(src+32+c*4)))*255);
      }
      dnd_draw_backend_vertices(vertices,(int)count,0x90,tex?&base:NULL,&material,object);first+=count;
    }
    dnd_performance_add_triangles(mesh->indices/3);
  }
  return 0;
}
static int draw_model_selected(DndInstance *in,const float world[16],const DndDrawEffect *effect,int filter,int only);
int dnd_model_draw_effect(int token, DndArray *transform, int effect_token) {
  DndInstance *in = dnd_instance_find(token);
  float world[16];
  if (!in || !in->model->ready || !dnd_model_frame_active ||
      !matrix_array(transform, world))
    return -1;
  DndModel *model = in->model;
  const DndDrawEffect *effect;
  if (dnd_effect_resolve(effect_token, &effect) < 0)
    return -2;
  /* Preflight referenced meshes before sorting, counters or GX submission. */
  if (effect && (effect->texture&2))return -3;
  if (effect && (effect->texture&1))
    for (unsigned mi = 0; mi < model->meshes; mi++)
      if (model->material[model->mesh[mi].material].texture < 0)
        return -3;
  return draw_model_selected(in,world,effect,0,-1);
}
static int draw_model_selected(DndInstance *in,const float world[16],const DndDrawEffect *effect,int filter,int only) {
  DndModel *model=in->model;
  if(only<0)dnd_model_sort_draws(in, world, frame_view);
  for (unsigned draw = 0; draw < (only>=0?1:model->meshes); draw++) {
    unsigned mi = only>=0?(unsigned)only:in->draw_order ? in->draw_order[draw] : draw;
    bool translucent=(model->material[model->mesh[mi].material].mode&4)!=0;
    if((filter==1&&translucent)||(filter==2&&!translucent))continue;
    DndMesh *mesh = &model->mesh[mi];
    float object[16], mv[16], normal[16];
    if (mesh->skin < 0)
      dnd_model_multiply(object, world, in->global + (unsigned)mesh->node * 16);
    else {
      if (!in->vertices[mi])
        return -1;
      memcpy(object, world, 64);
    }
    dnd_model_multiply(mv, frame_view, object);
    if (!dnd_model_inverse(normal, mv))
      return -1;
    dnd_performance_add_triangles(mesh->indices / 3);
    DndMaterial *mat = &model->material[mesh->material];
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
    dnd_draw_backend_restore();
    Mtx matrix;
    for (int r = 0; r < 3; r++)
      for (int c = 0; c < 4; c++)
        matrix[r][c] = mv[c * 4 + r];
    GX_LoadPosMtxImm(matrix, GX_PNMTX0);
    GX_SetCurrentMtx(GX_PNMTX0);
    float determinant =
        object[0] * (object[5] * object[10] - object[9] * object[6]) -
        object[4] * (object[1] * object[10] - object[9] * object[2]) +
        object[8] * (object[1] * object[6] - object[5] * object[2]);
    GX_SetCullMode(mat->two          ? GX_CULL_NONE
                   : determinant < 0 ? GX_CULL_FRONT
                                     : GX_CULL_BACK);
    GX_SetZMode(GX_TRUE, GX_LEQUAL, (mat->mode & 4) ? GX_FALSE : GX_TRUE);
    GX_SetBlendMode((mat->mode & 4) ? GX_BM_BLEND : GX_BM_NONE,
                    (mat->mode & 16)  ? (mat->mode >> 5) & 7
                    : (mat->mode & 4) ? GX_BL_SRCALPHA
                                      : GX_BL_ONE,
                    (mat->mode & 16)  ? (mat->mode >> 8) & 7
                    : (mat->mode & 8) ? GX_BL_ONE
                    : (mat->mode & 4) ? GX_BL_INVSRCALPHA
                                      : GX_BL_ZERO,
                    GX_LO_CLEAR);
    GX_SetAlphaCompare(mat->mode & 1 ? GX_GEQUAL : GX_ALWAYS,
                       (u8)(mat->cutoff * 255), GX_AOP_AND, GX_ALWAYS, 0);
    GX_ClearVtxDesc();
    GX_SetVtxDesc(GX_VA_POS, GX_DIRECT);
    GX_SetVtxDesc(GX_VA_CLR0, GX_DIRECT);
    GX_SetVtxDesc(GX_VA_TEX0, GX_DIRECT);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_POS, GX_POS_XYZ, GX_F32, 0);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_CLR0, GX_CLR_RGBA, GX_RGBA8, 0);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_TEX0, GX_TEX_ST, GX_F32, 0);
    GX_SetNumChans(1);
    GX_SetChanCtrl(GX_COLOR0A0, GX_DISABLE, GX_SRC_REG, GX_SRC_VTX,
                   GX_LIGHTNULL, GX_DF_NONE, GX_AF_NONE);
    GX_SetNumTexGens(!effect&&mat->combiner ? 2 : mat->texture >= 0 ? 1 : 0);
    if (mat->texture >= 0) {
      DndTexture *tx = &model->texture[mat->texture];
      GXTexObj tex;
      GX_InitTexObj(&tex, tx->data, (u16)tx->width, (u16)tx->height,
                    GX_TF_RGBA8, (u8)tx->wrap_s, (u8)tx->wrap_t,
                    tx->levels > 1);
      GX_InitTexObjLOD(
          &tex,
          tx->levels > 1 ? (tx->linear ? GX_LIN_MIP_LIN : GX_NEAR_MIP_NEAR)
                         : (tx->linear ? GX_LINEAR : GX_NEAR),
          tx->linear ? GX_LINEAR : GX_NEAR, 0, (f32)(tx->levels - 1), 0,
          GX_FALSE, GX_FALSE, GX_ANISO_1);
      GX_LoadTexObj(&tex, GX_TEXMAP0);
      GX_SetTexCoordGen(GX_TEXCOORD0, GX_TG_MTX2x4, GX_TG_TEX0, GX_IDENTITY);
    }
#endif
    DndDrawEffect imported={0};
    const DndDrawEffect *selected=effect;
    if(!effect&&mat->combiner){
      imported.count=3;
      imported.stages[0]=(DndTevStage){15,mat->texture>=0?8:15,mat->texture>=0?10:15,mat->texture>=0?15:10,0,0,0,0};
      imported.stages[1]=mat->combiner==1?(DndTevStage){15,0,8,15,0,0,0,1}:(DndTevStage){15,15,15,8,0,1,0,1};
      imported.stages[2]=mat->combiner==1?(DndTevStage){15,15,15,0,0,0,0,0}:(DndTevStage){15,15,15,2,0,0,0,0};
      // Add emission to the previously lit base; final stage returns to TEX0
      // so opacity remains base texture alpha * raster alpha.
      if(mat->combiner==2)imported.stages[2]=(DndTevStage){15,0,12,2,0,0,0,0};
      selected=&imported;
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
      DndTexture *tx=&model->texture[mat->secondary];GXTexObj tex;
      GX_InitTexObj(&tex,tx->data,(u16)tx->width,(u16)tx->height,GX_TF_RGBA8,(u8)tx->wrap_s,(u8)tx->wrap_t,tx->levels>1);
      GX_InitTexObjLOD(&tex,tx->levels>1?(tx->linear?GX_LIN_MIP_LIN:GX_NEAR_MIP_NEAR):(tx->linear?GX_LINEAR:GX_NEAR),tx->linear?GX_LINEAR:GX_NEAR,0,(f32)(tx->levels-1),0,GX_FALSE,GX_FALSE,GX_ANISO_1);
      GX_LoadTexObj(&tex,GX_TEXMAP1);
      GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
#endif
    }
    dnd_draw_backend_tev(selected, mat->texture >= 0);
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
    bool custom_scene=dnd_draw_backend_scene_active();
    if(custom_scene){
      if(!(mat->mode&2)){Mtx n;for(int r=0;r<3;r++)for(int c=0;c<4;c++)n[r][c]=c<3?normal[r*4+c]:0;GX_LoadNrmMtxImm(n,GX_PNMTX0);GX_SetVtxDesc(GX_VA_NRM,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_NRM,GX_NRM_XYZ,GX_F32,0);}
      dnd_draw_backend_scene(!(mat->mode&2),frame_view);
    }
    for (unsigned first = 0; first < mesh->indices;) {
      unsigned count = mesh->indices - first;
      if (count > 65535)
        count = 65535;
      GX_Begin(GX_TRIANGLES, GX_VTXFMT0, (u16)count);
      for (unsigned j = 0; j < count; j++) {
        unsigned vertex = dnd_model_u32(mesh->index + (first + j) * 4);
        const unsigned char *src = mesh->data + vertex * 80;
        float v[6];
        if (mesh->skin >= 0)
          memcpy(v, in->vertices[mi] + vertex * 6, 24);
        else
          for (int c = 0; c < 6; c++)
            v[c] = dnd_model_f32(src + c * 4);
        float shade = 1;
        if (!(mat->mode & 2)&&!custom_scene) {
          float n[3];
          for (int r = 0; r < 3; r++)
            n[r] = normal[r * 4] * v[3] + normal[r * 4 + 1] * v[4] +
                   normal[r * 4 + 2] * v[5];
          float len = sqrtf(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
          shade = 0.25f + 0.75f * fmaxf(0, len > 0 ? n[2] / len : 0);
        }
        u8 color[4];
        for (int c = 0; c < 4; c++) {
          float value = dnd_model_f32(src + 32 + c * 4) * mat->color[c] *
                        (c < 3 ? shade : 1);
          color[c] = (u8)(fminf(1, fmaxf(0, value)) * 255);
        }
        GX_Position3f32(v[0], v[1], v[2]);
        if(custom_scene&&!(mat->mode&2))GX_Normal3f32(v[3],v[4],v[5]);
        GX_Color4u8(color[0], color[1], color[2], color[3]);
        GX_TexCoord2f32(dnd_model_f32(src + 24), dnd_model_f32(src + 28));
      }
      GX_End();
      first += count;
    }
#endif
  }
  return 0;
}

/* A synchronous batch borrows the caller's flattened matrices. References are
 * bounded and application-executor-owned; no model handles or pointers survive
 * this call. Opaque meshes from all instances precede the global alpha pass. */
typedef struct {unsigned object,mesh,sequence;float depth;} ModelSubmission;
static ModelSubmission alpha_submissions[4096];
static bool submission_later(ModelSubmission a,ModelSubmission b){
  return a.depth>b.depth||(a.depth==b.depth&&a.sequence>b.sequence);
}
static void submission_sift(unsigned count,unsigned root){
  for(;;){unsigned child=root*2+1;if(child>=count)return;
    if(child+1<count&&submission_later(alpha_submissions[child+1],alpha_submissions[child]))child++;
    if(!submission_later(alpha_submissions[child],alpha_submissions[root]))return;
    ModelSubmission temp=alpha_submissions[root];alpha_submissions[root]=alpha_submissions[child];alpha_submissions[child]=temp;root=child;}
}
int dnd_model_draw_models(DndArray *tokens,DndArray *matrices,int count){
  if(!dnd_model_frame_active||count<0||count>DND_INSTANCE_SLOTS||!tokens||!matrices||
     tokens->element_size!=4||tokens->elements_are_references||tokens->length<(unsigned)count||
     matrices->element_size!=4||matrices->elements_are_references||matrices->length<(unsigned)count*16)return -1;
  int *ids=(int*)tokens->data;float *worlds=(float*)matrices->data;unsigned alpha_count=0,total=0;
  // All validation precedes sorting, accounting or any GX submissions.
  for(int i=0;i<count;i++){
    if(!ids[i])continue;
    DndInstance *in=dnd_instance_find(ids[i]);if(!in||!in->model->ready)return -1;
    float *world=worlds+i*16;for(int k=0;k<16;k++)if(!isfinite(world[k]))return -1;
    DndModel *model=in->model;if(model->meshes>4096-total)return -1;total+=model->meshes;
    for(unsigned j=0;j<model->meshes;j++){
      DndMesh *mesh=&model->mesh[j];float object[16],mv[16],inverse[16];
      if(mesh->skin<0)dnd_model_multiply(object,world,in->global+(unsigned)mesh->node*16);
      else {if(!in->vertices[j])return -1;memcpy(object,world,64);}
      dnd_model_multiply(mv,frame_view,object);if(!dnd_model_inverse(inverse,mv))return -1;
      if(model->material[mesh->material].mode&4){
        float depth=mv[2]*mesh->center[0]+mv[6]*mesh->center[1]+mv[10]*mesh->center[2]+mv[14];
        if(!isfinite(depth))return -1;
        alpha_submissions[alpha_count]=(ModelSubmission){(unsigned)i,j,alpha_count,depth};alpha_count++;
      }
    }
  }
  for(unsigned i=alpha_count/2;i>0;i--)submission_sift(alpha_count,i-1);
  for(unsigned end=alpha_count;end>1;end--){ModelSubmission t=alpha_submissions[0];alpha_submissions[0]=alpha_submissions[end-1];alpha_submissions[end-1]=t;submission_sift(end-1,0);}
  for(int i=0;i<count;i++)if(ids[i]&&draw_model_selected(dnd_instance_find(ids[i]),worlds+i*16,NULL,1,-1)<0)return -1;
  for(unsigned i=0;i<alpha_count;i++){
    ModelSubmission s=alpha_submissions[i];
    if(draw_model_selected(dnd_instance_find(ids[s.object]),worlds+s.object*16,NULL,0,(int)s.mesh)<0)return -1;
  }
  return 0;
}
