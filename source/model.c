#include "dnd_draw.h"
#include "dnd_model_internal.h"
#include "dnd_storage.h"
#include <limits.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <malloc.h>
#endif
DndModel *dnd_models[DND_MODEL_SLOTS];
DndInstance *dnd_instances[DND_INSTANCE_SLOTS];
bool dnd_model_frame_active;
static int next_token = 1;
unsigned dnd_model_u32(const unsigned char *p) {
  return ((unsigned)p[0] << 24) | ((unsigned)p[1] << 16) |
         ((unsigned)p[2] << 8) | p[3];
}
float dnd_model_f32(const unsigned char *p) {
  unsigned u = dnd_model_u32(p);
  float f;
  memcpy(&f, &u, 4);
  return f;
}
unsigned dnd_model_hash(const void *data, unsigned n) {
  const unsigned char *p = data;
  unsigned h = 2166136261u;
  while (n--)
    h = (h ^ *p++) * 16777619u;
  return h;
}
void *dnd_model_alloc(unsigned size, unsigned *used, unsigned budget) {
  if (size > UINT_MAX - 63)
    return NULL;
  unsigned total = (size + 63u) & ~31u;
  if (total > budget || *used > budget - total)
    return NULL;
#ifdef DND_GAMECUBE_STORAGE
  unsigned *p = memalign(32, total);
#else
  unsigned *p = aligned_alloc(32, total);
#endif
  if (!p)
    return NULL;
  memset(p, 0, total);
  p[0] = total;
  *used += total;
  return (unsigned char *)p + 32;
}
void dnd_model_free(void *data, unsigned *used) {
  if (!data)
    return;
  unsigned *p = (unsigned *)((unsigned char *)data - 32);
  *used -= p[0];
  free(p);
}
DndModel *dnd_model_find(int token, bool closed) {
  for (int i = 0; i < DND_MODEL_SLOTS; i++)
    if (dnd_models[i] && dnd_models[i]->token == token &&
        (closed || !dnd_models[i]->closed))
      return dnd_models[i];
  return NULL;
}
DndInstance *dnd_instance_find(int token) {
  for (int i = 0; i < DND_INSTANCE_SLOTS; i++)
    if (dnd_instances[i] && dnd_instances[i]->token == token)
      return dnd_instances[i];
  return NULL;
}
/* Texture payloads are immutable and shared across independently loaded models.
 */
