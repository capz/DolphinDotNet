/* PSO Episode III data adapters: PRS/BML, pointer-rooted REL/NJCM chunk
 * geometry and skeletal REL motion libraries. fmt2 stages are explicitly not
 * guessed. Source offsets and chunk-cache calls never become executable code.
 */
typedef struct {
  unsigned char *file, *expanded, *inline_texture;
  PsoReader view, texture;
  char selector[1024];
} PsoExAsset;
typedef struct {
  GcVertex vertex;
  unsigned bones[4], influences, complete, normal;
} PsoExVertex;
typedef struct {
  GcImport gc;
  PsoNode *node;
  unsigned nodes;
  PsoTexture *texture;
  unsigned textures, *slots;
  float (*world)[16], (*inverse)[16];
  ImportBuffer cache, samplers;
  PsoReader geometry, list;
  unsigned list_header;
  unsigned saved[256], operations;
  int gj;
} PsoExImport;
typedef struct {
  PsoReader r;
  unsigned cursor, bits, flags;
  int ok;
} PsoPrs;
static unsigned ep3_byte(PsoPrs *s) {
  if (!s->ok || s->cursor == s->r.size) {
    s->ok = 0;
    return 0;
  }
  return s->r.data[s->cursor++];
}
static unsigned ep3_bit(PsoPrs *s) {
  if (!s->bits) {
    s->flags = ep3_byte(s);
    s->bits = 8;
  }
  unsigned bit = s->flags & 1;
  s->flags >>= 1;
  s->bits--;
  return bit;
}
static unsigned char *ep3_prs(PsoReader source, unsigned expected,
                              unsigned *size) {
  PsoPrs s = {source, 0, 0, 0, 1};
  ImportBuffer out = {0};
  out.memory = source.memory;
  if (expected > DND_MODEL_LIMIT)
    return NULL;
  if (expected) {
    out.data = imp_alloc(source.memory, expected);
    if (!out.data)
      return NULL;
    out.capacity = expected;
  }
  int terminated = 0;
  while (s.cursor < source.size && s.ok && !source.memory->failed) {
    if (ep3_bit(&s)) {
      unsigned char byte = (unsigned char)ep3_byte(&s);
      if (expected && out.size == expected)
        goto fail;
      bytes(&out, &byte, 1);
    } else {
      unsigned length, distance;
      if (ep3_bit(&s)) {
        unsigned word = ep3_byte(&s);
        word |= ep3_byte(&s) << 8;
        if (!(word >> 3)) {
          terminated = 1;
          break;
        }
        distance = 8192 - (word >> 3);
        length = word & 7;
        length = length ? length + 2 : ep3_byte(&s) + 1;
      } else {
        length = ep3_bit(&s) << 1;
        length = (length | ep3_bit(&s)) + 2;
        distance = 256 - ep3_byte(&s);
      }
      if (!s.ok || distance > out.size || length > DND_MODEL_LIMIT - out.size ||
          (expected && length > expected - out.size))
        goto fail;
      for (unsigned i = 0; i < length; i++) {
        unsigned char byte = out.data[out.size - distance];
        bytes(&out, &byte, 1);
        if (source.memory->failed)
          goto fail;
      }
    }
  }
  if (!s.ok || source.memory->failed || !out.size ||
      (expected && out.size != expected) ||
      (!terminated && (!expected || s.cursor != source.size)))
    goto fail;
  for (unsigned i = s.cursor; i < source.size; i++)
    if (source.data[i])
      goto fail;
  *size = out.size;
  return out.data;
fail:
  imp_free(source.memory, out.data);
  return NULL;
}
static int ep3_member_name(PsoReader r, unsigned at, char out[64]) {
  if (!pso_range(r, at, 32))
    return 0;
  unsigned n = 0;
  while (n < 32 && r.data[at + n]) {
    unsigned b = r.data[at + n];
    if (b < 32 || b > 126)
      return 0;
    out[n++] = (char)b;
  }
  while (n && out[n - 1] == ' ')
    n--;
  out[n] = 0;
  return n != 0;
}
static int ep3_bml(PsoExAsset *a, PsoReader r, int kind) {
  if (r.size < 128)
    return 0;
  unsigned count = pso_u32(r, 4);
  if (!count || count > 31 || !pso_range(r, 64, count * 64))
    return 0;
  unsigned offset = 2048, found = 0;
  PsoReader selected = {0}, embedded = {0};
  unsigned selected_size = 0, embedded_size = 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned at = 64 + i * 64, packed = pso_u32(r, at + 32),
             expanded = pso_u32(r, at + 40);
    unsigned tp = pso_u32(r, at + 44), te = pso_u32(r, at + 48);
    char name_text[64], texture_name[64];
    if (!ep3_member_name(r, at, name_text) || !packed || !expanded ||
        expanded > DND_MODEL_LIMIT || !pso_range(r, offset, packed))
      return 0;
    PsoReader model = gc_slice(r, offset, packed);
    offset = (offset + packed + 31) & ~31u;
    if (!pso_range(r, offset, tp) || (!!tp != !!te) || te > DND_MODEL_LIMIT)
      return 0;
    PsoReader tex = gc_slice(r, offset, tp);
    offset = (offset + tp + 31) & ~31u;
    strcpy(texture_name, name_text);
    char *dot = strrchr(texture_name, '.');
    if (dot)
      *dot = 0;
    if (strlen(texture_name) > 59)
      return 0;
    strcat(texture_name, ".gvm");
    int match = *a->selector
                    ? !strcmp(a->selector, kind == 1 ? texture_name : name_text)
                : kind == 0 ? (fz_extension(name_text, ".gj") ||
                               fz_extension(name_text, ".nj") ||
                               fz_extension(name_text, ".rel"))
                : kind == 1 ? tp != 0
                            : fz_extension(name_text, ".njm");
    if (match) {
      if (kind == 1 && !tp)
        return 0;
      selected = kind == 1 ? tex : model;
      selected_size = kind == 1 ? te : expanded;
      embedded = tex;
      embedded_size = te;
      found++;
    }
  }
  if (found != 1)
    return 0;
  unsigned size;
  a->expanded = ep3_prs(selected, selected_size, &size);
  if (!a->expanded)
    return 0;
  a->view = (PsoReader){a->expanded, size, r.memory};
  if (!kind && embedded.size) {
    a->inline_texture = ep3_prs(embedded, embedded_size, &size);
    if (!a->inline_texture)
      return 0;
    a->texture = (PsoReader){a->inline_texture, size, r.memory};
  }
  *a->selector = 0;
  return 1;
}
static int ep3_asset(PsoExAsset *a, const char *path, ImportMemory *m,
                     int kind) {
  char filename[1024];
  if (strlen(path) >= sizeof(filename))
    return 0;
  strcpy(filename, path);
  char *selector = strchr(filename, '#');
  if (selector) {
    *selector++ = 0;
    if (!*selector || strchr(selector, '#'))
      return 0;
    strcpy(a->selector, selector);
  }
  unsigned size;
  a->file = gc_file(filename, m, &size);
  if (!a->file)
    return 0;
  a->view = (PsoReader){a->file, size, m};
  if (fz_extension(filename, ".bml"))
    return ep3_bml(a, a->view, kind);
  if (fz_extension(filename, ".prs")) {
    a->expanded = ep3_prs(a->view, 0, &size);
    if (!a->expanded)
      return 0;
    a->view = (PsoReader){a->expanded, size, m};
  }
  return !m->failed;
}
static void ep3_asset_free(PsoExAsset *a, ImportMemory *m) {
  imp_free(m, a->file);
  imp_free(m, a->expanded);
  imp_free(m, a->inline_texture);
  memset(a, 0, sizeof(*a));
}
/* Existing GJ/GVM/NMDM readers use an embedded allocator context. These short
 * bridge scopes transfer that accounting by value, run only native code, then
 * immediately rebind all returned buffers to the caller's stable context. */
