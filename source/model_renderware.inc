/* RenderWare stream adapter. Stream headers/scalars are LE, while verified GC
 * native arrays, texture headers and matrix blocks contain BE scalars. All
 * plugins are decoded as data; no pipeline code or source GX lists execute. */
typedef struct {
  PsoReader data;
  unsigned type, version;
} RwChunk;
typedef struct {
  DndPose pose;
  int parent, id;
  char name[64];
} RwFrame;
typedef struct {
  char name[64];
  unsigned ws, wt, filter, alpha;
} RwTexture;
typedef struct {
  GcImport gc;
  RwFrame *frame;
  PsoReader *geometry;
  RwTexture *texture;
  unsigned frames, geometries, bone_count, bone_node[256];
  int bone_id[256];
  int matrix_indices;
} RwImport;
static unsigned rw_u32(PsoReader r, unsigned at) {
  return pso_range(r, at, 4) ? le32(r.data + at) : 0;
}
static unsigned rw_u16(PsoReader r, unsigned at) {
  return pso_range(r, at, 2)
             ? (unsigned)r.data[at] | (unsigned)r.data[at + 1] << 8
             : 0;
}
static float rw_float(PsoReader r, unsigned at) {
  unsigned bits = rw_u32(r, at);
  float f;
  memcpy(&f, &bits, 4);
  if (!isfinite(f))
    r.memory->failed = 1;
  return isfinite(f) ? f : 0;
}
static unsigned rw_version(unsigned stamp) {
  return stamp & 0xffff0000u
             ? (((stamp >> 14 & 0x3ff00) + 0x30000) | (stamp >> 16 & 63))
             : stamp << 8;
}
static int rw_next(PsoReader r, unsigned *cursor, RwChunk *out) {
  if (!pso_range(r, *cursor, 12))
    return 0;
  unsigned n = rw_u32(r, *cursor + 4);
  if (n > r.size - *cursor - 12)
    return 0;
  out->type = rw_u32(r, *cursor);
  out->version = rw_version(rw_u32(r, *cursor + 8));
  out->data = gc_slice(r, *cursor + 12, n);
  *cursor += 12 + n;
  return !r.memory->failed;
}
static RwChunk rw_find(PsoReader r, unsigned type) {
  RwChunk found = {0}, item;
  unsigned at = 0;
  while (at < r.size) {
    if (!rw_next(r, &at, &item)) {
      r.memory->failed = 1;
      return (RwChunk){0};
    }
    if (item.type == type) {
      if (found.data.data) {
        r.memory->failed = 1;
        return (RwChunk){0};
      }
      found = item;
    }
  }
  return found;
}
static int rw_text(PsoReader r, unsigned offset, unsigned size, char out[64]) {
  if (!size || size > r.size || offset > r.size - size)
    return 0;
  unsigned length = 0;
  while (length < size && r.data[offset + length]) {
    unsigned c = r.data[offset + length];
    if (c < 32 || c > 126 || length == 63)
      return 0;
    out[length++] = (char)c;
  }
  if (length == size)
    return 0;
  out[length] = 0;
  return 1;
}
static int rw_sampler(unsigned flags, unsigned *s, unsigned *t, unsigned *min,
                      unsigned *mag) {
  unsigned filter = flags & 255, ws = flags >> 8 & 15, wt = flags >> 12 & 15;
  /* RW repeat=1, mirror=2, clamp=3. Native encoding has clamp=0. */
  if (!filter || filter > 6 || ws < 1 || ws > 3 || wt < 1 || wt > 3)
    return 0;
  *s = ws == 3 ? 0 : ws;
  *t = wt == 3 ? 0 : wt;
  static const unsigned filters[6] = {0, 1, 2, 3, 4, 5};
  *min = filters[filter - 1];
  *mag = (filter == 1 || filter == 3 || filter == 5) ? 0 : 1;
  return 1;
}
static int rw_txd(RwImport *c, PsoReader source) {
  unsigned cursor = 0;
  RwChunk dictionary;
  if (!rw_next(source, &cursor, &dictionary) || cursor != source.size ||
      dictionary.type != 0x16)
    return 0;
  RwChunk header = rw_find(dictionary.data, 1);
  if (!header.data.data || header.data.size < 4)
    return 0;
  unsigned count = rw_u16(header.data, 0);
  if (!count || count > 4096)
    return 0;
  c->texture = imp_alloc(&c->gc.memory, count * sizeof(*c->texture));
  if (!c->texture)
    return 0;
  cursor = 0;
  RwChunk entry;
  while (cursor < dictionary.data.size) {
    if (!rw_next(dictionary.data, &cursor, &entry))
      return 0;
    if (entry.type != 0x15)
      continue;
    if (c->gc.textures >= count)
      return 0;
    RwChunk image = rw_find(entry.data, 1);
    PsoReader r = image.data;
    if (!r.data || r.size < 88 || pso_u32(r, 0) != 6)
      return 0;
    unsigned names = image.version >= 0x33000 ? 24 : 8, at = names + 64;
    if (!pso_range(r, at, 16))
      return 0;
    RwTexture *texture = &c->texture[c->gc.textures];
    if (!rw_text(r, names, 32, texture->name) || !*texture->name)
      return 0;
    for (unsigned i = 0; i < c->gc.textures; i++)
      if (!strcmp(c->texture[i].name, texture->name))
        return 0;
    unsigned packed = pso_u32(r, 4), filter = packed & 255;
    /* GC serializes the two RW address nibbles opposite the LE texture word. */
    packed = (packed & 255) | (packed >> 4 & 0xf00) | (packed << 4 & 0xf000);
    unsigned ws, wt, min, mag;
    if (!rw_sampler(packed, &ws, &wt, &min, &mag))
      return 0;
    texture->ws = ws;
    texture->wt = wt;
    texture->filter = filter;
    unsigned raster = pso_u32(r, at), w, h, levels, format, pf = 0;
    if (image.version >= 0x33002) {
      texture->alpha = pso_u32(r, at + 12) != 0;
      w = pso_u16(r, at + 4);
      h = pso_u16(r, at + 6);
      levels = pso_byte(r, at + 9);
      format = pso_byte(r, at + 10);
      pf = pso_byte(r, at + 11);
    } else {
      texture->alpha = pso_u32(r, at + 4) != 0;
      w = pso_u16(r, at + 8);
      h = pso_u16(r, at + 10);
      levels = pso_byte(r, at + 13);
      unsigned raster_type = raster >> 8 & 15, palette_type = raster & 0x6000;
      if (pso_byte(r, at + 15))
        format = 14;
      else if (palette_type) {
        format = palette_type == 0x2000 ? 9 : 8;
        pf = raster_type == 2 ? 1 : 2;
      } else if (raster_type == 2)
        format = 4;
      else if (raster_type == 3)
        format = 5;
      else if (raster_type == 5 || raster_type == 6)
        format = 6;
      else
        return 0;
    }
    at += 16;
    unsigned palette = at, palette_count = format == 8   ? 16
                                           : format == 9 ? 256
                                                         : 0;
    if (palette_count) {
      if (pf > 2 || !pso_range(r, at, palette_count * 2))
        return 0;
      at += palette_count * 2;
    }
    if (!pso_range(r, at, 4))
      return 0;
    unsigned size = pso_u32(r, at);
    at += 4;
    if (!pso_range(r, at, size))
      return 0;
    PsoReader bounded = gc_slice(r, 0, at + size);
    if (!gc_texture(&c->gc, bounded, at, format, w, h, levels, ws, wt, min, mag,
                    palette, palette_count, pf))
      return 0;
  }
  return c->gc.textures == count && !c->gc.memory.failed;
}
static int rw_matrix_pose(PsoReader r, unsigned at, DndPose *pose) {
  ufbx_matrix matrix;
  for (unsigned col = 0; col < 4; col++)
    for (unsigned row = 0; row < 3; row++)
      matrix.cols[col].v[row] = rw_float(r, at + (col * 3 + row) * 4);
  ufbx_transform trs = ufbx_matrix_to_transform(&matrix);
  ufbx_matrix rebuilt = ufbx_transform_to_matrix(&trs);
  for (unsigned col = 0; col < 4; col++)
    for (unsigned row = 0; row < 3; row++) {
      double a = matrix.cols[col].v[row], b = rebuilt.cols[col].v[row];
      if (!isfinite(a) || !isfinite(b) || fabs(a - b) > 0.0001 * (1 + fabs(a)))
        return 0;
    }
  for (unsigned k = 0; k < 3; k++) {
    pose->t[k] = (float)trs.translation.v[k];
    pose->s[k] = (float)trs.scale.v[k];
    if (fabsf(pose->s[k]) < 1e-8f)
      return 0;
  }
  for (unsigned k = 0; k < 4; k++)
    pose->q[k] = (float)trs.rotation.v[k];
  return !r.memory->failed;
}
static int rw_frames(RwImport *c, RwChunk list) {
  RwChunk data = rw_find(list.data, 1);
  PsoReader r = data.data;
  if (!r.data || r.size < 4)
    return 0;
  c->frames = rw_u32(r, 0);
  if (!c->frames || c->frames > 1024 || !pso_range(r, 4, c->frames * 56))
    return 0;
  c->frame = imp_alloc(&c->gc.memory, c->frames * sizeof(*c->frame));
  if (!c->frame)
    return 0;
  for (unsigned i = 0; i < c->frames; i++) {
    RwFrame *f = &c->frame[i];
    unsigned at = 4 + i * 56;
    f->parent = (int)rw_u32(r, at + 48);
    f->id = -1;
    if ((f->parent != -1 && (f->parent < 0 || f->parent >= (int)i)) ||
        !rw_matrix_pose(r, at, &f->pose))
      return 0;
    snprintf(f->name, sizeof(f->name), "RW_Frame_%u", i);
  }
  unsigned cursor = 0, index = 0;
  RwChunk chunk;
  while (cursor < list.data.size) {
    if (!rw_next(list.data, &cursor, &chunk))
      return 0;
    if (chunk.type != 3)
      continue;
    if (index >= c->frames)
      return 0;
    RwChunk h = rw_find(chunk.data, 0x11e), n = rw_find(chunk.data, 0x253f2fe);
    if (n.data.data) {
      unsigned len = n.data.size < 63 ? n.data.size : 63;
      for (unsigned k = 0; k < len; k++) {
        unsigned b = n.data.data[k];
        if (!b)
          break;
        if (b < 32 || b > 126)
          return 0;
        c->frame[index].name[k] = (char)b;
        c->frame[index].name[k + 1] = 0;
      }
    }
    if (h.data.data) {
      if (h.data.size < 12 || rw_u32(h.data, 0) != 0x100)
        return 0;
      c->frame[index].id = (int)rw_u32(h.data, 4);
      unsigned count = rw_u32(h.data, 8);
      if (count) {
        if (count > 256 || c->bone_count || !pso_range(h.data, 20, count * 12))
          return 0;
        c->bone_count = count;
        unsigned char used[256] = {0};
        for (unsigned i = 0; i < count; i++) {
          unsigned bone = rw_u32(h.data, 24 + i * 12);
          if (bone >= count || used[bone])
            return 0;
          used[bone] = 1;
          c->bone_id[bone] = (int)rw_u32(h.data, 20 + i * 12);
        }
      }
    }
    index++;
  }
  if (index != c->frames)
    return 0;
  for (unsigned i = 0; i < c->bone_count; i++) {
    unsigned found = 0;
    for (unsigned j = 0; j < c->frames; j++)
      if (c->frame[j].id == c->bone_id[i]) {
        c->bone_node[i] = j;
        found++;
      }
    if (found != 1)
      return 0;
  }
  for (unsigned i = 0; i < c->frames; i++) {
    RwFrame *f = &c->frame[i];
    number(&c->gc.section[0], (unsigned)f->parent);
    name(&c->gc.section[0], f->name, "Frame");
    floats(&c->gc.section[0], f->pose.t, 3);
    floats(&c->gc.section[0], f->pose.q, 4);
    floats(&c->gc.section[0], f->pose.s, 3);
  }
  c->gc.nodes = c->frames;
  return !c->gc.memory.failed;
}