static struct {
  void *data;
  unsigned size, hash, refs;
} texture_cache[512];
static unsigned texture_cache_bytes;
static bool texture_acquire(DndModel *m, DndTexture *t, const void *data) {
  unsigned charge = (t->size + 63u) & ~31u;
  if (charge > m->budget || m->bytes > m->budget - charge)
    return false;
  unsigned hash = dnd_model_hash(data, t->size);
  int slot = -1;
  for (int i = 0; i < 512; i++) {
    if (!texture_cache[i].refs) {
      if (slot < 0)
        slot = i;
    } else if (texture_cache[i].size == t->size &&
               texture_cache[i].hash == hash &&
               !memcmp(texture_cache[i].data, data, t->size)) {
      texture_cache[i].refs++;
      t->cache = i;
      t->data = texture_cache[i].data;
      m->bytes += charge;
      return true;
    }
  }
  if (slot < 0)
    return false;
  void *copy = dnd_model_alloc(t->size, &texture_cache_bytes, DND_MODEL_LIMIT);
  if (!copy)
    return false;
  memcpy(copy, data, t->size);
  texture_cache[slot].data = copy;
  texture_cache[slot].size = t->size;
  texture_cache[slot].hash = hash;
  texture_cache[slot].refs = 1;
  t->cache = slot;
  t->data = copy;
  m->bytes += charge;
#ifdef DND_GAMECUBE_STORAGE
  DCFlushRange(copy, t->size);
#endif
  return true;
}
static void drop_textures(DndModel *m) {
  if (m->texture)
    for (unsigned i = 0; i < m->textures; i++) {
      DndTexture *t = &m->texture[i];
      if (!t->data)
        continue;
      m->bytes -= (t->size + 63u) & ~31u;
      if (--texture_cache[t->cache].refs == 0) {
        dnd_model_free(texture_cache[t->cache].data, &texture_cache_bytes);
        texture_cache[t->cache].data = NULL;
      }
      t->data = NULL;
    }
}
void dnd_model_release(DndModel *m) {
  if (--m->refs)
    return;
  for (int i = 0; i < DND_MODEL_SLOTS; i++)
    if (dnd_models[i] == m)
      dnd_models[i] = NULL;
  int file = m->file;
  m->file = 0;
  drop_textures(m);
  dnd_model_free(m->package, &m->bytes);
  dnd_model_free(m->staged, &m->bytes);
  dnd_model_free(m->node, &m->bytes);
  dnd_model_free(m->mesh, &m->bytes);
  dnd_model_free(m->material, &m->bytes);
  dnd_model_free(m->texture, &m->bytes);
  dnd_model_free(m->skin, &m->bytes);
  dnd_model_free(m->clip, &m->bytes);
  free(m);
  if (file)
    dnd_fs_close(file);
}
int dnd_model_dispose(int token) {
  DndModel *m = dnd_model_find(token, true);
  if (!m || m->closed)
    return -1;
  m->closed = true;
  dnd_model_release(m);
  return 0;
}
typedef struct {
  const unsigned char *p, *end;
  bool ok;
} Reader;
static const unsigned char *take(Reader *r, unsigned n) {
  if (!r->ok || n > (unsigned)(r->end - r->p)) {
    r->ok = false;
    return r->end;
  }
  const unsigned char *p = r->p;
  r->p += n;
  return p;
}
static unsigned number(Reader *r) {
  const unsigned char *p = take(r, 4);
  return r->ok ? dnd_model_u32(p) : 0;
}
static float scalar(Reader *r) {
  const unsigned char *p = take(r, 4);
  float f = r->ok ? dnd_model_f32(p) : 0;
  if (!isfinite(f))
    r->ok = false;
  return f;
}
static void floats(Reader *r, float *out, unsigned n) {
  while (n--)
    *out++ = scalar(r);
}
static void name(Reader *r, char out[64]) {
  const unsigned char *p = take(r, 64);
  if (!r->ok)
    return;
  memcpy(out, p, 64);
  if (!memchr(out, 0, 64))
    r->ok = false;
}
static bool validate(DndModel *m, unsigned char *package, bool restoring) {
  if (m->length < 160 || dnd_model_u32(package) != 0x444e444d ||
      (dnd_model_u32(package + 4) != 1 && dnd_model_u32(package + 4) != 2) ||
      dnd_model_u32(package + 8) != m->length ||
      dnd_model_u32(package + 12) != 6)
    return false;
  Reader r[6];
  unsigned last = 160;
  for (unsigned i = 0; i < 6; i++) {
    const unsigned char *d = package + 16 + i * 16;
    unsigned type = dnd_model_u32(d), off = dnd_model_u32(d + 4),
             len = dnd_model_u32(d + 8);
    if (type != i + 1 || off % 32 || off < last || off > m->length ||
        len > m->length - off || len < 4 ||
        dnd_model_hash(package + off, len) != dnd_model_u32(d + 12))
      return false;
    r[i] = (Reader){package + off, package + off + len, true};
    last = off + len;
  }
  unsigned counts[6];
  unsigned limits[6] = {1024, 4096, 4097, 4096, 1024, 256};
  for (unsigned i = 0; i < 6; i++) {
    counts[i] = number(&r[i]);
    if (counts[i] > limits[i])
      return false;
  }
  if (!counts[0] || !counts[1] || !counts[2])
    return false;
  if (restoring) {
    if (counts[0] != m->nodes || counts[1] != m->meshes ||
        counts[2] != m->materials || counts[3] != m->textures ||
        counts[4] != m->skins || counts[5] != m->clips)
      return false;
  } else {
    m->nodes = counts[0];
    m->meshes = counts[1];
    m->materials = counts[2];
    m->textures = counts[3];
    m->skins = counts[4];
    m->clips = counts[5];
#define ALLOC(field, count, type)                                              \
  m->field = dnd_model_alloc((count) * sizeof(type), &m->bytes, m->budget);    \
  if ((count) && !m->field)                                                    \
  return false
    ALLOC(node, m->nodes, DndNode);
    ALLOC(mesh, m->meshes, DndMesh);
    ALLOC(material, m->materials, DndMaterial);
    ALLOC(texture, m->textures, DndTexture);
    ALLOC(skin, m->skins, DndSkin);
    ALLOC(clip, m->clips, DndClip);
#undef ALLOC
  }
  for (unsigned i = 0; i < m->nodes; i++) {
    DndNode *n = &m->node[i];
    n->parent = (int)number(&r[0]);
    name(&r[0], n->name);
    floats(&r[0], n->rest.t, 3);
    floats(&r[0], n->rest.q, 4);
    floats(&r[0], n->rest.s, 3);
    if (n->parent < -1 || n->parent >= (int)m->nodes || n->parent == (int)i)
      return false;
    float q = 0;
    for (int j = 0; j < 4; j++)
      q += n->rest.q[j] * n->rest.q[j];
    if (q < 0.99f || q > 1.01f)
      return false;
    for (int j = 0; j < 3; j++)
      if (fabsf(n->rest.s[j]) < 1e-6f)
        return false;
  }
  for (unsigned i = 0; i < m->nodes; i++) {
    int n = (int)i;
    unsigned steps = 0;
    while (n >= 0) {
      if (++steps > 128)
        return false;
      n = m->node[n].parent;
    }
  }
  for (unsigned i = 0; i < m->materials; i++) {
    DndMaterial *mat = &m->material[i];
    floats(&r[2], mat->color, 4);
    mat->texture = (int)number(&r[2]);
    mat->mode = (int)number(&r[2]);
    mat->cutoff = scalar(&r[2]);
    mat->two = (int)number(&r[2]);
    mat->secondary = -1;mat->combiner=0;
    if(dnd_model_u32(package+4)==2){mat->secondary=(int)number(&r[2]);mat->combiner=(int)number(&r[2]);}
    if(mat->secondary < -1 || mat->secondary >= (int)m->textures ||
       mat->combiner<0 || mat->combiner>2 ||
       ((mat->combiner==0)!=(mat->secondary==-1)))return false;
    if (mat->texture < -1 || mat->texture >= (int)m->textures ||
        mat->mode < 0 || (mat->mode & ~0x7ff) ||
        ((mat->mode & 0x7e0) && !(mat->mode & 16)) ||
        ((mat->mode & 16) && (!(mat->mode & 4) || (mat->mode & 8))) ||
        ((mat->mode & 8) && !(mat->mode & 4)) ||
        ((mat->mode & 4) && (mat->mode & 1)) ||
        mat->two < 0 || mat->two > 1 ||
        mat->cutoff < 0 || mat->cutoff > 1)
      return false;
    for (int j = 0; j < 4; j++)
      if (mat->color[j] < 0 || mat->color[j] > 1)
        return false;
  }
  for (unsigned i = 0; i < m->textures; i++) {
    DndTexture *t = &m->texture[i];
    t->width = number(&r[3]);
    t->height = number(&r[3]);
    t->levels = number(&r[3]);
    t->wrap_s = (int)number(&r[3]);
    t->wrap_t = (int)number(&r[3]);
    t->linear = (int)number(&r[3]);
    t->size = number(&r[3]);
    if (t->width < 4 || t->height < 4 || t->width > 1024 || t->height > 1024 ||
        (t->width & (t->width - 1)) || (t->height & (t->height - 1)) ||
        t->levels < 1 || t->levels > 9 || t->wrap_s < 0 || t->wrap_s > 2 ||
        t->wrap_t < 0 || t->wrap_t > 2 || t->linear < 0 || t->linear > 1)
      return false;
    unsigned size = 0;
    for (unsigned j = 0; j < t->levels; j++) {
      unsigned w = t->width >> j, h = t->height >> j;
      if (w < 4 || h < 4)
        return false;
      size += w * h * 4;
    }
    if (size != t->size)
      return false;
    const unsigned char *data = take(&r[3], size);
    if (!r[3].ok)
      return false;
    if (!texture_acquire(m, t, data))
      return false;
  }
  for (unsigned i = 0; i < m->skins; i++) {
    DndSkin *sk = &m->skin[i];
    sk->count = (int)number(&r[4]);
    if (sk->count < 1 || sk->count > 256)
      return false;
    sk->data = r[4].p;
    for (int j = 0; j < sk->count; j++) {
      unsigned node = number(&r[4]);
      if (node >= m->nodes)
        return false;
      float matrix[16];
      floats(&r[4], matrix, 16);
    }
  }
  for (unsigned i = 0; i < m->meshes; i++) {
    DndMesh *mesh = &m->mesh[i];
    mesh->node = (int)number(&r[1]);
    mesh->skin = (int)number(&r[1]);
    mesh->material = (int)number(&r[1]);
    mesh->vertices = number(&r[1]);
    mesh->indices = number(&r[1]);
    float bounds[6];
    floats(&r[1], bounds, 6);
    for (unsigned c = 0; c < 3; c++) {
      if (bounds[c] > bounds[c + 3]) return false;
      mesh->center[c] = bounds[c] * .5f + bounds[c + 3] * .5f;
    }
    if (mesh->node < 0 || mesh->node >= (int)m->nodes || mesh->skin < -1 ||
        mesh->skin >= (int)m->skins || mesh->material < 0 ||
        mesh->material >= (int)m->materials || !mesh->vertices ||
        mesh->vertices > 1000000 || !mesh->indices || mesh->indices > 3000000 ||
        mesh->indices % 3)
      return false;
    mesh->data = r[1].p;
    for (unsigned v = 0; v < mesh->vertices; v++) {
      float attributes[12], weights[4];
      floats(&r[1], attributes, 12);
      unsigned joints[4];
      for (int j = 0; j < 4; j++)
        joints[j] = number(&r[1]);
      floats(&r[1], weights, 4);
      float sum = 0;
      for (int j = 0; j < 4; j++) {
        if (weights[j] < 0 ||
            (mesh->skin >= 0 &&
             joints[j] >= (unsigned)m->skin[mesh->skin].count))
          return false;
        sum += weights[j];
      }
      if (mesh->skin >= 0 && (sum < 0.99f || sum > 1.01f))
        return false;
    }
    mesh->index = r[1].p;
    for (unsigned j = 0; j < mesh->indices; j++)
      if (number(&r[1]) >= mesh->vertices)
        return false;
  }
  for (unsigned i = 0; i < m->clips; i++) {
    DndClip *clip = &m->clip[i];
    name(&r[5], clip->name);
    clip->duration = scalar(&r[5]);
    clip->size = number(&r[5]);
    if (clip->duration < 0 || clip->size < 4)
      return false;
    for (unsigned j = 0; j < i; j++)
      if (!strcmp(clip->name, m->clip[j].name))
        return false;
    clip->tracks = take(&r[5], clip->size);
    if (!r[5].ok)
      return false;
    Reader tr = {clip->tracks, clip->tracks + clip->size, true};
    unsigned count = number(&tr);
    if (!count || count > m->nodes * 3)
      return false;
    unsigned char seen[1024 * 3] = {0};
    for (unsigned j = 0; j < count; j++) {
      unsigned node = number(&tr), prop = number(&tr), mode = number(&tr),
               keys = number(&tr);
      if (node >= m->nodes || prop > 2 || mode > 2 || !keys || keys > 1000000 ||
          seen[node * 3 + prop])
        return false;
      seen[node * 3 + prop] = 1;
      unsigned width = prop == 1 ? 4 : 3;
      float prev = -1;
      for (unsigned k = 0; k < keys; k++) {
        float time = scalar(&tr);
        if (time < 0 || time <= prev || time > clip->duration)
          return false;
        prev = time;
        for (unsigned v = 0; v < width * (mode == 2 ? 3 : 1); v++)
          (void)scalar(&tr);
      }
    }
    if (!tr.ok || tr.p != tr.end)
      return false;
  }
  for (int i = 0; i < 6; i++)
    if (!r[i].ok || r[i].p != r[i].end)
      return false;
  return true;
}
/* Create a resident model from caller-owned memory (validated, copied first).
 * Also used by the direct console OBJ importer. */
