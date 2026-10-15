/* Original GameCube RE4 BIN/TPL/FCV adapter. Independently implemented from
 * documented file layouts and verified game reader field accesses. */
typedef struct {
  GcImport gc;
  PsoReader model;
  float (*world)[3];
  unsigned parts, flags, vertices, normals, position, normal, texcoord, color,
      weights;
  unsigned weight_count, weight_stride, shift;
  int rigid;
} Re4Import;

/* A DRS/DAS outer record table is distinguished from a raw BIN by its first
 * body record at 0x20. Compressed yz2 bodies deliberately fail the body check.
 * #N is a zero-based entry index; #TAG selects a unique entry of that tag. */
static int re4_member(PsoReader source, const char *selector, const char *tag,
                      PsoReader *out) {
  PsoReader r = source;
  if (r.size >= 0x400 && pso_u32(r, 0x20) == 0 && pso_u32(r, 0x28) == 0 &&
      pso_u32(r, 0x2c) == 0x400) {
    unsigned size = pso_u32(r, 0x24);
    if (!pso_range(r, 0x400, size) || pso_u32(r, 0x60) != UINT_MAX)
      return 0;
    r = gc_slice(r, 0x400, size);
  }
  if (r.size < 32 || pso_u32(r, 8) || pso_u32(r, 12))
    return 0;
  unsigned count = pso_u32(r, 0), end = pso_u32(r, 4), selected = UINT_MAX;
  if (!count || count > 4096 || !pso_range(r, 16, count * 8))
    return 0;
  if (!end)
    end = r.size;
  if (end > r.size || end < 16 + count * 8)
    return 0;
  int numeric = selector && *selector;
  unsigned index = 0;
  if (numeric)
    for (const char *p = selector; *p; p++) {
      if (*p < '0' || *p > '9') {
        numeric = 0;
        break;
      }
      if (index > 4096)
        return 0;
      index = index * 10 + (unsigned)(*p - '0');
    }
  unsigned found = 0, previous = (16 + count * 8 + 31) & ~31u;
  for (unsigned i = 0; i < count; i++) {
    unsigned at = pso_u32(r, 16 + i * 4),
             next = i + 1 < count ? pso_u32(r, 20 + i * 4) : end;
    if (at < previous || at % 32 || next < at || next > end)
      return 0;
    previous = next;
    char entry_tag[5] = {0};
    memcpy(entry_tag, r.data + 16 + count * 4 + i * 4, 4);
    int match =
        numeric ? i == index : !strcmp(entry_tag, selector ? selector : tag);
    if (match) {
      if (strcmp(entry_tag, tag) || at == next)
        return 0;
      selected = i;
      *out = gc_slice(r, at, next - at);
      found++;
    }
  }
  return selected != UINT_MAX && found == 1 && !r.memory->failed;
}
static int re4_asset(GcAsset *a, const char *path, ImportMemory *m,
                     const char *tag) {
  char filename[1024];
  if (strlen(path) >= sizeof(filename))
    return 0;
  strcpy(filename, path);
  char *selector = strchr(filename, '#');
  if (selector) {
    *selector++ = 0;
    if (!*selector || strchr(selector, '#'))
      return 0;
  }
  unsigned size;
  a->file = gc_file(filename, m, &size);
  if (!a->file)
    return 0;
  a->view = (PsoReader){a->file, size, m};
  int container = size >= 32 && pso_u32(a->view, 8) == 0 &&
                  pso_u32(a->view, 12) == 0 && pso_u32(a->view, 0) > 0 &&
                  pso_u32(a->view, 0) <= 4096;
  container |= size >= 0x400 && pso_u32(a->view, 0x20) == 0 &&
               pso_u32(a->view, 0x2c) == 0x400;
  if (container || selector)
    return re4_member(a->view, selector, tag, &a->view);
  return 1;
}
static int re4_joints(Re4Import *c) {
  PsoReader r = c->model;
  if (r.size < 64)
    return 0;
  unsigned version = pso_u32(r, 60), head = pso_u32(r, 0);
  if (version != 0x20010801 && version != 0x20030817 && version != 0x20030818)
    return 0;
  if (version == 0x20030818 && r.size < 72)
    return 0;
  c->parts = pso_byte(r, 25);
  if (!c->parts || !pso_range(r, head, c->parts * 16))
    return 0;
  c->world = imp_alloc(&c->gc.memory, c->parts * sizeof(*c->world));
  if (!c->world)
    return 0;
  float identity[10] = {0, 0, 0, 0, 0, 0, 1, 1, 1, 1};
  number(&c->gc.section[0], UINT_MAX);
  name(&c->gc.section[0], "RE4_Model", "RE4_Model");
  floats(&c->gc.section[0], identity, 10);
  for (unsigned i = 0; i < c->parts; i++) {
    unsigned at = head + i * 16, parent = pso_byte(r, at + 1);
    if (parent != 255 && parent >= i)
      return 0;
    float pos[3];
    for (unsigned k = 0; k < 3; k++) {
      pos[k] = pso_float(r, at + 4 + k * 4);
      c->world[i][k] = pos[k] + (parent == 255 ? 0 : c->world[parent][k]);
    }
    char text[32];
    snprintf(text, sizeof(text), "RE4_Part_%u", i);
    number(&c->gc.section[0], parent == 255 ? 0 : parent + 1);
    name(&c->gc.section[0], text, text);
    floats(&c->gc.section[0], pos, 3);
    floats(&c->gc.section[0], identity + 3, 7);
  }
  c->gc.nodes = c->parts + 1;
  c->flags = pso_u32(r, 32);
  c->shift = pso_byte(r, 40);
  c->position = pso_u32(r, 48);
  c->normal = pso_u32(r, 52);
  c->vertices = pso_u16(r, 56);
  c->normals = pso_u16(r, 58);
  c->color = pso_u32(r, 12);
  c->texcoord = pso_u32(r, 16);
  c->weights = pso_u32(r, 20);
  c->weight_count = pso_u16(r, 42);
  c->weight_stride = c->weight_count > 255 ? 12 : 8;
  if (c->weight_count <= 255)
    c->weight_count = pso_byte(r, 24);
  c->rigid = c->parts == 1 && c->weight_count <= 1;
  if (c->shift > 31 || !c->vertices || !c->normals ||
      (!c->weight_count && !c->rigid) ||
      !pso_range(r, c->position, c->vertices * 8) ||
      !pso_range(r, c->normal,
                 c->normals * ((c->flags & 0x20000000) ? 4 : 8)) ||
      !pso_range(r, c->weights, c->weight_count * c->weight_stride))
    return 0;
  return !c->gc.memory.failed;
}
static int re4_weights(Re4Import *c, unsigned palette, GcSkin *skin,
                       GcVertex *v) {
  PsoReader r = c->model;
  if (c->rigid) {
    float identity[16];
    dnd_model_identity(identity);
    int joint = gc_skin_joint(skin, 1, 0, identity);
    if (joint < 0)
      return 0;
    v->joints[0] = (unsigned)joint;
    v->weights[0] = 1;
    return 1;
  }
  if (palette >= c->weight_count)
    return 0;
  unsigned at = c->weights + palette * c->weight_stride;
  unsigned n =
      c->weight_stride == 12 ? pso_u16(r, at + 6) : pso_byte(r, at + 3);
  if (!n || n > 3)
    return 0;
  float total = 0;
  for (unsigned i = 0; i < n; i++) {
    unsigned node =
        c->weight_stride == 12 ? pso_u16(r, at + i * 2) : pso_byte(r, at + i);
    if (node >= c->parts)
      return 0;
    float bind[16];
    dnd_model_identity(bind);
    for (unsigned k = 0; k < 3; k++)
      bind[12 + k] = -c->world[node][k];
    int joint = gc_skin_joint(skin, node + 1, 1, bind);
    if (joint < 0)
      return 0;
    float weight =
        i + 1 == n
            ? 1 - total
            : pso_byte(r, at + (c->weight_stride == 12 ? 8 : 4) + i) / 100.0f;
    if (weight < 0 || weight > 1)
      return 0;
    total += weight;
    v->weights[i] = weight;
    v->joints[i] = (unsigned)joint;
  }
  return !c->gc.memory.failed;
}
static int re4_vertex(Re4Import *c, PsoReader dl, unsigned *cursor,
                      GcSkin *skin, GcVertex *v) {
  PsoReader r = c->model;
  unsigned bytes_per_vertex = c->flags & 0x80000000 ? 8 : 6;
  if (!pso_range(dl, *cursor, bytes_per_vertex))
    return 0;
  unsigned pos = pso_u16(dl, *cursor), normal = pso_u16(dl, *cursor + 2);
  unsigned color = c->flags & 0x80000000 ? pso_u16(dl, *cursor + 4) : 0;
  unsigned tex = pso_u16(dl, *cursor + bytes_per_vertex - 2);
  *cursor += bytes_per_vertex;
  if (pos >= c->vertices || normal >= c->normals || c->texcoord > r.size ||
      tex >= (r.size - c->texcoord) / 4 ||
      !pso_range(r, c->texcoord + tex * 4, 4))
    return 0;
  unsigned pa = c->position + pos * 8,
           na = c->normal + normal * ((c->flags & 0x20000000) ? 4 : 8);
  for (unsigned k = 0; k < 3; k++) {
    v->p[k] = ldexpf((float)(int16_t)pso_u16(r, pa + k * 2), -(int)c->shift);
    v->n[k] = c->flags & 0x20000000
                  ? (float)(int8_t)pso_byte(r, na + k) / 64
                  : (float)(int16_t)pso_u16(r, na + k * 2) / 16384;
  }
  for (unsigned k = 0; k < 2; k++)
    v->uv[k] =
        c->flags & 0x80000000
            ? (float)(int16_t)pso_u16(r, c->texcoord + tex * 4 + k * 2) / 256
            : (float)pso_u16(r, c->texcoord + tex * 4 + k * 2) / 32768;
  for (unsigned k = 0; k < 4; k++)
    v->color[k] = 1;
  if (c->flags & 0x80000000) {
    if (c->color > r.size || color >= (r.size - c->color) / 4 ||
        !pso_range(r, c->color + color * 4, 4))
      return 0;
    /* The game's trans_lit uses the register unless model bit30 selects vertex
     * color. */
    if (c->flags & 0x40000000)
      for (unsigned k = 0; k < 4; k++)
        v->color[k] = pso_byte(r, c->color + color * 4 + k) / 255.0f;
  }
  unsigned position_palette = pso_u16(r, pa + 6);
  unsigned normal_palette =
      c->flags & 0x20000000 ? pso_byte(r, na + 3) : pso_u16(r, na + 6);
  if (!re4_weights(c, position_palette, skin, v))
    return 0;
  if (!c->rigid && normal_palette != position_palette) {
    GcVertex nv = {0};
    if (!re4_weights(c, normal_palette, skin, &nv))
      return 0;
    int same = 1;
    for (unsigned i = 0; i < 4; i++) {
      float weight = 0;
      for (unsigned j = 0; j < 4; j++)
        if (nv.joints[j] == v->joints[i])
          weight += nv.weights[j];
      if (fabsf(weight - v->weights[i]) > 1e-6f && v->weights[i] > 0)
        same = 0;
    }
    if (!same && !c->gc.approximate)
      return 0;
  }
  return !c->gc.memory.failed;
}
static int re4_geometry(Re4Import *c) {
  PsoReader r = c->model;
  unsigned count = pso_u16(r, 26), part = pso_u32(r, 28),
           texture_count = pso_u32(r, 36);
  if (!count || count > 1024 || texture_count > c->gc.textures)
    return 0;
  for (unsigned i = 0; i < count; i++) {
    if (!pso_range(r, part, 32) || part % 32)
      return 0;
    unsigned flags = pso_byte(r, part + 11), tex = pso_byte(r, part + 12),
             length = pso_u32(r, part + 24);
    if (tex >= texture_count || !length || length % 32 ||
        !pso_range(r, part + 32, length))
      return 0;
    if (!c->gc.approximate &&
        (flags || pso_byte(r, part + 16) || pso_byte(r, part + 17) ||
         pso_byte(r, part + 18)))
      return 0;
    unsigned ref = pso_byte(r, part + 20), mode = ref ? 1 : 0;
    float white[4] = {1, 1, 1, 1};
    floats(&c->gc.section[2], white, 4);
    number(&c->gc.section[2], tex);
    number(&c->gc.section[2], mode);
    scalar(&c->gc.section[2], (ref + (ref ? 1.0f : 0.0f)) / 255);
    number(&c->gc.section[2], 0);
    unsigned material = c->gc.materials++;
    ImportBuffer vertices = {0}, indices = {0};
    vertices.memory = indices.memory = &c->gc.memory;
    GcSkin *skin = imp_alloc(&c->gc.memory, sizeof(GcSkin));
    int ok = 0;
    if (!skin)
      goto part_done;
    PsoReader dl = gc_slice(r, part + 32, length);
    unsigned cursor = 0;
    while (cursor < dl.size) {
      unsigned command = pso_byte(dl, cursor++);
      if (!command)
        continue;
      if ((command & 7) || !pso_range(dl, cursor, 2))
        goto part_done;
      unsigned n = pso_u16(dl, cursor), first = vertices.size / 80;
      cursor += 2;
      if (!n || first + n > 262144)
        goto part_done;
      for (unsigned k = 0; k < n; k++) {
        GcVertex v = {0};
        if (!re4_vertex(c, dl, &cursor, skin, &v))
          goto part_done;
        gc_emit(&vertices, &v);
      }
      if (!fz_primitive(&indices, first, n, command, 0))
        goto part_done;
    }
    ok = gc_mesh(&c->gc, &vertices, &indices, skin, material, 1);
  part_done:
    imp_free(&c->gc.memory, skin);
    imp_free(&c->gc.memory, vertices.data);
    imp_free(&c->gc.memory, indices.data);
    if (!ok)
      return 0;
    part += 32 + length;
  }
  return !c->gc.memory.failed;
}
static float re4_fcc_value(PsoReader r, unsigned *cursor, unsigned type) {
  float value = type == 0   ? pso_float(r, *cursor)
                : type == 1 ? (float)(int16_t)pso_u16(r, *cursor) * 0.0001f
                            : (float)(int8_t)pso_byte(r, *cursor) * 0.0001f;
  *cursor += type == 0 ? 4 : type == 1 ? 2 : 1;
  return value;
}
static int re4_curve(PsoReader r, unsigned *cursor, unsigned format,
                     unsigned duration, GcCurve *curve) {
  unsigned count = pso_u16(r, *cursor);
  *cursor += 2;
  if (!count || (format != 15 && (format / 4 > 2 || format % 4 > 2)) ||
      (format == 15 && count != 1))
    return 0;
  unsigned value_type = format == 15 ? 0 : format / 4,
           tangent_type = format % 4;
  unsigned stride = (value_type == 0   ? 4
                     : value_type == 1 ? 2
                                       : 1) +
                    (format == 15        ? 0
                     : tangent_type == 0 ? 8
                     : tangent_type == 1 ? 4
                                         : 2);
  if (!pso_range(r, *cursor, count * (2 + stride)))
    return 0;
  unsigned times = *cursor;
  *cursor += count * 2;
  curve->key = imp_alloc(r.memory, count * sizeof(GcKey));
  curve->count = count;
  if (!curve->key)
    return 0;
  for (unsigned i = 0; i < count; i++) {
    GcKey *k = &curve->key[i];
    k->time = (float)pso_u16(r, times + i * 2);
    k->value = re4_fcc_value(r, cursor, value_type);
    k->in = k->out = 0;
    if (format != 15) {
      k->in = re4_fcc_value(r, cursor, tangent_type);
      k->out = re4_fcc_value(r, cursor, tangent_type);
    }
  }
  return gc_curve_valid(curve, (float)duration) && !r.memory->failed;
}
static int re4_animation(Re4Import *c, PsoReader r, float fps) {
  if (r.size < 8)
    return 0;
  unsigned frames = pso_u16(r, 0) & 0x3fff, count = pso_byte(r, 2),
           table = (3 + count * 3 + 3) & ~3u;
  if (!count || !pso_range(r, table, 4 + count * 4))
    return 0;
  unsigned declared = pso_u32(r, table);
  if (declared && declared != r.size)
    return 0;
  /* IK targets are model-space constraints, not translations. Double-joint
   * blends also require the game's post-animation solver. Never silently
   * turn either into FK tracks. Camera and facial FCV are distinct profiles. */
  if (pso_u32(c->model, 60) == 0x20030818) {
    unsigned blend = pso_u32(c->model, 64);
    if (blend && (!pso_range(c->model, blend, 4) || pso_u32(c->model, blend)))
      return 0;
  }
  unsigned char seen[256 * 3] = {0};
  ImportBuffer tracks = {0};
  tracks.memory = &c->gc.memory;
  number(&tracks, 0);
  GcCurve curves[3] = {{0}};
  int ok = 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned kind = pso_u16(r, 3 + i * 2), flags = kind & 255;
    unsigned part = pso_byte(r, 3 + count * 2 + i), property, node;
    if ((flags & 0x30) || (kind & 0xf00))
      goto done;
    if (flags == 1) {
      node = 0;
      property = 0;
    } else if (flags == 0x40) {
      node = 0;
      property = 1;
    } else {
      if (part >= c->parts || (flags != 2 && flags != 4 && flags != 8))
        goto done;
      node = part + 1;
      property = flags == 2 ? 1 : flags == 4 ? 0 : 2;
    }
    if (seen[node * 3 + property])
      goto done;
    seen[node * 3 + property] = 1;
    unsigned cursor = pso_u32(r, table + 4 + i * 4);
    if (cursor < table + 4 + count * 4)
      goto done;
    for (unsigned axis = 0; axis < 3; axis++)
      if (!re4_curve(r, &cursor, kind >> 12, frames, &curves[axis]))
        goto done;
    if (!gc_track(&tracks, curves, node, property, (float)frames, fps, 1))
      goto done;
    gc_curves_free(&c->gc.memory, curves, 3);
  }
  gc_clip(&c->gc, &tracks, frames / fps, count);
  ok = !c->gc.memory.failed;
done:
  gc_curves_free(&c->gc.memory, curves, 3);
  imp_free(&c->gc.memory, tracks.data);
  return ok;
}
static int import_re4(const char *path, const char *texture_path,
                      const char *animation_path, float fps, int budget,
                      int approximate, int executor) {
  Re4Import c = {0};
  gc_init(&c.gc, budget, approximate, executor);
  GcAsset model = {0}, texture = {0}, animation = {0};
  int token = 0;
  if (!re4_asset(&model, path, &c.gc.memory, "BIN"))
    goto done;
  c.model = model.view;
  if (!texture_path ||
      !re4_asset(&texture, texture_path, &c.gc.memory, "TPL") ||
      !gc_tpl(&c.gc, texture.view) || !re4_joints(&c) || !re4_geometry(&c))
    goto done;
  if (animation_path &&
      (!re4_asset(&animation, animation_path, &c.gc.memory, "FCV") ||
       !re4_animation(&c, animation.view, fps)))
    goto done;
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&texture, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.world);
  c.world = NULL;
  token = gc_publish(&c.gc, budget);
done:
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&texture, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.world);
  gc_free(&c.gc);
  return token;
}
