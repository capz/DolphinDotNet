/* Shared, bounded GameCube asset readers. All offsets are decoded as data;
 * source display lists are never executed. No managed pointers live here. */
typedef struct {
  ImportMemory memory;
  ImportBuffer section[6];
  unsigned nodes, meshes, materials, textures, skins;
  int approximate;
} GcImport;
typedef struct {
  unsigned char *file, *unpacked, *member;
  PsoReader view;
} GcAsset;
typedef struct {
  float p[3], n[3], uv[2], color[4], weights[4];
  unsigned joints[4];
} GcVertex;
typedef struct {
  unsigned count, node[256], kind[256];
  float bind[256][16];
} GcSkin;

static PsoReader gc_slice(PsoReader r, unsigned at, unsigned size) {
  if (!pso_range(r, at, size))
    return (PsoReader){NULL, 0, r.memory};
  return (PsoReader){r.data + at, size, r.memory};
}
static unsigned char *gc_file(const char *path, ImportMemory *m,
                              unsigned *size) {
  int file = import_open(m, path);
  if (!file)
    return NULL;
  int64_t length = import_length(m, file);
  unsigned char *out = NULL;
  unsigned got = 0;
  if (length > 0 && length <= DND_MODEL_LIMIT) {
    out = imp_alloc(m, (size_t)length);
    while (out && got < (unsigned)length) {
      int n = import_read(m, file, out + got, (int)((unsigned)length - got));
      if (n <= 0)
        break;
      got += (unsigned)n;
    }
  }
  import_close(m, file);
  if (!out || got != (unsigned)length) {
    imp_free(m, out);
    return NULL;
  }
  *size = got;
  return out;
}
/* Yaz0 and Yay0 share the 12-bit backwards reference and long-run encoding,
 * but Yay0 keeps masks, links and literals in separate bounded streams. */
static int gc_unpack(PsoReader *r, unsigned char **owned) {
  for (unsigned depth = 0; depth < 3; depth++) {
    if (r->size < 4 ||
        (memcmp(r->data, "Yaz0", 4) && memcmp(r->data, "Yay0", 4)))
      return 1;
    if (r->size < 16)
      return 0;
    int yay = !memcmp(r->data, "Yay0", 4);
    unsigned length = pso_u32(*r, 4), mask = 16, links = 16, literal = 16;
    if (!length || length > DND_MODEL_LIMIT)
      return 0;
    if (yay) {
      links = pso_u32(*r, 8);
      literal = pso_u32(*r, 12);
      if (links < 16 || literal < links || literal > r->size)
        return 0;
    }
    unsigned mask_end = yay ? links : r->size,
             link_end = yay ? literal : r->size;
    unsigned char *out = imp_alloc(r->memory, length);
    if (!out)
      return 0;
    unsigned produced = 0, flags = 0, bits = 0;
    int ok = 1;
    while (produced < length && ok) {
      if (!bits) {
        if (mask >= mask_end) {
          ok = 0;
          break;
        }
        flags = r->data[mask++];
        bits = 8;
      }
      if (flags & 0x80) {
        unsigned *at = yay ? &literal : &mask;
        if (*at >= r->size) {
          ok = 0;
          break;
        }
        out[produced++] = r->data[(*at)++];
      } else {
        unsigned *at = yay ? &links : &mask;
        if (*at > link_end || link_end - *at < 2) {
          ok = 0;
          break;
        }
        unsigned v = (unsigned)r->data[*at] << 8 | r->data[*at + 1];
        *at += 2;
        unsigned count = v >> 12, distance = (v & 0xfff) + 1;
        if (!count) {
          unsigned *extra = yay ? &literal : &mask;
          if (*extra >= r->size) {
            ok = 0;
            break;
          }
          count = r->data[(*extra)++] + 18;
        } else
          count += 2;
        if (distance > produced || count > length - produced) {
          ok = 0;
          break;
        }
        for (unsigned k = 0; k < count; k++) {
          out[produced] = out[produced - distance];
          produced++;
        }
      }
      bits--;
      flags <<= 1;
    }
    if (!ok) {
      imp_free(r->memory, out);
      return 0;
    }
    imp_free(r->memory, *owned);
    *owned = out;
    *r = (PsoReader){out, length, r->memory};
  }
  return r->size < 4 ||
         (memcmp(r->data, "Yaz0", 4) && memcmp(r->data, "Yay0", 4));
}
/* RARC directory traversal rejects cycles, aliases and ambiguous selections.
 * File names are archive paths, never paths passed back to storage. */
