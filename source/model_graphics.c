#include "dnd_graphics.h"
#include "dnd_model_internal.h"
#include <math.h>
#include <string.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#endif
static float frame_view[16], frame_projection[16];
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
#ifdef DND_GAMECUBE_STORAGE
  dnd_graphics_model_begin();
  Mtx44 p;
  for (int r = 0; r < 4; r++)
    for (int c = 0; c < 4; c++)
      p[r][c] = frame_projection[c * 4 + r];
  GX_LoadProjectionMtx(p, GX_PERSPECTIVE);
#endif
  dnd_model_frame_active = true;
  return 0;
}
int dnd_model_frame_end(void) {
  if (!dnd_model_frame_active)
    return -1;
#ifdef DND_GAMECUBE_STORAGE
  dnd_graphics_model_end();
#endif
  dnd_model_frame_active = false;
  return 0;
}
int dnd_model_draw(int token, DndArray *transform) {
  DndInstance *in = dnd_instance_find(token);
  float world[16];
  if (!in || !in->model->ready || !dnd_model_frame_active ||
      !matrix_array(transform, world))
    return -1;
  DndModel *model = in->model;
  for (unsigned mi = 0; mi < model->meshes; mi++) {
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
#ifdef DND_GAMECUBE_STORAGE
    Mtx matrix;
    for (int r = 0; r < 3; r++)
      for (int c = 0; c < 4; c++)
        matrix[r][c] = mv[c * 4 + r];
    GX_LoadPosMtxImm(matrix, GX_PNMTX0);
    GX_SetCurrentMtx(GX_PNMTX0);
    DndMaterial *mat = &model->material[mesh->material];
    float determinant =
        object[0] * (object[5] * object[10] - object[9] * object[6]) -
        object[4] * (object[1] * object[10] - object[9] * object[2]) +
        object[8] * (object[1] * object[6] - object[5] * object[2]);
    GX_SetCullMode(mat->two          ? GX_CULL_NONE
                   : determinant < 0 ? GX_CULL_FRONT
                                     : GX_CULL_BACK);
    GX_SetZMode(GX_TRUE, GX_LEQUAL, GX_TRUE);
    GX_SetBlendMode(GX_BM_NONE, GX_BL_ONE, GX_BL_ZERO, GX_LO_CLEAR);
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
    GX_SetNumTevStages(1);
    GX_SetNumTexGens(mat->texture >= 0 ? 1 : 0);
    GX_SetTevOrder(
        GX_TEVSTAGE0, mat->texture >= 0 ? GX_TEXCOORD0 : GX_TEXCOORDNULL,
        mat->texture >= 0 ? GX_TEXMAP0 : GX_TEXMAP_NULL, GX_COLOR0A0);
    GX_SetTevOp(GX_TEVSTAGE0, mat->texture >= 0 ? GX_MODULATE : GX_PASSCLR);
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
        if (!(mat->mode & 2)) {
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