static int ep3_nodes(PsoExImport *c, PsoReader r, const unsigned *roots,
                     unsigned count) {
  PsoImport parser = {0};
  parser.memory = c->gc.memory;
  parser.nodes = c->node;
  parser.geometry = r;
  parser.geometry.memory = &parser.memory;
  for (unsigned i = 0; i < count; i++)
    pso_node(&parser, roots[i], -1, 0);
  c->gc.memory = parser.memory;
  c->nodes = parser.node_count;
  if (c->gc.memory.failed || !c->nodes)
    return 0;
  for (unsigned i = 0; i < c->nodes; i++) {
    PsoNode *n = &c->node[i];
    char text[32];
    snprintf(text, sizeof(text), "PSO_Node_%u", i);
    number(&c->gc.section[0], (unsigned)n->parent);
    name(&c->gc.section[0], text, text);
    floats(&c->gc.section[0], n->t, 3);
    floats(&c->gc.section[0], n->q, 4);
    floats(&c->gc.section[0], n->s, 3);
    ufbx_transform trs = {0};
    for (unsigned k = 0; k < 3; k++) {
      trs.translation.v[k] = n->t[k];
      trs.scale.v[k] = n->s[k];
    }
    for (unsigned k = 0; k < 4; k++)
      trs.rotation.v[k] = n->q[k];
    ufbx_matrix matrix = ufbx_transform_to_matrix(&trs);
    float local[16];
    dnd_model_identity(local);
    for (unsigned col = 0; col < 4; col++)
      for (unsigned row = 0; row < 3; row++)
        local[col * 4 + row] = (float)matrix.cols[col].v[row];
    if (n->parent < 0)
      memcpy(c->world[i], local, 64);
    else
      dnd_model_multiply(c->world[i], c->world[n->parent], local);
    if (!dnd_model_inverse(c->inverse[i], c->world[i]))
      return 0;
  }
  c->gc.nodes = c->nodes;
  return !c->gc.memory.failed;
}
static int ep3_textures(PsoExImport *c, PsoReader r) {
  if (!r.data)
    return 1;
  PsoImport parser = {0};
  parser.memory = c->gc.memory;
  parser.textures = c->texture;
  int ok = pso_archive(&parser, r.data, r.size);
  c->gc.memory = parser.memory;
  c->textures = parser.texture_count;
  if (!ok || c->gc.memory.failed)
    return 0;
  if (!c->gj)
    return 1; /* Ninja material commands select a sampler per texture use. */
  for (unsigned i = 0; i < c->textures; i++) {
    PsoTexture *t = &c->texture[i];
    PsoReader pixels = {t->pixels, t->size, &c->gc.memory};
    if (!gc_texture(&c->gc, pixels, 0, t->format, t->width, t->height, 1, 1, 1,
                    1, 1, 0, 0, 0))
      return 0;
  }
  return 1;
}
static int ep3_texture_index(PsoExImport *c, unsigned id) {
  if (c->list.data) {
    PsoReader r = c->list;
    unsigned count = pso_u32(r, c->list_header + 4),
             table = pso_u32(r, c->list_header);
    if (!count || count > 256 || id >= count ||
        !pso_range(r, table, count * 12))
      return -1;
    unsigned pointer = pso_u32(r, table + id * 12);
    char wanted[1024];
    if (!fz_string(r, pointer, r.size, wanted))
      return -1;
    char *dot = strrchr(wanted, '.');
    if (dot)
      *dot = 0;
    int found = -1;
    for (unsigned i = 0; i < c->textures; i++) {
      char candidate[64];
      memcpy(candidate, c->texture[i].name, 64);
      dot = strrchr(candidate, '.');
      if (dot)
        *dot = 0;
      if (c->texture[i].named && !strcasecmp(candidate, wanted)) {
        if (found >= 0)
          return -1;
        found = (int)i;
      }
    }
    if (found >= 0)
      return found;
  }
  for (unsigned i = 0; i < c->textures; i++)
    if (c->texture[i].index == id)
      return (int)i;
  return -1;
}
static int ep3_sampler(PsoExImport *c, unsigned flags, unsigned word) {
  int source = ep3_texture_index(c, word & 0x1fff);
  unsigned filter = word >> 14;
  if (source < 0)
    return -1;
  if (filter >= 2 || (word & 0x2000)) {
    if (!c->gc.approximate)
      return -1;
    filter = 1; /* Base-level fallback for mip/anisotropic effects. */
  }
  unsigned ws = flags & 0x20   ? 0
                : flags & 0x80 ? 2
                               : 1,
           wt = flags & 0x10   ? 0
                : flags & 0x40 ? 2
                               : 1;
  unsigned key = ws | wt << 2 | filter << 4;
  typedef struct {
    unsigned source, key, texture;
  } Sampler;
  Sampler *entries = (Sampler *)(void *)c->samplers.data;
  for (unsigned i = 0; i < c->samplers.size / sizeof(Sampler); i++)
    if (entries[i].source == (unsigned)source && entries[i].key == key)
      return (int)entries[i].texture;
  PsoTexture *t = &c->texture[source];
  PsoReader pixels = {t->pixels, t->size, &c->gc.memory};
  Sampler entry = {(unsigned)source, key, c->gc.textures};
  if (!gc_texture(&c->gc, pixels, 0, t->format, t->width, t->height, 1, ws, wt,
                  filter, filter, 0, 0, 0))
    return -1;
  bytes(&c->samplers, &entry, sizeof(entry));
  return c->gc.memory.failed ? -1 : (int)entry.texture;
}