int dnd_model_begin_data(const unsigned char *data, unsigned length, int budget) {
  if (!data || length < 160 || length > DND_MODEL_LIMIT ||
      budget < 1024 || budget > (int)DND_MODEL_LIMIT ||
      next_token == INT_MAX)
    return 0;
  int slot = -1;
  for (int i = 0; i < DND_MODEL_SLOTS; i++)
    if (!dnd_models[i]) {slot = i; break;}
  if (slot < 0) return 0;
  DndModel *m = calloc(1, sizeof(*m));
  if (!m) return 0;
  m->token = next_token++;
  m->refs = 1;
  m->budget = (unsigned)budget;
  m->bytes = sizeof(*m);
  m->length = length;
  m->package = dnd_model_alloc(length, &m->bytes, m->budget);
  if (!m->package) {free(m); return 0;}
  memcpy(m->package, data, length);
  dnd_models[slot] = m;
  if (!validate(m, m->package, false)) {
    dnd_model_dispose(m->token);
    return 0;
  }
  m->hash = dnd_model_hash(m->package, m->length);
  m->ready = true;
  return m->token;
}
int dnd_model_begin_bytes(DndArray *data, int budget) {
  if (!data || data->element_size != 1 || data->elements_are_references)
    return 0;
  return dnd_model_begin_data(data->data, data->length, budget);
}
int dnd_model_begin(DndString *path, int budget) {
  if (budget < 1024 || budget > (int)DND_MODEL_LIMIT || next_token == INT_MAX)
    return 0;
  int slot = -1;
  for (int i = 0; i < DND_MODEL_SLOTS; i++)
    if (!dnd_models[i]) {
      slot = i;
      break;
    }
  if (slot < 0)
    return 0;
  int file = dnd_fs_open(path, 3, 1, 1);
  if (!file)
    return 0;
  int64_t length = dnd_fs_length_native(file);
  if (length < 160 || length > DND_MODEL_LIMIT || length > budget) {
    dnd_fs_close(file);
    return 0;
  }
  DndModel *m = calloc(1, sizeof(*m));
  if (!m) {
    dnd_fs_close(file);
    return 0;
  }
  m->token = next_token++;
  m->refs = 1;
  m->file = file;
  m->budget = (unsigned)budget;
  m->bytes = sizeof(*m);
  m->length = (unsigned)length;
  m->package = dnd_model_alloc(m->length, &m->bytes, m->budget);
  if (!m->package) {
    free(m);
    dnd_fs_close(file);
    return 0;
  }
  dnd_models[slot] = m;
  return m->token;
}
int dnd_model_step(int token) {
  DndModel *m = dnd_model_find(token, false);
  if (!m || m->ready || !m->file)
    return -1;
  unsigned n = m->length - m->position;
  if (n > 4096)
    n = 4096;
  int got = dnd_fs_read_native(m->file, m->package + m->position, (int)n);
  if (got <= 0)
    return -1;
  m->position += (unsigned)got;
  if (m->position < m->length)
    return 0;
  int file = m->file;
  m->file = 0;
  dnd_fs_close(file);
  if (!validate(m, m->package, false))
    return -1;
  m->hash = dnd_model_hash(m->package, m->length);
  m->ready = true;
  return 1;
}
int dnd_model_stat(int token, int kind) {
  DndModel *m = dnd_model_find(token, false);
  if (!m)
    return -1;
  switch (kind) {
  case 0:
    return m->ready ? 1 : 0;
  case 1:
    return (int)m->bytes;
  case 2:
    return (int)m->length;
  case 3:
    return (int)m->clips;
  case 4:
    return (int)m->nodes;
  case 5:
    return (int)m->materials;
  case 6:
    return (int)texture_cache_bytes;
  case 7:
    return (int)m->meshes;
  default:
    return -1;
  }
}
DndString *dnd_model_name(DndManagedHeap *heap, int token, int clip, int node) {
  DndModel *m = dnd_model_find(token, false);
  if (!m)
    return NULL;
  if (node >= 0 && (unsigned)node < m->nodes)
    return dnd_string_from_utf8(heap, m->node[node].name);
  if (clip >= 0 && (unsigned)clip < m->clips)
    return dnd_string_from_utf8(heap, m->clip[clip].name);
  return NULL;
}
float dnd_model_duration(int token, int clip) {
  DndModel *m = dnd_model_find(token, false);
  return m && clip >= 0 && (unsigned)clip < m->clips ? m->clip[clip].duration
                                                     : -1;
}
int dnd_model_copy(int token, int offset, DndArray *array, int count,
                   int write) {
  DndModel *m = dnd_model_find(token, false);
  if (!m || !array || array->element_size != 1 ||
      array->elements_are_references || offset < 0 || count < 0 ||
      (unsigned)count > array->length || (unsigned)offset > m->length ||
      (unsigned)count > m->length - (unsigned)offset)
    return -1;
  unsigned char *p = write ? m->staged : m->package;
  if (!p || (!write && !m->ready))
    return -1;
  memcpy(write ? p + offset : array->data, write ? array->data : p + offset,
         (unsigned)count);
  return 0;
}
int dnd_model_unload(int token) {
  DndModel *m = dnd_model_find(token, false);
  if (!m || !m->ready || m->restoring || dnd_device_active())
    return -1;
  for (int i = 0; i < DND_INSTANCE_SLOTS; i++)
    if (dnd_instances[i] && dnd_instances[i]->model == m)
      dnd_model_drop_vertices(dnd_instances[i]);
  drop_textures(m);
  dnd_model_free(m->package, &m->bytes);
  m->package = NULL;
  m->ready = false;
  return 0;
}
int dnd_model_restore_begin(int token) {
  DndModel *m = dnd_model_find(token, false);
  if (!m || m->ready || m->restoring || m->file)
    return -1;
  m->staged = dnd_model_alloc(m->length, &m->bytes, m->budget);
  if (!m->staged)
    return -1;
  m->restoring = true;
  return 0;
}
int dnd_model_restore_end(int token, int commit) {
  DndModel *m = dnd_model_find(token, false);
  if (!m || !m->restoring)
    return -1;
  int result = 0;
  if (commit) {
    if (dnd_model_hash(m->staged, m->length) != m->hash ||
        !validate(m, m->staged, true)) {
      drop_textures(m);
      result = -1;
    } else {
      m->package = m->staged;
      m->staged = NULL;
      m->ready = true;
    }
  }
  dnd_model_free(m->staged, &m->bytes);
  m->staged = NULL;
  m->restoring = false;
  if (m->ready) {
    for (int i = 0; i < DND_INSTANCE_SLOTS; i++)
      if (dnd_instances[i] && dnd_instances[i]->model == m &&
          dnd_model_evaluate(dnd_instances[i]) < 0) {
        (void)dnd_model_unload(token);
        result = -1;
        break;
      }
  }
  return result;
}
int dnd_model_live_handles(void) {
  int n = 0;
  for (int i = 0; i < DND_MODEL_SLOTS; i++)
    if (dnd_models[i])
      n++;
  for (int i = 0; i < DND_INSTANCE_SLOTS; i++)
    if (dnd_instances[i])
      n++;
  return n;
}
