/* F-Zero GX source adapter. Included by model_source.c to share its bounded
 * allocators, storage gate scopes, DNMD builder and GX texel decoder.
 * Independent implementation from published GMA/GCMF/AV-TPL/U8 layouts.
 * Source display lists are decoded as data; they are NEVER submitted to GX. */
typedef struct {
  unsigned char *base, *expanded;
  PsoReader root, view;
} FzAsset;
typedef struct {
  unsigned source, sampler, output;
} FzTextureRef;
typedef struct {
  ImportMemory memory;
  ImportBuffer section[6];
  PsoReader tpl;
  FzTextureRef *texture;
  unsigned textures, nodes, meshes, materials;
  int approximate, snapshot;
} FzImport;
typedef struct {
  float p[3], n[3], uv[2], color[4];
} FzVertex;

static int fz_string(PsoReader r, unsigned at, unsigned end, char out[1024]) {
  if (end > r.size || at >= end)
    return 0;
  unsigned length = 0;
  while (at + length < end && r.data[at + length]) {
    unsigned c = r.data[at + length];
    if (c < 32 || c > 126 || length >= 1023)
      return 0;
    out[length++] = (char)c;
  }
  if (at + length == end)
    return 0;
  out[length] = 0;
  return 1;
}
static int fz_extension(const char *name, const char *extension) {
  size_t n = strlen(name), e = strlen(extension);
  if (n > 3 && !strcasecmp(name + n - 3, ".lz"))
    n -= 3;
  return n >= e && !strncasecmp(name + n - e, extension, e);
}
/* LZ has no magic. Prefer a recognizable uncompressed container before its
 * length heuristic: a BE GMA/TPL count can equal the file length when read LE.
 */
static int fz_plain(PsoReader r, int texture) {
  if (r.size < 8)
    return 0;
  unsigned first = dnd_model_u32(r.data);
  if (first == 0x55aa382d || (!texture && first == 0x47434d46))
    return 1;
  if (texture) {
    return first > 0 && first <= 4096 && first <= (r.size - 4) / 16 &&
           r.data[18] == 0x12 && r.data[19] == 0x34;
  }
  unsigned base = dnd_model_u32(r.data + 4);
  if (!first || first > 1024 || first > (r.size - 8) / 8 ||
      base < 8 + first * 8 || base > r.size || base % 32)
    return 0;
  for (unsigned i = 0; i < first; i++) {
    unsigned offset = dnd_model_u32(r.data + 8 + i * 8);
    if (offset == UINT_MAX)
      continue;
    return offset <= r.size - base && r.size - base - offset >= 4 &&
           dnd_model_u32(r.data + base + offset) == 0x47434d46;
  }
  return 1;
}
/* F-Zero LZSS: LE stored/expanded lengths, LSB-first flags, a zero-filled
 * 4096-byte dictionary initially writing at 4078, absolute 12-bit ring offsets.
 * Both observed length conventions (including/excluding the header) are valid.
 */
static int fz_unpack(PsoReader *r, unsigned char **owned, int texture) {
  for (unsigned depth = 0; depth < 3; depth++) {
    if (r->size < 8 || fz_plain(*r, texture))
      return 1;
    unsigned stored = le32(r->data), length = le32(r->data + 4);
    if (stored != r->size && stored != r->size - 8)
      return 1;
    if (!length || length > DND_MODEL_LIMIT)
      return 0;
    unsigned char *out = imp_alloc(r->memory, length);
    unsigned char *ring = imp_alloc(r->memory, 4096);
    if (!out || !ring) {
      imp_free(r->memory, out);
      imp_free(r->memory, ring);
      return 0;
    }
    unsigned input = 8, output = 0, cursor = 4078;
    int ok = 1;
    while (output < length && ok) {
      if (input == r->size) {
        ok = 0;
        break;
      }
      unsigned flags = r->data[input++];
      for (unsigned bit = 0; bit < 8 && output < length; bit++) {
        unsigned count = 1, from = 0;
        if (flags & (1u << bit)) {
          if (input == r->size) {
            ok = 0;
            break;
          }
          from = r->data[input++];
        } else {
          if (r->size - input < 2) {
            ok = 0;
            break;
          }
          from = r->data[input] | (unsigned)(r->data[input + 1] & 0xf0) << 4;
          count = (r->data[input + 1] & 15) + 3;
          input += 2;
        }
        if (count > length - output) {
          ok = 0;
          break;
        }
        for (unsigned k = 0; k < count; k++) {
          unsigned char c = (flags & (1u << bit)) ? (unsigned char)from
                                                  : ring[(from + k) & 4095];
          out[output++] = c;
          ring[cursor++ & 4095] = c;
        }
      }
    }
    imp_free(r->memory, ring);
    if (!ok || input != r->size) {
      imp_free(r->memory, out);
      return 0;
    }
    imp_free(r->memory, *owned);
    *owned = out;
    r->data = out;
    r->size = length;
  }
  /* Bound nested compression and reject a still-compressed result. */
  return r->size < 8 || fz_plain(*r, texture) ||
         (le32(r->data) != r->size && le32(r->data) != r->size - 8);
}
/* U8/ARC: validate every directory/file and resolve one unambiguous member.
 * A fragment selects its exact relative archive path, never a host path. */
