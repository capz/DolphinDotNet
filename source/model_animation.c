#include "dnd_draw.h"
#include "dnd_model_internal.h"
#include <limits.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>
static int next_instance = 1;
void dnd_model_identity(float m[16]) {
  memset(m, 0, 64);
  m[0] = m[5] = m[10] = m[15] = 1;
}
void dnd_model_multiply(float out[16], const float a[16], const float b[16]) {
  float t[16];
  for (int c = 0; c < 4; c++)
    for (int r = 0; r < 4; r++) {
      float v = 0;
      for (int k = 0; k < 4; k++)
        v += a[k * 4 + r] * b[c * 4 + k];
      t[c * 4 + r] = v;
    }
  memcpy(out, t, 64);
}
bool dnd_model_inverse(float out[16], const float m[16]) {
  for (int i = 0; i < 16; i++)
    if (!isfinite(m[i]))
      return false;
  float a[4][8];
  for (int r = 0; r < 4; r++)
    for (int c = 0; c < 8; c++)
      a[r][c] = c < 4 ? m[c * 4 + r] : (c - 4 == r ? 1 : 0);
  for (int c = 0; c < 4; c++) {
    int pivot = c;
    for (int r = c + 1; r < 4; r++)
      if (fabsf(a[r][c]) > fabsf(a[pivot][c]))
        pivot = r;
    if (fabsf(a[pivot][c]) < 1e-8f)
      return false;
    if (pivot != c)
      for (int j = 0; j < 8; j++) {
        float t = a[c][j];
        a[c][j] = a[pivot][j];
        a[pivot][j] = t;
      }
    float div = a[c][c];
    for (int j = 0; j < 8; j++)
      a[c][j] /= div;
    for (int r = 0; r < 4; r++)
      if (r != c) {
        float f = a[r][c];
        for (int j = 0; j < 8; j++)
          a[r][j] -= f * a[c][j];
      }
  }
  for (int r = 0; r < 4; r++)
    for (int c = 0; c < 4; c++) {
      out[c * 4 + r] = a[r][c + 4];
      if (!isfinite(out[c * 4 + r]))
        return false;
    }
  return true;
}
static bool normalize(float *q, int count) {
  float sum = 0;
  for (int i = 0; i < count; i++)
    sum += q[i] * q[i];
  if (!isfinite(sum) || sum < 1e-12f)
    return false;
  sum = 1 / sqrtf(sum);
  for (int i = 0; i < count; i++)
    q[i] *= sum;
  return true;
}
static void slerp(float *out, const float *a, const float *b, float t) {
  float dot = 0;
  for (int i = 0; i < 4; i++)
    dot += a[i] * b[i];
  float sign = dot < 0 ? -1 : 1;
  dot = fabsf(dot);
  float x = 1 - t, y = t;
  if (dot < 0.9995f) {
    float angle = acosf(fminf(dot, 1));
    float inv = 1 / sinf(angle);
    x = sinf((1 - t) * angle) * inv;
    y = sinf(t * angle) * inv;
  }
  for (int i = 0; i < 4; i++)
    out[i] = a[i] * x + b[i] * sign * y;
  (void)normalize(out, 4);
}
static void trs(float out[16], const DndPose *p) {
  float x = p->q[0], y = p->q[1], z = p->q[2], w = p->q[3];
  dnd_model_identity(out);
  out[0] = (1 - 2 * y * y - 2 * z * z) * p->s[0];
  out[1] = (2 * x * y + 2 * z * w) * p->s[0];
  out[2] = (2 * x * z - 2 * y * w) * p->s[0];
  out[4] = (2 * x * y - 2 * z * w) * p->s[1];
  out[5] = (1 - 2 * x * x - 2 * z * z) * p->s[1];
  out[6] = (2 * y * z + 2 * x * w) * p->s[1];
  out[8] = (2 * x * z + 2 * y * w) * p->s[2];
  out[9] = (2 * y * z - 2 * x * w) * p->s[2];
  out[10] = (1 - 2 * x * x - 2 * y * y) * p->s[2];
  out[12] = p->t[0];
  out[13] = p->t[1];
  out[14] = p->t[2];
}
static bool sample(DndModel *m, int clip, float time, DndPose *poses) {
  for (unsigned i = 0; i < m->nodes; i++)
    poses[i] = m->node[i].rest;
  if (clip < 0)
    return true;
  const unsigned char *p = m->clip[clip].tracks;
  unsigned tracks = dnd_model_u32(p);
  p += 4;
  for (unsigned track = 0; track < tracks; track++) {
    unsigned node = dnd_model_u32(p), prop = dnd_model_u32(p + 4),
             mode = dnd_model_u32(p + 8), keys = dnd_model_u32(p + 12);
    p += 16;
    unsigned width = prop == 1 ? 4 : 3,
             stride = 4 + width * 4 * (mode == 2 ? 3 : 1);
    const unsigned char *first = p;
    unsigned lo = 0, hi = keys - 1;
    while (lo < hi) {
      unsigned mid = (lo + hi + 1) / 2;
      if (dnd_model_f32(first + mid * stride) <= time)
        lo = mid;
      else
        hi = mid - 1;
    }
    unsigned left = lo, right = left + 1 < keys ? left + 1 : left;
    const unsigned char *a = first + left * stride, *b = first + right * stride;
    float ta = dnd_model_f32(a), tb = dnd_model_f32(b),
          u = tb > ta ? fminf(1, fmaxf(0, (time - ta) / (tb - ta))) : 0;
    float va[4], vb[4], value[4];
    for (unsigned j = 0; j < width; j++) {
      va[j] = dnd_model_f32(a + 4 + (mode == 2 ? width * 4 : 0) + j * 4);
      vb[j] = dnd_model_f32(b + 4 + (mode == 2 ? width * 4 : 0) + j * 4);
    }
    if (mode == 0 || left == right || time <= ta)
      memcpy(value, va, width * 4);
    else if (mode == 1 && prop == 1)
      slerp(value, va, vb, u);
    else
      for (unsigned j = 0; j < width; j++) {
        if (mode == 1)
          value[j] = va[j] + (vb[j] - va[j]) * u;
        else {
          float out = dnd_model_f32(a + 4 + width * 8 + j * 4) * (tb - ta),
                in = dnd_model_f32(b + 4 + j * 4) * (tb - ta), u2 = u * u,
                u3 = u2 * u;
          value[j] = (2 * u3 - 3 * u2 + 1) * va[j] + (u3 - 2 * u2 + u) * out +
                     (-2 * u3 + 3 * u2) * vb[j] + (u3 - u2) * in;
        }
      }
    if (prop == 1 && !normalize(value, 4))
      return false;
    float *dest = prop == 0   ? poses[node].t
                  : prop == 1 ? poses[node].q
                              : poses[node].s;
    memcpy(dest, value, width * 4);
    for (unsigned j = 0; j < width; j++)
      if (!isfinite(value[j]) || (prop == 2 && fabsf(value[j]) < 1e-6f))
        return false;
    p += keys * stride;
  }
  return true;
}
static void global(DndInstance *in, unsigned node) {
  if (in->done[node])
    return;
  int parent = in->model->node[node].parent;
  float local[16];
  trs(local, &in->pose[node]);
  if (parent >= 0) {
    global(in, (unsigned)parent);
    dnd_model_multiply(in->global + node * 16,
                       in->global + (unsigned)parent * 16, local);
  } else
    memcpy(in->global + node * 16, local, 64);
  in->done[node] = 1;
}
void dnd_model_drop_vertices(DndInstance *in) {
  dnd_model_free(in->workspace, &in->bytes);
  in->workspace = NULL;
  if (in->vertices)
    for (unsigned i = 0; i < in->model->meshes; i++) {
      dnd_model_free(in->vertices[i], &in->bytes);
      in->vertices[i] = NULL;
    }
}
int dnd_model_evaluate(DndInstance *in) {
  DndModel *m = in->model;
  if (!m->ready)
    return -1;
  if (!sample(m, in->clip, in->time, in->pose))
    return -1;
  if (in->next >= 0) {
    if (!sample(m, in->next, in->next_time, in->other))
      return -1;
    float u = in->fade > 0 ? fminf(1, in->elapsed / in->fade) : 1;
    for (unsigned i = 0; i < m->nodes; i++) {
      for (int j = 0; j < 3; j++) {
        in->pose[i].t[j] += (in->other[i].t[j] - in->pose[i].t[j]) * u;
        in->pose[i].s[j] += (in->other[i].s[j] - in->pose[i].s[j]) * u;
      }
      float q[4];
      slerp(q, in->pose[i].q, in->other[i].q, u);
      memcpy(in->pose[i].q, q, 16);
    }
  }
  memset(in->done, 0, m->nodes);
  for (unsigned i = 0; i < m->nodes; i++)
    global(in, i);
  for (unsigned i = 0; i < m->meshes; i++) {
    DndMesh *mesh = &m->mesh[i];
    if (mesh->skin < 0)
      continue;
    if (!in->vertices[i]) {
      in->vertices[i] =
          dnd_model_alloc(mesh->vertices * 6 * 4, &in->bytes, in->budget);
      if (!in->vertices[i])
        return -2;
    }
    DndSkin *skin = &m->skin[mesh->skin];
    if (!in->workspace) {
      in->workspace = dnd_model_alloc(256 * 32 * 4, &in->bytes, in->budget);
      if (!in->workspace)
        return -2;
    }
    float(*palette)[16] = (float(*)[16])in->workspace,
    (*normal)[16] = (float(*)[16])(in->workspace + 256 * 16);
    for (int j = 0; j < skin->count; j++) {
      const unsigned char *joint = skin->data + (unsigned)j * 68;
      float bind[16];
      for (int k = 0; k < 16; k++)
        bind[k] = dnd_model_f32(joint + 4 + k * 4);
      dnd_model_multiply(palette[j], in->global + dnd_model_u32(joint) * 16,
                         bind);
      if (!dnd_model_inverse(normal[j], palette[j]))
        return -1;
    }
    for (unsigned v = 0; v < mesh->vertices; v++) {
      const unsigned char *src = mesh->data + v * 80;
      float pos[3], n[3], out[6] = {0};
      for (int j = 0; j < 3; j++) {
        pos[j] = dnd_model_f32(src + j * 4);
        n[j] = dnd_model_f32(src + 12 + j * 4);
      }
      for (int j = 0; j < 4; j++) {
        unsigned bone = dnd_model_u32(src + 48 + j * 4);
        float weight = dnd_model_f32(src + 64 + j * 4);
        for (int r = 0; r < 3; r++) {
          out[r] += weight *
                    (palette[bone][r] * pos[0] + palette[bone][4 + r] * pos[1] +
                     palette[bone][8 + r] * pos[2] + palette[bone][12 + r]);
          out[3 + r] += weight * (normal[bone][r * 4] * n[0] +
                                  normal[bone][r * 4 + 1] * n[1] +
                                  normal[bone][r * 4 + 2] * n[2]);
        }
      }
      if (!normalize(out + 3, 3)) {
        out[3] = 0;
        out[4] = 1;
        out[5] = 0;
      }
      memcpy(in->vertices[i] + v * 6, out, 24);
    }
  }
  return 0;
}
int dnd_model_instance(int token, int budget) {
  DndModel *m = dnd_model_find(token, false);
  if (!m || !m->ready || budget < 1024 || budget > (int)DND_MODEL_LIMIT ||
      next_instance == INT_MAX)
    return 0;
  int slot = -1;
  for (int i = 0; i < DND_INSTANCE_SLOTS; i++)
    if (!dnd_instances[i]) {
      slot = i;
      break;
    }
  if (slot < 0)
    return 0;
  DndInstance *in = calloc(1, sizeof(*in));
  if (!in)
    return 0;
  in->model = m;
  in->token = next_instance++;
  in->budget = (unsigned)budget;
  in->bytes = sizeof(*in);
  in->clip = in->next = -1;
  in->speed = 1;
  in->pose =
      dnd_model_alloc(m->nodes * sizeof(DndPose), &in->bytes, in->budget);
  in->other =
      dnd_model_alloc(m->nodes * sizeof(DndPose), &in->bytes, in->budget);
  in->global = dnd_model_alloc(m->nodes * 64, &in->bytes, in->budget);
  in->done = dnd_model_alloc(m->nodes, &in->bytes, in->budget);
  in->vertices =
      dnd_model_alloc(m->meshes * sizeof(float *), &in->bytes, in->budget);
  m->refs++;
  dnd_instances[slot] = in;
  if (!in->pose || !in->other || !in->global || !in->done || !in->vertices ||
      dnd_model_evaluate(in) < 0) {
    dnd_model_instance_dispose(in->token);
    return 0;
  }
  return in->token;
}
int dnd_model_instance_dispose(int token) {
  DndInstance *in = dnd_instance_find(token);
  if (!in || dnd_device_active())
    return -1;
  for (int i = 0; i < DND_INSTANCE_SLOTS; i++)
    if (dnd_instances[i] == in)
      dnd_instances[i] = NULL;
  DndModel *m = in->model;
  dnd_model_drop_vertices(in);
  dnd_model_free(in->vertices, &in->bytes);
  dnd_model_free(in->pose, &in->bytes);
  dnd_model_free(in->other, &in->bytes);
  dnd_model_free(in->global, &in->bytes);
  dnd_model_free(in->done, &in->bytes);
  free(in);
  dnd_model_release(m);
  return 0;
}
int dnd_model_play(int token, int clip, int loop, float fade) {
  DndInstance *in = dnd_instance_find(token);
  if (!in || !in->model->ready || clip < 0 ||
      (unsigned)clip >= in->model->clips || !isfinite(fade) || fade < 0 ||
      dnd_device_active())
    return -1;
  if (fade > 0 && in->clip >= 0) {
    if (in->next >= 0)
      return -1;
    in->next = clip;
    in->next_time = 0;
    in->next_loop = loop != 0;
    in->fade = fade;
    in->elapsed = 0;
  } else {
    in->clip = clip;
    in->time = 0;
    in->loop = loop != 0;
    in->next = -1;
  }
  in->paused = false;
  return dnd_model_evaluate(in);
}
static float advance(float time, float delta, float duration, bool loop) {
  time += delta;
  if (duration <= 0)
    return 0;
  return loop ? fmodf(time, duration) : fminf(time, duration);
}
int dnd_model_control(int token, int op, float value) {
  DndInstance *in = dnd_instance_find(token);
  if (!in || !isfinite(value) || value < 0 || dnd_device_active())
    return -1;
  if (op == 1) {
    in->paused = true;
    return 0;
  }
  if (op == 2) {
    in->paused = false;
    return 0;
  }
  if (op == 5) {
    in->speed = value;
    return 0;
  }
  if (!in->model->ready)
    return -1;
  if (op == 0) {
    if (in->paused || value == 0 || (in->clip < 0 && in->next < 0))
      return 0;
    if (!in->paused) {
      float delta = value * in->speed;
      if (!isfinite(delta))
        return -1;
      if (in->clip >= 0)
        in->time = advance(in->time, delta, in->model->clip[in->clip].duration,
                           in->loop);
      if (in->next >= 0) {
        in->next_time =
            advance(in->next_time, delta, in->model->clip[in->next].duration,
                    in->next_loop);
        in->elapsed += value;
        if (in->elapsed >= in->fade) {
          in->clip = in->next;
          in->time = in->next_time;
          in->loop = in->next_loop;
          in->next = -1;
        }
      }
    }
  } else if (op == 3) {
    in->clip = in->next = -1;
    in->time = 0;
  } else if (op == 4) {
    if (in->clip < 0)
      return -1;
    in->next = -1;
    in->time = fminf(value, in->model->clip[in->clip].duration);
  } else
    return -1;
  return dnd_model_evaluate(in);
}
int dnd_model_node_matrix(int token, int node, DndArray *out) {
  DndInstance *in = dnd_instance_find(token);
  if (!in || node < 0 || (unsigned)node >= in->model->nodes || !out ||
      out->element_size != 4 || out->elements_are_references ||
      out->length != 16)
    return -1;
  memcpy(out->data, in->global + (unsigned)node * 16, 64);
  return 0;
}
int dnd_model_vertex(int token, int mesh, int vertex, float out[6]) {
  DndInstance *in = dnd_instance_find(token);
  if (!in || !in->model->ready || mesh < 0 ||
      (unsigned)mesh >= in->model->meshes || vertex < 0 ||
      (unsigned)vertex >= in->model->mesh[mesh].vertices)
    return -1;
  DndMesh *m = &in->model->mesh[mesh];
  if (m->skin >= 0) {
    if (!in->vertices[mesh])
      return -1;
    memcpy(out, in->vertices[mesh] + (unsigned)vertex * 6, 24);
  } else {
    const unsigned char *p = m->data + (unsigned)vertex * 80;
    for (int j = 0; j < 6; j++)
      out[j] = dnd_model_f32(p + j * 4);
  }
  return 0;
}

int dnd_model_instance_bytes(int token) {
  DndInstance *in = dnd_instance_find(token);
  return in ? (int)in->bytes : -1;
}