typedef struct {
  PsoReader r, result;
  const char *selector;
  unsigned nodes, node_table, files, file_table, strings, string_size, data;
  unsigned found, visited[4096 / 32];
  int animation;
} GcArchive;
static int gc_rarc_dir(GcArchive *a, unsigned node, char path[1024],
                       unsigned length, unsigned depth) {
  if (node >= a->nodes || depth > 32 ||
      (a->visited[node / 32] & (1u << (node % 32))))
    return 0;
  a->visited[node / 32] |= 1u << (node % 32);
  unsigned at = a->node_table + node * 16;
  unsigned count = pso_u16(a->r, at + 10), first = pso_u32(a->r, at + 12);
  if (first > a->files || count > a->files - first)
    return 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned e = a->file_table + (first + i) * 20;
    unsigned field = pso_u32(a->r, e + 4), flags = field >> 24,
             text = field & 0xffffff;
    char item[1024];
    if (text >= a->string_size ||
        !fz_string(a->r, a->strings + text, a->strings + a->string_size, item))
      return 0;
    if (!strcmp(item, ".") || !strcmp(item, ".."))
      continue;
    unsigned n = (unsigned)strlen(item);
    if (!n || strchr(item, '/') || strchr(item, '\\') || length + n + 2 > 1024)
      return 0;
    memcpy(path + length, item, n + 1);
    unsigned value = pso_u32(a->r, e + 8), size = pso_u32(a->r, e + 12);
    if (flags & 2) {
      path[length + n] = '/';
      path[length + n + 1] = 0;
      if (!gc_rarc_dir(a, value, path, length + n + 1, depth + 1))
        return 0;
    } else {
      if (!(flags & 1) || value > a->r.size - a->data ||
          size > a->r.size - a->data - value)
        return 0;
      int match =
          a->selector ? !strcmp(path, a->selector)
          : a->animation
              ? (fz_extension(item, ".bck") || fz_extension(item, ".bca"))
              : (fz_extension(item, ".bmd") || fz_extension(item, ".bdl"));
      if (match) {
        a->result = gc_slice(a->r, a->data + value, size);
        a->found++;
      }
    }
    path[length] = 0;
  }
  return !a->r.memory->failed;
}
static int gc_rarc(PsoReader r, const char *selector, int animation,
                   PsoReader *out) {
  if (r.size < 64 || memcmp(r.data, "RARC", 4) || pso_u32(r, 4) != r.size ||
      pso_u32(r, 8) != 32)
    return 0;
  GcArchive a = {0};
  a.r = r;
  a.selector = selector;
  a.animation = animation;
  a.nodes = pso_u32(r, 32);
  a.files = pso_u32(r, 40);
  unsigned nt = pso_u32(r, 36), ft = pso_u32(r, 44), st = pso_u32(r, 52),
           dt = pso_u32(r, 12);
  a.string_size = pso_u32(r, 48);
  if (!a.nodes || a.nodes > 4096 || a.files > 65536 || nt > r.size - 32 ||
      ft > r.size - 32 || st > r.size - 32 || dt > r.size - 32)
    return 0;
  a.node_table = nt + 32;
  a.file_table = ft + 32;
  a.strings = st + 32;
  a.data = dt + 32;
  if (!pso_range(r, a.node_table, a.nodes * 16) ||
      !pso_range(r, a.file_table, a.files * 20) ||
      !pso_range(r, a.strings, a.string_size) ||
      !pso_range(r, a.data, pso_u32(r, 16)))
    return 0;
  char path[1024] = {0};
  if (!gc_rarc_dir(&a, 0, path, 0, 0) || a.found != 1)
    return 0;
  *out = a.result;
  return 1;
}
static int gc_asset(GcAsset *a, const char *path, ImportMemory *m,
                    int animation) {
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
  unsigned size = 0;
  a->file = gc_file(filename, m, &size);
  if (!a->file)
    return 0;
  a->view = (PsoReader){a->file, size, m};
  if (!gc_unpack(&a->view, &a->unpacked))
    return 0;
  if (a->view.size >= 4 && !memcmp(a->view.data, "RARC", 4)) {
    if (!gc_rarc(a->view, selector, animation, &a->view) ||
        !gc_unpack(&a->view, &a->member))
      return 0;
  } else if (selector)
    return 0;
  return !m->failed;
}
static void gc_asset_free(GcAsset *a, ImportMemory *m) {
  imp_free(m, a->file);
  imp_free(m, a->unpacked);
  imp_free(m, a->member);
  memset(a, 0, sizeof(*a));
}
static void gc_init(GcImport *c, int budget, int approximate, int executor) {
  c->memory = (ImportMemory){0, (unsigned)budget, 0, executor};
  c->approximate = approximate;
  for (unsigned i = 0; i < 6; i++) {
    c->section[i].memory = &c->memory;
    number(&c->section[i], 0);
  }
}
static void gc_free(GcImport *c) {
  for (unsigned i = 0; i < 6; i++)
    imp_free(&c->memory, c->section[i].data);
}
static int gc_publish(GcImport *c, int budget) {
  if (c->memory.failed || !c->nodes || !c->meshes || !c->materials)
    return 0;
  unsigned counts[5] = {c->nodes, c->meshes, c->materials, c->textures,
                        c->skins};
  for (unsigned i = 0; i < 5; i++)
    put32(c->section[i].data, counts[i]);
  return scene_publish(&c->memory, c->section, budget);
}
static void gc_emit(ImportBuffer *b, const GcVertex *v) {
  floats(b, v->p, 3);
  floats(b, v->n, 3);
  floats(b, v->uv, 2);
  floats(b, v->color, 4);
  for (unsigned i = 0; i < 4; i++)
    number(b, v->joints[i]);
  floats(b, v->weights, 4);
}
static int gc_skin_joint(GcSkin *s, unsigned node, unsigned kind,
                         const float bind[16]) {
  for (unsigned i = 0; i < s->count; i++)
    if (s->node[i] == node && s->kind[i] == kind)
      return (int)i;
  if (s->count == 256)
    return -1;
  unsigned i = s->count++;
  s->node[i] = node;
  s->kind[i] = kind;
  memcpy(s->bind[i], bind, 64);
  return (int)i;
}
static int gc_mesh(GcImport *c, ImportBuffer *v, ImportBuffer *ix, GcSkin *skin,
                   unsigned material, int normals) {
  unsigned count = v->size / 80;
  if (!count || !ix->size || c->meshes >= 4096 || material >= c->materials ||
      !skin->count || c->skins >= 1024)
    return 0;
  if (!normals)
    fz_generate_normals(v, ix);
  float low[3] = {INFINITY, INFINITY, INFINITY},
        high[3] = {-INFINITY, -INFINITY, -INFINITY};
  for (unsigned i = 0; i < count; i++)
    for (unsigned k = 0; k < 3; k++) {
      float p = dnd_model_f32(v->data + i * 80 + k * 4);
      low[k] = fminf(low[k], p);
      high[k] = fmaxf(high[k], p);
    }
  ImportBuffer *out = &c->section[1];
  number(out, 0);
  number(out, c->skins++);
  number(out, material);
  number(out, count);
  number(out, ix->size / 4);
  floats(out, low, 3);
  floats(out, high, 3);
  bytes(out, v->data, v->size);
  bytes(out, ix->data, ix->size);
  number(&c->section[4], skin->count);
  for (unsigned i = 0; i < skin->count; i++) {
    number(&c->section[4], skin->node[i]);
    floats(&c->section[4], skin->bind[i], 16);
  }
  c->meshes++;
  return !c->memory.failed;
}
/* All eleven GX image formats, including CI4/CI8/CI14X2 + IA8/RGB565/RGB5A3
 * TLUTs. */