static int rw_material(RwImport *c, RwChunk chunk, unsigned flags) {
  RwChunk h = rw_find(chunk.data, 1), texture = rw_find(chunk.data, 6),
          extension = rw_find(chunk.data, 3);
  PsoReader r = h.data;
  if (!r.data || r.size < 16 || c->gc.materials >= 4097)
    return 0;
  if (!c->gc.approximate && extension.data.data &&
      rw_find(extension.data, 0x120).data.data)
    return 0;
  float color[4];
  for (unsigned k = 0; k < 4; k++)
    color[k] = pso_byte(r, 4 + k) / 255.0f;
  if (!(flags & 0x40))
    for (unsigned k = 0; k < 4; k++)
      color[k] = 1;
  int index = -1;
  if (rw_u32(r, 12)) {
    if (!texture.data.data)
      return 0;
    RwChunk sampler = rw_find(texture.data, 1), item;
    unsigned at = 0, strings = 0;
    char name_text[64] = {0};
    if (!sampler.data.data || sampler.data.size < 4)
      return 0;
    while (at < texture.data.size) {
      if (!rw_next(texture.data, &at, &item))
        return 0;
      if (item.type == 2 && strings++ == 0 &&
          !rw_text(item.data, 0, item.data.size, name_text))
        return 0;
    }
    unsigned ws, wt, min, mag, packed = rw_u32(sampler.data, 0);
    if (!*name_text || !rw_sampler(packed, &ws, &wt, &min, &mag))
      return 0;
    for (unsigned i = 0; i < c->gc.textures; i++)
      if (!strcmp(c->texture[i].name, name_text))
        index = (int)i;
    if (index < 0)
      return 0;
    if (!c->gc.approximate &&
        (ws != c->texture[index].ws || wt != c->texture[index].wt ||
         (packed & 255) != c->texture[index].filter))
      return 0;
  }
  unsigned mode = flags & 0x20 ? 0 : 2;
  if (color[3] < 1 || (index >= 0 && c->texture[index].alpha))
    mode |= 4;
  floats(&c->gc.section[2], color, 4);
  number(&c->gc.section[2], (unsigned)index);
  number(&c->gc.section[2], mode);
  scalar(&c->gc.section[2], .5f);
  number(&c->gc.section[2], 0);
  c->gc.materials++;
  return !c->gc.memory.failed;
}
static int rw_materials(RwImport *c, PsoReader geometry, unsigned flags,
                        unsigned **map, unsigned *count) {
  RwChunk list = rw_find(geometry, 8), h = rw_find(list.data, 1);
  if (!h.data.data || h.data.size < 4)
    return 0;
  unsigned n = rw_u32(h.data, 0);
  if (!n || n > 4096 || !pso_range(h.data, 4, n * 4))
    return 0;
  *map = imp_alloc(&c->gc.memory, n * sizeof(unsigned));
  *count = n;
  if (!*map)
    return 0;
  unsigned at = 0, next = 0;
  RwChunk item;
  for (unsigned i = 0; i < n; i++) {
    int ref = (int)rw_u32(h.data, 4 + i * 4);
    if (ref != -1) {
      if (ref < 0 || ref >= (int)i)
        return 0;
      (*map)[i] = (*map)[ref];
      continue;
    }
    int found = 0;
    while (at < list.data.size) {
      if (!rw_next(list.data, &at, &item))
        return 0;
      if (item.type == 7) {
        found = 1;
        break;
      }
    }
    if (!found)
      return 0;
    (*map)[i] = c->gc.materials;
    if (!rw_material(c, item, flags))
      return 0;
    next++;
  }
  while (at < list.data.size) {
    if (!rw_next(list.data, &at, &item) || item.type == 7)
      return 0;
  }
  return next != 0 && !c->gc.memory.failed;
}
typedef struct {
  PsoReader data;
  unsigned bones, used, influence, indices, weights, matrices, matrix_stride;
  int native;
} RwSkin;
static int rw_skin(RwImport *c, PsoReader extension, unsigned vertices,
                   int native, RwSkin *out) {
  RwChunk skin = rw_find(extension, 0x116);
  if (!skin.data.data)
    return 1;
  PsoReader r = skin.data;
  if (native) {
    if (r.size < 20 || rw_u32(r, 0) != 1 || rw_u32(r, 12) != 6)
      return 0;
    r = gc_slice(r, 16, r.size - 16);
  }
  if (r.size < 4)
    return 0;
  out->bones = pso_byte(r, 0);
  out->used = pso_byte(r, 1);
  out->influence = pso_byte(r, 2);
  if (!out->bones || out->bones > c->bone_count || out->used > out->bones ||
      (out->used && (!out->influence || out->influence > 4)))
    return 0;
  if (native && !out->used)
    return 0;
  unsigned at = 4 + out->used;
  if (!pso_range(r, 4, out->used))
    return 0;
  for (unsigned i = 0; i < out->used; i++)
    if (pso_byte(r, 4 + i) >= out->bones)
      return 0;
  out->indices = at;
  out->native = native;
  if (native && out->used == 1)
    out->weights = at;
  else {
    unsigned n = native ? out->influence : 4;
    if (!pso_range(r, at, vertices * n))
      return 0;
    at += vertices * n;
    out->weights = at;
    unsigned width = native ? 1 : 4;
    if (!pso_range(r, at, vertices * n * width))
      return 0;
    at += vertices * n * width;
  }
  out->matrices = at;
  out->matrix_stride = !native && !out->used ? 68 : 64;
  if (!pso_range(r, at, out->bones * out->matrix_stride))
    return 0;
  out->data = r;
  return !r.memory->failed;
}
static int rw_weights(RwImport *c, const RwSkin *s, unsigned vertex,
                      unsigned frame, GcSkin *skin, GcVertex *v) {
  if (!s->data.data) {
    float identity[16];
    dnd_model_identity(identity);
    int slot = gc_skin_joint(skin, frame, 0, identity);
    if (slot < 0)
      return 0;
    v->joints[0] = (unsigned)slot;
    v->weights[0] = 1;
    return 1;
  }
  unsigned n = s->native ? s->used == 1 ? 1 : s->influence : 4;
  float total = 0;
  for (unsigned i = 0; i < n; i++) {
    unsigned bone = s->native && s->used == 1
                        ? pso_byte(s->data, 4)
                        : pso_byte(s->data, s->indices + vertex * n + i);
    float w = s->native ? s->used == 1
                              ? 1
                              : pso_byte(s->data, s->weights + vertex * n + i) /
                                    128.0f
                        : rw_float(s->data, s->weights + (vertex * 4 + i) * 4);
    if (w < 0 || w > 1 || bone >= s->bones)
      return 0;
    float matrix[16];
    unsigned at = s->matrices + bone * s->matrix_stride +
                  (s->matrix_stride == 68 ? 4 : 0);
    for (unsigned k = 0; k < 16; k++)
      matrix[k] = s->native ? pso_float(s->data, at + k * 4)
                            : rw_float(s->data, at + k * 4);
    matrix[3] = matrix[7] = matrix[11] = 0;
    matrix[15] = 1;
    int slot = gc_skin_joint(skin, c->bone_node[bone], 1, matrix);
    if (slot < 0)
      return 0;
    v->joints[i] = (unsigned)slot;
    v->weights[i] = w;
    total += w;
  }
  return fabsf(total - 1) < .001f && !c->gc.memory.failed;
}
static int rw_native_geometry(RwImport *c, PsoReader extension,
                              const RwSkin *skin_data, unsigned vertices,
                              unsigned frame, unsigned *materials,
                              unsigned material_count) {
  RwChunk plugin = rw_find(extension, 0x510), split = rw_find(extension, 0x50e);
  PsoReader s = split.data, wrapper = plugin.data;
  if (!s.data || s.size < 12 || !wrapper.data || wrapper.size < 36 ||
      rw_u32(wrapper, 0) != 1 || rw_u32(wrapper, 12) != 6)
    return 0;
  unsigned splits = rw_u32(s, 4);
  if (!splits || splits > 1024 || s.size != 12 + splits * 8 || rw_u32(s, 0) > 1)
    return 0;
  PsoReader r = gc_slice(wrapper, 16, wrapper.size - 16);
  unsigned header = rw_u32(r, 0), size = rw_u32(r, 4),
           declarations = pso_u32(r, 16);
  if (header < 12 + splits * 8 || !declarations || declarations > 13 ||
      header > r.size - 8 || size > r.size - 8 - header)
    return 0;
  unsigned remain = header - 12 - splits * 8;
  if (remain % declarations || remain / declarations < 8 ||
      remain / declarations > 32)
    return 0;
  unsigned decl_stride = remain / declarations, seen = 0;
  J3dArray array[26] = {{0}};
  unsigned attrs[13], types[13];
  PsoReader data = gc_slice(r, 8 + header, size);
  for (unsigned i = 0; i < declarations; i++) {
    unsigned at = 20 + i * decl_stride, attr = pso_byte(r, at + 4),
             stride = pso_byte(r, at + 5), type = pso_byte(r, at + 6);
    if (!(attr == 9 || attr == 10 || attr == 11 || attr == 13 || attr == 14) ||
        (seen & (1u << attr)) || (type != 2 && type != 3))
      return 0;
    unsigned width = attr == 11 ? 4 : attr >= 13 ? 8 : 12;
    if (stride < width || stride > 64)
      return 0;
    seen |= 1u << attr;
    attrs[i] = attr;
    types[i] = type;
    array[attr].offset = pso_u32(r, at);
    array[attr].end = size;
    array[attr].stride = stride;
    array[attr].type = attr == 11 ? 5 : 4;
    array[attr].components = attr == 11 ? 4 : attr >= 13 ? 2 : 3;
    if (array[attr].offset >= size)
      return 0;
    for (unsigned j = 0; j < declarations; j++) {
      unsigned next = pso_u32(r, 20 + j * decl_stride);
      if (next > array[attr].offset && next < array[attr].end)
        array[attr].end = next;
    }
    if (attr == 9 && vertices > (array[attr].end - array[attr].offset) / stride)
      return 0;
  }
  if (!(seen & (1u << 9)))
    return 0;
  unsigned split_table = 20 + declarations * decl_stride;
  for (unsigned i = 0; i < splits; i++) {
    unsigned mat = rw_u32(s, 16 + i * 8),
             offset = pso_u32(r, split_table + i * 8),
             length = pso_u32(r, split_table + i * 8 + 4);
    if (mat >= material_count || !pso_range(data, offset, length))
      return 0;
    PsoReader dl = gc_slice(data, offset, length);
    ImportBuffer v = {0}, ix = {0};
    v.memory = ix.memory = &c->gc.memory;
    GcSkin *skin = imp_alloc(&c->gc.memory, sizeof(GcSkin));
    int ok = 0;
    if (!skin)
      goto done;
    unsigned at = 0;
    while (at < dl.size) {
      unsigned command = pso_byte(dl, at++);
      if (!command)
        continue;
      if ((command & 7) || !pso_range(dl, at, 2))
        goto done;
      unsigned count = pso_u16(dl, at), first = v.size / 80;
      at += 2;
      if (!count || first + count > 262144)
        goto done;
      for (unsigned k = 0; k < count; k++) {
        GcVertex vertex = {0};
        for (unsigned j = 0; j < 4; j++)
          vertex.color[j] = 1;
        if (c->matrix_indices) {
          unsigned matrix = pso_byte(dl, at++);
          if (matrix % 3 || matrix > 27)
            goto done;
        }
        unsigned source_vertex = UINT_MAX, ignored = 0;
        for (unsigned a = 0; a < declarations; a++) {
          if (attrs[a] == 9)
            source_vertex = types[a] == 2 ? pso_byte(dl, at) : pso_u16(dl, at);
          if (!gc_attribute(data, array, c->gc.approximate, dl, &at, attrs[a],
                            types[a], &vertex, &ignored))
            goto done;
        }
        if (source_vertex >= vertices ||
            !rw_weights(c, skin_data, source_vertex, frame, skin, &vertex))
          goto done;
        gc_emit(&v, &vertex);
      }
      if (!fz_primitive(&ix, first, count, command, 0))
        goto done;
    }
    ok = gc_mesh(&c->gc, &v, &ix, skin, materials[mat],
                 (seen & (1u << 10)) != 0);
  done:
    imp_free(&c->gc.memory, skin);
    imp_free(&c->gc.memory, v.data);
    imp_free(&c->gc.memory, ix.data);
    if (!ok)
      return 0;
  }
  return !c->gc.memory.failed;
}
static int rw_geometry(RwImport *c, PsoReader geometry, unsigned frame) {
  RwChunk h = rw_find(geometry, 1), extension = rw_find(geometry, 3);
  PsoReader r = h.data;
  if (!r.data || r.size < 16 || !extension.data.data)
    return 0;
  unsigned flags = rw_u32(r, 0), triangles = rw_u32(r, 4),
           vertices = rw_u32(r, 8), morphs = rw_u32(r, 12);
  if (!vertices || vertices > 262144 || triangles > 262144 || morphs != 1)
    return 0;
  int native = (flags & 0x01000000) != 0;
  unsigned *map = NULL, material_count = 0;
  int ok = 0;
  if (!rw_materials(c, geometry, flags, &map, &material_count))
    goto done;
  RwSkin weights = {0};
  if (!rw_skin(c, extension.data, vertices, native, &weights))
    goto done;
  if (native) {
    ok = rw_native_geometry(c, extension.data, &weights, vertices, frame, map,
                            material_count);
    goto done;
  }
  unsigned at = h.version < 0x34000 ? 28 : 16, colors = at, uvs, faces,
           positions, normals;
  if (flags & 8) {
    if (!pso_range(r, at, vertices * 4))
      goto done;
    at += vertices * 4;
  }
  unsigned layers = flags >> 16 & 255;
  if (!layers)
    layers = flags & 0x80 ? 2 : flags & 4 ? 1 : 0;
  if (layers > 8 || !pso_range(r, at, vertices * layers * 8))
    goto done;
  uvs = at;
  at += vertices * layers * 8;
  faces = at;
  if (!pso_range(r, at, triangles * 8))
    goto done;
  at += triangles * 8;
  if (!triangles || !pso_range(r, at, 24))
    goto done;
  unsigned has_positions = rw_u32(r, at + 16), has_normals = rw_u32(r, at + 20);
  at += 24;
  if (has_positions != 1 || has_normals > 1)
    goto done;
  positions = at;
  if (!pso_range(r, at, vertices * 12))
    goto done;
  at += vertices * 12;
  normals = at;
  if (has_normals && !pso_range(r, at, vertices * 12))
    goto done;
  for (unsigned material = 0; material < material_count; material++) {
    ImportBuffer v = {0}, ix = {0};
    v.memory = ix.memory = &c->gc.memory;
    GcSkin *skin = imp_alloc(&c->gc.memory, sizeof(GcSkin));
    int valid = 0;
    if (!skin)
      goto mesh_done;
    for (unsigned triangle = 0; triangle < triangles; triangle++) {
      unsigned f = faces + triangle * 8, mat = rw_u16(r, f + 4);
      if (mat >= material_count)
        goto mesh_done;
      if (mat != material)
        continue;
      unsigned ids[3] = {rw_u16(r, f + 2), rw_u16(r, f), rw_u16(r, f + 6)},
               first = v.size / 80;
      for (unsigned k = 0; k < 3; k++) {
        unsigned index = ids[k];
        GcVertex vertex = {0};
        if (index >= vertices || v.size / 80 >= 262144)
          goto mesh_done;
        for (unsigned j = 0; j < 3; j++) {
          vertex.p[j] = rw_float(r, positions + index * 12 + j * 4);
          if (has_normals)
            vertex.n[j] = rw_float(r, normals + index * 12 + j * 4);
        }
        for (unsigned j = 0; j < 4; j++)
          vertex.color[j] =
              flags & 8 ? pso_byte(r, colors + index * 4 + j) / 255.0f : 1;
        if (layers)
          for (unsigned j = 0; j < 2; j++)
            vertex.uv[j] = rw_float(r, uvs + index * 8 + j * 4);
        if (!rw_weights(c, &weights, index, frame, skin, &vertex))
          goto mesh_done;
        gc_emit(&v, &vertex);
      }
      if (!fz_primitive(&ix, first, 3, 0x90, 0))
        goto mesh_done;
    }
    valid = !v.size ||
            gc_mesh(&c->gc, &v, &ix, skin, map[material], (int)has_normals);
  mesh_done:
    imp_free(&c->gc.memory, skin);
    imp_free(&c->gc.memory, v.data);
    imp_free(&c->gc.memory, ix.data);
    if (!valid)
      goto done;
  }
  ok = !c->gc.memory.failed;
done:
  imp_free(&c->gc.memory, map);
  return ok;
}