static int fz_member(PsoReader archive, const char *selector, int texture,
                     PsoReader *out) {
  if (archive.size < 0x20 || pso_u32(archive, 0) != 0x55aa382d)
    return 0;
  unsigned root = pso_u32(archive, 4), header = pso_u32(archive, 8),
           data = pso_u32(archive, 12);
  if (root < 0x20 || root > archive.size || header > archive.size - root ||
      data < root + header || data > archive.size || header < 12 ||
      !pso_range(archive, root, 12) || pso_byte(archive, root) != 1 ||
      pso_u32(archive, root + 4) != 0)
    return 0;
  unsigned count = pso_u32(archive, root + 8);
  if (!count || count > 4096 || count > header / 12)
    return 0;
  unsigned strings = root + count * 12, end = root + header;
  unsigned ends[33] = {count}, indices[33] = {0}, lengths[33] = {0}, depth = 0,
           found = 0;
  char path[1024] = {0};
  for (unsigned i = 0; i < count; i++) {
    if (i)
      while (depth && i == ends[depth])
        depth--;
    if (i >= ends[depth])
      return 0;
    unsigned at = root + i * 12, type = pso_byte(archive, at),
             offset = pso_u32(archive, at) & 0xffffff,
             value = pso_u32(archive, at + 4), size = pso_u32(archive, at + 8);
    char name_value[1024];
    if (offset >= end - strings ||
        !fz_string(archive, strings + offset, end, name_value))
      return 0;
    if (!i) {
      if (*name_value || type != 1 || size != count)
        return 0;
      continue;
    }
    if (!*name_value || strchr(name_value, '/') || strchr(name_value, '\\') ||
        strchr(name_value, ':') || !strcmp(name_value, ".") ||
        !strcmp(name_value, ".."))
      return 0;
    size_t n = strlen(name_value), base = lengths[depth];
    if (base + n + 2 > sizeof(path))
      return 0;
    memcpy(path + base, name_value, n + 1);
    if (type == 1) {
      if (depth >= 31 || value != indices[depth] || size <= i ||
          size > ends[depth])
        return 0;
      path[base + n] = '/';
      path[base + n + 1] = 0;
      depth++;
      ends[depth] = size;
      indices[depth] = i;
      lengths[depth] = (unsigned)(base + n + 1);
    } else if (type == 0) {
      if (value < data || value > archive.size || size > archive.size - value)
        return 0;
      int match = selector  ? !strcmp(path, selector)
                  : texture ? fz_extension(path, ".tpl")
                            : (fz_extension(path, ".gma") ||
                               fz_extension(path, ".gcmf"));
      if (match) {
        *out = (PsoReader){archive.data + value, size, archive.memory};
        found++;
      }
    } else
      return 0;
  }
  return found == 1 && !archive.memory->failed;
}
static int fz_asset(FzAsset *a, const char *path, ImportMemory *m,
                    int texture) {
  char file_path[1024];
  strcpy(file_path, path);
  char *selector = strchr(file_path, '#');
  if (selector) {
    *selector++ = 0;
    if (!*selector || strchr(selector, '#'))
      return 0;
  }
  int file = import_open(m, file_path);
  if (!file)
    return 0;
  int64_t size = import_length(m, file);
  if (size <= 0 || size > DND_MODEL_LIMIT) {
    import_close(m, file);
    return 0;
  }
  a->base = imp_alloc(m, (size_t)size);
  unsigned got = 0;
  while (a->base && got < (unsigned)size) {
    int n = import_read(m, file, a->base + got, (int)((unsigned)size - got));
    if (n <= 0)
      break;
    got += (unsigned)n;
  }
  import_close(m, file);
  if (got != (unsigned)size)
    return 0;
  a->root = (PsoReader){a->base, (unsigned)size, m};
  if (!fz_unpack(&a->root, &a->base, texture))
    return 0;
  a->view = a->root;
  if (a->root.size >= 4 && pso_u32(a->root, 0) == 0x55aa382d) {
    if (!fz_member(a->root, selector, texture, &a->view))
      return 0;
  } else if (selector)
    return 0;
  return fz_unpack(&a->view, &a->expanded, texture);
}