static unsigned gc_texel_size(unsigned f, unsigned w, unsigned h) {
  unsigned tw =
      (f == 0 || f == 1 || f == 2 || f == 8 || f == 9 || f == 14) ? 8 : 4;
  unsigned th = (f == 0 || f == 8 || f == 14) ? 8 : 4;
  return ((w + tw - 1) / tw) * ((h + th - 1) / th) * (f == 6 ? 64 : 32);
}
static void gc_palette(unsigned char c[4], unsigned v, unsigned format) {
  if (format == 0) {
    memset(c, (int)(v & 255), 3);
    c[3] = (unsigned char)(v >> 8);
  } else if (format == 1)
    pso_rgb565(c, v);
  else if (v & 0x8000) {
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
}
static int gc_texture(GcImport *c, PsoReader r, unsigned offset,
                      unsigned format, unsigned w, unsigned h, unsigned levels,
                      unsigned ws, unsigned wt, unsigned min_filter,
                      unsigned mag_filter, unsigned palette,
                      unsigned palette_count, unsigned palette_format) {
  if (!w || !h || w > 1024 || h > 1024 || (w & (w - 1)) || (h & (h - 1)) ||
      !levels || levels > 11 || ws > 2 || wt > 2 || min_filter > 5 ||
      mag_filter > 1 ||
      !((format <= 6) || format == 8 || format == 9 || format == 10 ||
        format == 14) ||
      c->textures >= 4096)
    return 0;
  int indexed = format >= 8 && format <= 10;
  unsigned max_palette = format == 8 ? 16 : format == 9 ? 256 : 16384;
  if (indexed &&
      (!palette_count || palette_count > max_palette || palette_format > 2 ||
       !pso_range(r, palette, palette_count * 2)))
    return 0;
  unsigned source_offset = offset;
  unsigned sw = w, sh = h, ow = w < 4 ? 4 : w, oh = h < 4 ? 4 : h;
  unsigned kept = 0, output_size = 0;
  for (unsigned l = 0; l < levels; l++) {
    unsigned n = gc_texel_size(format, sw, sh);
    if (!pso_range(r, offset, n))
      return 0;
    if (!l || (sw >= 4 && sh >= 4)) {
      kept++;
      output_size += (sw < 4 ? 4 : sw) * (sh < 4 ? 4 : sh) * 4;
    }
    if (l + 1 < levels && sw == 1 && sh == 1)
      return 0;
    offset += n;
    sw = sw > 1 ? sw / 2 : 1;
    sh = sh > 1 ? sh / 2 : 1;
  }
  /* The runtime has one magnification/minification toggle. */
  if (!c->approximate && ((min_filter == 0 || min_filter == 2 ||
                           min_filter == 4) != (mag_filter == 0)))
    return 0;
  ImportBuffer *out = &c->section[3];
  number(out, ow);
  number(out, oh);
  number(out, kept);
  number(out, ws);
  number(out, wt);
  number(out, mag_filter);
  number(out, output_size);
  offset = source_offset;
  sw = w;
  sh = h;
  for (unsigned l = 0; l < levels; l++) {
    unsigned n = gc_texel_size(format, sw, sh);
    PsoTexture t = {0};
    t.width = sw;
    t.height = sh;
    t.format = format;
    t.pixels = r.data + offset;
    if (l < kept) {
      unsigned width = sw < 4 ? 4 : sw, height = sh < 4 ? 4 : sh;
      for (unsigned y = 0; y < height; y += 4)
        for (unsigned x = 0; x < width; x += 4) {
          unsigned char tile[64];
          for (unsigned row = 0; row < 4; row++)
            for (unsigned col = 0; col < 4; col++) {
              unsigned px = (x + col) * sw / width,
                       py = (y + row) * sh / height;
              unsigned char rgba[4];
              if (indexed) {
                unsigned tw = format == 10 ? 4 : 8, th = format == 8 ? 8 : 4;
                unsigned tile_at =
                    (py / th * ((sw + tw - 1) / tw) + px / tw) * 32;
                unsigned k = py % th * tw + px % tw, index;
                const unsigned char *p = t.pixels + tile_at;
                if (format == 8)
                  index = p[k / 2] >> (k % 2 ? 0 : 4) & 15;
                else if (format == 9)
                  index = p[k];
                else
                  index = ((unsigned)p[k * 2] << 8 | p[k * 2 + 1]) & 0x3fff;
                if (index >= palette_count)
                  return 0;
                gc_palette(rgba, pso_u16(r, palette + index * 2),
                           palette_format);
              } else
                pso_pixel(&t, px, py, rgba);
              unsigned k = (row * 4 + col) * 2;
              tile[k] = rgba[3];
              tile[k + 1] = rgba[0];
              tile[k + 32] = rgba[1];
              tile[k + 33] = rgba[2];
            }
          bytes(out, tile, 64);
        }
    }
    offset += n;
    sw = sw > 1 ? sw / 2 : 1;
    sh = sh > 1 ? sh / 2 : 1;
  }
  c->textures++;
  return !c->memory.failed;
}
static int gc_tpl(GcImport *c, PsoReader r) {
  if (r.size < 12 || pso_u32(r, 0) != 0x0020af30)
    return 0;
  unsigned count = pso_u32(r, 4), table = pso_u32(r, 8);
  if (!count || count > 4096 || !pso_range(r, table, count * 8))
    return 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned t = pso_u32(r, table + i * 8), p = pso_u32(r, table + i * 8 + 4);
    if (!pso_range(r, t, 36) || (p && !pso_range(r, p, 12)) ||
        pso_byte(r, t + 33) != 0)
      return 0;
    float bias = pso_float(r, t + 28);
    if (!c->approximate && (bias || pso_byte(r, t + 32)))
      return 0;
    if (!gc_texture(c, r, pso_u32(r, t + 8), pso_u32(r, t + 4),
                    pso_u16(r, t + 2), pso_u16(r, t), pso_byte(r, t + 34) + 1,
                    pso_u32(r, t + 12), pso_u32(r, t + 16), pso_u32(r, t + 20),
                    pso_u32(r, t + 24), p ? pso_u32(r, p + 8) : 0,
                    p ? pso_u16(r, p) : 0, p ? pso_u32(r, p + 4) : 0))
      return 0;
  }
  return 1;
}

