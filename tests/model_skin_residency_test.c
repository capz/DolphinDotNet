/* Bounded cold-instance reconstruction uses the production allocator and skin
 * math. No converter, ARAM timing assumptions or renderer fixture is needed. */
#include "dnd_model_internal.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static bool frame_active;
static unsigned flushed_bytes;
bool dnd_device_active(void) { return frame_active; }
void dnd_fs_close(int token) { (void)token; assert(!"No fixture file owner."); }
void DCFlushRange(void *address, unsigned length) {
  assert(address && length);
  flushed_bytes += length;
}

static void put(unsigned char *p, unsigned value) {
  for (unsigned i = 0; i < 4; i++) p[i] = (unsigned char)(value >> (24-i*8));
}
static void put_float(unsigned char *p, float value) {
  unsigned bits; memcpy(&bits, &value, sizeof(bits)); put(p, bits);
}
static void near(float a, float b) { assert(fabsf(a-b) < .0002f); }
static unsigned charged(unsigned size) { return (size+63u)&~31u; }

typedef struct {
  DndModel model;
  DndNode *nodes;
  DndMesh *meshes;
  DndSkin skins[2];
  DndMaterial material;
  unsigned char *joints[2], *source[2];
  unsigned token;
} Fixture;

static void fixture_init(Fixture *f, unsigned joints, unsigned vertices) {
  memset(f, 0, sizeof(*f));
  f->nodes = calloc(joints, sizeof(*f->nodes));
  f->meshes = calloc(4, sizeof(*f->meshes));
  assert(f->nodes && f->meshes);
  for (unsigned i = 0; i < joints; i++) {
    f->nodes[i].parent = -1;
    f->nodes[i].rest.q[3] = 1;
    f->nodes[i].rest.t[0] = (float)(i+1);
    f->nodes[i].rest.s[0] = 2;
    f->nodes[i].rest.s[1] = 3;
    f->nodes[i].rest.s[2] = 4;
  }
  for (unsigned s = 0; s < 2; s++) {
    unsigned count = s ? joints : 1;
    unsigned n = s ? vertices : 3;
    f->joints[s] = calloc(count, 68);
    f->source[s] = calloc(n, 80);
    assert(f->joints[s] && f->source[s]);
    f->skins[s] = (DndSkin){.count=(int)count,.data=f->joints[s]};
    for (unsigned i = 0; i < count; i++) {
      put(f->joints[s]+i*68, i);
      for (unsigned k = 0; k < 16; k++)
        put_float(f->joints[s]+i*68+4+k*4, k%5==0 ? 1 : 0);
    }
    for (unsigned v = 0; v < n; v++) {
      unsigned char *p = f->source[s]+v*80;
      put_float(p, 1+(float)(v%17)*.01f);
      put_float(p+4, 2); put_float(p+8, 3);
      for (unsigned k = 0; k < 3; k++) put_float(p+12+k*4, 1/sqrtf(3));
      for (unsigned k = 0; k < 4; k++) put(p+48+k*4, (v+k)%count);
      put_float(p+64, .6f); put_float(p+68, .4f);
    }
    f->meshes[s*2] = (DndMesh){.node=0,.skin=(int)s,.vertices=n,.data=f->source[s]};
    f->meshes[s*2+1] = (DndMesh){.node=0,.skin=-1,.vertices=1};
  }
  f->model = (DndModel){.token=901,.refs=1,.ready=true,.nodes=joints,
    .meshes=4,.skins=2,.materials=1,.node=f->nodes,.mesh=f->meshes,
    .skin=f->skins,.material=&f->material};
  assert(!dnd_models[0]); dnd_models[0] = &f->model;
  f->token = (unsigned)dnd_model_instance(f->model.token, 4*1024*1024);
  assert(f->token);
}
static void fixture_dispose(Fixture *f) {
  assert(dnd_model_instance_dispose((int)f->token)==0 && f->model.refs==1);
  dnd_models[0] = NULL;
  for (unsigned i = 0; i < 2; i++) { free(f->joints[i]); free(f->source[i]); }
  free(f->nodes); free(f->meshes);
}
static void retire(DndInstance *in, unsigned budget) {
  unsigned cursor = 0, steps = 0;
  for (;;) {
    unsigned before = cursor;
    int rc = dnd_model_skin_retire_step(in, &cursor, budget);
    assert(rc>=0 && cursor-before<=budget);
    for (unsigned m = 0; m+1 < cursor; m++) assert(!in->vertices[m]);
    if (cursor) assert(!in->workspace && !in->palette_joints);
    steps++;
    if (rc) break;
    assert(cursor>before && steps<=in->model->meshes+1);
  }
  assert(cursor==in->model->meshes+1);
  unsigned used = in->bytes;
  assert(dnd_model_skin_retire_step(in, &cursor, budget)==1 && in->bytes==used);
}
static unsigned prepared_vertices(const DndModel *m, const DndSkinPrepareProgress *p) {
  unsigned count = p->vertex;
  for (unsigned i = 0; i < p->mesh; i++)
    if (m->mesh[i].skin>=0) count += m->mesh[i].vertices;
  return count;
}
static void check_tail(const DndModel *m, DndInstance *in,
                       const DndSkinPrepareProgress *p, bool painted[4]) {
  for (unsigned i = 0; i < 4; i++) {
    if (!in->vertices[i]) continue;
    unsigned bytes = m->mesh[i].vertices*6*sizeof(float);
    unsigned complete = i<p->mesh ? bytes : i==p->mesh ? p->vertex*6*sizeof(float) : 0;
    unsigned char *raw = (unsigned char *)in->vertices[i];
    if (!painted[i]) { memset(raw+complete, 0xcd, bytes-complete); painted[i] = true; }
    /* Detect work beyond the reported vertex cursor, including an accidental
     * whole-buffer zeroing/skinning pass. Full output parity is checked below. */
    for (unsigned k = complete; k<bytes && k<complete+64; k++) assert(raw[k]==0xcd);
    unsigned last = bytes>64 ? bytes-64 : 0;
    if (last<complete) last = complete;
    for (unsigned k = last; k<bytes; k++) assert(raw[k]==0xcd);
  }
}
static void check_prepare(unsigned joints, unsigned vertices, unsigned budget) {
  Fixture f; fixture_init(&f, joints, vertices);
  DndInstance *in = dnd_instance_find((int)f.token); assert(in);
  unsigned warm_bytes = in->bytes;
  float *expected[2];
  for (unsigned i = 0; i < 2; i++) {
    unsigned n = f.meshes[i*2].vertices*6*sizeof(float);
    expected[i] = malloc(n); assert(expected[i]); memcpy(expected[i], in->vertices[i*2], n);
  }
  float value[6]; assert(dnd_model_vertex((int)f.token, 2, 0, value)==0);
  near(value[0], 2+(.6f+ .4f*(joints>1 ? 2 : 1)));
  near(value[1], 6); near(value[2], 12);
  float normal_length = sqrtf(.25f+1.f/9+1.f/16);
  near(value[3], .5f/normal_length);
  near(value[4], (1.f/3)/normal_length);
  near(value[5], .25f/normal_length);
  float *globals = malloc(joints*64); assert(globals); memcpy(globals, in->global, joints*64);
  in->time=.375f; in->next_time=.125f; in->elapsed=.25f; in->paused=true;
  retire(in, 1); unsigned cold_bytes = in->bytes;
  assert(cold_bytes<warm_bytes);
  f.model.ready = false; /* Async restoration has not published the model yet. */
  DndSkinPrepareProgress progress = {0}; bool painted[4] = {0}; unsigned steps = 0;
  for (;;) {
    unsigned before = prepared_vertices(&f.model, &progress);
    flushed_bytes = 0;
    int rc = dnd_model_skin_prepare_step(in, &progress, budget);
    assert(rc>=0 && progress.work<=budget);
    assert(flushed_bytes<=progress.work*6*sizeof(float));
    assert(prepared_vertices(&f.model, &progress)-before<=progress.work);
    check_tail(&f.model, in, &progress, painted);
    steps++;
    if (rc) break;
    assert(progress.work && steps<vertices+joints+32);
  }
  assert(in->bytes==warm_bytes && in->palette_joints==joints);
  assert(!memcmp(globals, in->global, joints*64));
  assert(in->time==.375f && in->next_time==.125f && in->elapsed==.25f && in->paused);
  for (unsigned i = 0; i < 2; i++) {
    assert(!memcmp(expected[i], in->vertices[i*2], f.meshes[i*2].vertices*6*sizeof(float)));
    free(expected[i]);
  }
  free(globals);
  assert(dnd_model_skin_prepare_step(in, &progress, budget)==1 && !progress.work);
  assert(in->bytes==warm_bytes);

  /* Cancellation may arrive after allocation, palette work or partial output.
   * Each of these private states must be reclaimable with bounded retirement. */
  for (unsigned cancel_after = 1; cancel_after <= 16; cancel_after++) {
    retire(in, 2); assert(in->bytes==cold_bytes);
    memset(&progress, 0, sizeof(progress));
    for (unsigned i = 0; i < cancel_after; i++)
      assert(dnd_model_skin_prepare_step(in, &progress, 1)>=0);
    retire(in, 1); assert(in->bytes==cold_bytes);
  }
  memset(&progress, 0, sizeof(progress));
  unsigned partial_steps = 0;
  while (progress.mesh!=2 || !progress.vertex) {
    assert(dnd_model_skin_prepare_step(in, &progress, 1)==0);
    assert(++partial_steps<joints+32);
  }
  assert(in->vertices[2] && progress.vertex==1);
  retire(in, 1); assert(in->bytes==cold_bytes);

  /* A successful earlier allocation must not leak when a later mesh allocation
   * exceeds the instance's exact budget. The caller can roll back in slices. */
  in->budget = cold_bytes + charged(joints*32*sizeof(float)) + charged(3*6*sizeof(float));
  memset(&progress, 0, sizeof(progress));
  int rc;
  do { rc = dnd_model_skin_prepare_step(in, &progress, 3); assert(progress.work<=3); } while (!rc);
  assert(rc==-2 && in->workspace && in->vertices[0] && !in->vertices[2]);
  retire(in, 1); assert(in->bytes==cold_bytes);
  in->budget = cold_bytes;
  memset(&progress, 0, sizeof(progress));
  do { rc = dnd_model_skin_prepare_step(in, &progress, 3); } while (!rc);
  assert(rc==-2 && in->bytes==cold_bytes && !in->workspace);
  retire(in, 1); assert(in->bytes==cold_bytes);
  in->budget = 4*1024*1024;
  float saved_scale = in->global[0]; in->global[0] = 0;
  memset(&progress, 0, sizeof(progress));
  do { rc = dnd_model_skin_prepare_step(in, &progress, 3); } while (!rc);
  assert(rc==-1 && in->workspace && in->vertices[0]);
  retire(in, 1); assert(in->bytes==cold_bytes); in->global[0] = saved_scale;
  memset(&progress, 0, sizeof(progress));
  frame_active = true;
  assert(dnd_model_skin_prepare_step(in, &progress, 8)==-1 && !in->workspace);
  unsigned cursor = 0;
  assert(dnd_model_skin_retire_step(in, &cursor, 8)==-1 && !cursor);
  frame_active = false;
  assert(dnd_model_skin_prepare_step(in, &progress, 0)==-1);
  assert(dnd_model_skin_retire_step(in, &cursor, 0)==-1);
  printf("Bounded skin restore: %u joints, %u vertices, budget %u, %u steps.\n",
         joints, vertices, budget, steps);
  fixture_dispose(&f);
}
static void check_static_walk(void) {
  DndModel model = {.meshes=1024};
  model.mesh = calloc(model.meshes, sizeof(*model.mesh)); assert(model.mesh);
  DndInstance in = {.model=&model};
  in.vertices = calloc(model.meshes, sizeof(*in.vertices)); assert(in.vertices);
  for (unsigned i = 0; i < model.meshes; i++) model.mesh[i].skin=-1;
  DndSkinPrepareProgress p = {0}; unsigned steps = 0;
  for (;;) {
    unsigned before = p.mesh;
    int rc = dnd_model_skin_prepare_step(&in, &p, 1);
    assert(rc>=0 && p.mesh-before<=1 && p.work<=1);
    steps++;
    if (rc) break;
  }
  assert(steps==model.meshes+1 && !in.workspace && !in.bytes);
  free(in.vertices); free(model.mesh);
}
int main(void) {
  check_prepare(1, 257, 1);
  check_prepare(2, 4097, 7);
  check_prepare(256, 65535, 31);
  check_static_walk();
  puts("Bounded cold skin restore/retire, playback preservation, normals, budgets and rollback passed.");
  return 0;
}
