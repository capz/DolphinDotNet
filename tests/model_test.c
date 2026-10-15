#define _POSIX_C_SOURCE 200809L
#include "dnd_model_internal.h"
#include "dnd_performance.h"
#include "dnd_storage.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static DndManagedHeap heap;
static int load(const char *path) {
  DndString *s = dnd_string_from_utf8(&heap, path);
  int m = dnd_model_begin(s, 8 * 1024 * 1024);
  assert(m);
  int status;
  while ((status = dnd_model_step(m)) == 0) {
  }
  assert(status == 1);
  return m;
}
static void near(float a, float b) { assert(fabsf(a - b) < 0.001f); }
static void put(unsigned char *p, unsigned value) {
  p[0] = (unsigned char)(value >> 24);
  p[1] = (unsigned char)(value >> 16);
  p[2] = (unsigned char)(value >> 8);
  p[3] = (unsigned char)value;
}
static void corrupt_cases(const char *root, const unsigned char *original,
                          unsigned length) {
  unsigned char *data = malloc(length);
  assert(data);
  char path[1024];
  snprintf(path, sizeof(path), "%s/sd/malformed.dndmodel", root);
  for (int test = 0; test < 6; test++) {
    memcpy(data, original, length);
    unsigned section = test == 0   ? 0
                       : test == 1 ? 1
                       : test == 2 ? 4
                       : test == 3 ? 5
                       : test == 4 ? 3
                                   : 1;
    unsigned char *dir = data + 16 + section * 16;
    unsigned off = dnd_model_u32(dir + 4), size = dnd_model_u32(dir + 8);
    if (test == 0)
      put(data + off + 4, 0); /* self-parent */
    if (test == 1)
      put(data + off + 4 + 44 + 48, 256); /* invalid vertex joint */
    if (test == 2)
      put(data + off + 8, 1024); /* invalid skin node */
    if (test == 3)
      put(data + off + 84, 3); /* unknown track property */
    if (test == 4)
      put(data + off + 4, 3); /* unsupported texture dimension */
    if (test == 5)
      put(dir + 4, 160); /* overlapping directory ranges */
    else
      put(dir + 12, dnd_model_hash(data + off, size));
    FILE *f = fopen(path, "wb");
    assert(f);
    assert(fwrite(data, 1, length, f) == length);
    assert(!fclose(f));
    DndString *name = dnd_string_from_utf8(&heap, "sd:/malformed.dndmodel");
    int token = dnd_model_begin(name, 8 * 1024 * 1024);
    assert(token);
    int result;
    while ((result = dnd_model_step(token)) == 0) {
    }
    assert(result < 0);
    assert(!dnd_model_dispose(token));
    assert(!dnd_fs_open_handles());
  }
  remove(path);
  free(data);
}
int main(int argc, char **argv) {
  assert(argc == 2);
  setenv("DND_STORAGE_ROOT", argv[1], 1);
  static unsigned char memory[128 * 1024];
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  assert(dnd_storage_mount(2));
  int m = load("sd:/animated.dndmodel"),
      a = dnd_model_instance(m, 4 * 1024 * 1024),
      b = dnd_model_instance(m, 4 * 1024 * 1024);
  assert(a && b);
  float v[6];
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0);
  assert(dnd_model_play(a, 0, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  assert(dnd_model_vertex(b, 0, 0, v) == 0);
  near(v[0], 0);
  assert(dnd_model_play(a, 1, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0);
  assert(dnd_model_control(a, 4, 1) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 2);
  assert(dnd_model_play(a, 2, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  assert(dnd_model_play(a, 3, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 1, v) == 0);
  near(v[0], 0.5f);
  near(v[1], 0.5f);
  near(v[5], 1);
  assert(dnd_model_play(a, 4, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 1);
  assert(dnd_model_play(a, 0, 1, 0) == 0);
  assert(dnd_model_control(a, 0, 1.25f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.25f);
  assert(dnd_model_play(a, 4, 0, 0.5f) == 0);
  assert(dnd_model_control(a, 0, 0.25f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  unsigned length = (unsigned)dnd_model_stat(m, 2);
  unsigned char *backup = malloc(length);
  assert(backup);
  DndArray *buffer = dnd_managed_array_new(&heap, 4096, 1);
  for (unsigned p = 0; p < length;) {
    unsigned n = length - p;
    if (n > 4096)
      n = 4096;
    assert(dnd_model_copy(m, (int)p, buffer, (int)n, 0) == 0);
    memcpy(backup + p, buffer->data, n);
    p += n;
  }
  int shared = load("sd:/animated.dndmodel");
  DndModel *original = dnd_model_find(m, false),
           *duplicate = dnd_model_find(shared, false);
  assert(original->texture[0].data == duplicate->texture[0].data);
  assert(dnd_model_dispose(shared) == 0);
  int bytes = dnd_model_stat(m, 1);
  assert(dnd_model_unload(m) == 0);
  assert(dnd_model_stat(m, 1) < bytes);
  assert(dnd_model_control(a, 0, 0) < 0);
  assert(dnd_model_restore_begin(m) == 0);
  buffer->data[0] = 99;
  assert(dnd_model_copy(m, 0, buffer, 1, 1) == 0);
  assert(dnd_model_restore_end(m, 1) < 0);
  assert(dnd_model_stat(m, 0) == 0);
  assert(dnd_model_restore_begin(m) == 0);
  for (unsigned p = 0; p < length;) {
    unsigned n = length - p;
    if (n > 4096)
      n = 4096;
    memcpy(buffer->data, backup + p, n);
    assert(dnd_model_copy(m, (int)p, buffer, (int)n, 1) == 0);
    p += n;
  }
  assert(dnd_model_restore_end(m, 1) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  corrupt_cases(argv[1], backup, length);
  free(backup);
  DndArray *matrix = dnd_managed_array_new(&heap, 16, 4);
  float identity[16];
  dnd_model_identity(identity);
  memcpy(matrix->data, identity, 64);
  assert(dnd_model_frame_begin(matrix, matrix) == 0);
  assert(dnd_performance_stats()->triangles == 0);
  assert(dnd_model_draw(a, matrix) == 0);
  assert(dnd_performance_stats()->triangles == 1);
  assert(dnd_model_draw(b, matrix) == 0);
  assert(dnd_performance_stats()->triangles == 2);
  assert(dnd_model_draw(0, matrix) < 0);
  assert(dnd_performance_stats()->triangles == 2);
  assert(dnd_model_unload(m) < 0);
  assert(dnd_model_instance_dispose(a) < 0);
  assert(dnd_model_frame_end() == 0);
  assert(dnd_model_instance(m, 1024) == 0);
  assert(dnd_model_dispose(m) == 0);
  assert(dnd_model_play(a, 0, 0, 0) == 0);
  assert(dnd_model_instance_dispose(a) == 0);
  assert(dnd_model_instance_dispose(b) == 0);
  assert(dnd_model_dispose(m) < 0);
  assert(!dnd_model_live_handles());
  assert(!dnd_fs_open_handles());
  for (int i = 0; i < 20; i++) {
    int model = load("sd:/animated.dndmodel");
    assert(dnd_model_dispose(model) == 0);
  }
  assert(!dnd_model_live_handles());
  assert(!dnd_exception_pending());
  int fbx = load("sd:/fbx.dndmodel");
  int fi = dnd_model_instance(fbx, 4 * 1024 * 1024);
  assert(fi);
  DndModel *fm = dnd_model_find(fbx, false);
  int walk = -1;
  for (unsigned i = 0; i < fm->clips; i++)
    if (!strcmp(fm->clip[i].name, "Walk"))
      walk = (int)i;
  assert(walk >= 0);
  assert(dnd_model_play(fi, walk, 0, 0) == 0);
  assert(dnd_model_control(fi, 0, 0.5f) == 0);
  assert(dnd_model_vertex(fi, 0, 0, v) == 0);
  near(v[0], 0.5f);
  assert(dnd_model_instance_dispose(fi) == 0);
  assert(dnd_model_dispose(fbx) == 0);
  int obj = load("sd:/obj.dndmodel");
  DndModel *om = dnd_model_find(obj, false);
  assert(om->textures == 1 && om->clips == 0 && om->mesh[0].indices == 6);
  assert(dnd_model_dispose(obj) == 0);
  assert(!dnd_model_live_handles());
  DndString *obj_path=dnd_string_from_utf8(&heap,"sd:/live.obj");
  int live=dnd_model_begin_obj(obj_path,8*1024*1024);
  assert(live);
  assert(dnd_model_stat(live,3)==0&&dnd_model_stat(live,4)==1);
  int live_instance=dnd_model_instance(live,4*1024*1024);
  assert(live_instance);
  assert(dnd_model_vertex(live_instance,0,1,v)==0);
  near(v[0],1.f);
  assert(dnd_model_instance_dispose(live_instance)==0);
  assert(dnd_model_dispose(live)==0);
  live=load("sd:/geometry-gj.dndmodel");
  live_instance=dnd_model_instance(live,4*1024*1024);
  assert(live_instance);
  assert(dnd_model_vertex(live_instance,0,1,v)==0);
  near(v[0],1.f); /* vertex 1 has local position (1,0,0). */
  near(v[1],0.f);
  assert(dnd_model_instance_dispose(live_instance)==0);
  assert(dnd_model_dispose(live)==0);
  assert(!dnd_model_live_handles());
  puts("Model validation, playback, skinning, live OBJ and PSO GJ import tests passed");
  return 0;
}
