#ifndef DND_MODEL_INTERNAL_H
#define DND_MODEL_INTERNAL_H
#include "dnd_model.h"
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
  const unsigned char *data, *index;
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
} DndMaterial;
typedef struct {
  unsigned width, height, levels, size;
  int wrap_s, wrap_t, linear, cache;
  void *data;
} DndTexture;
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
} DndModel;
typedef struct {
  int token, clip, next;
  bool loop, next_loop, paused;
  float time, next_time, speed, fade, elapsed;
  DndModel *model;
  unsigned budget, bytes;
  DndPose *pose, *other;
  float *global;
  unsigned char *done;
  float **vertices;
  float *workspace;
} DndInstance;
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
DndInstance *dnd_instance_find(int token);
void dnd_model_release(DndModel *model);
void dnd_model_identity(float m[16]);
void dnd_model_multiply(float out[16], const float a[16], const float b[16]);
bool dnd_model_inverse(float out[16], const float m[16]);
int dnd_model_evaluate(DndInstance *instance);
void dnd_model_drop_vertices(DndInstance *instance);
#endif
