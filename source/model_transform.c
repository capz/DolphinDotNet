#include "dnd_model_internal.h"
#include <math.h>
#include <string.h>
static float *array(DndArray *a, unsigned count) {
  return a && a->length == count && a->element_size == 4 &&
                 !a->elements_are_references
             ? (float *)a->data
             : NULL;
}
static bool valid(const float *p, unsigned n) {
  if (!p)
    return false;
  for (unsigned i = 0; i < n; i++)
    if (!isfinite(p[i]))
      return false;
  return true;
}
static bool unit(float *q, unsigned n) {
  float sum = 0;
  for (unsigned i = 0; i < n; i++)
    sum += q[i] * q[i];
  if (!isfinite(sum) || sum < 1e-12f)
    return false;
  float v = 1 / sqrtf(sum);
  for (unsigned i = 0; i < n; i++)
    q[i] *= v;
  return true;
}
int dnd_model_trs(DndArray *values, DndArray *matrix) {
  float *v = array(values, 10), *m = array(matrix, 16);
  if (!valid(v, 10) || !m)
    return -1;
  float q[4];
  memcpy(q, v + 3, 16);
  if (!unit(q, 4))
    return -1;
  for (int i = 7; i < 10; i++)
    if (fabsf(v[i]) < 1e-6f)
      return -1;
  float x = q[0], y = q[1], z = q[2], w = q[3];
  dnd_model_identity(m);
  m[0] = (1 - 2 * y * y - 2 * z * z) * v[7];
  m[1] = (2 * x * y + 2 * z * w) * v[7];
  m[2] = (2 * x * z - 2 * y * w) * v[7];
  m[4] = (2 * x * y - 2 * z * w) * v[8];
  m[5] = (1 - 2 * x * x - 2 * z * z) * v[8];
  m[6] = (2 * y * z + 2 * x * w) * v[8];
  m[8] = (2 * x * z + 2 * y * w) * v[9];
  m[9] = (2 * y * z - 2 * x * w) * v[9];
  m[10] = (1 - 2 * x * x - 2 * y * y) * v[9];
  m[12] = v[0];
  m[13] = v[1];
  m[14] = v[2];
  return valid(m, 16) ? 0 : -1;
}
int dnd_model_multiply_arrays(DndArray *a, DndArray *b, DndArray *out) {
  float *x = array(a, 16), *y = array(b, 16), *o = array(out, 16);
  if (!valid(x, 16) || !valid(y, 16) || !o)
    return -1;
  dnd_model_multiply(o, x, y);
  return valid(o, 16) ? 0 : -1;
}
int dnd_model_position(DndArray *values, DndArray *parent, float x, float y,
                       float z) {
  float *v = array(values, 10), *p = array(parent, 16), inverse[16];
  if (!v || !valid(p, 16) || !isfinite(x) || !isfinite(y) || !isfinite(z) ||
      !dnd_model_inverse(inverse, p))
    return -1;
  float point[3];
  for (int r = 0; r < 3; r++)
    point[r] = inverse[r] * x + inverse[4 + r] * y + inverse[8 + r] * z +
               inverse[12 + r];
  if (!valid(point, 3))
    return -1;
  memcpy(v, point, 12);
  return 0;
}
int dnd_model_rotate(DndArray *values, float x, float y, float z) {
  float *v = array(values, 10);
  if (!valid(v, 10) || !isfinite(x) || !isfinite(y) || !isfinite(z))
    return -1;
  const float rad = 0.008726646259971648f;
  x *= rad;
  y *= rad;
  z *= rad;
  float sx = sinf(x), cx = cosf(x), sy = sinf(y), cy = cosf(y), sz = sinf(z),
        cz = cosf(z);
  float q[4] = {sx * cy * cz - cx * sy * sz, cx * sy * cz + sx * cy * sz,
                cx * cy * sz - sx * sy * cz, cx * cy * cz + sx * sy * sz};
  float a = v[3], b = v[4], c = v[5], d = v[6],
        r[4] = {d * q[0] + a * q[3] + b * q[2] - c * q[1],
                d * q[1] - a * q[2] + b * q[3] + c * q[0],
                d * q[2] + a * q[1] - b * q[0] + c * q[3],
                d * q[3] - a * q[0] - b * q[1] - c * q[2]};
  if (!unit(r, 4))
    return -1;
  memcpy(v + 3, r, 16);
  return 0;
}
int dnd_model_camera(DndArray *settings, DndArray *view, DndArray *projection) {
  float *s = array(settings, 13), *v = array(view, 16),
        *p = array(projection, 16);
  if (!valid(s, 13) || !v || !p || s[9] <= 0 || s[9] >= 179 || s[10] <= 0 ||
      s[11] <= 0 || s[12] <= s[11])
    return -1;
  float z[3] = {s[0] - s[3], s[1] - s[4], s[2] - s[5]};
  if (!unit(z, 3))
    return -1;
  float x[3] = {s[7] * z[2] - s[8] * z[1], s[8] * z[0] - s[6] * z[2],
                s[6] * z[1] - s[7] * z[0]};
  if (!unit(x, 3))
    return -1;
  float y[3] = {z[1] * x[2] - z[2] * x[1], z[2] * x[0] - z[0] * x[2],
                z[0] * x[1] - z[1] * x[0]};
  dnd_model_identity(v);
  for (int i = 0; i < 3; i++) {
    v[i * 4] = x[i];
    v[i * 4 + 1] = y[i];
    v[i * 4 + 2] = z[i];
  }
  v[12] = -(x[0] * s[0] + x[1] * s[1] + x[2] * s[2]);
  v[13] = -(y[0] * s[0] + y[1] * s[1] + y[2] * s[2]);
  v[14] = -(z[0] * s[0] + z[1] * s[1] + z[2] * s[2]);
  float f = 1 / tanf(s[9] * 0.008726646259971648f);
  memset(p, 0, 64);
  p[0] = f / s[10];
  p[5] = f;
  p[10] = -s[11] / (s[12] - s[11]);
  p[11] = -1;
  p[14] = -(s[12] * s[11]) / (s[12] - s[11]);
  return valid(v, 16) && valid(p, 16) ? 0 : -1;
}

int dnd_model_translate(DndArray *values, float x, float y, float z) {
  float *v = array(values, 10);
  if (!valid(v, 10) || !isfinite(x) || !isfinite(y) || !isfinite(z))
    return -1;
  float q[4];
  memcpy(q, v + 3, 16);
  if (!unit(q, 4))
    return -1;
  float t[3] = {2 * (q[1] * z - q[2] * y), 2 * (q[2] * x - q[0] * z),
                2 * (q[0] * y - q[1] * x)};
  float result[3] = {v[0] + x + q[3] * t[0] + q[1] * t[2] - q[2] * t[1],
                     v[1] + y + q[3] * t[1] + q[2] * t[0] - q[0] * t[2],
                     v[2] + z + q[3] * t[2] + q[0] * t[1] - q[1] * t[0]};
  if (!valid(result, 3))
    return -1;
  memcpy(v, result, 12);
  return 0;
}
