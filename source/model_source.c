/* Bounded direct source import. GLB JSON is parsed by cgltf; binary scalars are
 * decoded explicitly as little-endian so the same path works on PowerPC.
 * This profile requires embedded buffers/images, core triangles and TRS. */
#include "dnd_async.h"
#include "dnd_model_internal.h"
#include "dnd_storage.h"
#include "dnd_ufbx.h"
#include <limits.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

typedef struct {
  unsigned used, budget;
  int failed, executor;
} ImportMemory;
typedef struct {
  unsigned char *data;
  unsigned size, capacity;
  ImportMemory *memory;
} ImportBuffer;
static unsigned char *source_file(const char *path, ImportMemory *memory,
                                  unsigned *size);
/* Called with the managed execution gate RELEASED. Storage and publication
 * briefly resume our service context. No managed pointer escapes these scopes;
 * failures become import failures instead of unwinding past native ownership.
 */
static int64_t import_io(ImportMemory *m, int operation, const char *path,
                         int file, void *data, int count, int64_t offset) {
  dnd_async_native_end(m->executor);
  volatile int64_t result = -1;
  DndEhFrame frame;
  dnd_eh_push(&frame);
  if (setjmp(frame.environment) == 0) {
    switch (operation) {
    case 0:
      result = dnd_fs_open_utf8_native(path, 3, 1, 1);
      break;
    case 1:
      result = dnd_fs_length_native(file);
      break;
    case 2:
      result = dnd_fs_read_native(file, data, count);
      break;
    case 3:
      result = dnd_fs_seek(file, offset, 1);
      break;
    case 4:
      dnd_fs_close(file);
      result = 0;
      break;
    }
  }
  dnd_eh_pop(&frame);
  if (dnd_exception_pending()) {
    dnd_exception_clear();
    m->failed = 1;
    result = -1;
  }
  m->executor = dnd_async_native_begin();
  return result;
}
static int import_open(ImportMemory *m, const char *path) {
  int64_t file = import_io(m, 0, path, 0, NULL, 0, 0);
  return file > 0 ? (int)file : 0;
}
static int64_t import_length(ImportMemory *m, int file) {
  return import_io(m, 1, NULL, file, NULL, 0, 0);
}
static int import_read(ImportMemory *m, int file, void *data, int count) {
  /* Give application storage work a gate opportunity between bounded reads. */
  return (int)import_io(m, 2, NULL, file, data, count > 4096 ? 4096 : count, 0);
}
static int64_t import_seek(ImportMemory *m, int file, int64_t offset) {
  return import_io(m, 3, NULL, file, NULL, 0, offset);
}
static void import_close(ImportMemory *m, int file) {
  (void)import_io(m, 4, NULL, file, NULL, 0, 0);
}
static int import_publish(ImportMemory *m, const unsigned char *package,
                          unsigned size, int budget) {
  dnd_async_native_end(m->executor);
  int token = !m->failed && m->used < (unsigned)budget
                  ? dnd_model_begin_data(package, size, budget - (int)m->used)
                  : 0;
  if (token)
    dnd_model_find(token, false)->budget = (unsigned)budget;
  m->executor = dnd_async_native_begin();
  return token;
}
static void *imp_alloc(void *user, size_t size) {
  ImportMemory *m = user;
  if (size > UINT_MAX) {
    m->failed = 1;
    return NULL;
  }
  void *p = dnd_model_alloc((unsigned)size, &m->used, m->budget);
  if (!p)
    m->failed = 1;
  return p;
}
static void imp_free(void *user, void *p) {
  ImportMemory *m = user;
  dnd_model_free(p, &m->used);
}
static ImportMemory
    *image_memory; /* Only the serialized service executor imports sources. */
static void *image_alloc(size_t size) { return imp_alloc(image_memory, size); }
static void image_free(void *p) { imp_free(image_memory, p); }
static void *image_resize(void *p, size_t old, size_t size) {
  void *q = image_alloc(size);
  if (q && p)
    memcpy(q, p, old < size ? old : size);
  if (q)
    image_free(p);
  return q;
}
#define CGLTF_IMPLEMENTATION
#include "dnd_cgltf.h"
#define STBI_MALLOC(n) image_alloc(n)
#define STBI_FREE(p) image_free(p)
#define STBI_REALLOC_SIZED(p, o, n) image_resize(p, o, n)
#define STBI_NO_STDIO
#define STBI_NO_THREAD_LOCALS
#define STBI_ONLY_PNG
#define STBI_ONLY_JPEG
#define STBI_ONLY_TGA
#define STBI_ONLY_BMP
#define STBI_MAX_DIMENSIONS 1024
#define STB_IMAGE_IMPLEMENTATION
#if defined(__GNUC__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wunused-function"
#endif
#include "dnd_stb_image.h"
#if defined(__GNUC__)
#pragma GCC diagnostic pop
#endif

static unsigned le32(const unsigned char *p) {
  return (unsigned)p[0] | (unsigned)p[1] << 8 | (unsigned)p[2] << 16 |
         (unsigned)p[3] << 24;
}
static void put32(unsigned char *p, unsigned v) {
  p[0] = (unsigned char)(v >> 24);
  p[1] = (unsigned char)(v >> 16);
  p[2] = (unsigned char)(v >> 8);
  p[3] = (unsigned char)v;
}
static void bytes(ImportBuffer *b, const void *data, unsigned n) {
  if (b->memory->failed)
    return;
  if (n > DND_MODEL_LIMIT - b->size) {
    b->memory->failed = 1;
    return;
  }
  if (b->size + n > b->capacity) {
    unsigned capacity = b->capacity ? b->capacity : 256;
    while (capacity < b->size + n)
      capacity *= 2;
    unsigned char *next = imp_alloc(b->memory, capacity);
    if (!next)
      return;
    if (b->size)
      memcpy(next, b->data, b->size);
    imp_free(b->memory, b->data);
    b->data = next;
    b->capacity = capacity;
  }
  if (n)
    memcpy(b->data + b->size, data, n);
  b->size += n;
}
static void number(ImportBuffer *b, unsigned value) {
  unsigned char p[4];
  put32(p, value);
  bytes(b, p, 4);
}
static void scalar(ImportBuffer *b, float value) {
  unsigned word;
  if (!isfinite(value)) {
    b->memory->failed = 1;
    return;
  }
  memcpy(&word, &value, 4);
  number(b, word);
}
static void floats(ImportBuffer *b, const float *values, unsigned count) {
  for (unsigned i = 0; i < count; i++)
    scalar(b, values[i]);
}
static void name(ImportBuffer *b, const char *value, const char *fallback) {
  unsigned char p[64] = {0};
  if (!value || !*value)
    value = fallback;
  size_t size = strlen(value);
  if (size > 63) {
    b->memory->failed = 1;
    return;
  }
  memcpy(p, value, size);
  bytes(b, p, 64);
}
static const unsigned char *view(ImportMemory *m, const cgltf_buffer_view *v) {
  if (!v || !v->buffer || !v->buffer->data || v->has_meshopt_compression ||
      v->offset > v->buffer->size || v->size > v->buffer->size - v->offset) {
    m->failed = 1;
    return NULL;
  }
  return (const unsigned char *)v->buffer->data + v->offset;
}
static int accessor_valid(ImportMemory *m, const cgltf_accessor *a,
                          cgltf_type shape) {
  if (!a || a->type != shape || a->is_sparse || a->count < 1 ||
      a->count > 196608 || !view(m, a->buffer_view)) {
    m->failed = 1;
    return 0;
  }
  size_t component = cgltf_component_size(a->component_type),
         width = cgltf_num_components(a->type) * component;
  if (!component || a->stride < width || a->offset > a->buffer_view->size ||
      width > a->buffer_view->size - a->offset ||
      (a->count - 1) > (a->buffer_view->size - a->offset - width) / a->stride) {
    m->failed = 1;
    return 0;
  }
  return 1;
}
static float component(ImportMemory *m, const cgltf_accessor *a, size_t index,
                       unsigned c) {
  if (m->failed || index >= a->count || c >= cgltf_num_components(a->type)) {
    m->failed = 1;
    return 0;
  }
  const unsigned char *data = view(m, a->buffer_view);
  if (!data)
    return 0;
  size_t size = cgltf_component_size(a->component_type);
  const unsigned char *p = data + a->offset + index * a->stride + c * size;
  float f = 0;
  switch (a->component_type) {
  case cgltf_component_type_r_8:
    f = (float)(signed char)p[0];
    if (a->normalized)
      f = fmaxf(-1, f / 127);
    break;
  case cgltf_component_type_r_8u:
    f = p[0];
    if (a->normalized)
      f /= 255;
    break;
  case cgltf_component_type_r_16: {
    unsigned v = (unsigned)p[0] | (unsigned)p[1] << 8;
    f = (float)(int16_t)v;
    if (a->normalized)
      f = fmaxf(-1, f / 32767);
    break;
  }
  case cgltf_component_type_r_16u:
    f = (float)((unsigned)p[0] | (unsigned)p[1] << 8);
    if (a->normalized)
      f /= 65535;
    break;
  case cgltf_component_type_r_32u:
    if (a->normalized)
      m->failed = 1;
    f = (float)le32(p);
    break;
  case cgltf_component_type_r_32f: {
    unsigned word = le32(p);
    if (a->normalized)
      m->failed = 1;
    memcpy(&f, &word, 4);
    break;
  }
  default:
    m->failed = 1;
    break;
  }
  if (!isfinite(f)) {
    m->failed = 1;
    return 0;
  }
  return f;
}
static int selected(ImportMemory *m, const cgltf_data *d,
                    const cgltf_node *node) {
  const cgltf_node *p = node;
  for (unsigned depth = 0; p->parent; depth++) {
    if (depth >= 128) {
      m->failed = 1;
      return 0;
    }
    p = p->parent;
  }
  if (!d->scene)
    return 1;
  for (size_t i = 0; i < d->scene->nodes_count; i++)
    if (d->scene->nodes[i] == p)
      return 1;
  return 0;
}
static const cgltf_accessor *attribute(const cgltf_primitive *p,
                                       cgltf_attribute_type type) {
  for (size_t i = 0; i < p->attributes_count; i++)
    if (p->attributes[i].type == type && p->attributes[i].index == 0)
      return p->attributes[i].data;
  return NULL;
}
static unsigned vertex_index(ImportMemory *m, const cgltf_primitive *p,
                             unsigned index, unsigned count) {
  float value = p->indices ? component(m, p->indices, index, 0) : (float)index;
  if (value < 0 || value >= count || value != (float)(unsigned)value) {
    m->failed = 1;
    return 0;
  }
  return (unsigned)value;
}
static int wrap(cgltf_wrap_mode value) {
  return value == cgltf_wrap_mode_repeat            ? 1
         : value == cgltf_wrap_mode_clamp_to_edge   ? 0
         : value == cgltf_wrap_mode_mirrored_repeat ? 2
                                                    : -1;
}