static unsigned fz_texel_size(unsigned f, unsigned w, unsigned h) {
  unsigned tw = (f == 0 || f == 1 || f == 2 || f == 14) ? 8 : 4,
           th = (f == 0 || f == 14) ? 8 : 4;
  return ((w + tw - 1) / tw) * ((h + th - 1) / th) * (f == 6 ? 64 : 32);
}
static int fz_texture(FzImport *ctx, unsigned source, unsigned flags) {
  unsigned sampler =
      flags & (0x3c | 0x800 | 0x780); /* wrap, magnification, mip enable */
  for (unsigned i = 0; i < ctx->textures; i++)
    if (ctx->texture[i].source == source && ctx->texture[i].sampler == sampler)
      return (int)ctx->texture[i].output;
  PsoReader r = ctx->tpl;
  if (!r.data || r.size < 4 || source >= pso_u32(r, 0) ||
      pso_u32(r, 0) > 4096 || !pso_range(r, 4, pso_u32(r, 0) * 16) ||
      ctx->textures >= 4096)
    return -1;
  unsigned at = 4 + source * 16, format = pso_u32(r, at),
           offset = pso_u32(r, at + 4), w = pso_u16(r, at + 8),
           h = pso_u16(r, at + 10), levels = pso_u16(r, at + 12);
  if (pso_u16(r, at + 14) != 0x1234 || !w || !h || w > 1024 || h > 1024 ||
      (w & (w - 1)) || (h & (h - 1)) || !levels || levels > 11 ||
      !((format <= 6) || format == 14) || offset < 4 + pso_u32(r, 0) * 16)
    return -1;
  unsigned limit_w = w, limit_h = h, max_levels = 1;
  while (limit_w > 1 || limit_h > 1) {
    limit_w = limit_w > 1 ? limit_w / 2 : 1;
    limit_h = limit_h > 1 ? limit_h / 2 : 1;
    max_levels++;
  }
  if (levels > max_levels)
    return -1;
  unsigned ow = w < 4 ? 4 : w, oh = h < 4 ? 4 : h, retained = 1,
           output_size = ow * oh * 4;
  unsigned max_lod = (flags >> 7) & 15;
  if (max_lod == 15) {
    unsigned dimension = w < h ? w : h;
    max_lod = 0;
    while (dimension > 16) {
      dimension >>= 1;
      max_lod++;
    }
  }
  unsigned wrap_s = (flags >> 2) & 3, wrap_t = (flags >> 4) & 3;
  if (wrap_s > 2 || wrap_t > 2)
    return -1;
  if (max_lod)
    while (retained < levels && retained <= max_lod && (w >> retained) >= 4 &&
           (h >> retained) >= 4) {
      output_size += (w >> retained) * (h >> retained) * 4;
      retained++;
    }
  ImportBuffer *out = &ctx->section[3];
  number(out, ow);
  number(out, oh);
  number(out, retained);
  number(out, wrap_s);
  number(out, wrap_t);
  number(out, (flags & 0x800) ? 0 : 1);
  number(out, output_size);
  for (unsigned level = 0; level < levels; level++) {
    unsigned lw = w >> level, lh = h >> level;
    if (!lw)
      lw = 1;
    if (!lh)
      lh = 1;
    unsigned needed = fz_texel_size(format, lw, lh), stored = needed;
    /* GX's source encoder aligns CMPR to 4 instead of 8 before rounding bytes
     * to 32. Consume that exact stride even for discarded sub-4 mip levels. */
    if (format == 14)
      stored = (((((lw + 3) & ~3u) * ((lh + 3) & ~3u)) / 2 + 31) & ~31u);
    if (!pso_range(r, offset, stored))
      return -1;
    if (level < retained) {
      unsigned char *padded = NULL;
      const unsigned char *pixels = r.data + offset;
      if (stored < needed) {
        if (!ctx->approximate)
          return -1;
        padded = imp_alloc(&ctx->memory, needed);
        if (!padded)
          return -1;
        memcpy(padded, pixels, stored);
        pixels = padded;
      }
      PsoTexture texture = {0};
      texture.width = lw;
      texture.height = lh;
      texture.format = format;
      texture.pixels = pixels;
      unsigned dw = level ? lw : ow, dh = level ? lh : oh;
      for (unsigned y = 0; y < dh; y += 4)
        for (unsigned x = 0; x < dw; x += 4) {
          unsigned char tile[64];
          for (unsigned row = 0; row < 4; row++)
            for (unsigned col = 0; col < 4; col++) {
              unsigned char c[4];
              pso_pixel(&texture, (x + col) * lw / dw, (y + row) * lh / dh, c);
              unsigned k = (row * 4 + col) * 2;
              tile[k] = c[3];
              tile[k + 1] = c[0];
              tile[k + 32] = c[1];
              tile[k + 33] = c[2];
            }
          bytes(out, tile, 64);
        }
      imp_free(&ctx->memory, padded);
    }
    offset += stored;
  }
  unsigned id = ctx->textures++;
  ctx->texture[id] = (FzTextureRef){source, sampler, id};
  return ctx->memory.failed ? -1 : (int)id;
}
static int fz_material(FzImport *ctx, PsoReader model, unsigned shape,
                       unsigned layers, unsigned translucent,
                       unsigned *uv_set) {
  unsigned flags = pso_u32(model, shape), count = pso_byte(model, shape + 18);
  if (count > 3 || ctx->materials >= 4097)
    return -1;
  unsigned mode = ((flags & (1 | 128)) || !count) ? 2 : 0;
  if (translucent || pso_byte(model, shape + 17) < 255)
    mode |= 4;
  else
    mode |= 1; /* F-Zero discards alpha-zero opaque texels. */
  if (!ctx->approximate && (flags & ~0x1efu))
    return -1;
  /* Unsupported shading changes are opt-in, never dropped by accident. */
  if (!ctx->approximate && (count > 1 || (flags & 8)))
    return -1;
  unsigned blend = pso_u32(model, shape + 64),
           src = (flags & 32) ? blend & 15 : 4,
           dst = (flags & 64) ? (blend >> 4) & 15 : 5;
  if (src > 7 || dst > 7)
    return -1;
  if (src != 4 || dst != 5) {
    mode = (mode & ~1u) | 4;
    if (src == 4 && dst == 1)
      mode |= 8;
    else
      mode |= 16 | (src << 5) | (dst << 8);
  }
  int texture = -1;
  *uv_set = 0;
  for (unsigned i = 0; i < count; i++) {
    int layer = (int16_t)pso_u16(model, shape + 22 + i * 2);
    if (layer < 0 || (unsigned)layer >= layers)
      return -1;
    unsigned at = 64 + (unsigned)layer * 32, tf = pso_u32(model, at),
             type = tf & (1 | 2 | 0x2000 | 0x8000);
    unsigned color_type = pso_byte(model, at + 19) & 15,
             alpha_type = pso_byte(model, at + 19) >> 4 & 7;
    if (!ctx->approximate &&
        (type || color_type || alpha_type || pso_byte(model, at + 13)))
      return -1;
    /* Prefer the first ordinary diffuse layer when approximation is requested.
     */
    if (texture < 0 && (!type || i + 1 == count)) {
      texture = fz_texture(ctx, pso_u16(model, at + 4), tf);
      if (texture < 0)
        return -1;
      *uv_set = i;
    }
  }
  if (ctx->memory.failed)
    return -1;
  ImportBuffer *out = &ctx->section[2];
  for (unsigned c = 0; c < 3; c++)
    scalar(out, ((flags & (8 | 128)) || !count)
                    ? pso_byte(model, shape + 4 + c) / 255.f
                    : 1.f);
  scalar(out, pso_byte(model, shape + 17) / 255.f);
  number(out, (unsigned)texture);
  number(out, mode);
  scalar(out, (mode & 1) ? (1.f / 255) : 0);
  number(out, (flags & 2) != 0);
  return (int)ctx->materials++;
}