static int ep3_texture_list(PsoReader r, unsigned header) {
  if (header % 4 || !pso_range(r, header, 8))
    return 0;
  unsigned table = pso_u32(r, header), count = pso_u32(r, header + 4);
  if (!count || count > 256 || table >= header ||
      count * 12 != header - table || !pso_range(r, table, count * 12))
    return 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned pointer = pso_u32(r, table + i * 12);
    char text[1024];
    if (pointer >= table || !fz_string(r, pointer, table, text) || !*text)
      return 0;
  }
  return 1;
}
static int ep3_model(PsoExImport *c, PsoExAsset *a) {
  PsoReader r = a->view;
  unsigned roots[64], count = 0;
  if (r.size >= 8 &&
      (!memcmp(r.data, "GJCM", 4) || !memcmp(r.data, "NJCM", 4) ||
       !memcmp(r.data, "NJTL", 4) || !memcmp(r.data, "GJTL", 4))) {
    unsigned at = 0;
    while (at < r.size) {
      if (!pso_range(r, at, 8))
        return 0;
      unsigned length = rw_u32(r, at + 4);
      if (!pso_range(r, at + 8, length))
        return 0;
      if (!memcmp(r.data + at, "GJCM", 4) || !memcmp(r.data + at, "NJCM", 4)) {
        if (c->geometry.data)
          return 0;
        c->gj = r.data[at] == 'G';
        c->geometry = gc_slice(r, at + 8, length);
      } else if (!memcmp(r.data + at, "GJTL", 4) ||
                 !memcmp(r.data + at, "NJTL", 4)) {
        if (c->list.data)
          return 0;
        c->list = gc_slice(r, at + 8, length);
      }
      at += 8 + length;
    }
    if (!c->geometry.data || *a->selector)
      return 0;
    roots[count++] = 0;
  } else {
    if (r.size < 64 || r.size % 4)
      return 0;
    unsigned slot = pso_u32(r, r.size - 16),
             relocation = pso_u32(r, r.size - 32),
             entries = pso_u32(r, r.size - 28);
    if (slot % 4 || relocation % 4 || slot >= relocation ||
        relocation > r.size - 32 || !entries ||
        entries > (r.size - 32 - relocation) / 2 ||
        pso_u32(r, r.size - 24) != 1 || pso_u32(r, r.size - 20) ||
        !pso_range(r, slot, 4))
      return 0;
    if (pso_u32(r, slot) == 0x666d7432)
      return 0; /* fmt2 stage descriptor is a distinct profile. */
    unsigned field = 0;
    for (unsigned i = 0; i < entries; i++) {
      unsigned delta = pso_u16(r, relocation + i * 2) * 4;
      if (delta > relocation - field)
        return 0;
      field += delta;
      if (field > relocation - 4 || pso_u32(r, field) >= relocation)
        return 0;
    }
    c->geometry = gc_slice(r, 0, relocation);
    int ended = 0;
    for (unsigned at = slot; at + 4 <= relocation && at - slot < 256; at += 4) {
      unsigned pointer = pso_u32(r, at);
      if (!pointer) {
        ended = 1;
        break;
      }
      if (ep3_texture_list(c->geometry, pointer)) {
        if (c->list.data)
          return 0;
        c->list = c->geometry;
        c->list_header = pointer;
      } else {
        if (count == 64 || !pso_range(c->geometry, pointer, 52))
          return 0;
        roots[count++] = pointer;
      }
    }
    if (!ended || !count)
      return 0;
    if (*a->selector) {
      unsigned index = 0;
      for (const char *p = a->selector; *p; p++) {
        if (*p < '0' || *p > '9' || index > 64)
          return 0;
        index = index * 10 + (unsigned)(*p - '0');
      }
      if (index >= count)
        return 0;
      roots[0] = roots[index];
      count = 1;
    }
    if (!c->list.data) {
      unsigned limit = roots[0] < 8192 ? roots[0] : 8192;
      for (unsigned at = 12; at + 8 <= limit; at += 4) {
        unsigned table = pso_u32(r, at), n = pso_u32(r, at + 4);
        if (!n || n > 256 || table >= at || n * 12 != at - table)
          continue;
        if (ep3_texture_list(c->geometry, at)) {
          if (c->list.data)
            return 0;
          c->list = c->geometry;
          c->list_header = at;
        }
      }
    }
  }
  if (!ep3_nodes(c, c->geometry, roots, count))
    return 0;
  if (!c->gj) {
    /* Ninja's default XYZ Euler application is Rz*Ry*Rx. ZXY eval is a
     * separate transform profile; do not reinterpret it as default Euler. */
    for (unsigned i = 0; i < c->nodes; i++) {
      PsoNode *n = &c->node[i];
      if (n->flags & 32)
        return 0;
      n->flags |= 32;
      float e[3];
      for (unsigned k = 0; k < 3; k++)
        e[k] = n->flags & 2 ? 0
                            : (float)(int32_t)pso_u32(c->geometry,
                                                      n->offset + 20 + k * 4) *
                                  (6.283185307179586f / 65536);
      pso_euler(n->q, e, 32);
      /* Rebuild both the serialized node pose and its bind-space matrices. */
      unsigned at = 4 + i * 108 + 68 + 12;
      for (unsigned k = 0; k < 4; k++) {
        unsigned bits;
        memcpy(&bits, &n->q[k], 4);
        put32(c->gc.section[0].data + at + k * 4, bits);
      }
      ufbx_transform trs = {0};
      for (unsigned k = 0; k < 3; k++) {
        trs.translation.v[k] = n->t[k];
        trs.scale.v[k] = n->s[k];
      }
      for (unsigned k = 0; k < 4; k++)
        trs.rotation.v[k] = n->q[k];
      ufbx_matrix matrix = ufbx_transform_to_matrix(&trs);
      float local[16];
      dnd_model_identity(local);
      for (unsigned col = 0; col < 4; col++)
        for (unsigned row = 0; row < 3; row++)
          local[col * 4 + row] = (float)matrix.cols[col].v[row];
      if (n->parent < 0)
        memcpy(c->world[i], local, 64);
      else
        dnd_model_multiply(c->world[i], c->world[n->parent], local);
      if (!dnd_model_inverse(c->inverse[i], c->world[i]))
        return 0;
    }
  }
  return !c->gc.memory.failed;
}
static PsoExVertex *ep3_cache(PsoExImport *c, unsigned index, int create) {
  if (index >= 65536)
    return NULL;
  unsigned slot = c->slots[index];
  if (!slot && create) {
    PsoExVertex blank = {0};
    unsigned count = c->cache.size / sizeof(blank);
    if (count == 65536)
      return NULL;
    bytes(&c->cache, &blank, sizeof(blank));
    if (c->gc.memory.failed)
      return NULL;
    slot = count + 1;
    c->slots[index] = slot;
  }
  return slot ? (PsoExVertex *)(void *)c->cache.data + slot - 1 : NULL;
}
static void ep3_argb(float color[4], unsigned value) {
  color[0] = (value >> 16 & 255) / 255.0f;
  color[1] = (value >> 8 & 255) / 255.0f;
  color[2] = (value & 255) / 255.0f;
  color[3] = (value >> 24) / 255.0f;
}
static int ep3_vertices(PsoExImport *c, unsigned node, unsigned at) {
  PsoReader r = c->geometry;
  for (unsigned commands = 0; commands < 65536; commands++) {
    if (!pso_range(r, at, 4))
      return 0;
    unsigned first = pso_u32(r, at);
    if ((first & 255) == 255)
      return 1;
    if (!first) {
      at += 4;
      continue;
    }
    if (!pso_range(r, at, 8))
      return 0;
    unsigned header = pso_u32(r, at), type = header & 255,
             flags = header >> 8 & 255;
    unsigned count = pso_u16(r, at + 4), base = pso_u16(r, at + 6), stride;
    int weighted = type == 37 || type == 44;
    if (type == 32)
      stride = 16;
    else if (type == 33)
      stride = 32;
    else if (type == 34)
      stride = 12;
    else if (type == 35 || type == 37 || type == 48)
      stride = 16;
    else if (type == 41)
      stride = 24;
    else if (type == 42 || type == 44)
      stride = 28;
    else if (type == 49)
      stride = 20;
    else
      return 0;
    if (!count || (header >> 16) != 1 + count * stride / 4 ||
        !pso_range(r, at + 8, count * stride) || (weighted && (flags & 3) == 3))
      return 0;
    unsigned cursor = at + 8;
    for (unsigned i = 0; i < count; i++, cursor += stride) {
      float local[3], p[3], n[3] = {0, 1, 0};
      for (unsigned k = 0; k < 3; k++)
        local[k] = pso_float(r, cursor + k * 4);
      for (unsigned row = 0; row < 3; row++)
        p[row] = c->world[node][row] * local[0] +
                 c->world[node][4 + row] * local[1] +
                 c->world[node][8 + row] * local[2] + c->world[node][12 + row];
      unsigned normal = type == 33 || type >= 41;
      if (normal) {
        float raw[3];
        unsigned offset = cursor + (type == 33 ? 16 : 12);
        if (type >= 48) {
          unsigned packed = pso_u32(r, offset);
          for (unsigned k = 0; k < 3; k++) {
            int v = (int)(packed >> ((2 - k) * 10) & 1023);
            if (v & 512)
              v -= 1024;
            raw[k] = v / 512.0f;
          }
        } else
          for (unsigned k = 0; k < 3; k++)
            raw[k] = pso_float(r, offset + k * 4);
        for (unsigned row = 0; row < 3; row++)
          n[row] = c->inverse[node][row * 4] * raw[0] +
                   c->inverse[node][row * 4 + 1] * raw[1] +
                   c->inverse[node][row * 4 + 2] * raw[2];
      }
      unsigned attributes = weighted ? pso_u32(r, cursor + stride - 4) : 0;
      unsigned index = base + (weighted ? attributes & 65535 : i);
      PsoExVertex *v = ep3_cache(c, index, 1);
      if (!v)
        return 0;
      float weight = weighted ? (attributes >> 16) / 255.0f : 1;
      if (weight <= 0 || weight > 1)
        return 0;
      if (!weighted || !(flags & 3)) {
        memset(v, 0, sizeof(*v));
        memcpy(v->vertex.p, p, 12);
        memcpy(v->vertex.n, n, 12);
        for (unsigned k = 0; k < 4; k++)
          v->vertex.color[k] = 1;
        if (type == 35 || type == 42 || type == 49)
          ep3_argb(v->vertex.color, pso_u32(r, cursor + stride - 4));
        v->normal = normal;
      } else {
        if (!v->influences || v->complete)
          return 0;
        /* A native four-weight vertex has one bind position. Reject source
         * contributions whose rest positions cannot share that representation.
         */
        for (unsigned k = 0; k < 3; k++)
          if (fabsf(v->vertex.p[k] - p[k]) > .001f ||
              (normal && fabsf(v->vertex.n[k] - n[k]) > .001f))
            return 0;
      }
      if (v->influences == 4)
        return 0;
      v->bones[v->influences] = node;
      v->vertex.weights[v->influences++] = weight;
      v->complete = !weighted || (flags & 3) == 2;
      if (v->complete) {
        float total = 0;
        for (unsigned k = 0; k < v->influences; k++)
          total += v->vertex.weights[k];
        if (fabsf(total - 1) > .006f)
          return 0;
        for (unsigned k = 0; k < v->influences; k++)
          v->vertex.weights[k] /= total;
      }
    }
    at += 8 + count * stride;
  }
  return 0;
}
typedef struct {
  float color[4];
  int texture;
  unsigned blend;
} PsoExMaterial;
static int ep3_polygons(PsoExImport *c, unsigned node, unsigned at,
                        PsoExMaterial *material, unsigned depth) {
  PsoReader r = c->geometry;
  if (depth > 8)
    return 0;
  while (++c->operations < 65536) {
    if (!pso_range(r, at, 2))
      return 0;
    unsigned header = pso_u16(r, at), type = header & 255, flags = header >> 8;
    at += 2;
    if (type == 255)
      return 1;
    if (!type)
      continue;
    if (type == 4) {
      c->saved[flags] = at;
      return 1;
    }
    if (type == 5) {
      if (!c->saved[flags] ||
          !ep3_polygons(c, node, c->saved[flags], material, depth + 1))
        return 0;
      continue;
    }
    if (type == 1) {
      material->blend = flags;
      continue;
    }
    if (type == 2 || type == 3) {
      if (!c->gc.approximate)
        return 0;
      continue;
    }
    if (type == 8 || type == 9) {
      unsigned texture = pso_u16(r, at);
      at += 2;
      if (type == 9) {
        if (!c->gc.approximate)
          return 0;
        continue;
      }
      material->texture = ep3_sampler(c, flags, texture);
      if (material->texture < 0)
        return 0;
      continue;
    }
    if (!pso_range(r, at, 2))
      return 0;
    unsigned words = pso_u16(r, at);
    at += 2;
    if (!words || !pso_range(r, at, words * 2))
      return 0;
    unsigned end = at + words * 2;
    if (type >= 17 && type <= 23) {
      material->blend = flags;
      if (type & 1) {
        if (!pso_range(r, at, 4))
          return 0;
        ep3_argb(material->color, pso_u32(r, at));
        at += 4;
      }
      if ((type & 6) && !c->gc.approximate)
        return 0;
      if (at > end)
        return 0;
      at = end;
      continue;
    }
    int uv = type == 65 || type == 66 || type == 68 || type == 69 ||
             type == 74 || type == 75;
    int corner_normal = type >= 67 && type <= 69,
        uv2 = type == 74 || type == 75;
    if (!(type >= 64 && type <= 69) && !uv2 && type != 73)
      return 0;
    if (type >= 73 && !c->gc.approximate)
      return 0;
    if ((flags & 0x40) && !c->gc.approximate)
      return 0;
    if (c->gc.materials >= 4097 || !pso_range(r, at, 2))
      return 0;
    unsigned info = pso_u16(r, at), strips = info & 0x3fff, user = info >> 14;
    at += 2;
    if (!strips || strips > 4096)
      return 0;
    unsigned mode = flags & 1 ? 2 : 0;
    if (flags & 8)
      mode |=
          4 | 16 | (material->blend >> 3 & 7) << 5 | (material->blend & 7) << 8;
    unsigned material_index = c->gc.materials++;
    floats(&c->gc.section[2], material->color, 4);
    number(&c->gc.section[2], (unsigned)material->texture);
    number(&c->gc.section[2], mode);
    scalar(&c->gc.section[2], .5f);
    number(&c->gc.section[2], (flags & 16) != 0);
    ImportBuffer v = {0}, ix = {0};
    v.memory = ix.memory = &c->gc.memory;
    GcSkin *skin = imp_alloc(&c->gc.memory, sizeof(GcSkin));
    int ok = 0, normals = 1;
    if (!skin)
      goto done;
    for (unsigned strip = 0; strip < strips; strip++) {
      if (!pso_range(r, at, 2))
        goto done;
      int signed_count = (int16_t)pso_u16(r, at);
      at += 2;
      unsigned count =
                   (unsigned)(signed_count < 0 ? -signed_count : signed_count),
               first = v.size / 80;
      if (count < 3 || first + count > 262144)
        goto done;
      for (unsigned i = 0; i < count; i++) {
        unsigned index = pso_u16(r, at);
        at += 2;
        PsoExVertex *cached = ep3_cache(c, index, 0);
        if (!cached || !cached->complete || !cached->influences)
          goto done;
        GcVertex vertex = cached->vertex;
        normals &= cached->normal != 0;
        if (uv) {
          float scale =
              type == 66 || type == 69 || type == 75 ? 1023.0f : 255.0f;
          vertex.uv[0] = (float)(int16_t)pso_u16(r, at) / scale;
          vertex.uv[1] = (float)(int16_t)pso_u16(r, at + 2) / scale;
          at += uv2 ? 8 : 4;
        }
        if (corner_normal) {
          float raw[3];
          for (unsigned k = 0; k < 3; k++)
            raw[k] = (float)(int16_t)pso_u16(r, at + k * 2) / 32767;
          for (unsigned row = 0; row < 3; row++)
            vertex.n[row] = c->inverse[node][row * 4] * raw[0] +
                            c->inverse[node][row * 4 + 1] * raw[1] +
                            c->inverse[node][row * 4 + 2] * raw[2];
          at += 6;
          normals = 1;
        }
        if (i > 1)
          at += user * 2;
        if (at > end || r.memory->failed)
          goto done;
        for (unsigned k = 0; k < cached->influences; k++) {
          unsigned bone = cached->bones[k];
          int slot = gc_skin_joint(skin, bone, 1, c->inverse[bone]);
          if (slot < 0)
            goto done;
          vertex.joints[k] = (unsigned)slot;
        }
        gc_emit(&v, &vertex);
      }
      if (!fz_primitive(&ix, first, count, 0x98, signed_count < 0))
        goto done;
    }
    if (at > end)
      goto done;
    ok = gc_mesh(&c->gc, &v, &ix, skin, material_index,
                 normals && !(flags & 32));
  done:
    imp_free(&c->gc.memory, skin);
    imp_free(&c->gc.memory, v.data);
    imp_free(&c->gc.memory, ix.data);
    if (!ok)
      return 0;
    at = end;
  }
  return 0;
}

