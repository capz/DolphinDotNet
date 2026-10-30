#ifndef DND_MODEL_H
#define DND_MODEL_H
#include "dnd_managed.h"
int dnd_model_trs(DndArray *values, DndArray *matrix);
int dnd_model_multiply_arrays(DndArray *a, DndArray *b, DndArray *out);
int dnd_model_position(DndArray *values, DndArray *parent, float x, float y,
                       float z);
int dnd_model_translate(DndArray *values, float x, float y, float z);
int dnd_model_rotate(DndArray *values, float x, float y, float z);
int dnd_model_camera(DndArray *settings, DndArray *view, DndArray *projection);
int dnd_model_begin(DndString *path, int budget);
/* Runtime static OBJ import, including MTL/base-color images; OBJ has no clips. */
int dnd_model_begin_obj(DndString *path, int budget);
/* Source imports run on the service executor (or standalone/manual native
 * tests). They release the managed gate only over native-owned parsing data,
 * and reacquire it for storage and publication. */
/* Bounded embedded GLB profile, parsed natively without a desktop runtime. */
int dnd_model_begin_glb(DndString *path, int budget, int approximate_materials);
int dnd_model_begin_pso(DndString *path, DndString *textures, DndString *motion,
                        float motion_fps, int euler_key_bytes, int budget);
int dnd_model_begin_scene(DndString *path, int fbx, float sample_rate,
                          int budget, int approximate_materials);
/* F-Zero GX GMA/GCMF, AV TPL, LZSS and U8/ARC; selection=-1 loads all
 * GMA entries. options: 1=material approximation, 2=static deformation snapshot. */
int dnd_model_begin_format(DndString *path, DndString *textures,
                              DndString *motion, float fps, int format,
                              int budget, int approximate);
int dnd_model_begin_pso_ep3(DndString *path, DndString *textures, DndString *motion,
                             float fps, int euler_bytes, int budget, int approximate);
int dnd_model_begin_fzero(DndString *path, DndString *textures, int selection,
                          int budget, int options);
/* Runtime load of an already-compiled model from memory. */
int dnd_model_begin_bytes(DndArray *data, int budget);
int dnd_model_step(int token);
int dnd_model_dispose(int token);
int dnd_model_stat(int token,
                   int kind); /* 0 resident, 1 native bytes, 2 package length, 3
                                 clip count, 4 node count */
DndString *dnd_model_name(DndManagedHeap *heap, int token, int clip, int node);
float dnd_model_duration(int token, int clip);
int dnd_model_copy(int token, int offset, DndArray *array, int count,
                   int write);
int dnd_model_restore_begin(int token);
int dnd_model_restore_end(int token, int commit);
int dnd_model_unload(int token);
int dnd_model_instance(int token, int budget);
int dnd_model_instance_bytes(int token);
int dnd_model_instance_dispose(int token);
int dnd_model_play(int token, int clip, int loop, float fade);
int dnd_model_control(
    int token, int operation,
    float value); /* 0 update,1 pause,2 resume,3 stop,4 seek,5 speed */
int dnd_model_node_matrix(int token, int node, DndArray *matrix);
int dnd_model_draw(int token, DndArray *transform);
/* Per-draw RGB override; no model/instance mutation or ownership transfer.
 * 0=success, -1=invalid model/instance/frame/transform, -2=invalid/stale/pending
 * effect, -3=texture-required effect on a referenced untextured mesh. */
int dnd_model_draw_effect(int token,DndArray *transform,int effect);
/* All opaque, then global depth-sorted translucent submeshes. Max 128 objects / 4096 meshes. */
int dnd_model_draw_models(DndArray *tokens,DndArray *matrices,int count);
/* Optional opt-in per-material JSystem animated rendering. */
int dnd_model_draw_animated(int token, DndArray *transform, DndArray *channels);
int dnd_model_draw_animated_shapes(int token,DndArray *transform,DndArray *channels,DndArray *mesh_visibility);
int dnd_model_draw_material(int token,DndArray *transform,int texture,int secondary,uint32_t color,int flags,float cutoff,int effect);
int dnd_model_frame_begin(DndArray *view, DndArray *projection);
int dnd_model_frame_end(void);
/* Reference-output inspection for tests and profiling, not managed heap
 * allocations. */
int dnd_model_vertex(int token, int mesh, int vertex, float out[6]);
int dnd_model_live_handles(void);
#endif
