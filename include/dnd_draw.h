#ifndef DND_DRAW_H
#define DND_DRAW_H
#include "dnd_managed.h"
#include "dnd_effect.h"
#include <stdint.h>
#include <math.h>
#include <stdbool.h>

typedef struct { float x,y,z,nx,ny,nz,u,v; uint32_t color; } DndDrawVertex;
typedef struct { const void *data; uint32_t bytes; int width,height,format; bool linear; } DndDrawTexture;
typedef struct { uint32_t color; int flags; float cutoff; const DndDrawEffect *effect; } DndDrawMaterial;
/* One graphics owner and one active pass: 1=3D, 2=2D, 3=raw, 4=compatibility immediate. */
int dnd_device_begin(float r,float g,float b,float a);
int dnd_device_end(void);
int dnd_device_pass_begin(int pass);
int dnd_device_pass_end(int pass);
int dnd_device_pass(void);
bool dnd_device_active(void);
int dnd_device_raw_begin(void);
int dnd_device_raw_end(void);
int dnd_device_viewport(int x,int y,int width,int height);
int dnd_device_scissor(int x,int y,int width,int height);
int dnd_device_width(void);
int dnd_device_height(void);
int dnd_device_wait(void);
void dnd_device_shutdown(void);
void dnd_immediate_begin(float r,float g,float b,float a);
void dnd_immediate_end(void);
bool dnd_draw_backend_immediate_ready(void);

int dnd_buffer_create(int bytes,int alignment);
uint64_t dnd_buffer_address(int token,int offset);
int dnd_buffer_copy(int token,int offset,DndArray *array,int index,int count,int write);
int dnd_buffer_write(int token,int offset,DndArray *a,int index,int count);
int dnd_buffer_read(int token,int offset,DndArray *a,int index,int count);
int dnd_buffer_flush(int token,int offset,int count,int invalidate);
int dnd_draw_release(int token);
int dnd_draw_bytes(int token);
int dnd_draw_live(void);
int dnd_draw_total_bytes(void);
void dnd_draw_collect(void);
void dnd_draw_shutdown(void);
int dnd_texture_create(int width,int height,DndArray *rgba,int linear);
int dnd_texture_png(DndArray *png,int linear);
int dnd_texture_package(DndArray *package,int linear);
int dnd_texture_dimension(int token,int axis);
int dnd_mesh_create(DndArray *vertices,DndArray *indices,int format,int dynamic);
int dnd_mesh_update(int token,DndArray *vertices,int offset,int count,int format);
int dnd_effect_create(DndArray *package);
int dnd_effect_color(int token,int slot,uint32_t color);
/* Borrowed for this native call only. 0 resolves to NULL; invalid, wrong-kind,
 * stale or pending resources return -2. Never retain across frame collection. */
int dnd_effect_resolve(int token,const DndDrawEffect **result);
int dnd_canvas_effect(int token,int effect);
int dnd_mesh_draw_effect(int token,int tex,DndArray *world,uint32_t color,int flags,float cutoff,int effect);
int dnd_mesh_draw(int mesh,int texture,DndArray *world,uint32_t color,int flags,float cutoff);
int dnd_mesh_primitives(DndArray *vertices,int offset,int count,int format,int primitive,DndArray *world,uint32_t color,int flags);
int dnd_canvas_create(int capacity);
int dnd_canvas_begin(int token,int width,int height);
int dnd_canvas_vertices(int token,int texture,DndArray *vertices,int count,uint32_t color);
int dnd_canvas_flush(int token);
int dnd_canvas_end(int token);
int dnd_canvas_clip(int token,int x,int y,int width,int height);
int dnd_canvas_blend(int token,int blend);
int dnd_canvas_stats(int token,int kind);
int dnd_draw_copy_texture(int texture,int x,int y,int width,int height);
void dnd_draw_set_camera(const float *view,const float *projection);
void dnd_draw_backend_vertices(const DndDrawVertex *vertices,int count,int primitive,const DndDrawTexture *texture,const DndDrawMaterial *material,const float *world);
void dnd_draw_backend_restore(void);
/* Combiner state only: callers retain texture, raster, sampler and draw state. */
void dnd_draw_backend_tev(const DndDrawEffect *effect,bool texture_bound);
void dnd_draw_backend_camera(const float *view,const float *projection);
void dnd_draw_backend_2d(int width,int height);
void dnd_draw_backend_viewport(int x,int y,int width,int height);
void dnd_draw_backend_scissor(int x,int y,int width,int height);
void dnd_draw_backend_copy(const DndDrawTexture *texture,int x,int y,int width,int height);
void dnd_draw_backend_wait(void);
void dnd_draw_backend_external(void);
void dnd_draw_backend_abort(void);
void dnd_draw_backend_flush(void *data,int bytes,int invalidate);
/* Raw command guard: no executor handoff while a primitive/display list is open. */
int dnd_raw_enter(int kind);
int dnd_raw_leave(int kind);
int dnd_raw_depth(void);
int dnd_device_claim_external(void);
uint64_t dnd_buffer_uncached(int token);
int dnd_native_object_size(int kind);
void dnd_raw_command(int operation,const uint64_t *args,int count);
uint64_t dnd_raw_record(int operation,const uint64_t *args,int count);
#endif