static float fz_component(PsoReader r, unsigned *at, unsigned vat, int normal) {
  float value;
  if (vat) {
    value = (int16_t)pso_u16(r, *at) / (normal ? 16384.f : 8192.f);
    *at += 2;
  } else {
    value = pso_float(r, *at);
    *at += 4;
  }
  return value;
}
static int fz_vertex(PsoReader r, unsigned *at, unsigned attrs, unsigned vat,
                     unsigned uv_set, FzVertex *v, unsigned *matrix) {
  memset(v, 0, sizeof(*v));
  for (unsigned c = 0; c < 4; c++)
    v->color[c] = 1;
  v->n[1] = 1;
  *matrix = 0;
  for (unsigned a = 0; a <= 8; a++)
    if (attrs & (1u << a)) {
      unsigned index = pso_byte(r, (*at)++);
      if (!a)
        *matrix = index;
      else if (index != 0 && index != 60 && !r.memory->failed)
        return 0; /* Unverified texture matrices. */
    }
  for (unsigned c = 0; c < 3; c++)
    v->p[c] = fz_component(r, at, vat, 0);
  if (attrs & (1u << 25)) {
    /* GX NBT occupies NRM's position in the vertex layout, not an extra tail.
     */
    for (unsigned c = 0; c < 9; c++) {
      float x = fz_component(r, at, vat, 1);
      if (c < 3)
        v->n[c] = x;
    }
  } else if (attrs & (1u << 10))
    for (unsigned c = 0; c < 3; c++)
      v->n[c] = fz_component(r, at, vat, 1);
  for (unsigned a = 11; a <= 12; a++)
    if (attrs & (1u << a))
      for (unsigned c = 0; c < 4; c++) {
        float x = pso_byte(r, (*at)++) / 255.f;
        if (a == 11)
          v->color[c] = x;
      }
  for (unsigned a = 13; a <= 20; a++)
    if (attrs & (1u << a))
      for (unsigned c = 0; c < 2; c++) {
        float x = fz_component(r, at, vat, 0);
        if (a == 13 + uv_set)
          v->uv[c] = x;
      }
  return !r.memory->failed;
}
static int fz_transform(PsoReader model, unsigned matrices,
                        unsigned matrix_base, const unsigned char slots[8],
                        unsigned index, FzVertex *v) {
  /* GCMF reserves GX position slot zero for identity; its eight dynamic
   * slots are addressed by PNMTXIDX 3,6,...,24. */
  if (!index)
    return 1;
  if (!matrices || index % 3 || index > 24)
    return 0;
  unsigned id = slots[index / 3 - 1];
  if (id == 255)
    return 0; /* Referencing an unbound dynamic matrix slot is invalid. */
  if (id >= matrices)
    return 0;
  float m[16], inverse[16], p[3] = {0}, n[3] = {0};
  dnd_model_identity(m);
  for (unsigned row = 0; row < 3; row++)
    for (unsigned col = 0; col < 4; col++)
      m[col * 4 + row] =
          pso_float(model, matrix_base + id * 48 + row * 16 + col * 4);
  if (!dnd_model_inverse(inverse, m))
    return 0;
  for (unsigned row = 0; row < 3; row++) {
    p[row] = m[12 + row];
    for (unsigned col = 0; col < 3; col++) {
      p[row] += m[col * 4 + row] * v->p[col];
      n[row] += inverse[row * 4 + col] * v->n[col];
    }
  }
  memcpy(v->p, p, 12);
  memcpy(v->n, n, 12);
  return !model.memory->failed;
}
static void fz_emit(ImportBuffer *out, const FzVertex *v) {
  floats(out, v->p, 3);
  floats(out, v->n, 3);
  floats(out, v->uv, 2);
  floats(out, v->color, 4);
  for (unsigned i = 0; i < 8; i++)
    number(out, 0);
}
static int fz_triangle(ImportBuffer *indices, unsigned a, unsigned b,
                       unsigned c, int reverse) {
  number(indices, a);
  number(indices, reverse ? c : b);
  number(indices, reverse ? b : c);
  return !indices->memory->failed;
}
static int fz_primitive(ImportBuffer *indices, unsigned first, unsigned count,
                        unsigned primitive, int reverse) {
  if (primitive == 0x90) {
    if (count % 3)
      return 0;
    for (unsigned i = 0; i < count; i += 3)
      fz_triangle(indices, first + i, first + i + 1, first + i + 2, reverse);
  } else if (primitive == 0x80 || primitive == 0x88) {
    if (count % 4)
      return 0;
    for (unsigned i = 0; i < count; i += 4) {
      fz_triangle(indices, first + i, first + i + 1, first + i + 2, reverse);
      fz_triangle(indices, first + i, first + i + 2, first + i + 3, reverse);
    }
  } else if (primitive == 0x98 || primitive == 0xa0) {
    if (count < 3)
      return 0;
    for (unsigned i = 2; i < count; i++) {
      unsigned a = primitive == 0xa0 ? first : first + i - 2, b = first + i - 1;
      fz_triangle(indices, a, b, first + i,
                  reverse ^ (primitive == 0x98 && (i & 1)));
    }
  } else
    return 0;
  return !indices->memory->failed;
}
static void fz_generate_normals(ImportBuffer *vertices,
                                const ImportBuffer *indices) {
  for (unsigned at = 0; at < indices->size; at += 12) {
    unsigned id[3];
    float p[3][3], a[3], b[3], n[3];
    for (unsigned k = 0; k < 3; k++) {
      id[k] = dnd_model_u32(indices->data + at + k * 4);
      for (unsigned c = 0; c < 3; c++)
        p[k][c] = dnd_model_f32(vertices->data + id[k] * 80 + c * 4);
    }
    for (unsigned c = 0; c < 3; c++) {
      a[c] = p[1][c] - p[0][c];
      b[c] = p[2][c] - p[0][c];
    }
    n[0] = a[1] * b[2] - a[2] * b[1];
    n[1] = a[2] * b[0] - a[0] * b[2];
    n[2] = a[0] * b[1] - a[1] * b[0];
    for (unsigned k = 0; k < 3; k++)
      for (unsigned c = 0; c < 3; c++) {
        unsigned char *pout = vertices->data + id[k] * 80 + 12 + c * 4;
        float sum = dnd_model_f32(pout) + n[c];
        unsigned word;
        memcpy(&word, &sum, 4);
        put32(pout, word);
      }
  }
}
static int fz_display(FzImport *ctx, PsoReader model, unsigned offset,
                      unsigned size, unsigned attrs, unsigned node,
                      int material, unsigned uv, unsigned matrices,
                      unsigned matrix_base, unsigned char slots[8], int reverse,
                      int indexed, unsigned pool, unsigned pool_count) {
  if (!size)
    return 1;
  if (!pso_range(model, offset, size) || !(attrs & (1u << 9)) ||
      (attrs & ~0x021fffffu) || ctx->meshes >= 4096)
    return 0;
  PsoReader r = {model.data + offset, size, &ctx->memory};
  ImportBuffer vertices = {NULL, 0, 0, &ctx->memory}, indices = vertices;
  unsigned at = 0, count = 0;
  int ok = 0;
  float low[3] = {INFINITY, INFINITY, INFINITY},
        high[3] = {-INFINITY, -INFINITY, -INFINITY};
  while (at < size) {
    unsigned primitive = 0x98, vat = 0, n;
    if (!indexed) {
      unsigned command = pso_byte(r, at++);
      if (!command)
        continue; /* Leading zero and alignment NOPs. */
      primitive = command & 0xf8;
      vat = command & 7;
      if (vat > 1 ||
          (primitive != 0x80 && primitive != 0x88 && primitive != 0x90 &&
           primitive != 0x98 && primitive != 0xa0))
        goto done;
      n = pso_u16(r, at);
      at += 2;
    } else {
      n = pso_u32(r, at);
      at += 4;
      if (!n) {
        while (at < size)
          if (pso_byte(r, at++))
            goto done;
        break;
      }
    }
    if (ctx->memory.failed || n < 3 || n > 65536 - count)
      goto done;
    unsigned first = count;
    for (unsigned i = 0; i < n; i++) {
      FzVertex v;
      unsigned matrix = 0;
      if (indexed) {
        unsigned ref = pso_u32(r, at);
        at += 4;
        if (ref % 64 || ref / 64 >= pool_count ||
            !pso_range(model, pool + ref, 64))
          goto done;
        unsigned p = pool + ref;
        memset(&v, 0, sizeof(v));
        for (unsigned c = 0; c < 3; c++) {
          v.p[c] = pso_float(model, p + c * 4);
          v.n[c] = (attrs & ((1u << 10) | (1u << 25)))
                       ? pso_float(model, p + 12 + c * 4)
                       : 0;
        }
        for (unsigned c = 0; c < 2; c++)
          v.uv[c] = (attrs & (1u << (13 + uv)))
                        ? pso_float(model, p + 24 + uv * 8 + c * 4)
                        : 0;
        for (unsigned c = 0; c < 4; c++)
          v.color[c] =
              (attrs & (1u << 11)) ? pso_byte(model, p + 48 + c) / 255.f : 1;
      } else if (!fz_vertex(r, &at, attrs, vat, uv, &v, &matrix))
        goto done;
      if (!indexed &&
          !fz_transform(model, matrices, matrix_base, slots, matrix, &v))
        goto done;
      for (unsigned c = 0; c < 3; c++) {
        low[c] = fminf(low[c], v.p[c]);
        high[c] = fmaxf(high[c], v.p[c]);
      }
      if (!(attrs & ((1u << 10) | (1u << 25))))
        memset(v.n, 0, sizeof(v.n));
      fz_emit(&vertices, &v);
      count++;
    }
    if (at > size || !fz_primitive(&indices, first, n, primitive, reverse) ||
        ctx->memory.failed)
      goto done;
  }
  if (!count || !indices.size || ctx->memory.failed)
    goto done;
  if (!(attrs & ((1u << 10) | (1u << 25))))
    fz_generate_normals(&vertices, &indices);
  /* Normalize imported and generated normals without silently accepting NaNs.
   */
  for (unsigned i = 0; i < count; i++) {
    float n[3], length = 0;
    for (unsigned c = 0; c < 3; c++) {
      n[c] = dnd_model_f32(vertices.data + i * 80 + 12 + c * 4);
      length += n[c] * n[c];
    }
    length = sqrtf(length);
    if (!isfinite(length))
      goto done;
    if (length < 1e-12f) {
      n[0] = n[2] = 0;
      n[1] = 1;
      length = 1;
    }
    for (unsigned c = 0; c < 3; c++) {
      float v = n[c] / length;
      unsigned word;
      memcpy(&word, &v, 4);
      put32(vertices.data + i * 80 + 12 + c * 4, word);
    }
  }
  ImportBuffer *out = &ctx->section[1];
  number(out, node);
  number(out, UINT_MAX);
  number(out, (unsigned)material);
  number(out, count);
  number(out, indices.size / 4);
  floats(out, low, 3);
  floats(out, high, 3);
  bytes(out, vertices.data, vertices.size);
  bytes(out, indices.data, indices.size);
  ctx->meshes++;
  ok = !ctx->memory.failed;
done:
  imp_free(&ctx->memory, vertices.data);
  imp_free(&ctx->memory, indices.data);
  return ok;
}
static int fz_model(FzImport *ctx, PsoReader r, const char *model_name) {
  if (!pso_range(r, 0, 64) || pso_u32(r, 0) != 0x47434d46 || ctx->nodes >= 1024)
    return 0;
  unsigned flags = pso_u32(r, 4), layers = pso_u16(r, 24),
           opaque = pso_u16(r, 26), translucent = pso_u16(r, 28),
           matrices = pso_byte(r, 30), header = pso_u32(r, 32);
  int indexed = (flags & 24) != 0;
  /* Empty GCMF placeholders need no drawable resources or name truncation. */
  if (!opaque && !translucent)
    return !ctx->memory.failed;
  if (strlen(model_name) > 63)
    return 0;
  if ((flags & ~29u) || (indexed && !ctx->snapshot) || layers > 4096 ||
      opaque + translucent > 4096 || header < 64 || header % 32 ||
      header < 64 + layers * 32 + matrices * 48 || header > r.size ||
      !pso_range(r, 64, layers * 32 + matrices * 48))
    return 0;
  for (unsigned c = 0; c < 4; c++)
    (void)pso_float(r, 8 + c * 4);
  if (ctx->memory.failed)
    return 0;
  /* Empty archive slots are valid but do not consume renderer nodes. */
  if (!opaque && !translucent)
    return 1;
  unsigned node = ctx->nodes++;
  ImportBuffer *nodes = &ctx->section[0];
  number(nodes, UINT_MAX);
  name(nodes, model_name, "GCMF");
  float rest[10] = {0, 0, 0, 0, 0, 0, 1, 1, 1, 1};
  floats(nodes, rest, 10);
  unsigned char slots[8];
  memcpy(slots, r.data + 40, 8);
  unsigned at = header, pool = 0, pool_count = 0, shape_base = 0;
  if (indexed) {
    if (!pso_range(r, at, 32))
      return 0;
    pool_count = pso_u32(r, at);
    pool = pso_u32(r, at + 8);
    unsigned mesh_data = pso_u32(r, at + 12);
    shape_base = header + 32;
    if (!pso_range(r, shape_base, (opaque + translucent) * 96) ||
        pool < 32 + (opaque + translucent) * 96 || pool > r.size - header ||
        mesh_data > r.size - header || mesh_data < pool ||
        pool_count > (mesh_data - pool) / 64)
      return 0;
    if (!pool_count || pool_count > 65536 || pool > r.size - header ||
        !pso_range(r, header + pool, pool_count * 64))
      return 0;
    pool += header;
    at = header + mesh_data;
  }
  for (unsigned s = 0; s < opaque + translucent; s++) {
    unsigned shape = indexed ? shape_base + s * 96 : at;
    if (!pso_range(r, shape, 96))
      return 0;
    unsigned uv = 0, attrs = pso_u32(r, shape + 28),
             lists = pso_byte(r, shape + 19), size0 = pso_u32(r, shape + 40),
             size1 = pso_u32(r, shape + 44);
    if (indexed && (lists & 12))
      return 0;
    if ((lists & ~15u) || (!(lists & 1) && size0) || (!(lists & 2) && size1))
      return 0;
    if (indexed) {
      if (size0 > UINT_MAX / 4 || size1 > UINT_MAX / 4)
        return 0;
      size0 *= 4;
      size1 *= 4;
    }
    int material = fz_material(ctx, r, shape, layers, s >= opaque, &uv);
    if (material < 0)
      return 0;
    if (layers && pso_byte(r, shape + 18) && !(attrs & (1u << (13 + uv))))
      return 0;
    for (unsigned slot = 0; slot < 8; slot++)
      if (pso_byte(r, shape + 32 + slot) != 255)
        slots[slot] = (unsigned char)pso_byte(r, shape + 32 + slot);
    if (!indexed)
      at += 96;
    if ((lists & 1) &&
        !fz_display(ctx, r, at, size0, attrs, node, material, uv, matrices,
                    64 + layers * 32, slots, 1, indexed, pool, pool_count))
      return 0;
    if (size0 > r.size - at)
      return 0;
    at += size0;
    if ((lists & 2) &&
        !fz_display(ctx, r, at, size1, attrs, node, material, uv, matrices,
                    64 + layers * 32, slots, 0, indexed, pool, pool_count))
      return 0;
    if (size1 > r.size - at)
      return 0;
    at += size1;
    if (lists & 12) {
      if ((lists & 12) != 12 || !pso_range(r, at, 32))
        return 0;
      for (unsigned slot = 0; slot < 8; slot++)
        if (pso_byte(r, at + slot) != 255)
          slots[slot] = (unsigned char)pso_byte(r, at + slot);
      unsigned size2 = pso_u32(r, at + 8), size3 = pso_u32(r, at + 12);
      if (indexed) {
        if (size2 > UINT_MAX / 4 || size3 > UINT_MAX / 4)
          return 0;
        size2 *= 4;
        size3 *= 4;
      }
      at += 32;
      if (!fz_display(ctx, r, at, size2, attrs, node, material, uv, matrices,
                      64 + layers * 32, slots, 1, indexed, pool, pool_count))
        return 0;
      if (size2 > r.size - at)
        return 0;
      at += size2;
      if (!fz_display(ctx, r, at, size3, attrs, node, material, uv, matrices,
                      64 + layers * 32, slots, 0, indexed, pool, pool_count))
        return 0;
      if (size3 > r.size - at)
        return 0;
      at += size3;
    }
  }
  return !ctx->memory.failed;
}
static int fz_geometry(FzImport *ctx, PsoReader r, int selection) {
  if (r.size < 8)
    return 0;
  if (pso_u32(r, 0) == 0x47434d46)
    return selection <= 0 && fz_model(ctx, r, "GCMF");
  unsigned count = pso_u32(r, 0), base = pso_u32(r, 4);
  if (!count || count > 1024 || selection >= (int)count ||
      base < 8 + count * 8 || base > r.size || base % 32 ||
      !pso_range(r, 8, count * 8))
    return 0;
  unsigned names = 8 + count * 8;
  for (unsigned i = 0; i < count; i++) {
    unsigned offset = pso_u32(r, 8 + i * 8), text = pso_u32(r, 12 + i * 8);
    if (offset == UINT_MAX) {
      if (selection == (int)i)
        return 0;
      continue;
    }
    if (offset > r.size - base || text >= base - names)
      return 0;
    char model_name[1024];
    if (!fz_string(r, names + text, base, model_name))
      return 0;
    unsigned end = r.size - base;
    for (unsigned j = 0; j < count; j++) {
      unsigned other = pso_u32(r, 8 + j * 8);
      if (other > offset && other < end)
        end = other;
    }
    if (end < offset || !pso_range(r, base + offset, end - offset))
      return 0;
    if (selection >= 0 && selection != (int)i)
      continue;
    PsoReader model = {r.data + base + offset, end - offset, &ctx->memory};
    if (!fz_model(ctx, model, model_name))
      return 0;
  }
  return !ctx->memory.failed;
}
static int import_fzero(const char *path, const char *textures, int selection,
                        int budget, int options, int executor) {
  FzImport ctx = {0};
  ctx.memory = (ImportMemory){0, (unsigned)budget, 0, executor};
  ctx.approximate = options & 1;
  ctx.snapshot = options & 2;
  FzAsset geometry = {0}, texture = {0};
  int token = 0;
  for (unsigned i = 0; i < 6; i++) {
    ctx.section[i].memory = &ctx.memory;
    number(&ctx.section[i], 0);
  }
  ctx.texture = imp_alloc(&ctx.memory, 4096 * sizeof(*ctx.texture));
  if (!ctx.texture || !fz_asset(&geometry, path, &ctx.memory, 0))
    goto done;
  if (textures) {
    if (!fz_asset(&texture, textures, &ctx.memory, 1))
      goto done;
    ctx.tpl = texture.view;
  } else if (geometry.root.size >= 4 &&
             pso_u32(geometry.root, 0) == 0x55aa382d) {
    /* Optional in-archive TPL. Missing/ambiguous references fail when used. */
    PsoReader found = {0};
    if (fz_member(geometry.root, NULL, 1, &found)) {
      texture.view = found;
      if (!fz_unpack(&texture.view, &texture.expanded, 1))
        goto done;
      ctx.tpl = texture.view;
    }
  }
  if (!fz_geometry(&ctx, geometry.view, selection) || !ctx.nodes ||
      !ctx.meshes || !ctx.materials)
    goto done;
  put32(ctx.section[0].data, ctx.nodes);
  put32(ctx.section[1].data, ctx.meshes);
  put32(ctx.section[2].data, ctx.materials);
  put32(ctx.section[3].data, ctx.textures);
  imp_free(&ctx.memory, geometry.base);
  geometry.base = NULL;
  imp_free(&ctx.memory, geometry.expanded);
  geometry.expanded = NULL;
  imp_free(&ctx.memory, texture.base);
  texture.base = NULL;
  imp_free(&ctx.memory, texture.expanded);
  texture.expanded = NULL;
  imp_free(&ctx.memory, ctx.texture);
  ctx.texture = NULL;
  token = scene_publish(&ctx.memory, ctx.section, budget);
done:
  imp_free(&ctx.memory, geometry.base);
  imp_free(&ctx.memory, geometry.expanded);
  imp_free(&ctx.memory, texture.base);
  imp_free(&ctx.memory, texture.expanded);
  imp_free(&ctx.memory, ctx.texture);
  for (unsigned i = 0; i < 6; i++)
    imp_free(&ctx.memory, ctx.section[i].data);
  return token;
}
int dnd_model_begin_fzero(DndString *path, DndString *textures, int selection,
                          int budget, int options) {
  if (!path || selection < -1 || budget < 1024 ||
      budget > (int)DND_MODEL_LIMIT || (options & ~3) ||
      (dnd_async_running() && dnd_async_context() != 1))
    return 0;
  char filename[1024], archive[1024];
  if (!dnd_fs_path_utf8_native(path, filename, sizeof(filename)) ||
      (textures &&
       !dnd_fs_path_utf8_native(textures, archive, sizeof(archive))))
    return 0;
  const char *texture_path = textures ? archive : NULL;
  int executor = dnd_async_native_begin();
  int token = import_fzero(filename, texture_path, selection, budget, options,
                           executor);
  dnd_async_native_end(executor);
  return token;
}
