/* Independent J3D2 BMD3/BDL4, TEX1/BTI, ANK1/BCK and ANF1/BCA adapter.
 * Layout references and rendering limits are documented in
 * gamecube-model-formats.md. */
typedef struct {
  unsigned offset, end, count, type, shift, components, stride;
} J3dArray;
typedef struct {
  GcImport gc;
  PsoReader inf, vtx, evp, drw, jnt, shp, mat, tex;
  J3dArray array[26];
  int *parents, *shape_material;
  unsigned *joint_record;
  unsigned char *vertex_color;
} J3dImport;

static PsoReader j3d_chunk(PsoReader r, const char *magic, const char *chunk) {
  PsoReader found = {NULL, 0, r.memory};
  if (r.size < 32 || memcmp(r.data, magic, 8) || pso_u32(r, 8) != r.size)
    return found;
  unsigned count = pso_u32(r, 12), at = 32;
  if (!count || count > 64)
    return found;
  for (unsigned i = 0; i < count; i++) {
    if (!pso_range(r, at, 8))
      return (PsoReader){NULL, 0, r.memory};
    unsigned length = pso_u32(r, at + 4);
    if (length < 8 || !pso_range(r, at, length))
      return (PsoReader){NULL, 0, r.memory};
    if (!memcmp(r.data + at, chunk, 4)) {
      if (found.data)
        return (PsoReader){NULL, 0, r.memory};
      found = gc_slice(r, at, length);
    }
    at += length;
  }
  return at == r.size ? found : (PsoReader){NULL, 0, r.memory};
}
static int j3d_name(PsoReader r, unsigned table, unsigned index, char out[64]) {
  if (!pso_range(r, table, 4) || index >= pso_u16(r, table) ||
      !pso_range(r, table + 4, (index + 1) * 4))
    return 0;
  unsigned offset = pso_u16(r, table + 6 + index * 4);
  if (offset >= r.size - table)
    return 0;
  char text[1024];
  if (!fz_string(r, table + offset, r.size, text) || strlen(text) > 63)
    return 0;
  strcpy(out, text);
  return 1;
}
static int j3d_hierarchy(J3dImport *c) {
  GcImport *g = &c->gc;
  unsigned joints = pso_u16(c->jnt, 8), shapes = pso_u16(c->shp, 8);
  unsigned materials = pso_u16(c->mat, 8);
  if (!joints || joints > 1024 || !shapes || shapes > 1024 || !materials ||
      materials > 4096)
    return 0;
  c->parents = imp_alloc(&g->memory, joints * sizeof(int));
  c->shape_material = imp_alloc(&g->memory, shapes * sizeof(int));
  c->joint_record = imp_alloc(&g->memory, joints * sizeof(unsigned));
  c->vertex_color = imp_alloc(&g->memory, materials);
  if (!c->parents || !c->shape_material || !c->joint_record || !c->vertex_color)
    return 0;
  for (unsigned i = 0; i < joints; i++)
    c->parents[i] = -2;
  for (unsigned i = 0; i < shapes; i++)
    c->shape_material[i] = -1;
  int stack[33], current = -1, last = -1, material = -1;
  unsigned depth = 0, at = pso_u32(c->inf, 20), seen = 0;
  stack[0] = -1;
  for (unsigned entries = 0; entries < 65536; entries++, at += 4) {
    if (!pso_range(c->inf, at, 4))
      return 0;
    unsigned type = pso_u16(c->inf, at), value = pso_u16(c->inf, at + 2);
    if (!type) {
      if (depth || seen != joints)
        return 0;
      for (unsigned i = 0; i < shapes; i++)
        if (c->shape_material[i] < 0)
          return 0;
      g->nodes = joints;
      return 1;
    }
    if (type == 1) {
      if (depth == 32 || last < 0)
        return 0;
      stack[++depth] = last;
      current = last;
    } else if (type == 2) {
      if (!depth)
        return 0;
      current = stack[--depth];
    } else if (type == 0x10) {
      if (value >= joints || c->parents[value] != -2 || current >= (int)value)
        return 0;
      c->parents[value] = current;
      last = (int)value;
      seen++;
    } else if (type == 0x11) {
      if (value >= materials)
        return 0;
      material = (int)value;
    } else if (type == 0x12) {
      if (value >= shapes || material < 0 || c->shape_material[value] >= 0)
        return 0;
      c->shape_material[value] = material;
    } else
      return 0;
  }
  return 0;
}
static int j3d_joints(J3dImport *c) {
  PsoReader r = c->jnt;
  unsigned table = pso_u32(r, 12), remap = pso_u32(r, 16),
           strings = pso_u32(r, 20);
  unsigned scaling = pso_u16(c->inf, 8) & 15;
  if (scaling > 2 || !pso_range(r, remap, c->gc.nodes * 2))
    return 0;
  for (unsigned i = 0; i < c->gc.nodes; i++) {
    unsigned idx = pso_u16(r, remap + i * 2);
    if (idx >= c->gc.nodes || table > r.size || idx > (r.size - table) / 64 ||
        !pso_range(r, table + idx * 64, 64))
      return 0;
    unsigned at = table + idx * 64;
    c->joint_record[i] = at;
    char text[64];
    if (!j3d_name(r, strings, i, text))
      return 0;
    float s[3], t[3], euler[3], q[4];
    for (unsigned k = 0; k < 3; k++) {
      s[k] = pso_float(r, at + 4 + k * 4);
      t[k] = pso_float(r, at + 24 + k * 4);
      euler[k] = (float)(int16_t)pso_u16(r, at + 16 + k * 2) *
                 (3.14159265358979323846f / 32767);
      if (fabsf(s[k]) < 1e-8f)
        return 0;
    }
    /* Native TRS cannot express Maya's inverse parent scale before rotation.
     * Unit-scale parents are exact; scaled SSC is rejected, including clips. */
    if (scaling == 2 && (pso_byte(r, at + 2) & 1) && c->parents[i] >= 0) {
      unsigned parent = c->joint_record[c->parents[i]];
      for (unsigned k = 0; k < 3; k++)
        if (fabsf(pso_float(r, parent + 4 + k * 4) - 1) > 1e-6f)
          return 0;
    }
    pso_euler(q, euler, 32);
    number(&c->gc.section[0], (unsigned)c->parents[i]);
    name(&c->gc.section[0], text, "Joint");
    floats(&c->gc.section[0], t, 3);
    floats(&c->gc.section[0], q, 4);
    floats(&c->gc.section[0], s, 3);
  }
  return !c->gc.memory.failed;
}
static int j3d_arrays(J3dImport *c) {
  static const unsigned attrs[13] = {9,  10, 25, 11, 12, 13, 14,
                                     15, 16, 17, 18, 19, 20};
  PsoReader r = c->vtx;
  if (r.size < 64)
    return 0;
  for (unsigned i = 0; i < 13; i++) {
    J3dArray *a = &c->array[attrs[i]];
    a->offset = pso_u32(r, 12 + i * 4);
    a->end = r.size;
    if (a->offset > r.size)
      return 0;
    for (unsigned j = 0; j < 13; j++) {
      unsigned next = pso_u32(r, 12 + j * 4);
      if (next > a->offset && next < a->end)
        a->end = next;
    }
  }
  unsigned at = pso_u32(r, 8), seen = 0;
  for (unsigned i = 0; i < 26; i++, at += 16) {
    if (!pso_range(r, at, 4))
      return 0;
    unsigned attr = pso_u32(r, at);
    if (attr == 255)
      return (seen & (1u << 9)) != 0;
    if (attr >= 26 || attr < 9 || (attr > 20 && attr != 25) ||
        (seen & (1u << attr)) || !pso_range(r, at, 16))
      return 0;
    seen |= 1u << attr;
    J3dArray *a = &c->array[attr];
    unsigned n = pso_u32(r, at + 4);
    a->type = pso_u32(r, at + 8);
    a->shift = pso_byte(r, at + 12);
    if (a->shift > 31)
      return 0;
    if (attr == 11 || attr == 12) {
      static const unsigned sizes[6] = {2, 3, 4, 2, 3, 4};
      if (a->type > 5 || n > 1)
        return 0;
      a->components = 4;
      a->stride = sizes[a->type];
    } else {
      if (a->type > 4 || (attr == 9 && n > 1) ||
          (attr >= 13 && attr <= 20 && n > 1))
        return 0;
      a->components = attr == 9                  ? n + 2
                      : attr == 10 || attr == 25 ? (n == 0 ? 3 : 9)
                                                 : n + 1;
      if ((attr == 10 || attr == 25) && n > 1)
        return 0; /* NBT3 has three separate indices. */
      a->count = n;
      a->stride = a->components * (a->type < 2 ? 1 : a->type < 4 ? 2 : 4);
    }
    if (!a->offset || a->offset >= a->end)
      return 0;
  }
  return 0;
}
static float j3d_component(PsoReader r, unsigned at, const J3dArray *a,
                           int normal) {
  if (a->type == 4)
    return pso_float(r, at);
  float v = a->type == 0   ? (float)pso_byte(r, at)
            : a->type == 1 ? (float)(int8_t)pso_byte(r, at)
            : a->type == 2 ? (float)pso_u16(r, at)
                           : (float)(int16_t)pso_u16(r, at);
  unsigned shift = normal ? (a->type < 2 ? 6 : 14) : a->shift;
  return ldexpf(v, -(int)shift);
}
static int gc_attribute(PsoReader source, J3dArray arrays[26], int approximate,
                        PsoReader dl, unsigned *cursor, unsigned attr,
                        unsigned type, GcVertex *v, unsigned *matrix) {
  if (attr <= 8) {
    if (type != 1 || !pso_range(dl, *cursor, 1))
      return 0;
    unsigned value = pso_byte(dl, (*cursor)++);
    if (!attr) {
      if (value % 3 || value / 3 >= 10)
        return 0;
      *matrix = value / 3;
    } else if (value != 60 && !approximate)
      return 0;
    return 1;
  }
  if (attr >= 26 || !arrays[attr].stride || type < 1 || type > 3)
    return 0;
  J3dArray *a = &arrays[attr];
  PsoReader data = source;
  unsigned at;
  if (type == 1) {
    data = dl;
    at = *cursor;
    *cursor += a->stride;
  } else {
    unsigned index = type == 2 ? pso_byte(dl, *cursor) : pso_u16(dl, *cursor);
    *cursor += type == 2 ? 1 : 2;
    if (index >= (a->end - a->offset) / a->stride)
      return 0;
    at = a->offset + index * a->stride;
  }
  if (!pso_range(data, at, a->stride))
    return 0;
  if (attr == 11 || attr == 12) {
    unsigned char rgba[4] = {255, 255, 255, 255};
    unsigned packed = 0;
    if (a->type == 0)
      pso_rgb565(rgba, pso_u16(data, at));
    else if (a->type == 3) {
      packed = pso_u16(data, at);
      for (unsigned k = 0; k < 4; k++)
        rgba[k] = (unsigned char)((packed >> (12 - k * 4) & 15) * 17);
    } else if (a->type == 4) {
      packed = pso_byte(data, at) << 16 | pso_byte(data, at + 1) << 8 |
               pso_byte(data, at + 2);
      for (unsigned k = 0; k < 4; k++) {
        unsigned x = packed >> (18 - k * 6) & 63;
        rgba[k] = (unsigned char)(x << 2 | x >> 4);
      }
    } else {
      for (unsigned k = 0; k < (a->type == 5 ? 4u : 3u); k++)
        rgba[k] = (unsigned char)pso_byte(data, at + k);
    }
    if (attr == 11)
      for (unsigned k = 0; k < 4; k++)
        v->color[k] = rgba[k] / 255.0f;
  } else {
    unsigned width = a->type < 2 ? 1 : a->type < 4 ? 2 : 4;
    for (unsigned k = 0; k < a->components; k++) {
      float value =
          j3d_component(data, at + k * width, a, attr == 10 || attr == 25);
      if (attr == 9 && k < 3)
        v->p[k] = value;
      else if ((attr == 10 || attr == 25) && k < 3)
        v->n[k] = value;
      else if (attr == 13 && k < 2)
        v->uv[k] = value;
    }
  }
  return !source.memory->failed;
}
static int j3d_weights(J3dImport *c, unsigned drw, GcSkin *skin, GcVertex *v) {
  PsoReader r = c->drw;
  unsigned count = pso_u16(r, 8), kinds = pso_u32(r, 12),
           params = pso_u32(r, 16);
  if (drw >= count || !pso_range(r, kinds, count) ||
      !pso_range(r, params, count * 2))
    return 0;
  unsigned kind = pso_byte(r, kinds + drw),
           param = pso_u16(r, params + drw * 2);
  if (kind > 1)
    return 0;
  unsigned n = 1, first = 0, ids = 0, weights = 0, bind = 0;
  PsoReader e = c->evp;
  if (kind) {
    if (!e.data || param >= pso_u16(e, 8))
      return 0;
    unsigned counts = pso_u32(e, 12);
    if (!pso_range(e, counts, pso_u16(e, 8)))
      return 0;
    for (unsigned i = 0; i < param; i++)
      first += pso_byte(e, counts + i);
    n = pso_byte(e, counts + param);
    ids = pso_u32(e, 16);
    weights = pso_u32(e, 20);
    bind = pso_u32(e, 24);
    if (!n || n > 4 || !bind || !pso_range(e, ids, (first + n) * 2) ||
        !pso_range(e, weights, (first + n) * 4))
      return 0;
  }
  float total = 0;
  for (unsigned k = 0; k < n; k++) {
    unsigned node = kind ? pso_u16(e, ids + (first + k) * 2) : param;
    if (node >= c->gc.nodes)
      return 0;
    float matrix[16];
    dnd_model_identity(matrix);
    if (kind) {
      if (bind > e.size || node > (e.size - bind) / 48 ||
          !pso_range(e, bind + node * 48, 48))
        return 0;
      for (unsigned row = 0; row < 3; row++)
        for (unsigned col = 0; col < 4; col++)
          matrix[col * 4 + row] =
              pso_float(e, bind + node * 48 + row * 16 + col * 4);
    }
    int index = gc_skin_joint(skin, node, kind, matrix);
    if (index < 0)
      return 0;
    v->joints[k] = (unsigned)index;
    v->weights[k] = kind ? pso_float(e, weights + (first + k) * 4) : 1;
    if (v->weights[k] < 0)
      return 0;
    total += v->weights[k];
  }
  return fabsf(total - 1) <= 0.001f && !c->gc.memory.failed;
}
static int j3d_shapes(J3dImport *c) {
  PsoReader r = c->shp;
  unsigned count = pso_u16(r, 8), table = pso_u32(r, 12),
           remap = pso_u32(r, 16);
  if (r.size < 44 || !pso_range(r, remap, count * 2))
    return 0;
  for (unsigned i = 0; i < count; i++) {
    ImportBuffer vertices = {0}, indices = {0};
    vertices.memory = indices.memory = &c->gc.memory;
    GcSkin *skin = imp_alloc(&c->gc.memory, sizeof(GcSkin));
    int ok = 0;
    unsigned idx = pso_u16(r, remap + i * 2);
    if (!skin || idx >= count || table > r.size ||
        idx > (r.size - table) / 40 || !pso_range(r, table + idx * 40, 40))
      goto shape_done;
    unsigned at = table + idx * 40, matrix_type = pso_byte(r, at),
             groups = pso_u16(r, at + 2);
    if ((matrix_type != 0 && matrix_type != 3) || !groups || groups > 4096)
      goto shape_done;
    unsigned decl_base = pso_u32(r, 24), decl_offset = pso_u16(r, at + 4);
    if (decl_base > r.size || decl_offset > r.size - decl_base)
      goto shape_done;
    unsigned decl = decl_base + decl_offset, attrs[26], types[26],
             attr_count = 0, seen = 0;
    for (; attr_count < 26; attr_count++) {
      if (!pso_range(r, decl + attr_count * 8, 8))
        goto shape_done;
      unsigned attr = pso_u32(r, decl + attr_count * 8),
               type = pso_u32(r, decl + attr_count * 8 + 4);
      if (attr == 255)
        break;
      if (attr >= 26 || (seen & (1u << attr)) || type < 1 || type > 3)
        goto shape_done;
      seen |= 1u << attr;
      attrs[attr_count] = attr;
      types[attr_count] = type;
    }
    if (attr_count == 26 || !(seen & (1u << 9)))
      goto shape_done;
    unsigned palette[10];
    for (unsigned k = 0; k < 10; k++)
      palette[k] = UINT_MAX;
    for (unsigned group = 0; group < groups; group++) {
      unsigned mg = pso_u32(r, 36), dg = pso_u32(r, 40), mt = pso_u32(r, 28),
               dlbase = pso_u32(r, 32);
      unsigned mgi = pso_u16(r, at + 6) + group,
               dgi = pso_u16(r, at + 8) + group;
      if (mg > r.size || mgi > (r.size - mg) / 8 ||
          !pso_range(r, mg + mgi * 8, 8) || dg > r.size ||
          dgi > (r.size - dg) / 8 || !pso_range(r, dg + dgi * 8, 8))
        goto shape_done;
      mg += mgi * 8;
      dg += dgi * 8;
      unsigned pn = pso_u16(r, mg + 2), first = pso_u32(r, mg + 4);
      if (!pn || pn > 10 || mt > r.size || first > (r.size - mt) / 2 ||
          !pso_range(r, mt + first * 2, pn * 2) ||
          (matrix_type == 0 && pn != 1))
        goto shape_done;
      for (unsigned k = 0; k < pn; k++) {
        unsigned value = pso_u16(r, mt + (first + k) * 2);
        if (value != 65535)
          palette[k] = value;
      }
      unsigned length = pso_u32(r, dg), offset = pso_u32(r, dg + 4);
      if (dlbase > r.size || offset > r.size - dlbase ||
          !pso_range(r, dlbase + offset, length))
        goto shape_done;
      PsoReader dl = gc_slice(r, dlbase + offset, length);
      unsigned cursor = 0;
      while (cursor < dl.size) {
        unsigned command = pso_byte(dl, cursor++);
        if (!command)
          continue;
        if ((command & 7) || !pso_range(dl, cursor, 2))
          goto shape_done;
        unsigned n = pso_u16(dl, cursor), start = vertices.size / 80;
        cursor += 2;
        if (!n || n > 65535 || vertices.size / 80 + n > 262144)
          goto shape_done;
        for (unsigned vtx = 0; vtx < n; vtx++) {
          GcVertex v = {0};
          for (unsigned k = 0; k < 4; k++)
            v.color[k] = 1;
          unsigned matrix = 0;
          for (unsigned a = 0; a < attr_count; a++)
            if (!gc_attribute(c->vtx, c->array, c->gc.approximate, dl, &cursor,
                              attrs[a], types[a], &v, &matrix))
              goto shape_done;
          if (matrix >= pn || palette[matrix] == UINT_MAX ||
              !j3d_weights(c, palette[matrix], skin, &v))
            goto shape_done;
          if (!c->vertex_color[c->shape_material[i]])
            for (unsigned k = 0; k < 4; k++)
              v.color[k] = 1;
          gc_emit(&vertices, &v);
        }
        if (!fz_primitive(&indices, start, n, command, 0))
          goto shape_done;
      }
    }
    ok = gc_mesh(&c->gc, &vertices, &indices, skin,
                 (unsigned)c->shape_material[i],
                 (seen & ((1u << 10) | (1u << 25))) != 0);
  shape_done:
    imp_free(&c->gc.memory, skin);
    imp_free(&c->gc.memory, vertices.data);
    imp_free(&c->gc.memory, indices.data);
    if (!ok)
      return 0;
  }
  return 1;
}
static int j3d_textures(J3dImport *c) {
  PsoReader r = c->tex;
  if (!r.data)
    return 1;
  unsigned count = pso_u16(r, 8), table = pso_u32(r, 12);
  if (count > 4096 || !pso_range(r, table, count * 32))
    return 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned at = table + i * 32, pixels = pso_u32(r, at + 28),
             palette = pso_u32(r, at + 12);
    if (!pixels || pixels > r.size - at || palette > r.size - at)
      return 0;
    if (!c->gc.approximate && (pso_byte(r, at + 22) || pso_u16(r, at + 26)))
      return 0;
    if (!gc_texture(&c->gc, r, at + pixels, pso_byte(r, at), pso_u16(r, at + 2),
                    pso_u16(r, at + 4), pso_byte(r, at + 24),
                    pso_byte(r, at + 6), pso_byte(r, at + 7),
                    pso_byte(r, at + 20), pso_byte(r, at + 21), at + palette,
                    pso_u16(r, at + 10), pso_byte(r, at + 9)))
      return 0;
  }
  return 1;
}
static unsigned j3d_lookup(PsoReader r, unsigned header, unsigned index,
                           unsigned stride, unsigned size) {
  unsigned table = pso_u32(r, header);
  if (!table || table > r.size || index > (r.size - table) / stride ||
      !pso_range(r, table + index * stride, size)) {
    r.memory->failed = 1;
    return 0;
  }
  return table + index * stride;
}
/* Exact simple diffuse TEV profile. More elaborate console materials require
 * opt-in approximation to the first diffuse texture and material/vertex color.
 */