static int rw_animation(RwImport *c, PsoReader source) {
  RwChunk animation = rw_find(source, 0x1b);
  PsoReader r = animation.data;
  if (!r.data || r.size < 20 || rw_u32(r, 0) != 0x100 || rw_u32(r, 4) != 1 ||
      !c->bone_count)
    return 0;
  unsigned count = rw_u32(r, 8);
  float duration = rw_float(r, 16);
  if (count < c->bone_count || count > 262144 || duration < 0 ||
      !pso_range(r, 20, count * 36))
    return 0;
  unsigned *owner = imp_alloc(&c->gc.memory, count * sizeof(unsigned));
  unsigned counts[256] = {0}, last[256] = {0};
  ImportBuffer tracks = {0};
  tracks.memory = &c->gc.memory;
  number(&tracks, 0);
  int ok = 0;
  if (!owner)
    goto done;
  for (unsigned i = 0; i < count; i++) {
    unsigned at = 20 + i * 36, previous = rw_u32(r, at + 32), bone;
    float time = rw_float(r, at);
    if (time < 0 || time > duration)
      goto done;
    if (i < c->bone_count) {
      if (time != 0)
        goto done;
      bone = i;
    } else {
      if (previous % 36 || previous / 36 >= i)
        goto done;
      bone = owner[previous / 36];
      if (last[bone] != previous / 36 ||
          time <= rw_float(r, 20 + last[bone] * 36))
        goto done;
    }
    owner[i] = bone;
    last[bone] = i;
    counts[bone]++;
    float length = 0;
    for (unsigned k = 0; k < 4; k++) {
      float q = rw_float(r, at + 4 + k * 4);
      length += q * q;
    }
    if (!isfinite(length) || length < 1e-12f)
      goto done;
    for (unsigned k = 0; k < 3; k++)
      (void)rw_float(r, at + 20 + k * 4);
  }
  for (unsigned bone = 0; bone < c->bone_count; bone++) {
    unsigned previous = rw_u32(r, 20 + bone * 36 + 32);
    if (previous != UINT_MAX && previous != 0xff30c9d8 &&
        (previous % 36 || previous / 36 >= count ||
         owner[previous / 36] != bone || previous / 36 != last[bone]))
      goto done;
    for (unsigned property = 0; property < 2; property++) {
      number(&tracks, c->bone_node[bone]);
      number(&tracks, property);
      number(&tracks, 1);
      number(&tracks, counts[bone]);
      for (unsigned i = 0; i < count; i++)
        if (owner[i] == bone) {
          unsigned at = 20 + i * 36;
          scalar(&tracks, rw_float(r, at));
          if (property) {
            float q[4], norm = 0;
            for (unsigned k = 0; k < 4; k++) {
              q[k] = rw_float(r, at + 4 + k * 4);
              norm += q[k] * q[k];
            }
            norm = sqrtf(norm);
            for (unsigned k = 0; k < 4; k++)
              q[k] /= norm;
            floats(&tracks, q, 4);
          } else
            for (unsigned k = 0; k < 3; k++)
              scalar(&tracks, rw_float(r, at + 20 + k * 4));
        }
    }
  }
  gc_clip(&c->gc, &tracks, duration, c->bone_count * 2);
  ok = !c->gc.memory.failed;
done:
  imp_free(&c->gc.memory, owner);
  imp_free(&c->gc.memory, tracks.data);
  return ok;
}
static int rw_clump(RwImport *c, PsoReader source) {
  RwChunk clump = rw_find(source, 0x10), frames = rw_find(clump.data, 0xe),
          list = rw_find(clump.data, 0x1a);
  if (!clump.data.data || !frames.data.data || !list.data.data ||
      !rw_frames(c, frames))
    return 0;
  RwChunk h = rw_find(list.data, 1);
  unsigned at = 0;
  RwChunk item;
  if (!h.data.data || h.data.size < 4)
    return 0;
  c->geometries = rw_u32(h.data, 0);
  if (!c->geometries || c->geometries > 4096)
    return 0;
  c->geometry = imp_alloc(&c->gc.memory, c->geometries * sizeof(PsoReader));
  if (!c->geometry)
    return 0;
  unsigned n = 0;
  while (at < list.data.size) {
    if (!rw_next(list.data, &at, &item))
      return 0;
    if (item.type == 0xf) {
      if (n == c->geometries)
        return 0;
      c->geometry[n++] = item.data;
    }
  }
  if (n != c->geometries)
    return 0;
  at = 0;
  unsigned atomics = 0;
  while (at < clump.data.size) {
    if (!rw_next(clump.data, &at, &item))
      return 0;
    if (item.type != 0x14)
      continue;
    RwChunk atomic = rw_find(item.data, 1);
    if (!atomic.data.data || atomic.data.size < 16)
      return 0;
    unsigned frame = rw_u32(atomic.data, 0), geometry = rw_u32(atomic.data, 4);
    if (frame >= c->frames || geometry >= c->geometries || ++atomics > 1024)
      return 0;
    if (!rw_geometry(c, c->geometry[geometry], frame))
      return 0;
  }
  RwChunk header = rw_find(clump.data, 1);
  return header.data.data && header.data.size >= 4 &&
         rw_u32(header.data, 0) == atomics && atomics && !c->gc.memory.failed;
}
static int import_renderware(const char *path, const char *texture_path,
                             const char *animation_path, int budget,
                             int options, int executor) {
  RwImport c = {0};
  gc_init(&c.gc, budget, options & 1, executor);
  c.matrix_indices = (options & 2) != 0;
  GcAsset model = {0}, textures = {0}, animation = {0};
  int token = 0;
  unsigned size;
  model.file = gc_file(path, &c.gc.memory, &size);
  if (!model.file)
    goto done;
  model.view = (PsoReader){model.file, size, &c.gc.memory};
  if (texture_path) {
    textures.file = gc_file(texture_path, &c.gc.memory, &size);
    if (!textures.file ||
        !rw_txd(&c, (PsoReader){textures.file, size, &c.gc.memory}))
      goto done;
  }
  if (!rw_clump(&c, model.view))
    goto done;
  if (animation_path) {
    animation.file = gc_file(animation_path, &c.gc.memory, &size);
    if (!animation.file ||
        !rw_animation(&c, (PsoReader){animation.file, size, &c.gc.memory}))
      goto done;
  }
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&textures, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.frame);
  c.frame = NULL;
  imp_free(&c.gc.memory, c.geometry);
  c.geometry = NULL;
  imp_free(&c.gc.memory, c.texture);
  c.texture = NULL;
  token = gc_publish(&c.gc, budget);
done:
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&textures, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.frame);
  imp_free(&c.gc.memory, c.geometry);
  imp_free(&c.gc.memory, c.texture);
  gc_free(&c.gc);
  return token;
}