static int import_glb(const char *path, int budget, int approximate,
                      int executor) {
  ImportMemory mem = {0, (unsigned)budget, 0, executor};
  ImportBuffer sections[6];
  for (int i = 0; i < 6; i++)
    sections[i] = (ImportBuffer){NULL, 0, 0, &mem};
  cgltf_data *data = NULL;
  unsigned char *source = NULL, *package = NULL;
  int token = 0, file = 0;
  file = import_open(&mem, path);
  if (!file)
    goto done;
  int64_t length = import_length(&mem, file);
  if (length < 28 || length > 2 * 1024 * 1024)
    goto done;
  source = imp_alloc(&mem, (size_t)length);
  if (!source)
    goto done;
  for (int64_t pos = 0; pos < length;) {
    int n = import_read(&mem, file, source + pos, (int)(length - pos));
    if (n <= 0)
      goto done;
    pos += n;
  }
  import_close(&mem, file);
  file = 0;
  if (le32(source) != 0x46546c67 || le32(source + 4) != 2 ||
      le32(source + 8) != (unsigned)length)
    goto done;
  unsigned json_size = le32(source + 12);
  if (le32(source + 16) != 0x4e4f534a || json_size % 4 ||
      json_size > (unsigned)length - 28)
    goto done;
  unsigned bin_offset = 20 + json_size, bin_size = le32(source + bin_offset);
  if (le32(source + bin_offset + 4) != 0x004e4942 || bin_size % 4 ||
      bin_size != (unsigned)length - bin_offset - 8)
    goto done;
  cgltf_options options = {0};
  options.type = cgltf_file_type_gltf;
  options.memory.alloc_func = imp_alloc;
  options.memory.free_func = imp_free;
  options.memory.user_data = &mem;
  if (cgltf_parse(&options, source + 20, json_size, &data) !=
      cgltf_result_success)
    goto done;
  if (!data->asset.version || strcmp(data->asset.version, "2.0") ||
      data->buffers_count != 1 || data->buffers[0].uri ||
      data->buffers[0].size > bin_size || data->nodes_count < 1 ||
      data->nodes_count > 1024 || data->materials_count > 4095 ||
      data->textures_count > 256 || data->skins_count > 256 ||
      data->animations_count > 256)
    goto done;
  for (size_t i = 0; i < data->extensions_required_count; i++)
    if (strcmp(data->extensions_required[i], "KHR_materials_unlit"))
      goto done;
  data->buffers[0].data = source + bin_offset + 8;
  number(&sections[0], (unsigned)data->nodes_count);
  unsigned meshes = 0;
  for (size_t i = 0; i < data->nodes_count; i++) {
    cgltf_node *n = &data->nodes[i];
    if (n->has_matrix || n->weights_count || n->has_mesh_gpu_instancing)
      goto done;
    if (selected(&mem, data, n) && n->mesh)
      meshes += (unsigned)n->mesh->primitives_count;
    number(&sections[0],
           n->parent ? (unsigned)(n->parent - data->nodes) : UINT_MAX);
    name(&sections[0], n->name, "Node");
    float t[3] = {0}, q[4] = {0, 0, 0, 1}, s[3] = {1, 1, 1};
    floats(&sections[0], n->has_translation ? n->translation : t, 3);
    floats(&sections[0], n->has_rotation ? n->rotation : q, 4);
    floats(&sections[0], n->has_scale ? n->scale : s, 3);
  }
  if (!meshes || meshes > 4096 || mem.failed)
    goto done;
  number(&sections[1], meshes);
  number(&sections[2], (unsigned)data->materials_count + 1);
  float white[4] = {1, 1, 1, 1};
  floats(&sections[2], white, 4);
  number(&sections[2], UINT_MAX);
  number(&sections[2], 0);
  scalar(&sections[2], .5f);
  number(&sections[2], 0);
  for (size_t i = 0; i < data->materials_count; i++) {
    cgltf_material *m = &data->materials[i];
    cgltf_texture_view tx = m->pbr_metallic_roughness.base_color_texture;
    if (m->alpha_mode == cgltf_alpha_mode_blend || tx.texcoord != 0 ||
        tx.has_transform)
      goto done;
    if (!approximate &&
        (m->normal_texture.texture || m->occlusion_texture.texture ||
         m->emissive_texture.texture || m->has_pbr_specular_glossiness ||
         m->has_clearcoat || m->has_transmission || m->has_sheen ||
         m->has_iridescence || m->has_anisotropy || m->has_volume ||
         m->has_diffuse_transmission ||
         (!m->unlit &&
          (m->pbr_metallic_roughness.metallic_factor > 0 ||
           m->pbr_metallic_roughness.metallic_roughness_texture.texture))))
      goto done;
    floats(&sections[2], m->pbr_metallic_roughness.base_color_factor, 4);
    number(&sections[2],
           tx.texture ? (unsigned)(tx.texture - data->textures) : UINT_MAX);
    number(&sections[2], (m->alpha_mode == cgltf_alpha_mode_mask ? 1u : 0u) |
                             (m->unlit ? 2u : 0u));
    scalar(&sections[2], m->alpha_cutoff);
    number(&sections[2], m->double_sided ? 1 : 0);
  }
  number(&sections[3], (unsigned)data->textures_count);
  image_memory = &mem;
  for (size_t i = 0; i < data->textures_count; i++) {
    cgltf_texture *tx = &data->textures[i];
    cgltf_image *im = tx->image;
    if (!im || im->uri || !im->buffer_view || tx->has_basisu || tx->has_webp)
      goto done;
    const unsigned char *encoded = view(&mem, im->buffer_view);
    if (!encoded || im->buffer_view->size > INT_MAX)
      goto done;
    if (!im->mime_type || (strcmp(im->mime_type, "image/png") &&
                           strcmp(im->mime_type, "image/jpeg")))
      goto done;
    if (!strcmp(im->mime_type, "image/png")) {
      static const unsigned char signature[8] = {137, 80, 78, 71,
                                                 13,  10, 26, 10};
      if (im->buffer_view->size < 8 || memcmp(encoded, signature, 8))
        goto done;
    } else if (im->buffer_view->size < 2 || encoded[0] != 255 ||
               encoded[1] != 216)
      goto done;
    int w, h, channels;
    if (!stbi_info_from_memory(encoded, (int)im->buffer_view->size, &w, &h,
                               &channels) ||
        w < 4 || h < 4 || w > 1024 || h > 1024 || (w & (w - 1)) ||
        (h & (h - 1)))
      goto done;
    unsigned char *pixels = stbi_load_from_memory(
        encoded, (int)im->buffer_view->size, &w, &h, &channels, 4);
    if (!pixels)
      goto done;
    int ws = tx->sampler ? wrap(tx->sampler->wrap_s) : 1,
        wt = tx->sampler ? wrap(tx->sampler->wrap_t) : 1;
    number(&sections[3], (unsigned)w);
    number(&sections[3], (unsigned)h);
    number(&sections[3], 1);
    number(&sections[3], (unsigned)ws);
    number(&sections[3], (unsigned)wt);
    number(&sections[3], !tx->sampler || tx->sampler->mag_filter !=
                                             cgltf_filter_type_nearest);
    number(&sections[3], (unsigned)(w * h * 4));
    for (int y = 0; y < h; y += 4)
      for (int x = 0; x < w; x += 4) {
        unsigned char tile[64];
        for (int row = 0; row < 4; row++)
          for (int col = 0; col < 4; col++) {
            unsigned k = (unsigned)(row * 4 + col) * 2;
            const unsigned char *p = pixels + ((y + row) * w + x + col) * 4;
            tile[k] = p[3];
            tile[k + 1] = p[0];
            tile[k + 32] = p[1];
            tile[k + 33] = p[2];
          }
        bytes(&sections[3], tile, 64);
      }
    stbi_image_free(pixels);
    if (mem.failed)
      goto done;
  }
  number(&sections[4], (unsigned)data->skins_count);
  for (size_t i = 0; i < data->skins_count; i++) {
    cgltf_skin *sk = &data->skins[i];
    if (sk->joints_count < 1 || sk->joints_count > 256)
      goto done;
    if (sk->inverse_bind_matrices &&
        (!accessor_valid(&mem, sk->inverse_bind_matrices, cgltf_type_mat4) ||
         sk->inverse_bind_matrices->component_type !=
             cgltf_component_type_r_32f ||
         sk->inverse_bind_matrices->count != sk->joints_count))
      goto done;
    number(&sections[4], (unsigned)sk->joints_count);
    for (size_t j = 0; j < sk->joints_count; j++) {
      number(&sections[4], (unsigned)(sk->joints[j] - data->nodes));
      for (unsigned k = 0; k < 16; k++)
        scalar(&sections[4],
               sk->inverse_bind_matrices
                   ? component(&mem, sk->inverse_bind_matrices, j, k)
                   : (k % 5 == 0 ? 1.f : 0.f));
    }
  }
  for (size_t ni = 0; ni < data->nodes_count; ni++) {
    cgltf_node *n = &data->nodes[ni];
    if (!n->mesh || !selected(&mem, data, n))
      continue;
    for (size_t pi = 0; pi < n->mesh->primitives_count; pi++) {
      cgltf_primitive *p = &n->mesh->primitives[pi];
      if (p->type != cgltf_primitive_type_triangles || p->targets_count ||
          p->has_draco_mesh_compression || n->mesh->weights_count)
        goto done;
      const cgltf_accessor *pos = attribute(p, cgltf_attribute_type_position),
                           *normal = attribute(p, cgltf_attribute_type_normal),
                           *uv = attribute(p, cgltf_attribute_type_texcoord),
                           *color = attribute(p, cgltf_attribute_type_color),
                           *joints = attribute(p, cgltf_attribute_type_joints),
                           *weights =
                               attribute(p, cgltf_attribute_type_weights);
      if (!accessor_valid(&mem, pos, cgltf_type_vec3) ||
          pos->component_type != cgltf_component_type_r_32f ||
          pos->count > 65536)
        goto done;
      for (size_t ai = 0; ai < p->attributes_count; ai++) {
        cgltf_attribute a = p->attributes[ai];
        if (a.index != 0 || a.type == cgltf_attribute_type_tangent ||
            a.type == cgltf_attribute_type_invalid)
          goto done;
        if (a.data->count != pos->count)
          goto done;
      }
      if ((normal && !accessor_valid(&mem, normal, cgltf_type_vec3)) ||
          (uv && !accessor_valid(&mem, uv, cgltf_type_vec2)) ||
          (color &&
           !accessor_valid(&mem, color,
                           color->type == cgltf_type_vec3 ? cgltf_type_vec3
                                                          : cgltf_type_vec4)))
        goto done;
      if (p->material &&
          p->material->pbr_metallic_roughness.base_color_texture.texture && !uv)
        goto done;
      if ((n->skin && (!joints || !weights)) ||
          (!n->skin && (joints || weights)))
        goto done;
      if (joints && (!accessor_valid(&mem, joints, cgltf_type_vec4) ||
                     !accessor_valid(&mem, weights, cgltf_type_vec4) ||
                     (joints->component_type != cgltf_component_type_r_8u &&
                      joints->component_type != cgltf_component_type_r_16u) ||
                     joints->normalized))
        goto done;
      if (p->indices &&
          (!accessor_valid(&mem, p->indices, cgltf_type_scalar) ||
           p->indices->normalized ||
           (p->indices->component_type != cgltf_component_type_r_8u &&
            p->indices->component_type != cgltf_component_type_r_16u &&
            p->indices->component_type != cgltf_component_type_r_32u)))
        goto done;
      unsigned nv = (unsigned)pos->count,
               count = p->indices ? (unsigned)p->indices->count : nv;
      if (!count || count % 3)
        goto done;
      float *generated = NULL;
      if (!normal) {
        generated = imp_alloc(&mem, nv * 3 * sizeof(float));
        if (!generated)
          goto done;
        for (unsigned k = 0; k < count; k += 3) {
          unsigned ids[3];
          float v[3][3];
          for (unsigned j = 0; j < 3; j++) {
            ids[j] = vertex_index(&mem, p, k + j, nv);
            for (unsigned c = 0; c < 3; c++)
              v[j][c] = component(&mem, pos, ids[j], c);
          }
          float a[3], b[3], cross[3];
          for (unsigned c = 0; c < 3; c++) {
            a[c] = v[1][c] - v[0][c];
            b[c] = v[2][c] - v[0][c];
          }
          cross[0] = a[1] * b[2] - a[2] * b[1];
          cross[1] = a[2] * b[0] - a[0] * b[2];
          cross[2] = a[0] * b[1] - a[1] * b[0];
          for (unsigned j = 0; j < 3; j++)
            for (unsigned c = 0; c < 3; c++)
              generated[ids[j] * 3 + c] += cross[c];
        }
        for (unsigned v = 0; v < nv; v++) {
          float *a = generated + v * 3,
                l = sqrtf(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
          if (l < 1e-12f)
            a[1] = 1;
          else
            for (unsigned c = 0; c < 3; c++)
              a[c] /= l;
        }
      }
      number(&sections[1], (unsigned)ni);
      number(&sections[1],
             n->skin ? (unsigned)(n->skin - data->skins) : UINT_MAX);
      number(&sections[1],
             p->material ? (unsigned)(p->material - data->materials) + 1 : 0);
      number(&sections[1], nv);
      number(&sections[1], count);
      float low[3] = {INFINITY, INFINITY, INFINITY},
            high[3] = {-INFINITY, -INFINITY, -INFINITY};
      for (unsigned v = 0; v < nv; v++)
        for (unsigned c = 0; c < 3; c++) {
          float value = component(&mem, pos, v, c);
          low[c] = fminf(low[c], value);
          high[c] = fmaxf(high[c], value);
        }
      floats(&sections[1], low, 3);
      floats(&sections[1], high, 3);
      for (unsigned v = 0; v < nv; v++) {
        for (unsigned c = 0; c < 3; c++)
          scalar(&sections[1], component(&mem, pos, v, c));
        for (unsigned c = 0; c < 3; c++)
          scalar(&sections[1],
                 normal ? component(&mem, normal, v, c) : generated[v * 3 + c]);
        for (unsigned c = 0; c < 2; c++)
          scalar(&sections[1], uv ? component(&mem, uv, v, c) : 0);
        for (unsigned c = 0; c < 4; c++)
          scalar(&sections[1], color && c < cgltf_num_components(color->type)
                                   ? component(&mem, color, v, c)
                                   : 1);
        for (unsigned c = 0; c < 4; c++)
          number(&sections[1],
                 joints ? (unsigned)component(&mem, joints, v, c) : 0);
        float sum = 0;
        for (unsigned c = 0; weights && c < 4; c++)
          sum += component(&mem, weights, v, c);
        if (weights && sum <= 0) {
          imp_free(&mem, generated);
          goto done;
        }
        for (unsigned c = 0; c < 4; c++)
          scalar(&sections[1],
                 weights ? component(&mem, weights, v, c) / sum : 0);
      }
      for (unsigned k = 0; k < count; k++)
        number(&sections[1], vertex_index(&mem, p, k, nv));
      imp_free(&mem, generated);
      if (mem.failed)
        goto done;
    }
  }
  number(&sections[5], (unsigned)data->animations_count);
  for (size_t ai = 0; ai < data->animations_count; ai++) {
    cgltf_animation *anim = &data->animations[ai];
    ImportBuffer tracks = {NULL, 0, 0, &mem};
    float duration = 0;
    if (!anim->channels_count || anim->channels_count > data->nodes_count * 3)
      goto done;
    number(&tracks, (unsigned)anim->channels_count);
    for (size_t ci = 0; ci < anim->channels_count; ci++) {
      cgltf_animation_channel *ch = &anim->channels[ci];
      cgltf_animation_sampler *s = ch->sampler;
      unsigned property =
          ch->target_path == cgltf_animation_path_type_translation ? 0
          : ch->target_path == cgltf_animation_path_type_rotation  ? 1
          : ch->target_path == cgltf_animation_path_type_scale     ? 2
                                                                   : 3;
      if (!ch->target_node || ch->target_node->has_matrix || property == 3 ||
          !s || !accessor_valid(&mem, s->input, cgltf_type_scalar) ||
          !accessor_valid(&mem, s->output,
                          property == 1 ? cgltf_type_vec4 : cgltf_type_vec3) ||
          s->input->component_type != cgltf_component_type_r_32f ||
          s->output->component_type != cgltf_component_type_r_32f) {
        imp_free(&mem, tracks.data);
        goto done;
      }
      unsigned factor =
          s->interpolation == cgltf_interpolation_type_cubic_spline ? 3 : 1;
      if (s->output->count != s->input->count * factor) {
        imp_free(&mem, tracks.data);
        goto done;
      }
      number(&tracks, (unsigned)(ch->target_node - data->nodes));
      number(&tracks, property);
      number(&tracks, s->interpolation == cgltf_interpolation_type_step ? 0
                      : factor == 3                                     ? 2
                                                                        : 1);
      number(&tracks, (unsigned)s->input->count);
      float previous = -1;
      for (size_t k = 0; k < s->input->count; k++) {
        float time = component(&mem, s->input, k, 0);
        if (time < 0 || time <= previous)
          mem.failed = 1;
        previous = time;
        duration = fmaxf(duration, time);
        scalar(&tracks, time);
        for (unsigned j = 0; j < factor; j++)
          for (unsigned c = 0; c < (property == 1 ? 4u : 3u); c++)
            scalar(&tracks, component(&mem, s->output, k * factor + j, c));
      }
    }
    char fallback[32];
    snprintf(fallback, sizeof(fallback), "Animation%u", (unsigned)ai);
    name(&sections[5], anim->name, fallback);
    scalar(&sections[5], duration);
    number(&sections[5], tracks.size);
    bytes(&sections[5], tracks.data, tracks.size);
    imp_free(&mem, tracks.data);
    if (mem.failed)
      goto done;
  }
  {
    unsigned total = 160, offsets[6];
    for (unsigned i = 0; i < 6; i++) {
      offsets[i] = total;
      if (sections[i].size > DND_MODEL_LIMIT - total)
        goto done;
      total = (total + sections[i].size + 31) & ~31u;
    }
    package = imp_alloc(&mem, total);
    if (!package)
      goto done;
    put32(package, 0x444e444d);
    put32(package + 4, 1);
    put32(package + 8, total);
    put32(package + 12, 6);
    for (unsigned i = 0; i < 6; i++) {
      unsigned char *dir = package + 16 + i * 16;
      put32(dir, i + 1);
      put32(dir + 4, offsets[i]);
      put32(dir + 8, sections[i].size);
      put32(dir + 12, dnd_model_hash(sections[i].data, sections[i].size));
      memcpy(package + offsets[i], sections[i].data, sections[i].size);
      imp_free(&mem, sections[i].data);
      sections[i].data = NULL;
    }
    cgltf_free(data);
    data = NULL;
    imp_free(&mem, source);
    source = NULL;
    token = import_publish(&mem, package, total, budget);
  }
done:
  if (file)
    import_close(&mem, file);
  if (data)
    cgltf_free(data);
  for (unsigned i = 0; i < 6; i++)
    imp_free(&mem, sections[i].data);
  imp_free(&mem, source);
  imp_free(&mem, package);
  image_memory = NULL;
  return token;
}

/* FBX and OBJ share the on-device ufbx reader. Every allocator and external
 * stream is bounded by the caller's native model budget and mounted volumes. */
typedef struct {
  ImportMemory *memory;
  int file;
} SceneStream;
typedef struct {
  ImportMemory memory;
  char filename[1024], directory[1024], volume[32];
} SceneImport;
typedef struct {
  ufbx_texture *texture;
  int wrap_s, wrap_t;
} SceneTexture;
static void *scene_resize(void *user, void *old, size_t old_size,
                          size_t new_size) {
  if (!new_size) {
    imp_free(user, old);
    return NULL;
  }
  void *next = imp_alloc(user, new_size);
  if (next && old)
    memcpy(next, old, old_size < new_size ? old_size : new_size);
  if (next)
    imp_free(user, old);
  return next;
}
static void scene_free(void *user, void *p, size_t size) {
  (void)size;
  imp_free(user, p);
}
static ufbx_allocator_opts scene_allocator(SceneImport *ctx) {
  ufbx_allocator_opts a = {0};
  a.allocator.user = &ctx->memory;
  a.allocator.alloc_fn = imp_alloc;
  a.allocator.realloc_fn = scene_resize;
  a.allocator.free_fn = scene_free;
  a.memory_limit = ctx->memory.budget;
  a.allocation_limit = 100000;
  return a;
}
static int scene_path(SceneImport *ctx, const char *reference, size_t size,
                      char path[1024]) {
  if (size >= 1024 || memchr(reference, 0, size))
    return 0;
  char value[1024];
  memcpy(value, reference, size);
  value[size] = 0;
  for (size_t i = 0; i < size; i++)
    if (value[i] == '\\')
      value[i] = '/';
  if (strchr(value, ':')) {
    if (strncasecmp(value, ctx->volume, strlen(ctx->volume)))
      return 0;
    memcpy(path, value, size + 1);
  } else {
    int n = snprintf(path, 1024, "%s%s", ctx->directory, value);
    if (n < 0 || n >= 1024)
      return 0;
  }
  return 1;
}
static size_t scene_read(void *user, void *data, size_t size) {
  SceneStream *stream = user;
  if (size > INT_MAX)
    return SIZE_MAX;
  int n = import_read(stream->memory, stream->file, data, (int)size);
  return n < 0 ? SIZE_MAX : (size_t)n;
}
static bool scene_skip(void *user, size_t size) {
  SceneStream *s = user;
#if SIZE_MAX > INT64_MAX
  if (size > INT64_MAX)
    return false;
#endif
  return import_seek(s->memory, s->file, (int64_t)size) >= 0;
}
static uint64_t scene_size(void *user) {
  SceneStream *s = user;
  int64_t size = import_length(s->memory, s->file);
  return size < 0 ? UINT64_MAX : (uint64_t)size;
}
static void scene_close(void *user) {
  SceneStream *s = user;
  import_close(s->memory, s->file);
  imp_free(s->memory, s);
}
static bool scene_open(void *user, ufbx_stream *stream, const char *reference,
                       size_t length, const ufbx_open_file_info *info) {
  SceneImport *ctx = user;
  char path[1024];
  if (info->type != UFBX_OPEN_FILE_OBJ_MTL ||
      !scene_path(ctx, reference, length, path))
    return false;
  int file = import_open(&ctx->memory, path);
  if (!file)
    return false;
  int64_t size = import_length(&ctx->memory, file);
  if (size < 0 || size > 2 * 1024 * 1024) {
    import_close(&ctx->memory, file);
    return false;
  }
  SceneStream *s = imp_alloc(&ctx->memory, sizeof(*s));
  if (!s) {
    import_close(&ctx->memory, file);
    return false;
  }
  *s = (SceneStream){&ctx->memory, file};
  stream->user = s;
  stream->read_fn = scene_read;
  stream->skip_fn = scene_skip;
  stream->size_fn = scene_size;
  stream->close_fn = scene_close;
  return true;
}
static unsigned char *scene_image_file(SceneImport *ctx, ufbx_string reference,
                                       unsigned *length) {
  char path[1024];
  if (!scene_path(ctx, reference.data, reference.length, path))
    return NULL;
  int file = import_open(&ctx->memory, path);
  if (!file)
    return NULL;
  int64_t size = import_length(&ctx->memory, file);
  unsigned char *data = NULL;
  if (size > 0 && size <= 2 * 1024 * 1024) {
    data = imp_alloc(&ctx->memory, (size_t)size);
    if (data) {
      unsigned p = 0;
      while (p < (unsigned)size) {
        int n = import_read(&ctx->memory, file, data + p,
                            (int)((unsigned)size - p));
        if (n <= 0)
          break;
        p += (unsigned)n;
      }
      if (p != (unsigned)size) {
        imp_free(&ctx->memory, data);
        data = NULL;
      } else
        *length = (unsigned)size;
    }
  }
  import_close(&ctx->memory, file);
  return data;
}
static int scene_texture(SceneImport *ctx, ImportBuffer *out,
                         SceneTexture texture) {
  ufbx_texture *tx = texture.texture;
  if (tx->type != UFBX_TEXTURE_FILE || tx->has_uv_transform)
    return 0;
  ufbx_blob content = tx->content;
  if (!content.size && tx->video)
    content = tx->video->content;
  unsigned size = (unsigned)content.size;
  const unsigned char *data = content.data;
  unsigned char *owned = NULL;
  if (content.size > 2 * 1024 * 1024)
    return 0;
  if (!size) {
    owned = scene_image_file(ctx, tx->filename, &size);
    if (!owned)
      return 0;
    data = owned;
  }
  int w, h, channels;
  image_memory = &ctx->memory;
  unsigned char *pixels = NULL;
  int ok = 0;
  if (!stbi_info_from_memory(data, (int)size, &w, &h, &channels) || w < 1 ||
      h < 1 || w > 1024 || h > 1024)
    goto done;
  pixels = stbi_load_from_memory(data, (int)size, &w, &h, &channels, 4);
  if (!pixels)
    goto done;
  imp_free(&ctx->memory, owned);
  owned = NULL;
  unsigned width = 4, height = 4;
  while (width < (unsigned)w)
    width *= 2;
  while (height < (unsigned)h)
    height *= 2;
  number(out, width);
  number(out, height);
  number(out, 1);
  number(out, (unsigned)texture.wrap_s);
  number(out, (unsigned)texture.wrap_t);
  number(out, 1);
  number(out, width * height * 4);
  for (unsigned y = 0; y < height; y += 4)
    for (unsigned x = 0; x < width; x += 4) {
      unsigned char tile[64];
      for (unsigned row = 0; row < 4; row++)
        for (unsigned col = 0; col < 4; col++) {
          unsigned k = (row * 4 + col) * 2;
          unsigned sx = (x + col) * (unsigned)w / width,
                   sy = (y + row) * (unsigned)h / height;
          const unsigned char *p = pixels + (sy * (unsigned)w + sx) * 4;
          tile[k] = p[3];
          tile[k + 1] = p[0];
          tile[k + 32] = p[1];
          tile[k + 33] = p[2];
        }
      bytes(out, tile, 64);
    }
  ok = !ctx->memory.failed;
done:
  if (pixels)
    stbi_image_free(pixels);
  imp_free(&ctx->memory, owned);
  image_memory = NULL;
  return ok;
}
static void scene_vec(ImportBuffer *b, ufbx_vec3 v) {
  scalar(b, (float)v.x);
  scalar(b, (float)v.y);
  scalar(b, (float)v.z);
}
static void scene_quat(ImportBuffer *b, ufbx_quat q) {
  scalar(b, (float)q.x);
  scalar(b, (float)q.y);
  scalar(b, (float)q.z);
  scalar(b, (float)q.w);
}
static void scene_matrix(ImportBuffer *b, ufbx_matrix matrix) {
  for (unsigned c = 0; c < 4; c++) {
    scene_vec(b, matrix.cols[c]);
    scalar(b, c == 3 ? 1 : 0);
  }
}
static int scene_material(ufbx_material *m, int approximate) {
  if (!approximate &&
      ((m->pbr.metalness.has_value && m->pbr.metalness.value_real > 0) ||
       (m->fbx.emission_factor.has_value &&
        m->fbx.emission_factor.value_real > 0 &&
        (m->fbx.emission_color.value_vec3.x > 0 ||
         m->fbx.emission_color.value_vec3.y > 0 ||
         m->fbx.emission_color.value_vec3.z > 0))))
    return 0;
  if (!approximate) {
    for (unsigned i = 0; i < UFBX_MATERIAL_FBX_MAP_COUNT; i++)
      if (m->fbx.maps[i].texture && i != UFBX_MATERIAL_FBX_DIFFUSE_COLOR)
        return 0;
    for (unsigned i = 0; i < UFBX_MATERIAL_PBR_MAP_COUNT; i++)
      if (m->pbr.maps[i].texture && i != UFBX_MATERIAL_PBR_BASE_COLOR)
        return 0;
  }
  return 1;
}
static int scene_publish(ImportMemory *m, ImportBuffer section[6], int budget) {
  if (m->failed)
    return 0;
  unsigned total = 160, offset[6];
  for (unsigned i = 0; i < 6; i++) {
    offset[i] = total;
    if (section[i].size > DND_MODEL_LIMIT - total)
      return 0;
    total = (total + section[i].size + 31) & ~31u;
  }
  unsigned char *package = imp_alloc(m, total);
  if (!package)
    return 0;
  put32(package, 0x444e444d);
  put32(package + 4, 1);
  put32(package + 8, total);
  put32(package + 12, 6);
  for (unsigned i = 0; i < 6; i++) {
    unsigned char *dir = package + 16 + i * 16;
    put32(dir, i + 1);
    put32(dir + 4, offset[i]);
    put32(dir + 8, section[i].size);
    put32(dir + 12, dnd_model_hash(section[i].data, section[i].size));
    memcpy(package + offset[i], section[i].data, section[i].size);
    imp_free(m, section[i].data);
    section[i].data = NULL;
  }
  int token = import_publish(m, package, total, budget);
  imp_free(m, package);
  return token;
}

static int import_scene(const char *path, int fbx, float sample_rate,
                        int budget, int approximate, int executor) {
  SceneImport ctx = {0};
  ctx.memory.budget = (unsigned)budget;
  ctx.memory.executor = executor;
  ImportBuffer section[6];
  for (unsigned i = 0; i < 6; i++)
    section[i] = (ImportBuffer){NULL, 0, 0, &ctx.memory};
  ufbx_scene *scene = NULL;
  ufbx_baked_anim *baked = NULL;
  unsigned char *source = NULL;
  uint32_t *triangles = NULL;
  SceneTexture *textures = NULL;
  int token = 0;
  unsigned source_size = 0;
  strcpy(ctx.filename, path);
  strcpy(ctx.directory, ctx.filename);
  char *slash = strrchr(ctx.directory, '/');
  if (!slash)
    goto done;
  slash[1] = 0;
  char *colon = strchr(ctx.filename, ':');
  if (!colon || (size_t)(colon - ctx.filename) + 2 >= sizeof(ctx.volume))
    goto done;
  memcpy(ctx.volume, ctx.filename, (size_t)(colon - ctx.filename) + 2);
  source = source_file(path, &ctx.memory, &source_size);
  if (!source)
    goto done;
  ufbx_load_opts opts = {0};
  ufbx_error error;
  opts.file_format = fbx ? UFBX_FILE_FORMAT_FBX : UFBX_FILE_FORMAT_OBJ;
  opts.filename.data = ctx.filename;
  opts.filename.length = strlen(ctx.filename);
  opts.target_axes = ufbx_axes_right_handed_y_up;
  opts.target_unit_meters = 1;
  opts.obj_unit_meters = 1;
  opts.obj_axes = ufbx_axes_right_handed_y_up;
  opts.geometry_transform_handling =
      UFBX_GEOMETRY_TRANSFORM_HANDLING_HELPER_NODES;
  opts.inherit_mode_handling = UFBX_INHERIT_MODE_HANDLING_HELPER_NODES;
  opts.generate_missing_normals = true;
  opts.index_error_handling = UFBX_INDEX_ERROR_HANDLING_ABORT_LOADING;
  opts.load_external_files = true;
  opts.open_file_cb.fn = scene_open;
  opts.open_file_cb.user = &ctx;
  opts.temp_allocator = scene_allocator(&ctx);
  opts.result_allocator = scene_allocator(&ctx);
  scene = ufbx_load_memory(source, source_size, &opts, &error);
  if (!scene)
    goto done;
  if (scene->nodes.count < 1 || scene->nodes.count > 1024 ||
      scene->materials.count > 4095 || scene->anim_stacks.count > 256 ||
      scene->constraints.count)
    goto done;
  textures = imp_alloc(&ctx.memory, 256 * sizeof(*textures));
  if (!textures)
    goto done;
  number(&section[0], (unsigned)scene->nodes.count);
  for (size_t i = 0; i < scene->nodes.count; i++) {
    ufbx_node *n = scene->nodes.data[i];
    number(&section[0], n->parent ? n->parent->typed_id : UINT_MAX);
    name(&section[0], n->name.data, "Node");
    scene_vec(&section[0], n->local_transform.translation);
    scene_quat(&section[0], n->local_transform.rotation);
    scene_vec(&section[0], n->local_transform.scale);
  }
  unsigned num_textures = 0;
  number(&section[2], (unsigned)scene->materials.count + 1);
  float white[4] = {1, 1, 1, 1};
  floats(&section[2], white, 4);
  number(&section[2], UINT_MAX);
  number(&section[2], 0);
  scalar(&section[2], .5f);
  number(&section[2], 0);
  for (size_t i = 0; i < scene->materials.count; i++) {
    ufbx_material *m = scene->materials.data[i];
    if (!scene_material(m, approximate))
      goto done;
    ufbx_material_map base = m->features.pbr.enabled ? m->pbr.base_color
                                                     : m->fbx.diffuse_color,
                      factor = m->features.pbr.enabled ? m->pbr.base_factor
                                                       : m->fbx.diffuse_factor;
    ufbx_vec3 color =
        base.has_value ? base.value_vec3 : (ufbx_vec3){.x = 1, .y = 1, .z = 1};
    double mul = factor.has_value ? factor.value_real : 1;
    double opacity = m->pbr.opacity.has_value ? m->pbr.opacity.value_real : 1;
    if (opacity < 0 || opacity > 1 || (!approximate && opacity < 1))
      goto done;
    int texture = -1;
    ufbx_texture *tx = base.texture;
    if (tx) {
      if (tx->type != UFBX_TEXTURE_FILE || tx->has_uv_transform)
        goto done;
      for (unsigned t = 0; t < num_textures; t++)
        if (textures[t].texture == tx)
          texture = (int)t;
      if (texture < 0) {
        if (num_textures >= 256)
          goto done;
        texture = (int)num_textures;
        textures[num_textures++] =
            (SceneTexture){tx, tx->wrap_u == UFBX_WRAP_CLAMP ? 0 : 1,
                           tx->wrap_v == UFBX_WRAP_CLAMP ? 0 : 1};
      }
    }
    scalar(&section[2], (float)(color.x * mul));
    scalar(&section[2], (float)(color.y * mul));
    scalar(&section[2], (float)(color.z * mul));
    scalar(&section[2], (float)opacity);
    number(&section[2], texture < 0 ? UINT_MAX : (unsigned)texture);
    number(&section[2], (tx || opacity < 1 ? 1u : 0u) |
                            (m->features.unlit.enabled ? 2u : 0u));
    scalar(&section[2], .5f);
    number(&section[2], m->features.double_sided.enabled ? 1 : 0);
  }
  number(&section[3], num_textures);
  for (unsigned i = 0; i < num_textures; i++)
    if (!scene_texture(&ctx, &section[3], textures[i]))
      goto done;
  unsigned primitives = 0, skins = 0;
  for (size_t i = 0; i < scene->nodes.count; i++) {
    ufbx_mesh *mesh = scene->nodes.data[i]->mesh;
    if (!mesh)
      continue;
    if (mesh->blend_deformers.count || mesh->cache_deformers.count ||
        mesh->skin_deformers.count > 1 || mesh->uv_sets.count > 1)
      goto done;
    for (size_t j = 0; j < mesh->material_parts.count; j++)
      if (mesh->material_parts.data[j].num_triangles) {
        primitives++;
        if (mesh->skin_deformers.count)
          skins++;
      }
  }
  if (!primitives || primitives > 4096 || skins > 256)
    goto done;
  number(&section[1], primitives);
  number(&section[4], skins);
  unsigned skin_index = 0;
  for (size_t ni = 0; ni < scene->nodes.count; ni++) {
    ufbx_node *node = scene->nodes.data[ni];
    ufbx_mesh *mesh = node->mesh;
    if (!mesh)
      continue;
    ufbx_skin_deformer *skin =
        mesh->skin_deformers.count ? mesh->skin_deformers.data[0] : NULL;
    if (skin && (!skin->clusters.count || skin->clusters.count > 256 ||
                 skin->skinning_method != UFBX_SKINNING_METHOD_LINEAR))
      goto done;
    if (mesh->max_face_triangles > 21845)
      goto done;
    unsigned capacity = (unsigned)mesh->max_face_triangles * 3;
    triangles = imp_alloc(&ctx.memory, capacity * sizeof(*triangles));
    if (!triangles)
      goto done;
    ufbx_matrix normal_matrix =
        ufbx_matrix_for_normals(&node->geometry_to_node);
    for (size_t pi = 0; pi < mesh->material_parts.count; pi++) {
      ufbx_mesh_part part = mesh->material_parts.data[pi];
      if (!part.num_triangles)
        continue;
      if (part.num_triangles > 21845 || part.num_line_faces ||
          part.num_point_faces || part.num_empty_faces)
        goto done;
      unsigned vertices = (unsigned)part.num_triangles * 3;
      ufbx_material *mat = part.index < node->materials.count
                               ? node->materials.data[part.index]
                               : NULL;
      number(&section[1], node->typed_id);
      number(&section[1], skin ? skin_index++ : UINT_MAX);
      number(&section[1], mat ? mat->typed_id + 1 : 0);
      number(&section[1], vertices);
      number(&section[1], vertices);
      unsigned bounds_at = section[1].size;
      float low[3] = {INFINITY, INFINITY, INFINITY},
            high[3] = {-INFINITY, -INFINITY, -INFINITY};
      for (unsigned i = 0; i < 6; i++)
        scalar(&section[1], 0);
      if (skin) {
        number(&section[4], (unsigned)skin->clusters.count);
        for (size_t j = 0; j < skin->clusters.count; j++) {
          ufbx_skin_cluster *cluster = skin->clusters.data[j];
          if (!cluster->bone_node)
            goto done;
          number(&section[4], cluster->bone_node->typed_id);
          scene_matrix(&section[4], cluster->mesh_node_to_bone);
        }
      }
      unsigned emitted = 0;
      for (size_t fi = 0; fi < part.face_indices.count; fi++) {
        ufbx_face face = mesh->faces.data[part.face_indices.data[fi]];
        if (face.num_indices < 3)
          goto done;
        unsigned count = ufbx_triangulate_face(triangles, capacity, mesh, face);
        if (count != face.num_indices - 2)
          goto done;
        for (unsigned k = 0; k < count * 3; k++) {
          unsigned corner = triangles[k],
                   vertex = mesh->vertex_indices.data[corner];
          ufbx_vec3 position = ufbx_transform_position(
              &node->geometry_to_node,
              ufbx_get_vertex_vec3(&mesh->vertex_position, corner));
          scene_vec(&section[1], position);
          for (unsigned c = 0; c < 3; c++) {
            low[c] = fminf(low[c], (float)position.v[c]);
            high[c] = fmaxf(high[c], (float)position.v[c]);
          }
          ufbx_vec3 normal = ufbx_transform_direction(
              &normal_matrix,
              ufbx_get_vertex_vec3(&mesh->vertex_normal, corner));
          double norm = sqrt(normal.x * normal.x + normal.y * normal.y +
                             normal.z * normal.z);
          if (norm < 1e-12)
            goto done;
          normal.x /= norm;
          normal.y /= norm;
          normal.z /= norm;
          scene_vec(&section[1], normal);
          ufbx_vec2 uv = mesh->vertex_uv.exists
                             ? ufbx_get_vertex_vec2(&mesh->vertex_uv, corner)
                             : (ufbx_vec2){.x = 0, .y = 0};
          scalar(&section[1], (float)uv.x);
          scalar(&section[1], (float)(1 - uv.y));
          ufbx_vec4 color =
              mesh->vertex_color.exists
                  ? ufbx_get_vertex_vec4(&mesh->vertex_color, corner)
                  : (ufbx_vec4){.x = 1, .y = 1, .z = 1, .w = 1};
          for (unsigned c = 0; c < 4; c++)
            scalar(&section[1], (float)color.v[c]);
          unsigned joints[4] = {0}, influences = 0;
          float weights[4] = {0}, sum = 0;
          if (skin) {
            if (vertex >= skin->vertices.count)
              goto done;
            ufbx_skin_vertex v = skin->vertices.data[vertex];
            for (unsigned j = 0; j < v.num_weights; j++) {
              ufbx_skin_weight w = skin->weights.data[v.weight_begin + j];
              if (w.weight == 0)
                continue;
              if (w.weight < 0 || influences >= 4 ||
                  w.cluster_index >= skin->clusters.count)
                goto done;
              joints[influences] = w.cluster_index;
              weights[influences] = (float)w.weight;
              sum += weights[influences++];
            }
            if (!influences || !isfinite(sum) || sum <= 0)
              goto done;
          }
          for (unsigned c = 0; c < 4; c++)
            number(&section[1], joints[c]);
          for (unsigned c = 0; c < 4; c++)
            scalar(&section[1], skin ? weights[c] / sum : 0);
          emitted++;
        }
      }
      if (emitted != vertices || ctx.memory.failed)
        goto done;
      for (unsigned c = 0; c < 3; c++) {
        unsigned word;
        memcpy(&word, &low[c], 4);
        put32(section[1].data + bounds_at + c * 4, word);
        memcpy(&word, &high[c], 4);
        put32(section[1].data + bounds_at + 12 + c * 4, word);
      }
      for (unsigned v = 0; v < vertices; v++)
        number(&section[1], v);
    }
    imp_free(&ctx.memory, triangles);
    triangles = NULL;
  }
  number(&section[5], (unsigned)scene->anim_stacks.count);
  for (size_t ai = 0; ai < scene->anim_stacks.count; ai++) {
    ufbx_anim_stack *stack = scene->anim_stacks.data[ai];
    ufbx_bake_opts opts = {0};
    opts.trim_start_time = true;
    opts.resample_rate = sample_rate;
    opts.temp_allocator = scene_allocator(&ctx);
    opts.result_allocator = scene_allocator(&ctx);
    baked = ufbx_bake_anim(scene, stack->anim, &opts, &error);
    if (!baked || baked->elements.count ||
        !isfinite(baked->playback_duration) || baked->playback_duration < 0)
      goto done;
    unsigned tracks = 0;
    for (size_t i = 0; i < baked->nodes.count; i++) {
      ufbx_baked_node *n = &baked->nodes.data[i];
      tracks += (n->translation_keys.count > 0) + (n->rotation_keys.count > 0) +
                (n->scale_keys.count > 0);
    }
    if (!tracks || tracks > scene->nodes.count * 3)
      goto done;
    char fallback[32];
    snprintf(fallback, sizeof(fallback), "Animation%u", (unsigned)ai);
    name(&section[5], stack->name.data, fallback);
    scalar(&section[5], (float)baked->playback_duration);
    unsigned size_at = section[5].size;
    number(&section[5], 0);
    unsigned start = section[5].size;
    number(&section[5], tracks);
    for (size_t i = 0; i < baked->nodes.count; i++)
      for (unsigned prop = 0; prop < 3; prop++) {
        ufbx_baked_node *n = &baked->nodes.data[i];
        size_t count = prop == 0   ? n->translation_keys.count
                       : prop == 1 ? n->rotation_keys.count
                                   : n->scale_keys.count;
        if (!count)
          continue;
        if (count > 120000)
          goto done;
        number(&section[5], n->typed_id);
        number(&section[5], prop);
        number(&section[5], 1);
        number(&section[5], (unsigned)count);
        float previous = -1;
        for (size_t k = 0; k < count; k++) {
          double t = prop == 0   ? n->translation_keys.data[k].time
                     : prop == 1 ? n->rotation_keys.data[k].time
                                 : n->scale_keys.data[k].time;
          float time = (float)t;
          if (time < 0 || time <= previous)
            goto done;
          previous = time;
          scalar(&section[5], time);
          if (prop == 1)
            scene_quat(&section[5], n->rotation_keys.data[k].value);
          else
            scene_vec(&section[5], prop == 0 ? n->translation_keys.data[k].value
                                             : n->scale_keys.data[k].value);
        }
      }
    if (ctx.memory.failed)
      goto done;
    put32(section[5].data + size_at, section[5].size - start);
    ufbx_free_baked_anim(baked);
    baked = NULL;
  }
  ufbx_free_scene(scene);
  scene = NULL;
  imp_free(&ctx.memory, source);
  source = NULL;
  imp_free(&ctx.memory, textures);
  textures = NULL;
  token = scene_publish(&ctx.memory, section, budget);
done:
  if (baked)
    ufbx_free_baked_anim(baked);
  if (scene)
    ufbx_free_scene(scene);
  imp_free(&ctx.memory, source);
  imp_free(&ctx.memory, triangles);
  imp_free(&ctx.memory, textures);
  for (unsigned i = 0; i < 6; i++)
    imp_free(&ctx.memory, section[i].data);
  image_memory = NULL;
  return token;
}

/* PSO GameCube GJ/GVM/NMDM profile. All pointers below are checked offsets,
 * never executable display-list addresses or native pointers from the file. */
typedef struct {
  const unsigned char *data;
  unsigned size;
  ImportMemory *memory;
} PsoReader;
typedef struct {
  unsigned offset, flags, mesh;
  int parent;
  float t[3], q[4], s[3];
} PsoNode;
typedef struct {
  unsigned index, width, height, format, size;
  const unsigned char *pixels;
  char name[64];
  int named;
} PsoTexture;
typedef struct {
  ImportMemory memory;
  ImportBuffer section[6];
  PsoReader geometry, list;
  PsoNode *nodes;
  PsoTexture *textures;
  unsigned node_count, texture_count, meshes;
} PsoImport;
static int pso_range(PsoReader r, unsigned p, unsigned size) {
  if (p > r.size || size > r.size - p) {
    r.memory->failed = 1;
    return 0;
  }
  return 1;
}
static unsigned pso_u32(PsoReader r, unsigned p) {
  return pso_range(r, p, 4) ? dnd_model_u32(r.data + p) : 0;
}
static unsigned pso_u16(PsoReader r, unsigned p) {
  return pso_range(r, p, 2) ? (unsigned)r.data[p] << 8 | r.data[p + 1] : 0;
}
static unsigned pso_byte(PsoReader r, unsigned p) {
  return pso_range(r, p, 1) ? r.data[p] : 0;
}
static float pso_float(PsoReader r, unsigned p) {
  unsigned word = pso_u32(r, p);
  float f;
  memcpy(&f, &word, 4);
  if (!isfinite(f)) {
    r.memory->failed = 1;
    return 0;
  }
  return f;
}
static unsigned char *source_file(const char *path, ImportMemory *m,
                                  unsigned *size) {
  if (!path)
    return NULL;
  int file = import_open(m, path);
  if (!file)
    return NULL;
  int64_t length = import_length(m, file);
  unsigned char *p = NULL;
  if (length > 0 && length <= 2 * 1024 * 1024) {
    p = imp_alloc(m, (size_t)length);
    if (p) {
      unsigned got = 0;
      while (got < (unsigned)length) {
        int n = import_read(m, file, p + got, (int)((unsigned)length - got));
        if (n <= 0)
          break;
        got += (unsigned)n;
      }
      if (got != (unsigned)length) {
        imp_free(m, p);
        p = NULL;
      } else
        *size = (unsigned)length;
    }
  }
  import_close(m, file);
  return p;
}
static void pso_qmul(float out[4], const float a[4], const float b[4]) {
  float q[4] = {a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
                a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
                a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
                a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]};
  memcpy(out, q, 16);
}
static void pso_euler(float out[4], const float euler[3], unsigned flags) {
  float x[4] = {sinf(euler[0] / 2), 0, 0, cosf(euler[0] / 2)},
        y[4] = {0, sinf(euler[1] / 2), 0, cosf(euler[1] / 2)},
        z[4] = {0, 0, sinf(euler[2] / 2), cosf(euler[2] / 2)};
  if (flags & 32) {
    pso_qmul(out, z, y);
    pso_qmul(out, out, x);
  } else {
    pso_qmul(out, x, y);
    pso_qmul(out, out, z);
  }
}
static void pso_node(PsoImport *ctx, unsigned offset, int parent,
                     unsigned depth) {
  PsoReader r = ctx->geometry;
  do {
    if (ctx->memory.failed)
      return;
    if (depth >= 128 || ctx->node_count >= 1024 || !pso_range(r, offset, 52)) {
      ctx->memory.failed = 1;
      return;
    }
    for (unsigned i = 0; i < ctx->node_count; i++)
      if (ctx->nodes[i].offset == offset) {
        ctx->memory.failed = 1;
        return;
      }
    unsigned id = ctx->node_count++;
    PsoNode *n = &ctx->nodes[id];
    n->offset = offset;
    n->parent = parent;
    n->flags = pso_u32(r, offset);
    n->mesh = (n->flags & 8) ? 0 : pso_u32(r, offset + 4);
    if (n->flags > 0x1fff) {
      ctx->memory.failed = 1;
      return;
    }
    float angles[3];
    for (unsigned c = 0; c < 3; c++) {
      n->t[c] = (n->flags & 1) ? 0 : pso_float(r, offset + 8 + c * 4);
      angles[c] = (n->flags & 2)
                      ? 0
                      : (float)(int32_t)pso_u32(r, offset + 20 + c * 4) *
                            (6.283185307179586f / 65536);
      n->s[c] = (n->flags & 4) ? 1 : pso_float(r, offset + 32 + c * 4);
    }
    pso_euler(n->q, angles, n->flags);
    unsigned child = (n->flags & 16) ? 0 : pso_u32(r, offset + 44),
             sibling = pso_u32(r, offset + 48);
    if (child)
      pso_node(ctx, child, (int)id, depth + 1);
    offset = sibling;
  } while (offset);
}
static unsigned expand5(unsigned v) { return v << 3 | v >> 2; }
static void pso_rgb565(unsigned char c[4], unsigned v) {
  c[0] = (unsigned char)expand5(v >> 11);
  unsigned g = v >> 5 & 63;
  c[1] = (unsigned char)(g << 2 | g >> 4);
  c[2] = (unsigned char)expand5(v & 31);
  c[3] = 255;
}
static void pso_pixel(const PsoTexture *t, unsigned x, unsigned y,
                      unsigned char c[4]) {
  unsigned tw = (t->format == 0 || t->format == 1 || t->format == 2 ||
                 t->format == 14)
                    ? 8
                    : 4,
           th = (t->format == 0 || t->format == 14) ? 8 : 4;
  unsigned tile = (y / th * ((t->width + tw - 1) / tw) + (x / tw)) *
                  (t->format == 6 ? 64 : 32),
           k = (y % th) * tw + x % tw;
  const unsigned char *p = t->pixels + tile;
  unsigned v = 0;
  if (t->format == 0) {
    v = (p[k / 2] >> (k % 2 ? 0 : 4) & 15) * 17;
    memset(c, (int)v, 4);
  } else if (t->format == 1)
    memset(c, p[k], 4);
  else if (t->format == 2) {
    memset(c, (p[k] & 15) * 17, 3);
    c[3] = (unsigned char)((p[k] >> 4) * 17);
  } else if (t->format == 3) {
    memset(c, p[k * 2 + 1], 3);
    c[3] = p[k * 2];
  } else if (t->format == 4)
    pso_rgb565(c, (unsigned)p[k * 2] << 8 | p[k * 2 + 1]);
  else if (t->format == 5) {
    v = (unsigned)p[k * 2] << 8 | p[k * 2 + 1];
    if (v & 0x8000) {
      c[0] = (unsigned char)expand5(v >> 10 & 31);
      c[1] = (unsigned char)expand5(v >> 5 & 31);
      c[2] = (unsigned char)expand5(v & 31);
      c[3] = 255;
    } else {
      c[0] = (unsigned char)((v >> 8 & 15) * 17);
      c[1] = (unsigned char)((v >> 4 & 15) * 17);
      c[2] = (unsigned char)((v & 15) * 17);
      unsigned a = v >> 12 & 7;
      c[3] = (unsigned char)(a << 5 | a << 2 | a >> 1);
    }
  } else if (t->format == 6) {
    c[0] = p[k * 2 + 1];
    c[1] = p[32 + k * 2];
    c[2] = p[33 + k * 2];
    c[3] = p[k * 2];
  } else {
    p += (y % 8 / 4 * 2 + x % 8 / 4) * 8;
    unsigned a = (unsigned)p[0] << 8 | p[1], b = (unsigned)p[2] << 8 | p[3],
             index = p[4 + y % 4] >> (6 - 2 * (x % 4)) & 3;
    unsigned char ca[4], cb[4];
    pso_rgb565(ca, a);
    pso_rgb565(cb, b);
    if (index < 2)
      memcpy(c, index ? cb : ca, 4);
    else {
      for (unsigned j = 0; j < 3; j++)
        c[j] =
            (unsigned char)(a > b ? (index == 2 ? (ca[j] * 5 + cb[j] * 3) / 8
                                                : (ca[j] * 3 + cb[j] * 5) / 8)
                                  : (ca[j] + cb[j]) / 2);
      c[3] = (unsigned char)(a <= b && index == 3 ? 0 : 255);
    }
  }
}
static int pso_archive(PsoImport *ctx, const unsigned char *data,
                       unsigned size) {
  PsoReader r = {data, size, &ctx->memory};
  unsigned p = 0, count = 1;
  if (size < 16)
    return 0;
  if (!memcmp(data, "GVMH", 4)) {
    unsigned header = le32(data + 4);
    if (header > size - 8)
      return 0;
    unsigned flags = pso_u16(r, 8);
    count = pso_u16(r, 10);
    p = 12;
    if (flags > 15 || !count || count > 256)
      return 0;
    for (unsigned i = 0; i < count; i++) {
      unsigned width = 2 + ((flags & 8) ? 28 : 0) + ((flags & 4) ? 2 : 0) +
                       ((flags & 2) ? 2 : 0) + ((flags & 1) ? 4 : 0);
      if (p > header + 8 || width > header + 8 - p || !pso_range(r, p, width))
        return 0;
      PsoTexture *t = &ctx->textures[i];
      t->index = pso_u16(r, p);
      t->named = (flags & 8) != 0;
      if (t->named) {
        memcpy(t->name, data + p + 2, 28);
        t->name[28] = 0;
        t->named = t->name[0] != 0;
      }
      for (unsigned j = 0; j < i; j++)
        if (ctx->textures[j].index == t->index)
          return 0;
      p += width;
    }
    p = header + 8;
  } else if (memcmp(data, "GVRT", 4))
    return 0;
  ctx->texture_count = count;
  for (unsigned i = 0; i < count; i++) {
    while (p < size && data[p] == 0)
      p++;
    if (!pso_range(r, p, 16) || memcmp(data + p, "GVRT", 4))
      return 0;
    unsigned length = le32(data + p + 4);
    if (length < 8 || length > size - p - 8)
      return 0;
    PsoTexture *t = &ctx->textures[i];
    t->width = pso_u16(r, p + 12);
    t->height = pso_u16(r, p + 14);
    t->format = data[p + 11];
    t->pixels = data + p + 16;
    t->size = length - 8;
    unsigned w = t->width, h = t->height, f = t->format;
    if (w < 4 || h < 4 || w > 1024 || h > 1024 || (w & (w - 1)) ||
        (h & (h - 1)) || !((f <= 6) || f == 14))
      return 0;
    unsigned tw = (f == 0 || f == 1 || f == 2 || f == 14) ? 8 : 4,
             th = (f == 0 || f == 14) ? 8 : 4;
    unsigned tiles = (w + tw - 1) / tw * ((h + th - 1) / th),
             needed = tiles * (f == 6 ? 64 : 32);
    if (t->size < needed)
      return 0;
    p += length + 8;
  }
  while (p < size && data[p] == 0)
    p++;
  return p == size && !ctx->memory.failed;
}
static int pso_texture(PsoImport *ctx, unsigned id) {
  int named = 0;
  for (unsigned i = 0; i < ctx->texture_count; i++)
    named |= ctx->textures[i].named;
  if (ctx->list.data && named) {
    unsigned count = pso_u32(ctx->list, 4), table = pso_u32(ctx->list, 0);
    if (count > 4096 || id >= count || !pso_range(ctx->list, table, count * 12))
      return -1;
    unsigned offset = pso_u32(ctx->list, table + id * 12), length = 0;
    char wanted[64];
    while (length < 63 && pso_byte(ctx->list, offset + length)) {
      wanted[length] = (char)pso_byte(ctx->list, offset + length);
      length++;
    }
    if (length == 63 || ctx->memory.failed)
      return -1;
    wanted[length] = 0;
    char *dot = strrchr(wanted, '.');
    if (dot)
      *dot = 0;
    int found = -1;
    for (unsigned i = 0; i < ctx->texture_count; i++) {
      char candidate[64];
      memcpy(candidate, ctx->textures[i].name, 64);
      dot = strrchr(candidate, '.');
      if (dot)
        *dot = 0;
      if (!strcasecmp(candidate, wanted)) {
        if (found >= 0)
          return -1;
        found = (int)i;
      }
    }
    return found;
  }
  for (unsigned i = 0; i < ctx->texture_count; i++)
    if (ctx->textures[i].index == id)
      return (int)i;
  return -1;
}
static int pso_group(PsoImport *ctx, unsigned node, unsigned group,
                     const unsigned props[4], const unsigned counts[4]) {
  PsoReader r = ctx->geometry;
  unsigned mat = pso_u32(r, group), nm = pso_u32(r, group + 4),
           start = pso_u32(r, group + 8), length = pso_u32(r, group + 12);
  if (nm > 2048 || !pso_range(r, mat, nm * 8) || !pso_range(r, start, length))
    return 0;
  unsigned flags = 0;
  int texture = -1;
  for (unsigned i = 0; i < nm; i++) {
    unsigned type = pso_byte(r, mat + i * 8),
             value = pso_u32(r, mat + i * 8 + 4);
    if (type == 1)
      flags = value;
    else if (type == 8) {
      texture = pso_texture(ctx, value & 0x1fff);
      if (texture < 0)
        return 0;
    } else
      return 0;
  }
  if (!(flags & 8))
    return 0;
  ImportBuffer vertices = {NULL, 0, 0, &ctx->memory},
               indices = {NULL, 0, 0, &ctx->memory};
  unsigned count = 0, p = start, end = start + length;
  float low[3] = {INFINITY, INFINITY, INFINITY},
        high[3] = {-INFINITY, -INFINITY, -INFINITY};
  while (p < end && !ctx->memory.failed) {
    unsigned primitive = pso_byte(r, p++);
    if (!primitive)
      continue;
    if ((primitive != 0x90 && primitive != 0x98 && primitive != 0xa0) ||
        end - p < 2)
      goto fail;
    unsigned n = pso_u16(r, p);
    p += 2;
    if (n < 3 || (primitive == 0x90 && n % 3) || count + n > 65536)
      goto fail;
    unsigned base = count;
    count += n;
    for (unsigned v = 0; v < n; v++) {
      int ix[4] = {-1, -1, -1, -1};
      unsigned present[4] = {8, 32, 128, 2048}, wide[4] = {4, 16, 64, 1024};
      for (unsigned a = 0; a < 4; a++)
        if (flags & present[a]) {
          unsigned width = (flags & wide[a]) ? 2 : 1;
          if (width > end - p)
            goto fail;
          ix[a] = (int)(width == 2 ? pso_u16(r, p) : pso_byte(r, p));
          p += width;
          if ((unsigned)ix[a] >= counts[a])
            goto fail;
        }
      unsigned position = props[0] + (unsigned)ix[0] * 12;
      for (unsigned a = 0; a < 3; a++) {
        float value = pso_float(r, position + a * 4);
        scalar(&vertices, value);
        low[a] = fminf(low[a], value);
        high[a] = fmaxf(high[a], value);
      }
      for (unsigned a = 0; a < 3; a++)
        scalar(&vertices,
               ix[1] < 0
                   ? (a == 1 ? 1.f : 0.f)
                   : pso_float(r, props[1] + (unsigned)ix[1] * 12 + a * 4));
      for (unsigned a = 0; a < 2; a++) {
        float value = ix[3] < 0
                          ? 0
                          : (float)(int16_t)pso_u16(
                                r, props[3] + (unsigned)ix[3] * 4 + a * 2) /
                                255;
        scalar(&vertices, a == 1 && ix[3] >= 0 ? 1 - value : value);
      }
      for (unsigned a = 0; a < 4; a++)
        scalar(&vertices,
               ix[2] < 0
                   ? 1
                   : (float)pso_byte(r, props[2] + (unsigned)ix[2] * 4 + a) /
                         255);
      for (unsigned a = 0; a < 8; a++)
        number(&vertices, 0);
    }
    for (unsigned j = 0; j + 2 < n; j++) {
      if (primitive == 0x90 && j % 3)
        continue;
      unsigned a = base + j, b = base + j + 1, c = base + j + 2;
      if (primitive == 0xa0) {
        a = base;
        b = base + j + 1;
        c = base + j + 2;
      } else if (primitive == 0x98 && (j & 1)) {
        unsigned swap = a;
        a = b;
        b = swap;
      }
      number(&indices, a);
      number(&indices, c);
      number(&indices, b);
    }
  }
  if (!count || ctx->memory.failed)
    goto fail;
  number(&ctx->section[1], node);
  number(&ctx->section[1], UINT_MAX);
  number(&ctx->section[1], (unsigned)(texture + 1));
  number(&ctx->section[1], count);
  number(&ctx->section[1], indices.size / 4);
  floats(&ctx->section[1], low, 3);
  floats(&ctx->section[1], high, 3);
  bytes(&ctx->section[1], vertices.data, vertices.size);
  bytes(&ctx->section[1], indices.data, indices.size);
  imp_free(&ctx->memory, vertices.data);
  imp_free(&ctx->memory, indices.data);
  return !ctx->memory.failed;
fail:
  imp_free(&ctx->memory, vertices.data);
  imp_free(&ctx->memory, indices.data);
  return 0;
}
static int pso_mesh(PsoImport *ctx, unsigned node) {
  PsoReader r = ctx->geometry;
  unsigned offset = ctx->nodes[node].mesh;
  if (!pso_range(r, offset, 36))
    return 0;
  unsigned table = pso_u32(r, offset), solid = pso_u32(r, offset + 8),
           count = pso_u16(r, offset + 16);
  if (pso_u16(r, offset + 18) || count > 4096 ||
      !pso_range(r, solid, count * 16))
    return 0; /* blended alpha groups need a different renderer profile */
  unsigned props[4] = {0}, counts[4] = {0};
  int ended = 0;
  for (unsigned i = 0; i < 16384; i++) {
    unsigned at = table + i * 16, type = pso_byte(r, at);
    if (type == 255) {
      ended = 1;
      break;
    }
    if (!pso_range(r, at, 16))
      return 0;
    int a = type == 1 ? 0 : type == 2 ? 1 : type == 3 ? 2 : type == 5 ? 3 : -1;
    if (a < 0)
      return 0;
    if (counts[a])
      return 0;
    counts[a] = pso_u16(r, at + 2);
    props[a] = pso_u32(r, at + 8);
    unsigned length = pso_u32(r, at + 12), width = a < 2 ? 12 : 4;
    if (counts[a] * width > length || !pso_range(r, props[a], length))
      return 0;
  }
  if (!ended || !counts[0])
    return 0;
  for (unsigned i = 0; i < count; i++)
    if (!pso_group(ctx, node, solid + i * 16, props, counts))
      return 0;
  return !ctx->memory.failed;
}