static int j3d_materials(J3dImport *c) {
  PsoReader r = c->mat;
  unsigned count = pso_u16(r, 8), table = pso_u32(r, 12),
           remap = pso_u32(r, 16);
  if (r.size < 132 || !pso_range(r, remap, count * 2))
    return 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned index = pso_u16(r, remap + i * 2);
    if (index >= count || table > r.size || index > (r.size - table) / 332 ||
        !pso_range(r, table + index * 332, 332))
      return 0;
    unsigned at = table + index * 332, tex = UINT_MAX, textures = 0;
    unsigned material_mode = pso_byte(r, at);
    if (material_mode != 1 && material_mode != 2 && material_mode != 4)
      return 0;
    for (unsigned k = 0; k < 8; k++) {
      unsigned ref = pso_u16(r, at + 132 + k * 2);
      if (ref != 65535) {
        unsigned t = pso_u16(r, j3d_lookup(r, 72, ref, 2, 2));
        if (t >= c->gc.textures)
          return 0;
        if (!textures)
          tex = t;
        textures++;
      }
    }
    float color[4] = {1, 1, 1, 1};
    unsigned color_idx = pso_u16(r, at + 8);
    if (color_idx != 65535) {
      unsigned ca = j3d_lookup(r, 32, color_idx, 4, 4);
      for (unsigned k = 0; k < 4; k++)
        color[k] = pso_byte(r, ca + k) / 255.0f;
    }
    unsigned ch = pso_u16(r, at + 12), lighting = 0;
    if (ch != 65535) {
      unsigned ca = j3d_lookup(r, 40, ch, 8, 8);
      lighting = pso_byte(r, ca);
      c->vertex_color[i] = (unsigned char)pso_byte(r, ca + 1);
      if (c->vertex_color[i] > 1 || lighting > 1)
        return 0;
    }
    if (c->vertex_color[i])
      for (unsigned k = 0; k < 4; k++)
        color[k] = 1;
    unsigned mode = lighting ? 0 : 2;
    unsigned cull = pso_u32(r, j3d_lookup(r, 28, pso_byte(r, at + 1), 4, 4));
    if (cull > 2 || (cull == 1 && !c->gc.approximate))
      return 0;
    unsigned blend = j3d_lookup(r, 112, pso_u16(r, at + 328), 4, 4);
    unsigned bm = pso_byte(r, blend), src = pso_byte(r, blend + 1),
             dst = pso_byte(r, blend + 2);
    if (bm > 1 || src > 7 || dst > 7)
      return 0;
    if (bm)
      mode |= 4 | 16 | src << 5 | dst << 8;
    unsigned alpha = j3d_lookup(r, 108, pso_u16(r, at + 326), 8, 8);
    unsigned ac = pso_byte(r, alpha), ref = pso_byte(r, alpha + 1),
             op = pso_byte(r, alpha + 2), bc = pso_byte(r, alpha + 3);
    float cutoff = 0.5f;
    int trivial_alpha = ac == 7 && bc == 7 && op == 0;
    if (!trivial_alpha) {
      if (!bm && (ac == 4 || ac == 6) && bc == 7 && op == 0) {
        mode |= 1;
        cutoff = (ref + (ac == 4 ? 1.0f : 0.0f)) / 255;
      } else if (!c->gc.approximate)
        return 0;
    }
    if (!c->gc.approximate) {
      unsigned channels = j3d_lookup(r, 36, pso_byte(r, at + 2), 1, 1);
      unsigned generators = j3d_lookup(r, 52, pso_byte(r, at + 3), 1, 1);
      unsigned stage_count = j3d_lookup(r, 88, pso_byte(r, at + 4), 1, 1);
      if (pso_byte(r, channels) != 1 ||
          pso_byte(r, generators) != (textures ? 1u : 0u) ||
          pso_byte(r, stage_count) != 1 || ch == 65535)
        return 0;
      unsigned alpha_channel = pso_u16(r, at + 14);
      if (alpha_channel == 65535)
        return 0;
      unsigned channel = j3d_lookup(r, 40, alpha_channel, 8, 8);
      if (pso_byte(r, channel) ||
          pso_byte(r, channel + 1) != c->vertex_color[i])
        return 0;
      if (textures) {
        unsigned tc_index = pso_u16(r, at + 40);
        if (tc_index == 65535)
          return 0;
        unsigned tc = j3d_lookup(r, 56, tc_index, 4, 4);
        if (pso_byte(r, tc) != 1 || pso_byte(r, tc + 1) != 4 ||
            pso_byte(r, tc + 2) != 60)
          return 0;
      }
      unsigned stages = 0, first_stage = 0;
      for (unsigned k = 0; k < 16; k++) {
        unsigned st = pso_u16(r, at + 228 + k * 2);
        if (st != 65535) {
          if (!stages)
            first_stage = st;
          stages++;
        }
      }
      if (textures > 1 || stages != 1 || lighting)
        return 0;
      unsigned st = j3d_lookup(r, 92, first_stage, 20, 20);
      /* (0 * (1-raster) + texture * raster) + 0. Untextured: raster. */
      const unsigned char textured[4] = {15, 8, 10, 15},
                          plain[4] = {15, 15, 15, 10};
      const unsigned char atex[4] = {7, 4, 5, 7}, aplain[4] = {7, 7, 7, 5};
      if (memcmp(r.data + st + 1, textures ? textured : plain, 4) ||
          memcmp(r.data + st + 10, textures ? atex : aplain, 4) ||
          pso_byte(r, st + 5) || pso_byte(r, st + 6) || pso_byte(r, st + 7) ||
          pso_byte(r, st + 9) || pso_byte(r, st + 14) || pso_byte(r, st + 15) ||
          pso_byte(r, st + 16) || pso_byte(r, st + 18))
        return 0;
      /* Reject texture transforms, indirect stages, alternate UVs and fog. */
      for (unsigned k = 0; k < 10; k++)
        if (pso_u16(r, at + 72 + k * 2) != 65535)
          return 0;
      unsigned indirect = pso_u32(r, 24);
      if (indirect && indirect != pso_u32(r, 20)) {
        if (indirect > r.size || i > (r.size - indirect) / 312 ||
            !pso_range(r, indirect + i * 312, 312) ||
            pso_byte(r, indirect + i * 312))
          return 0;
      }
      unsigned order = j3d_lookup(r, 76, pso_u16(r, at + 188), 4, 4);
      if (pso_byte(r, order + 2) != 4)
        return 0;
      if (textures && (pso_byte(r, order) || pso_byte(r, order + 1)))
        return 0;
      unsigned fog = pso_u16(r, at + 324);
      if (fog != 65535 && pso_byte(r, j3d_lookup(r, 104, fog, 44, 44)))
        return 0;
      unsigned z = j3d_lookup(r, 116, pso_byte(r, at + 6), 4, 4);
      if (!pso_byte(r, z) || pso_byte(r, z + 1) != 3 ||
          (!!pso_byte(r, z + 2) == !!bm))
        return 0;
    }
    floats(&c->gc.section[2], color, 4);
    number(&c->gc.section[2], tex);
    number(&c->gc.section[2], mode);
    scalar(&c->gc.section[2], cutoff);
    number(&c->gc.section[2], cull == 0);
    c->gc.materials++;
  }
  return !c->gc.memory.failed;
}