typedef struct {
  float time, value, in, out;
  unsigned mode; /* 0 Hermite, 1 linear, 2 held until the next key. */
} GcKey;
typedef struct {
  GcKey *key;
  unsigned count;
} GcCurve;
static float gc_curve(const GcCurve *c, float time, int normalized_tangents) {
  if (time <= c->key[0].time || c->count == 1)
    return c->key[0].value;
  if (time >= c->key[c->count - 1].time)
    return c->key[c->count - 1].value;
  unsigned low = 0, high = c->count - 1;
  while (high - low > 1) {
    unsigned mid = low + (high - low) / 2;
    if (c->key[mid].time <= time)
      low = mid;
    else
      high = mid;
  }
  const GcKey *a = &c->key[low], *b = &c->key[high];
  float dt = b->time - a->time, t = (time - a->time) / dt, t2 = t * t,
        t3 = t2 * t;
  if (a->mode == 1)
    return a->value + (b->value - a->value) * t;
  if (a->mode == 2)
    return a->value;
  float scale = normalized_tangents ? 1 : dt;
  return (2 * t3 - 3 * t2 + 1) * a->value + (t3 - 2 * t2 + t) * a->out * scale +
         (-2 * t3 + 3 * t2) * b->value + (t3 - t2) * b->in * scale;
}
static int gc_curve_valid(const GcCurve *c, float duration) {
  for (unsigned i = 0; i < c->count; i++) {
    const GcKey *k = &c->key[i];
    if (!isfinite(k->time) || !isfinite(k->value) || !isfinite(k->in) ||
        !isfinite(k->out) || k->time < 0 || k->time > duration ||
        (i && k->time <= c->key[i - 1].time))
      return 0;
  }
  return c->count != 0;
}
/* Bake independently keyed Euler channels to quaternion tracks. Source curves
 * are evaluated at 60 Hz and at their authored key times. The native tracks use
 * linear interpolation between the baked values. */