static int ep3_gj_geometry(PsoExImport *c) {
  PsoImport parser = {0};
  parser.memory = c->gc.memory;
  parser.nodes = c->node;
  parser.node_count = c->nodes;
  parser.geometry = c->geometry;
  parser.geometry.memory = &parser.memory;
  parser.list = c->list;
  parser.list.memory = &parser.memory;
  parser.textures = c->texture;
  parser.texture_count = c->textures;
  parser.section[1] = c->gc.section[1];
  parser.section[1].memory = &parser.memory;
  int ok = 1;
  for (unsigned i = 0; i < c->nodes; i++)
    if (c->node[i].mesh) {
      unsigned count = pso_u16(parser.geometry, c->node[i].mesh + 16);
      if (count > 4096 - c->gc.meshes || !pso_mesh(&parser, i)) {
        ok = 0;
        break;
      }
      c->gc.meshes += count;
    }
  c->gc.memory = parser.memory;
  c->gc.section[1] = parser.section[1];
  c->gc.section[1].memory = &c->gc.memory;
  for (unsigned i = 0; i <= c->textures; i++) {
    float color[4] = {1, 1, 1, 1};
    floats(&c->gc.section[2], color, 4);
    number(&c->gc.section[2], i ? i - 1 : UINT_MAX);
    number(&c->gc.section[2], 0);
    scalar(&c->gc.section[2], .5f);
    number(&c->gc.section[2], 0);
  }
  c->gc.materials = c->textures + 1;
  return ok && !c->gc.memory.failed;
}
static int ep3_geometry(PsoExImport *c) {
  if (c->gj)
    return ep3_gj_geometry(c);
  c->slots = imp_alloc(&c->gc.memory, 65536 * sizeof(unsigned));
  c->cache.memory = &c->gc.memory;
  if (!c->slots)
    return 0;
  for (unsigned i = 0; i < c->nodes; i++) {
    unsigned mesh = c->node[i].mesh;
    if (!mesh)
      continue;
    if (!pso_range(c->geometry, mesh, 24))
      return 0;
    unsigned vertices = pso_u32(c->geometry, mesh),
             polygons = pso_u32(c->geometry, mesh + 4);
    if (vertices && !ep3_vertices(c, i, vertices))
      return 0;
    if (polygons) {
      PsoExMaterial material = {{1, 1, 1, 1}, -1, 0x25};
      if (!ep3_polygons(c, i, polygons, &material, 0))
        return 0;
    }
  }
  return !c->gc.memory.failed;
}
static int ep3_motion(PsoExImport *c, PsoExAsset *motion, float fps,
                      int euler_bytes) {
  PsoReader r = motion->view;
  unsigned header = 0;
  int strict = 1;
  if (r.size >= 8 && !memcmp(r.data, "NMDM", 4)) {
    if (*motion->selector || rw_u32(r, 4) != r.size - 8)
      return 0;
    r = gc_slice(r, 8, r.size - 8);
  } else {
    if (r.size < 64 || r.size % 4)
      return 0;
    unsigned slot = pso_u32(r, r.size - 16);
    if (slot % 4 || slot > r.size - 20)
      return 0;
    unsigned table = pso_u32(r, slot);
    if (table % 4 || !table || table >= slot || (slot - table) % 8)
      return 0;
    unsigned count = (slot - table) / 8, selected = UINT_MAX;
    if (count > 4096)
      return 0;
    if (*motion->selector) {
      selected = 0;
      for (const char *p = motion->selector; *p; p++) {
        if (*p < '0' || *p > '9' || selected > 4096)
          return 0;
        selected = selected * 10 + (unsigned)(*p - '0');
      }
    } else if (count == 1)
      selected = 0;
    if (selected >= count)
      return 0;
    for (unsigned i = 0; i < count; i++) {
      unsigned pointer = pso_u32(r, table + i * 8 + 4);
      if (pso_u32(r, table + i * 8) || pointer % 4 ||
          !pso_range(r, pointer, 12))
        return 0;
      unsigned type = pso_u16(r, pointer + 8),
               channels = !!(type & 1) + !!(type & 2) + !!(type & 4) +
                          !!(type & 0x2000);
      unsigned data = pso_u32(r, pointer);
      if (!channels || (type & ~0x2007u) || ((type & 2) && (type & 0x2000)) ||
          data >= pointer || (pointer - data) % (channels * 8) ||
          (pointer - data) / (channels * 8) > 1024)
        return 0;
      if (i == selected) {
        unsigned targets = 0;
        for (unsigned j = 0; j < c->nodes; j++)
          targets += !(c->node[j].flags & 64);
        if ((pointer - data) / (channels * 8) != targets)
          return 0;
        header = pointer;
      }
    }
    strict = 0;
  }
  PsoImport parser = {0};
  parser.memory = c->gc.memory;
  parser.nodes = c->node;
  parser.node_count = c->nodes;
  r.memory = &parser.memory;
  parser.section[5].memory = &parser.memory;
  int ok = pso_motion_reader(&parser, r, header, fps, euler_bytes, strict);
  c->gc.memory = parser.memory;
  if (ok) {
    imp_free(&c->gc.memory, c->gc.section[5].data);
    c->gc.section[5] = parser.section[5];
    c->gc.section[5].memory = &c->gc.memory;
  } else
    imp_free(&c->gc.memory, parser.section[5].data);
  return ok && !c->gc.memory.failed;
}
static int import_pso_ep3(const char *path, const char *textures_path,
                          const char *motion_path, float fps, int euler_bytes,
                          int budget, int approximate, int executor) {
  PsoExImport c = {0};
  gc_init(&c.gc, budget, approximate, executor);
  c.samplers.memory = &c.gc.memory;
  PsoExAsset model = {0}, texture = {0}, motion = {0};
  int token = 0;
  c.node = imp_alloc(&c.gc.memory, 1024 * sizeof(PsoNode));
  c.texture = imp_alloc(&c.gc.memory, 4096 * sizeof(PsoTexture));
  c.world = imp_alloc(&c.gc.memory, 1024 * 64);
  c.inverse = imp_alloc(&c.gc.memory, 1024 * 64);
  if (!c.node || !c.texture || !c.world || !c.inverse ||
      !ep3_asset(&model, path, &c.gc.memory, 0) || !ep3_model(&c, &model))
    goto done;
  PsoReader images = model.texture;
  if (textures_path) {
    if (!ep3_asset(&texture, textures_path, &c.gc.memory, 1) ||
        *texture.selector)
      goto done;
    images = texture.view;
  }
  if (!ep3_textures(&c, images) || !ep3_geometry(&c))
    goto done;
  if (motion_path && (!ep3_asset(&motion, motion_path, &c.gc.memory, 2) ||
                      !ep3_motion(&c, &motion, fps, euler_bytes)))
    goto done;
  ep3_asset_free(&model, &c.gc.memory);
  ep3_asset_free(&texture, &c.gc.memory);
  ep3_asset_free(&motion, &c.gc.memory);
  imp_free(&c.gc.memory, c.node);
  c.node = NULL;
  imp_free(&c.gc.memory, c.texture);
  c.texture = NULL;
  imp_free(&c.gc.memory, c.world);
  c.world = NULL;
  imp_free(&c.gc.memory, c.inverse);
  c.inverse = NULL;
  imp_free(&c.gc.memory, c.slots);
  c.slots = NULL;
  imp_free(&c.gc.memory, c.cache.data);
  c.cache.data = NULL;
  imp_free(&c.gc.memory, c.samplers.data);
  c.samplers.data = NULL;
  token = gc_publish(&c.gc, budget);
done:
  ep3_asset_free(&model, &c.gc.memory);
  ep3_asset_free(&texture, &c.gc.memory);
  ep3_asset_free(&motion, &c.gc.memory);
  imp_free(&c.gc.memory, c.node);
  imp_free(&c.gc.memory, c.texture);
  imp_free(&c.gc.memory, c.world);
  imp_free(&c.gc.memory, c.inverse);
  imp_free(&c.gc.memory, c.slots);
  imp_free(&c.gc.memory, c.cache.data);
  imp_free(&c.gc.memory, c.samplers.data);
  gc_free(&c.gc);
  return token;
}
int dnd_model_begin_pso_ep3(DndString *path, DndString *textures,
                            DndString *motion, float fps, int euler_bytes,
                            int budget, int approximate) {
  if (!path || !isfinite(fps) || fps < 1 || fps > 240 ||
      (euler_bytes != 8 && euler_bytes != 16) || budget < 1024 ||
      budget > (int)DND_MODEL_LIMIT || (approximate != 0 && approximate != 1) ||
      (dnd_async_running() && dnd_async_context() != 1))
    return 0;
  char filename[1024], archive[1024], animation[1024];
  if (!dnd_fs_path_utf8_native(path, filename, sizeof(filename)) ||
      (textures &&
       !dnd_fs_path_utf8_native(textures, archive, sizeof(archive))) ||
      (motion &&
       !dnd_fs_path_utf8_native(motion, animation, sizeof(animation))))
    return 0;
  int executor = dnd_async_native_begin();
  int token = import_pso_ep3(filename, textures ? archive : NULL,
                             motion ? animation : NULL, fps, euler_bytes,
                             budget, approximate, executor);
  dnd_async_native_end(executor);
  return token;
}