static float j3d_anim_value(PsoReader r, unsigned table, unsigned count,
                            unsigned index, int rotation) {
  unsigned width = rotation ? 2 : 4;
  if (index >= count || table > r.size || !pso_range(r, table, count * width)) {
    r.memory->failed = 1;
    return 0;
  }
  return rotation ? (float)(int16_t)pso_u16(r, table + index * 2)
                  : pso_float(r, table + index * 4);
}
static int j3d_curve(PsoReader r, unsigned desc, unsigned table,
                     unsigned values, int rotation, float rotation_scale,
                     int full, unsigned duration, GcCurve *curve) {
  unsigned count = pso_u16(r, desc), first = pso_u16(r, desc + 2);
  unsigned tangent = full ? 0 : pso_u16(r, desc + 4);
  if (!count || tangent > 1 || count > 65535)
    return 0;
  curve->key = imp_alloc(r.memory, count * sizeof(GcKey));
  curve->count = count;
  if (!curve->key)
    return 0;
  unsigned stride = full || count == 1 ? 1 : tangent ? 4 : 3;
  if (first > values || count > (values - first) / stride)
    return 0;
  for (unsigned i = 0; i < count; i++) {
    GcKey *key = &curve->key[i];
    unsigned at = first + i * stride;
    key->time = full         ? (float)i
                : count == 1 ? 0
                             : j3d_anim_value(r, table, values, at++, rotation);
    key->value = j3d_anim_value(r, table, values, at++, rotation);
    key->in = key->out = 0;
    if (!full && count > 1) {
      key->in = j3d_anim_value(r, table, values, at++, rotation);
      key->out =
          tangent ? j3d_anim_value(r, table, values, at, rotation) : key->in;
    }
    if (rotation) {
      key->value *= rotation_scale;
      key->in *= rotation_scale;
      key->out *= rotation_scale;
    }
  }
  /* ANF1 full-frame data interpolates between samples, not with zero slopes. */
  if (full && count > 1)
    for (unsigned i = 0; i < count; i++) {
      curve->key[i].in = i ? curve->key[i].value - curve->key[i - 1].value : 0;
      curve->key[i].out =
          i + 1 < count ? curve->key[i + 1].value - curve->key[i].value : 0;
    }
  return gc_curve_valid(curve, (float)duration) && !r.memory->failed;
}
static int j3d_animation(J3dImport *c, PsoReader source, float fps) {
  int full = source.size >= 8 && !memcmp(source.data, "J3D1bca1", 8);
  PsoReader r =
      j3d_chunk(source, full ? "J3D1bca1" : "J3D1bck1", full ? "ANF1" : "ANK1");
  if (!r.data || r.size < 36 || pso_byte(r, 8) > 4)
    return 0;
  unsigned duration = pso_u16(r, 10), count = pso_u16(r, 12),
           exponent = pso_byte(r, 9);
  if (!count || count > c->gc.nodes || (!full && exponent > 15))
    return 0;
  unsigned desc = pso_u32(r, 20), stride = full ? 36 : 54;
  if (!pso_range(r, desc, count * stride))
    return 0;
  float rotation_scale =
      ldexpf(3.14159265358979323846f / 32767, full ? 0 : (int)exponent);
  ImportBuffer tracks = {0};
  tracks.memory = &c->gc.memory;
  number(&tracks, 0);
  GcCurve curves[9] = {{0}};
  int ok = 0;
  for (unsigned node = 0; node < count; node++) {
    for (unsigned axis = 0; axis < 3; axis++)
      for (unsigned prop = 0; prop < 3; prop++) {
        unsigned d = desc + node * stride + (axis * 3 + prop) * (full ? 4 : 6);
        if (!j3d_curve(r, d, pso_u32(r, 24 + prop * 4),
                       pso_u16(r, 14 + prop * 2), prop == 1, rotation_scale,
                       full, duration, &curves[prop * 3 + axis]))
          goto done;
      }
    /* Reject animated nonunit parent scales on Maya SSC joints. */
    if ((pso_u16(c->inf, 8) & 15) == 2) {
      for (unsigned child = 0; child < c->gc.nodes; child++)
        if (c->parents[child] == (int)node &&
            (pso_byte(c->jnt, c->joint_record[child] + 2) & 1))
          for (unsigned axis = 0; axis < 3; axis++)
            for (unsigned key = 0; key < curves[axis].count; key++) {
              GcKey *k = &curves[axis].key[key];
              if (fabsf(k->value - 1) > 1e-6f || k->in || k->out)
                goto done;
            }
    }
    static const unsigned property[3] = {2, 1, 0};
    for (unsigned prop = 0; prop < 3; prop++)
      if (!gc_track(&tracks, curves + prop * 3, node, property[prop],
                    (float)duration, fps, 0))
        goto done;
    gc_curves_free(&c->gc.memory, curves, 9);
  }
  gc_clip(&c->gc, &tracks, duration / fps, count * 3);
  ok = !c->gc.memory.failed;
done:
  gc_curves_free(&c->gc.memory, curves, 9);
  imp_free(&c->gc.memory, tracks.data);
  return ok;
}
static int import_j3d(const char *path, const char *animation_path, float fps,
                      int budget, int approximate, int executor) {
  J3dImport c = {0};
  gc_init(&c.gc, budget, approximate, executor);
  GcAsset model = {0}, animation = {0};
  int token = 0;
  if (!gc_asset(&model, path, &c.gc.memory, 0) || model.view.size < 32)
    goto done;
  const char *magic =
      !memcmp(model.view.data, "J3D2bdl4", 8) ? "J3D2bdl4" : "J3D2bmd3";
  c.inf = j3d_chunk(model.view, magic, "INF1");
  c.vtx = j3d_chunk(model.view, magic, "VTX1");
  c.evp = j3d_chunk(model.view, magic, "EVP1");
  c.drw = j3d_chunk(model.view, magic, "DRW1");
  c.jnt = j3d_chunk(model.view, magic, "JNT1");
  c.shp = j3d_chunk(model.view, magic, "SHP1");
  c.mat = j3d_chunk(model.view, magic, "MAT3");
  c.tex = j3d_chunk(model.view, magic, "TEX1");
  if (!c.inf.data || !c.vtx.data || !c.drw.data || !c.jnt.data || !c.shp.data ||
      !c.mat.data || !j3d_hierarchy(&c) || !j3d_joints(&c) || !j3d_arrays(&c) ||
      !j3d_textures(&c) || !j3d_materials(&c) || !j3d_shapes(&c))
    goto done;
  if (animation_path &&
      (!gc_asset(&animation, animation_path, &c.gc.memory, 1) ||
       !j3d_animation(&c, animation.view, fps)))
    goto done;
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.parents);
  c.parents = NULL;
  imp_free(&c.gc.memory, c.shape_material);
  c.shape_material = NULL;
  imp_free(&c.gc.memory, c.joint_record);
  c.joint_record = NULL;
  imp_free(&c.gc.memory, c.vertex_color);
  c.vertex_color = NULL;
  token = gc_publish(&c.gc, budget);
done:
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.parents);
  imp_free(&c.gc.memory, c.shape_material);
  imp_free(&c.gc.memory, c.joint_record);
  imp_free(&c.gc.memory, c.vertex_color);
  gc_free(&c.gc);
  return token;
}