typedef struct {
  unsigned node, channel, offset, count, stride;
} PsoMotionTrack;
static unsigned pso_frame(PsoReader r, const PsoMotionTrack *track,
                          unsigned key) {
  unsigned p = track->offset + key * track->stride;
  return track->stride == 8 ? pso_u16(r, p) : pso_u32(r, p);
}
static void pso_angles(PsoReader r, const PsoMotionTrack *track, unsigned key,
                       float value[3]) {
  unsigned p = track->offset + key * track->stride;
  for (unsigned i = 0; i < 3; i++)
    value[i] =
        (float)(track->stride == 8 ? (int16_t)pso_u16(r, p + 2 + i * 2)
                                   : (int32_t)pso_u32(r, p + 4 + i * 4)) *
        (6.283185307179586f / 65536);
}
static unsigned pso_rotation_steps(unsigned frames, const float a[3],
                                   const float b[3]) {
  float delta =
      fmaxf(fabsf(b[0] - a[0]), fmaxf(fabsf(b[1] - a[1]), fabsf(b[2] - a[2])));
  if (!isfinite(delta) || delta > 60000 * (3.141592653589793f / 32))
    return 60001;
  unsigned angular = (unsigned)ceilf(delta / (3.141592653589793f / 32));
  return angular > frames ? angular : frames;
}
static int pso_motion_reader(PsoImport *ctx, PsoReader r, unsigned header, float fps, int euler_bytes, int strict) {
  unsigned table = pso_u32(r, header), frames = pso_u32(r, header + 4), type = pso_u16(r, header + 8),
           attributes = pso_u16(r, header + 10);
  if (!frames || frames > 60000 || !type || (type & ~0x2007u) ||
      ((type & 2) && (type & 0x2000)) || (attributes & ~0xcfu) ||
      (attributes & 0xc0))
    return 0;
  unsigned channels[4], nc = 0;
  if (type & 1)
    channels[nc++] = 0;
  if (type & 2)
    channels[nc++] = 1;
  if (type & 0x2000)
    channels[nc++] = 3;
  if (type & 4)
    channels[nc++] = 2;
  if ((attributes & 15) && (attributes & 15) != nc)
    return 0;
  unsigned targets = 0;
  for (unsigned i = 0; i < ctx->node_count; i++)
    if (!(ctx->nodes[i].flags & 64))
      targets++;
  unsigned end_table = table + targets * nc * 8;
  if ((strict && table < 12) || !pso_range(r, table, targets * nc * 8))
    return 0;
  PsoMotionTrack *entries =
      imp_alloc(&ctx->memory, targets * nc * sizeof(*entries));
  if (!entries)
    return 0;
  unsigned count = 0, row = 0, first = UINT_MAX;
  int ok = 0;
  ImportBuffer tracks = {NULL, 0, 0, &ctx->memory};
  for (unsigned node = 0; node < ctx->node_count; node++) {
    if (ctx->nodes[node].flags & 64)
      continue;
    for (unsigned c = 0; c < nc; c++) {
      unsigned offset = pso_u32(r, table + row * nc * 8 + c * 4),
               keys = pso_u32(r, table + row * nc * 8 + nc * 4 + c * 4);
      if (!keys)
        continue;
      if (keys > 60001 || (strict && offset < end_table) || offset >= r.size)
        goto done;
      PsoMotionTrack entry = {node, channels[c], offset, keys,
                              channels[c] == 1   ? (unsigned)euler_bytes
                              : channels[c] == 3 ? 20u
                                                 : 16u};
      if (keys > (r.size - offset) / entry.stride)
        goto done;
      if (offset < first)
        first = offset;
      entries[count++] = entry;
    }
    row++;
  }
  if (!count || (strict && first - end_table > 31))
    goto done;
  number(&tracks, count);
  for (unsigned i = 0; i < count; i++) {
    PsoMotionTrack *entry = &entries[i];
    unsigned boundary = r.size;
    for (unsigned j = 0; j < count; j++)
      if (entries[j].offset > entry->offset && entries[j].offset < boundary)
        boundary = entries[j].offset;
    if (entry->count > (boundary - entry->offset) / entry->stride)
      goto done;
    unsigned previous = 0, baked = 0;
    float a[3] = {0};
    for (unsigned k = 0; k < entry->count; k++) {
      unsigned frame = pso_frame(r, entry, k);
      if (frame > frames || (k && frame <= previous))
        goto done;
      unsigned steps = 1;
      if (entry->channel == 1) {
        float b[3];
        pso_angles(r, entry, k, b);
        if (k)
          steps = pso_rotation_steps(frame - previous, a, b);
        memcpy(a, b, 12);
      }
      if (steps > 60000 || baked > 120000 - steps)
        goto done;
      baked += steps;
      previous = frame;
    }
    number(&tracks, entry->node);
    number(&tracks, entry->channel == 3 ? 1 : entry->channel);
    number(&tracks, 1);
    number(&tracks, baked);
    previous = 0;
    for (unsigned k = 0; k < entry->count; k++) {
      unsigned frame = pso_frame(r, entry, k),
               p = entry->offset + k * entry->stride;
      if (entry->channel == 1) {
        float b[3];
        pso_angles(r, entry, k, b);
        unsigned steps = k ? pso_rotation_steps(frame - previous, a, b) : 1;
        for (unsigned step = 1; step <= steps; step++) {
          float f = step / (float)steps, euler[3], q[4];
          for (unsigned c = 0; c < 3; c++)
            euler[c] = k ? a[c] + (b[c] - a[c]) * f : b[c];
          pso_euler(q, euler, ctx->nodes[entry->node].flags);
          scalar(&tracks,
                 k ? (previous + (frame - previous) * f) / fps : frame / fps);
          floats(&tracks, q, 4);
        }
        memcpy(a, b, 12);
      } else {
        scalar(&tracks, frame / fps);
        if (entry->channel == 3) {
          float q[4];
          for (unsigned c = 0; c < 3; c++)
            q[c] = pso_float(r, p + 8 + c * 4);
          q[3] = pso_float(r, p + 4);
          float norm =
              sqrtf(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
          if (norm < 1e-12f)
            goto done;
          for (unsigned c = 0; c < 4; c++)
            scalar(&tracks, q[c] / norm);
        } else
          for (unsigned c = 0; c < 3; c++) {
            float value = pso_float(r, p + 4 + c * 4);
            if (entry->channel == 2 && fabsf(value) < 1e-6f)
              goto done;
            scalar(&tracks, value);
          }
      }
      previous = frame;
    }
  }
  number(&ctx->section[5], 1);
  name(&ctx->section[5], "Motion", "Motion");
  scalar(&ctx->section[5], frames / fps);
  number(&ctx->section[5], tracks.size);
  bytes(&ctx->section[5], tracks.data, tracks.size);
  ok = !ctx->memory.failed;
done:
  imp_free(&ctx->memory, entries);
  imp_free(&ctx->memory, tracks.data);
  return ok;
}

static int pso_motion(PsoImport *ctx, const unsigned char *source,
                      unsigned size, float fps, int euler_bytes) {
  if (!source) {
    number(&ctx->section[5], 0);
    return 1;
  }
  if (size < 20 || memcmp(source, "NMDM", 4) || le32(source + 4) != size - 8 ||
      !isfinite(fps) || fps <= 0 || fps > 240 ||
      (euler_bytes != 8 && euler_bytes != 16))
    return 0;
  PsoReader r = {source + 8, size - 8, &ctx->memory};
  return pso_motion_reader(ctx, r, 0, fps, euler_bytes, 1);
}

static int import_pso(const char *path, const char *archive_path,
                      const char *motion_path, float fps, int euler_bytes,
                      int budget, int executor) {
  PsoImport ctx = {0};
  ctx.memory.budget = (unsigned)budget;
  ctx.memory.executor = executor;
  for (unsigned i = 0; i < 6; i++)
    ctx.section[i].memory = &ctx.memory;
  unsigned size = 0, archive_size = 0, motion_size = 0;
  unsigned char *source = source_file(path, &ctx.memory, &size),
                *archive = NULL, *package = NULL, *motion = NULL;
  int token = 0;
  if (!source)
    goto done;
  ctx.nodes = imp_alloc(&ctx.memory, 1024 * sizeof(PsoNode));
  ctx.textures = imp_alloc(&ctx.memory, 256 * sizeof(PsoTexture));
  if (!ctx.nodes || !ctx.textures)
    goto done;
  if (archive_path) {
    archive = source_file(archive_path, &ctx.memory, &archive_size);
    if (!archive || !pso_archive(&ctx, archive, archive_size))
      goto done;
  }
  for (unsigned p = 0; p < size;) {
    if (size - p < 8)
      goto done;
    unsigned length = le32(source + p + 4);
    if (length > size - p - 8)
      goto done;
    PsoReader r = {source + p + 8, length, &ctx.memory};
    if (!memcmp(source + p, "GJCM", 4)) {
      if (ctx.geometry.data)
        goto done;
      ctx.geometry = r;
    }
    if (!memcmp(source + p, "GJTL", 4)) {
      if (ctx.list.data)
        goto done;
      ctx.list = r;
    }
    p += 8 + length;
  }
  if (!ctx.geometry.data)
    goto done;
  pso_node(&ctx, 0, -1, 0);
  if (ctx.memory.failed || !ctx.node_count)
    goto done;
  number(&ctx.section[0], ctx.node_count);
  for (unsigned i = 0; i < ctx.node_count; i++) {
    PsoNode *n = &ctx.nodes[i];
    char fallback[32];
    snprintf(fallback, sizeof(fallback), "PSO_Node_%u", i);
    number(&ctx.section[0], (unsigned)n->parent);
    name(&ctx.section[0], fallback, fallback);
    floats(&ctx.section[0], n->t, 3);
    floats(&ctx.section[0], n->q, 4);
    floats(&ctx.section[0], n->s, 3);
    if (n->mesh) {
      ctx.meshes += pso_u16(ctx.geometry, n->mesh + 16);
      if (ctx.meshes > 4096)
        goto done;
    }
  }
  if (!ctx.meshes)
    goto done;
  number(&ctx.section[1], ctx.meshes);
  for (unsigned i = 0; i < ctx.node_count; i++)
    if (ctx.nodes[i].mesh && !pso_mesh(&ctx, i))
      goto done;
  number(&ctx.section[2], ctx.texture_count + 1);
  for (unsigned i = 0; i <= ctx.texture_count; i++) {
    float color[4] = {1, 1, 1, 1};
    floats(&ctx.section[2], color, 4);
    number(&ctx.section[2], i ? i - 1 : UINT_MAX);
    number(&ctx.section[2], 0);
    scalar(&ctx.section[2], .5f);
    number(&ctx.section[2], 0);
  }
  number(&ctx.section[3], ctx.texture_count);
  for (unsigned i = 0; i < ctx.texture_count; i++) {
    PsoTexture *t = &ctx.textures[i];
    number(&ctx.section[3], t->width);
    number(&ctx.section[3], t->height);
    number(&ctx.section[3], 1);
    number(&ctx.section[3], 1);
    number(&ctx.section[3], 1);
    number(&ctx.section[3], 1);
    number(&ctx.section[3], t->width * t->height * 4);
    for (unsigned y = 0; y < t->height; y += 4)
      for (unsigned x = 0; x < t->width; x += 4) {
        unsigned char tile[64];
        for (unsigned row = 0; row < 4; row++)
          for (unsigned col = 0; col < 4; col++) {
            unsigned char c[4];
            pso_pixel(t, x + col, y + row, c);
            unsigned k = (row * 4 + col) * 2;
            tile[k] = c[3];
            tile[k + 1] = c[0];
            tile[k + 32] = c[1];
            tile[k + 33] = c[2];
          }
        bytes(&ctx.section[3], tile, 64);
      }
  }
  number(&ctx.section[4], 0);
  if (motion_path) {
    motion = source_file(motion_path, &ctx.memory, &motion_size);
    if (!motion)
      goto done;
  }
  if (!pso_motion(&ctx, motion, motion_size, fps, euler_bytes) ||
      ctx.memory.failed)
    goto done;
  {
    unsigned total = 160, offset[6];
    for (unsigned i = 0; i < 6; i++) {
      offset[i] = total;
      if (ctx.section[i].size > DND_MODEL_LIMIT - total)
        goto done;
      total = (total + ctx.section[i].size + 31) & ~31u;
    }
    imp_free(&ctx.memory, source);
    source = NULL;
    imp_free(&ctx.memory, archive);
    archive = NULL;
    imp_free(&ctx.memory, motion);
    motion = NULL;
    imp_free(&ctx.memory, ctx.nodes);
    ctx.nodes = NULL;
    imp_free(&ctx.memory, ctx.textures);
    ctx.textures = NULL;
    package = imp_alloc(&ctx.memory, total);
    if (!package)
      goto done;
    put32(package, 0x444e444d);
    put32(package + 4, 1);
    put32(package + 8, total);
    put32(package + 12, 6);
    for (unsigned i = 0; i < 6; i++) {
      unsigned char *dir = package + 16 + i * 16;
      put32(dir, i + 1);
      put32(dir + 4, offset[i]);
      put32(dir + 8, ctx.section[i].size);
      put32(dir + 12, dnd_model_hash(ctx.section[i].data, ctx.section[i].size));
      memcpy(package + offset[i], ctx.section[i].data, ctx.section[i].size);
      imp_free(&ctx.memory, ctx.section[i].data);
      ctx.section[i].data = NULL;
    }
    token = import_publish(&ctx.memory, package, total, budget);
  }
done:
  imp_free(&ctx.memory, source);
  imp_free(&ctx.memory, archive);
  imp_free(&ctx.memory, motion);
  imp_free(&ctx.memory, package);
  imp_free(&ctx.memory, ctx.nodes);
  imp_free(&ctx.memory, ctx.textures);
  for (unsigned i = 0; i < 6; i++)
    imp_free(&ctx.memory, ctx.section[i].data);
  return token;
}

#include "model_fzero.inc"
#include "model_gc.inc"
#include "model_j3d.inc"
#include "model_re4.inc"
#include "model_renderware.inc"
#include "model_melee.inc"
#include "model_pso_ep3.inc"

int dnd_model_begin_gamecube(DndString *path, DndString *textures,
                              DndString *motion, float fps, int format,
                              int budget, int approximate) {
  if (!path || (format < 6 || format > 9) || budget < 1024 ||
      budget > (int)DND_MODEL_LIMIT || !isfinite(fps) || fps < 1 || fps > 240 ||
      (approximate < 0 || approximate > (format == 8 ? 3 : 1)) ||
      (dnd_async_running() && dnd_async_context() != 1))
    return 0;
  char filename[1024], archive[1024], animation[1024];
  if (!dnd_fs_path_utf8_native(path, filename, sizeof(filename)) ||
      (textures && !dnd_fs_path_utf8_native(textures, archive, sizeof(archive))) ||
      (motion && !dnd_fs_path_utf8_native(motion, animation, sizeof(animation))))
    return 0;
  int executor = dnd_async_native_begin();
  int token = format == 8
      ? import_renderware(filename, textures ? archive : NULL, motion ? animation : NULL, budget, approximate, executor)
      : format == 9
      ? import_melee(filename, motion ? animation : NULL, fps, budget, approximate, executor)
      : format == 6
      ? import_j3d(filename, motion ? animation : NULL, fps, budget, approximate, executor)
      : import_re4(filename, textures ? archive : NULL, motion ? animation : NULL,
                   fps, budget, approximate, executor);
  dnd_async_native_end(executor);
  return token;
}

/* Snapshot managed paths while the gate is held; only native stack copies are
 * visible while it is released. One service executor serializes all imports,
 * including stb_image's allocator context. Direct calls remain usable in manual
 * native tests, but cannot start competing imports on the application executor.
 */
static int begin_source(DndString *path, DndString *textures, DndString *motion,
                        int format, int budget, int approximate, float fps,
                        int euler_bytes) {
  if (!path || budget < 1024 || budget > (int)DND_MODEL_LIMIT ||
      (dnd_async_running() && dnd_async_context() != 1))
    return 0;
  char filename[1024], archive[1024], animation[1024];
  if (!dnd_fs_path_utf8_native(path, filename, sizeof(filename)) ||
      (textures &&
       !dnd_fs_path_utf8_native(textures, archive, sizeof(archive))) ||
      (motion &&
       !dnd_fs_path_utf8_native(motion, animation, sizeof(animation))))
    return 0;
  const char *archive_path = textures ? archive : NULL;
  const char *motion_path = motion ? animation : NULL;
  int executor = dnd_async_native_begin();
  int token = format == 2 ? import_glb(filename, budget, approximate, executor)
              : format == 3 ? import_pso(filename, archive_path, motion_path,
                                         fps, euler_bytes, budget, executor)
                            : import_scene(filename, format == 4, fps, budget,
                                           approximate, executor);
  dnd_async_native_end(executor);
  return token;
}
int dnd_model_begin_glb(DndString *path, int budget, int approximate) {
  return begin_source(path, NULL, NULL, 2, budget, approximate, 0, 16);
}
int dnd_model_begin_scene(DndString *path, int fbx, float sample_rate,
                          int budget, int approximate) {
  if (!isfinite(sample_rate) || sample_rate < 1 || sample_rate > 240)
    return 0;
  return begin_source(path, NULL, NULL, fbx ? 4 : 1, budget, approximate,
                      sample_rate, 16);
}
int dnd_model_begin_pso(DndString *path, DndString *textures, DndString *motion,
                        float fps, int euler_bytes, int budget) {
  return begin_source(path, textures, motion, 3, budget, 0, fps, euler_bytes);
}
