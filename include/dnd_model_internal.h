#ifndef DND_MODEL_INTERNAL_H
#define DND_MODEL_INTERNAL_H
#include "dnd_model.h"
#include "dnd_model_material.h"
#define DND_MODEL_SLOTS 64
#define DND_INSTANCE_SLOTS 128
#define DND_MODEL_LIMIT (16u * 1024u * 1024u)
typedef struct {
  float t[3], q[4], s[3];
} DndPose;
typedef struct {
  int parent;
  char name[64];
  DndPose rest;
} DndNode;
typedef struct {
  int node, skin, material;
  unsigned vertices, indices;
  float center[3];
  const unsigned char *data, *index;
  /* Validated once while publishing/restoring immutable package geometry. */
  bool uniform_color, unit_normals; /* unit_normals also permits zero normals. */
  float color[4];
} DndMesh;
typedef struct {
  int count;
  const unsigned char *data;
} DndSkin;
typedef struct {
  char name[64];
  float duration;
  const unsigned char *tracks;
  unsigned size;
} DndClip;
typedef struct {
  float color[4], cutoff;
  int texture, mode, two;
  int secondary, combiner; /* DNMD v2: 0=original, 1=multiply, 2=add */
} DndMaterial;
typedef struct {
  unsigned width, height, levels, size;
  int wrap_s, wrap_t, linear, cache;
  unsigned hash; /* Retained immutable content hash for sliced cache restore. */
  void *data;
} DndTexture;
struct DndModelResidency;
typedef struct {
  int token, file, refs;
  bool closed, ready, restoring;
  unsigned budget, bytes, length, position, hash;
  unsigned char *package, *staged;
  unsigned nodes, meshes, materials, textures, skins, clips;
  DndNode *node;
  DndMesh *mesh;
  DndMaterial *material;
  DndTexture *texture;
  DndSkin *skin;
  DndClip *clip;
  DndAuthoredMaterial *authored;
  unsigned authored_count;
  struct DndModelResidency *residency;
  unsigned char residency_digest[32];
  bool residency_digest_valid, residency_offsets, residency_disabled;
} DndModel;
typedef struct {
  int token, clip, next;
  bool loop, next_loop, paused;
  float time, next_time, speed, fade, elapsed;
  DndModel *model;
  unsigned budget, bytes;
  DndPose *pose, *other, *offset;
  unsigned char *offset_set, *color_set;
  float *colors;
  float *global;
  unsigned char *done;
  float **vertices;
  float *workspace;
  unsigned palette_joints;
  unsigned *draw_order;
  float *draw_depth;
} DndInstance;
static inline bool dnd_model_translucent(const DndInstance *in,unsigned material) {
  return (in->model->material[material].mode&4)||
    (in->color_set&&(in->color_set[material*4]&2)&&in->colors[material*16+3]<255);
}
extern DndModel *dnd_models[DND_MODEL_SLOTS];
extern DndInstance *dnd_instances[DND_INSTANCE_SLOTS];
extern bool dnd_model_frame_active;
unsigned dnd_model_u32(const unsigned char *p);
float dnd_model_f32(const unsigned char *p);
unsigned dnd_model_hash(const void *p, unsigned length);
void *dnd_model_alloc(unsigned size, unsigned *used, unsigned budget);
void dnd_model_free(void *p, unsigned *used);
DndModel *dnd_model_find(int token, bool closed);
int dnd_model_begin_data(const unsigned char *bytes, unsigned length, int budget);
int dnd_model_begin_authored_data(const unsigned char *bytes,unsigned length,int budget,
                                 const DndAuthoredMaterial *materials,unsigned count);
DndInstance *dnd_instance_find(int token);
void dnd_model_release(DndModel *model);
void dnd_model_identity(float m[16]);
void dnd_model_multiply(float out[16], const float a[16], const float b[16]);
bool dnd_model_inverse(float out[16], const float m[16]);
int dnd_model_evaluate(DndInstance *instance);
void dnd_model_sort_draws(DndInstance *instance, const float world[16],
                           const float view[16]);
void dnd_model_drop_vertices(DndInstance *instance);
/* Residency rebuilds use retained global matrices and never advance playback.
 * Each charged work item is one skin/mesh inspection, allocation, joint matrix
 * or output vertex. work reports units consumed by the most recent call. */
typedef struct {
  unsigned mesh, joint, vertex, phase, work;
} DndSkinPrepareProgress;
void *dnd_model_alloc_uninitialized(unsigned size, unsigned *used,
                                    unsigned budget);
int dnd_model_skin_prepare_step(DndInstance *instance,
                                DndSkinPrepareProgress *progress,
                                unsigned maximum_work);
/* Cursor zero retires the palette workspace; cursors 1..meshes retire one
 * output each. Resources may only be retired after their last GPU reader. */
int dnd_model_skin_retire_step(DndInstance *instance, unsigned *cursor,
                               unsigned maximum_work);
#endif
