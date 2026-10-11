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
int dnd_model_instance_dispose(int token);
int dnd_model_play(int token, int clip, int loop, float fade);
int dnd_model_control(
    int token, int operation,
    float value); /* 0 update,1 pause,2 resume,3 stop,4 seek,5 speed */
int dnd_model_node_matrix(int token, int node, DndArray *matrix);
int dnd_model_draw(int token, DndArray *transform);
int dnd_model_frame_begin(DndArray *view, DndArray *projection);
int dnd_model_frame_end(void);
/* Reference-output inspection for tests and profiling, not managed heap
 * allocations. */
int dnd_model_vertex(int token, int mesh, int vertex, float out[6]);
int dnd_model_live_handles(void);
#endif