static int gc_track(ImportBuffer *out, GcCurve curves[3], unsigned node,
                    unsigned property, float frames, float fps,
                    int normalized_tangents) {
  unsigned samples = (unsigned)ceilf(frames * 60 / fps);
  if (samples > 65535)
    return 0;
  if (!samples)
    samples = 1;
  /* A sorted merged key grid prevents short authored events from disappearing.
   */
  unsigned extra = curves[0].count + curves[1].count + curves[2].count;
  if (extra > 196605 || samples + extra + 1 > 262144)
    return 0;
  float *times = imp_alloc(out->memory, (samples + extra + 1) * sizeof(float));
  if (!times)
    return 0;
  unsigned n = 0, key[3] = {0}, end[3], grid = 0;
  for (unsigned axis = 0; axis < 3; axis++) {
    while (key[axis] < curves[axis].count &&
           curves[axis].key[key[axis]].time < 0)
      key[axis]++;
    end[axis] = curves[axis].count;
    while (end[axis] && curves[axis].key[end[axis] - 1].time > frames)
      end[axis]--;
  }
  while (grid <= samples || key[0] < end[0] || key[1] < end[1] ||
         key[2] < end[2]) {
    float next = grid <= samples ? frames * (float)grid / samples : INFINITY;
    for (unsigned axis = 0; axis < 3; axis++)
      if (key[axis] < end[axis])
        next = fminf(next, curves[axis].key[key[axis]].time);
    if (grid <= samples && frames * (float)grid / samples == next)
      grid++;
    for (unsigned axis = 0; axis < 3; axis++)
      if (key[axis] < end[axis] && curves[axis].key[key[axis]].time == next)
        key[axis]++;
    if (!n || next > times[n - 1])
      times[n++] = next;
  }
  number(out, node);
  number(out, property);
  number(out, 1);
  number(out, n);
  float previous[4] = {0, 0, 0, 1};
  for (unsigned i = 0; i < n; i++) {
    scalar(out, times[i] / fps);
    float value[3];
    for (unsigned axis = 0; axis < 3; axis++)
      value[axis] = gc_curve(&curves[axis], times[i], normalized_tangents);
    if (property == 1) {
      float q[4];
      pso_euler(q, value, 32);
      float dot = 0;
      for (unsigned k = 0; k < 4; k++)
        dot += previous[k] * q[k];
      for (unsigned k = 0; k < 4; k++) {
        if (dot < 0)
          q[k] = -q[k];
        previous[k] = q[k];
      }
      floats(out, q, 4);
    } else
      floats(out, value, 3);
  }
  imp_free(out->memory, times);
  return !out->memory->failed;
}
static void gc_curves_free(ImportMemory *m, GcCurve *curves, unsigned count) {
  for (unsigned i = 0; i < count; i++) {
    imp_free(m, curves[i].key);
    curves[i].key = NULL;
  }
}
static void gc_clip(GcImport *c, ImportBuffer *tracks, float duration,
                    unsigned count) {
  put32(tracks->data, count);
  put32(c->section[5].data, 1);
  name(&c->section[5], "Motion", "Motion");
  scalar(&c->section[5], duration);
  number(&c->section[5], tracks->size);
  bytes(&c->section[5], tracks->data, tracks->size);
}
