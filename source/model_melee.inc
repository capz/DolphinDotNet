/* Melee HSD archive/JOBJ/DOBJ/POBJ/MOBJ/TOBJ and skeletal animation adapter.
 * Archive pointers remain checked file offsets; relocation never writes to or
 * executes the source. FOBJ's quantized scalars are LE inside a BE archive. */
typedef struct {
  PsoReader data;
  unsigned root;
} HsdArchive;
typedef struct {
  unsigned offset, flags, inverse;
  int parent;
  DndPose pose;
  float euler[3], bind[16];
} HsdJoint;
typedef struct {
  GcImport gc;
  HsdArchive archive;
  HsdJoint *joint;
  unsigned joints, objects;
} HsdImport;
static int hsd_archive(PsoReader source, const char *selector,
                       const char *suffix, HsdArchive *out) {
  if (source.size < 32 || pso_u32(source, 0) != source.size)
    return 0;
  unsigned data_size = pso_u32(source, 4), relocations = pso_u32(source, 8),
           publics = pso_u32(source, 12), externs = pso_u32(source, 16);
  if (!data_size || data_size > source.size - 32 ||
      relocations > data_size / 4 || !publics || publics > 4096 || externs)
    return 0; /* External fixups need a symbol resolver. */
  unsigned at = 32 + data_size;
  if (!pso_range(source, at, relocations * 4))
    return 0;
  PsoReader data = gc_slice(source, 32, data_size);
  for (unsigned i = 0; i < relocations; i++) {
    unsigned field = pso_u32(source, at + i * 4);
    if (field % 4 || !pso_range(data, field, 4) ||
        pso_u32(data, field) >= data.size)
      return 0;
  }
  at += relocations * 4;
  if (!pso_range(source, at, publics * 8))
    return 0;
  unsigned strings = at + publics * 8, found = 0, root = 0;
  for (unsigned i = 0; i < publics; i++) {
    unsigned offset = pso_u32(source, at + i * 8),
             text = pso_u32(source, at + i * 8 + 4);
    char name_text[1024];
    if (offset >= data.size || text >= source.size - strings ||
        !fz_string(source, strings + text, source.size, name_text))
      return 0;
    if (selector ? !strcmp(selector, name_text)
                 : fz_extension(name_text, suffix)) {
      root = offset;
      found++;
    }
  }
  if (found != 1)
    return 0;
  out->data = data;
  out->root = root;
  return !data.memory->failed;
}
static int hsd_asset(GcAsset *asset, HsdArchive *archive, const char *path,
                     ImportMemory *m, int animation) {
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
  asset->file = gc_file(filename, m, &size);
  if (!asset->file)
    return 0;
  PsoReader source = {asset->file, size, m};
  if (animation && selector && *selector >= '0' && *selector <= '9') {
    unsigned index = 0;
    for (const char *p = selector; *p; p++) {
      if (*p < '0' || *p > '9' || index > 4096)
        return 0;
      index = index * 10 + (unsigned)(*p - '0');
    }
    unsigned at = 0;
    for (unsigned i = 0; i <= index; i++) {
      if (!pso_range(source, at, 32))
        return 0;
      unsigned length = pso_u32(source, at);
      if (length < 32 || !pso_range(source, at, length))
        return 0;
      if (i == index) {
        source = gc_slice(source, at, length);
        break;
      }
      at += length;
    }
    selector = NULL;
  }
  const char *suffix = animation ? "_figatree" : "_joint";
  if (animation && selector && fz_extension(selector, "_animjoint"))
    suffix = "_animjoint";
  if (!hsd_archive(source, selector, suffix, archive)) {
    if (!animation || selector || m->failed ||
        !hsd_archive(source, NULL, "_animjoint", archive))
      return 0;
  }
  asset->view = source;
  return 1;
}
static int hsd_find_joint(HsdImport *c, unsigned offset) {
  for (unsigned i = 0; i < c->joints; i++)
    if (c->joint[i].offset == offset)
      return (int)i;
  return -1;
}
static int hsd_joints(HsdImport *c, unsigned offset, int parent,
                      unsigned depth) {
  PsoReader r = c->archive.data;
  do {
    if (depth > 64 || c->joints == 1024 || offset % 4 ||
        !pso_range(r, offset, 64) || hsd_find_joint(c, offset) >= 0)
      return 0;
    unsigned index = c->joints++;
    HsdJoint *j = &c->joint[index];
    j->offset = offset;
    j->parent = parent;
    j->flags = pso_u32(r, offset + 4);
    j->inverse = pso_u32(r, offset + 56);
    const unsigned unsupported = 0xf00 | 0x1000 | 0x2000 | 0x4000 | 0x20000 |
                                 0xe00000 | 0x3000000 | 0x20;
    if ((j->flags & unsupported) || pso_u32(r, offset + 60))
      return 0;
    for (unsigned k = 0; k < 3; k++) {
      j->euler[k] = pso_float(r, offset + 20 + k * 4);
      j->pose.s[k] = pso_float(r, offset + 32 + k * 4);
      j->pose.t[k] = pso_float(r, offset + 44 + k * 4);
      /* HSD's accumulated segment-scale compensation is not ordinary TRS. */
      if (fabsf(j->pose.s[k] - 1) > 1e-6f)
        return 0;
    }
    pso_euler(j->pose.q, j->euler, 32);
    dnd_model_identity(j->bind);
    if (j->inverse) {
      if (!pso_range(r, j->inverse, 48))
        return 0;
      for (unsigned row = 0; row < 3; row++)
        for (unsigned col = 0; col < 4; col++)
          j->bind[col * 4 + row] =
              pso_float(r, j->inverse + row * 16 + col * 4);
    }
    unsigned child = pso_u32(r, offset + 8);
    if (child && !hsd_joints(c, child, (int)index, depth + 1))
      return 0;
    offset = pso_u32(r, offset + 12);
  } while (offset);
  return !r.memory->failed;
}
static int hsd_array(PsoReader r, unsigned desc, J3dArray arrays[26],
                     unsigned attrs[26], unsigned types[26], unsigned *count,
                     unsigned *seen) {
  *count = *seen = 0;
  for (unsigned i = 0; i < 26; i++) {
    if (!pso_range(r, desc, 24))
      return 0;
    unsigned attr = pso_u32(r, desc), type = pso_u32(r, desc + 4),
             n = pso_u32(r, desc + 8), component = pso_u32(r, desc + 12);
    if (attr == 255)
      return (*seen & (1u << 9)) != 0;
    if (attr >= 26 || (attr > 20 && attr != 25) || (*seen & (1u << attr)) ||
        type < 1 || type > 3)
      return 0;
    *seen |= 1u << attr;
    attrs[i] = attr;
    types[i] = type;
    *count = i + 1;
    J3dArray *a = &arrays[attr];
    if (attr < 9) {
      if (type != 1)
        return 0;
      a->stride = 1;
      desc += 24;
      continue;
    }
    a->type = component;
    a->shift = pso_byte(r, desc + 16);
    unsigned encoded;
    if (attr == 11 || attr == 12) {
      static const unsigned width[6] = {2, 3, 4, 2, 3, 4};
      if (component > 5 || n > 1)
        return 0;
      encoded = width[component];
      a->components = 4;
    } else {
      if (component > 4 || a->shift > 31 || n > 1)
        return 0;
      a->components = attr == 9                  ? n + 2
                      : attr == 10 || attr == 25 ? n ? 9 : 3
                                                 : n + 1;
      encoded = a->components * (component < 2 ? 1 : component < 4 ? 2 : 4);
    }
    a->stride = type == 1 ? encoded : pso_u16(r, desc + 18);
    a->offset = pso_u32(r, desc + 20);
    a->end = r.size;
    if (a->stride < encoded || a->stride > 256 ||
        (type != 1 && (!a->offset || !pso_range(r, a->offset, a->stride))))
      return 0;
    desc += 24;
  }
  return 0;
}
typedef struct {
  unsigned material, rgb, alpha;
} HsdMaterial;
static int hsd_material(HsdImport *c, unsigned mobj, unsigned cull,
                        HsdMaterial *out) {
  PsoReader r = c->archive.data;
  if (!mobj || !pso_range(r, mobj, 24) || c->gc.materials >= 4097)
    return 0;
  unsigned flags = pso_u32(r, mobj + 4), tobj = pso_u32(r, mobj + 8),
           material = pso_u32(r, mobj + 12);
  if (!material || !pso_range(r, material, 20))
    return 0;
  float color[4];
  for (unsigned k = 0; k < 3; k++)
    color[k] = pso_byte(r, material + 4 + k) / 255.0f;
  color[3] = pso_float(r, material + 12);
  if (color[3] < 0 || color[3] > 1)
    return 0;
  out->rgb = (flags & 3) >= 2;
  out->alpha = (flags >> 13 & 3) >= 2;
  if ((flags & 3) == 2)
    for (unsigned k = 0; k < 3; k++)
      color[k] = 1;
  if ((flags >> 13 & 3) == 2)
    color[3] = 1;
  if (!c->gc.approximate && ((flags & ((1u << 3) | (1u << 26) | (1u << 27))) ||
                             pso_u32(r, mobj + 16)))
    return 0;
  unsigned texture = UINT_MAX, mode = flags & 4 ? 0 : 2;
  if (tobj) {
    if (!pso_range(r, tobj, 92))
      return 0;
    unsigned tex_flags = pso_u32(r, tobj + 64), image = pso_u32(r, tobj + 76),
             palette = pso_u32(r, tobj + 80), lod = pso_u32(r, tobj + 84);
    if (!image || !pso_range(r, image, 24) ||
        (palette && !pso_range(r, palette, 16)))
      return 0;
    unsigned min = lod ? pso_u32(r, lod) : 1, mag = pso_u32(r, tobj + 72),
             mipmaps = pso_u32(r, image + 12);
    float minlod = pso_float(r, image + 16), maxlod = pso_float(r, image + 20);
    if (minlod < 0 || maxlod < minlod || maxlod > 10 || mipmaps > 1 ||
        (mipmaps && floorf(maxlod) != maxlod))
      return 0;
    if (!c->gc.approximate) {
      if (pso_u32(r, tobj + 4) || pso_u32(r, tobj + 12) != 4 ||
          pso_u32(r, tobj + 88) || (tex_flags & 15) ||
          (tex_flags >> 16 & 15) != 4 || (tex_flags >> 20 & 15) != 3 ||
          pso_byte(r, tobj + 60) != 1 || pso_byte(r, tobj + 61) != 1 || minlod)
        return 0;
      for (unsigned k = 0; k < 3; k++)
        if (pso_float(r, tobj + 16 + k * 4) ||
            pso_float(r, tobj + 40 + k * 4) ||
            fabsf(pso_float(r, tobj + 28 + k * 4) - 1) > 1e-6f)
          return 0;
      if (lod && (!pso_range(r, lod, 16) || pso_float(r, lod + 4)))
        return 0;
    }
    texture = c->gc.textures;
    if (!gc_texture(&c->gc, r, pso_u32(r, image), pso_u32(r, image + 8),
                    pso_u16(r, image + 4), pso_u16(r, image + 6),
                    mipmaps ? (unsigned)maxlod + 1 : 1, pso_u32(r, tobj + 52),
                    pso_u32(r, tobj + 56), min, mag,
                    palette ? pso_u32(r, palette) : 0,
                    palette ? pso_u16(r, palette + 12) : 0,
                    palette ? pso_u32(r, palette + 4) : 0))
      return 0;
  }
  float cutoff = .5f;
  unsigned pe = pso_u32(r, mobj + 20);
  if (pe) {
    if (!pso_range(r, pe, 12))
      return 0;
    unsigned blend = pso_byte(r, pe + 4), src = pso_byte(r, pe + 5),
             dst = pso_byte(r, pe + 6);
    if (blend > 1 || src > 7 || dst > 7)
      return 0;
    if (blend)
      mode |= 4 | 16 | src << 5 | dst << 8;
    unsigned ac = pso_byte(r, pe + 9), bc = pso_byte(r, pe + 11),
             op = pso_byte(r, pe + 10);
    if (ac != 7 || bc != 7 || op != 0) {
      if (!blend && (ac == 4 || ac == 6) && bc == 7 && op == 0) {
        mode |= 1;
        cutoff = (pso_byte(r, pe + 1) + (ac == 4 ? 1.0f : 0)) / 255;
      } else if (!c->gc.approximate)
        return 0;
    }
    if (!c->gc.approximate &&
        (pso_byte(r, pe + 8) != 3 || !(pso_byte(r, pe) & 16)))
      return 0;
  } else if (flags & (1u << 30))
    mode |= 4;
  if (cull == 0x4000 && !c->gc.approximate)
    return 0;
  out->material = c->gc.materials++;
  floats(&c->gc.section[2], color, 4);
  number(&c->gc.section[2], texture);
  number(&c->gc.section[2], mode);
  scalar(&c->gc.section[2], cutoff);
  number(&c->gc.section[2], cull == 0);
  return !c->gc.memory.failed;
}
static int hsd_weights(HsdImport *c, unsigned owner, unsigned kind,
                       unsigned contents, unsigned matrix, GcSkin *skin,
                       GcVertex *v) {
  PsoReader r = c->archive.data;
  if (!kind) {
    int node = contents ? hsd_find_joint(c, contents) : (int)owner;
    if (node < 0 || matrix)
      return 0;
    float identity[16];
    dnd_model_identity(identity);
    int slot = gc_skin_joint(skin, (unsigned)node, 0, identity);
    if (slot < 0)
      return 0;
    v->joints[0] = (unsigned)slot;
    v->weights[0] = 1;
    return 1;
  }
  if (kind != 0x2000 || !contents || matrix >= 10 ||
      !pso_range(r, contents, (matrix + 1) * 4))
    return 0;
  unsigned envelope = pso_u32(r, contents + matrix * 4);
  if (!envelope)
    return 0;
  unsigned count = 0, ids[4];
  float weights[4], total = 0;
  for (; count < 5; count++) {
    if (!pso_range(r, envelope + count * 8, 8))
      return 0;
    unsigned joint = pso_u32(r, envelope + count * 8);
    if (!joint)
      break;
    if (count == 4)
      return 0;
    int id = hsd_find_joint(c, joint);
    if (id < 0)
      return 0;
    ids[count] = (unsigned)id;
    weights[count] = pso_float(r, envelope + count * 8 + 4);
    if (weights[count] < 0 || weights[count] > 1)
      return 0;
    total += weights[count];
  }
  if (!count || fabsf(total - 1) > .001f)
    return 0;
  int root = (c->joint[owner].flags & 2) != 0;
  if (!root && !(c->joint[owner].flags & 1))
    return 0;
  float model_bind[16];
  dnd_model_identity(model_bind);
  if (!root && !dnd_model_inverse(model_bind, c->joint[owner].bind))
    return 0;
  for (unsigned i = 0; i < count; i++) {
    float bind[16];
    if (root && count == 1)
      dnd_model_identity(bind);
    else
      dnd_model_multiply(bind, c->joint[ids[i]].bind, model_bind);
    int slot =
        gc_skin_joint(skin, ids[i], root && count == 1 ? 0 : owner + 1, bind);
    if (slot < 0)
      return 0;
    v->joints[i] = (unsigned)slot;
    v->weights[i] = weights[i];
  }
  return !c->gc.memory.failed;
}
static int hsd_polygon(HsdImport *c, unsigned owner, unsigned offset,
                       unsigned mobj) {
  PsoReader r = c->archive.data;
  while (offset) {
    if (++c->objects > 4096 || !pso_range(r, offset, 24))
      return 0;
    unsigned flags = pso_u16(r, offset + 12), kind = flags & 0x3000;
    if ((kind != 0 && kind != 0x2000) || (flags & 0xc000) == 0xc000)
      return 0;
    HsdMaterial material;
    if (!hsd_material(c, mobj, flags & 0xc000, &material))
      return 0;
    J3dArray array[26] = {{0}};
    unsigned attrs[26], types[26], count, seen;
    if (!hsd_array(r, pso_u32(r, offset + 8), array, attrs, types, &count,
                   &seen))
      return 0;
    unsigned dl_offset = pso_u32(r, offset + 16),
             length = pso_u16(r, offset + 14) * 32;
    if (!length || !pso_range(r, dl_offset, length))
      return 0;
    PsoReader dl = gc_slice(r, dl_offset, length);
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
      unsigned n = pso_u16(dl, at), first = v.size / 80;
      at += 2;
      if (!n || first + n > 262144)
        goto done;
      for (unsigned i = 0; i < n; i++) {
        GcVertex vertex = {0};
        unsigned matrix = 0;
        for (unsigned k = 0; k < 4; k++)
          vertex.color[k] = 1;
        for (unsigned a = 0; a < count; a++)
          if (!gc_attribute(r, array, c->gc.approximate, dl, &at, attrs[a],
                            types[a], &vertex, &matrix))
            goto done;
        if (!material.rgb)
          for (unsigned k = 0; k < 3; k++)
            vertex.color[k] = 1;
        if (!material.alpha)
          vertex.color[3] = 1;
        if (!hsd_weights(c, owner, kind, pso_u32(r, offset + 20), matrix, skin,
                         &vertex))
          goto done;
        gc_emit(&v, &vertex);
      }
      if (!fz_primitive(&ix, first, n, command, 0))
        goto done;
    }
    ok = gc_mesh(&c->gc, &v, &ix, skin, material.material,
                 (seen & ((1u << 10) | (1u << 25))) != 0);
  done:
    imp_free(&c->gc.memory, skin);
    imp_free(&c->gc.memory, v.data);
    imp_free(&c->gc.memory, ix.data);
    if (!ok)
      return 0;
    offset = pso_u32(r, offset + 4);
  }
  return !r.memory->failed;
}
static int hsd_geometry(HsdImport *c) {
  PsoReader r = c->archive.data;
  for (unsigned i = 0; i < c->joints; i++) {
    HsdJoint *j = &c->joint[i];
    char text[32];
    snprintf(text, sizeof(text), "HSD_Joint_%u", i);
    number(&c->gc.section[0], (unsigned)j->parent);
    name(&c->gc.section[0], text, text);
    floats(&c->gc.section[0], j->pose.t, 3);
    floats(&c->gc.section[0], j->pose.q, 4);
    floats(&c->gc.section[0], j->pose.s, 3);
    unsigned dobj = pso_u32(r, j->offset + 16);
    if (j->flags & 16)
      continue;
    while (dobj) {
      if (++c->objects > 4096 || !pso_range(r, dobj, 16) ||
          !hsd_polygon(c, i, pso_u32(r, dobj + 12), pso_u32(r, dobj + 8)))
        return 0;
      dobj = pso_u32(r, dobj + 4);
    }
  }
  c->gc.nodes = c->joints;
  return !r.memory->failed;
}

static unsigned hsd_vlq(PsoReader r, unsigned *at) {
  unsigned value = 0;
  for (unsigned i = 0; i < 5; i++) {
    if (!pso_range(r, *at, 1))
      return 0;
    unsigned b = pso_byte(r, (*at)++);
    if (i == 4 && (b & 0xf0)) {
      r.memory->failed = 1;
      return 0;
    }
    value |= (b & 127) << (i * 7);
    if (!(b & 128))
      return value;
  }
  r.memory->failed = 1;
  return 0;
}
static float hsd_value(PsoReader r, unsigned *at, unsigned format) {
  unsigned kind = format >> 5, shift = format & 31;
  float value;
  if (kind > 4 || (!kind && shift)) {
    r.memory->failed = 1;
    return 0;
  }
  if (!kind) {
    value = rw_float(r, *at);
    *at += 4;
  } else if (kind == 1 || kind == 2) {
    unsigned n = rw_u16(r, *at);
    *at += 2;
    value = kind == 1 ? (float)(int16_t)n : (float)n;
  } else {
    unsigned n = pso_byte(r, (*at)++);
    value = kind == 3 ? (float)(int8_t)n : (float)n;
  }
  return ldexpf(value, -(int)shift);
}
static int hsd_curve(PsoReader r, unsigned pointer, unsigned length,
                     unsigned frac_value, unsigned frac_slope, float start,
                     GcCurve *out) {
  if (!length || !pso_range(r, pointer, length) || !isfinite(start))
    return 0;
  PsoReader stream = gc_slice(r, pointer, length);
  ImportBuffer points = {0};
  points.memory = r.memory;
  unsigned at = 0, previous = 0, wait = 0;
  float time = -start, p0 = 0, p1 = 0, d0 = 0, d1 = 0;
  int slope = 0, ok = 0;
  while (at < stream.size) {
    unsigned packed = hsd_vlq(stream, &at), opcode = packed & 15,
             count = (packed >> 4) + 1;
    if (!opcode)
      break;
    if (opcode > 6 || count > 65536 || stream.memory->failed)
      goto done;
    for (unsigned i = 0; i < count; i++) {
      if (opcode == 5) {
        d0 = d1;
        d1 = hsd_value(stream, &at, frac_slope);
        slope = 1;
        continue;
      }
      float value = hsd_value(stream, &at, frac_value);
      p0 = p1;
      p1 = value;
      if (opcode == 3 || opcode == 4) {
        d0 = d1;
        d1 = opcode == 4 ? hsd_value(stream, &at, frac_slope) : 0;
      } else if (!slope) {
        d0 = d1;
        d1 = 0;
      }
      if (opcode == 6)
        p0 = p1;
      slope = 0;
      if (previous) {
        GcKey a = {0}, b = {0};
        a.time = time;
        a.value = p0;
        a.out = d0;
        a.mode = previous == 1 || previous == 6 ? 2 : previous == 2 ? 1 : 0;
        b.time = time + (float)wait;
        b.value = p1;
        b.in = d1;
        b.out = d1;
        b.mode = 2;
        if (!isfinite(b.time) || b.time > 1000000)
          goto done;
        if (points.size) {
          GcKey *last =
              (GcKey *)(void *)(points.data + points.size - sizeof(GcKey));
          if (last->time != a.time)
            goto done;
          a.in = last->in;
          memcpy(last, &a, sizeof(a));
        } else
          bytes(&points, &a, sizeof(a));
        if (wait)
          bytes(&points, &b, sizeof(b));
        else if (points.size)
          memcpy(points.data + points.size - sizeof(b), &b, sizeof(b));
        time += (float)wait;
      }
      /* KEY has no following duration in the HSD stream. */
      wait = opcode == 6 ? 0 : hsd_vlq(stream, &at);
      previous = opcode;
      if (stream.memory->failed || points.size / sizeof(GcKey) > 65535)
        goto done;
    }
  }
  if (!points.size) {
    GcKey key = {0};
    key.time = time;
    key.value = p1;
    key.mode = 2;
    bytes(&points, &key, sizeof(key));
  }
  if (r.memory->failed)
    goto done;
  out->key = (GcKey *)(void *)points.data;
  out->count = points.size / sizeof(GcKey);
  for (unsigned i = 0; i < out->count; i++) {
    GcKey *key = &out->key[i];
    if (!isfinite(key->value) || !isfinite(key->in) || !isfinite(key->out) ||
        (i && key->time <= out->key[i - 1].time)) {
      out->key = NULL;
      out->count = 0;
      goto done;
    }
  }
  points.data = NULL;
  ok = 1;
done:
  imp_free(r.memory, points.data);
  return ok;
}
static int hsd_anim_nodes(HsdImport *c, PsoReader r, unsigned offset,
                          int parent, unsigned depth, unsigned *index,
                          unsigned *aobjs, unsigned *seen) {
  do {
    if (depth > 64 || *index >= c->joints || !pso_range(r, offset, 20))
      return 0;
    for (unsigned i = 0; i < *index; i++)
      if (seen[i] == offset)
        return 0;
    unsigned node = (*index)++;
    if (c->joint[node].parent != parent || pso_u32(r, offset + 12))
      return 0;
    seen[node] = offset;
    aobjs[node] = pso_u32(r, offset + 8);
    unsigned child = pso_u32(r, offset);
    if (child && child != UINT_MAX &&
        !hsd_anim_nodes(c, r, child, (int)node, depth + 1, index, aobjs, seen))
      return 0;
    offset = pso_u32(r, offset + 4);
  } while (offset);
  return !r.memory->failed;
}
static int hsd_animation(HsdImport *c, HsdArchive archive, float fps) {
  PsoReader r = archive.data;
  int figa = pso_u32(r, archive.root) == 1;
  unsigned *aobjs = NULL, *seen = NULL, counts = 0, table = 0;
  float duration = 0;
  int ok = 0;
  ImportBuffer tracks = {0};
  tracks.memory = &c->gc.memory;
  number(&tracks, 0);
  GcCurve curves[9] = {{0}};
  if (figa) {
    if (!pso_range(r, archive.root, 20))
      goto done;
    duration = pso_float(r, archive.root + 8);
    counts = pso_u32(r, archive.root + 12);
    table = pso_u32(r, archive.root + 16);
    if (!pso_range(r, counts, c->joints + 1) ||
        pso_byte(r, counts + c->joints) != 255)
      goto done;
  } else {
    aobjs = imp_alloc(&c->gc.memory, c->joints * sizeof(unsigned));
    seen = imp_alloc(&c->gc.memory, c->joints * sizeof(unsigned));
    unsigned index = 0;
    if (!aobjs || !seen ||
        !hsd_anim_nodes(c, r, archive.root, -1, 0, &index, aobjs, seen) ||
        index != c->joints)
      goto done;
    for (unsigned i = 0; i < c->joints; i++)
      if (aobjs[i]) {
        if (!pso_range(r, aobjs[i], 16) || pso_u32(r, aobjs[i] + 12))
          goto done;
        duration = fmaxf(duration, pso_float(r, aobjs[i] + 4));
      }
  }
  if (!isfinite(duration) || duration < 0 || duration * 60 / fps > 65535)
    goto done;
  for (unsigned node = 0; node < c->joints; node++) {
    unsigned descriptors = figa ? pso_byte(r, counts + node) : 0;
    unsigned linked = !figa && aobjs[node] ? pso_u32(r, aobjs[node] + 8) : 0;
    for (unsigned i = 0; figa ? i < descriptors : linked != 0; i++) {
      if (i >= 128)
        goto done;
      unsigned desc = figa ? table : linked;
      if (!pso_range(r, desc, figa ? 12 : 20))
        goto done;
      unsigned type = pso_byte(r, desc + (figa ? 4 : 12)), property, axis;
      if (type >= 1 && type <= 3) {
        property = 1;
        axis = type - 1;
      } else if (type >= 5 && type <= 7) {
        property = 0;
        axis = type - 5;
      } else if (type >= 8 && type <= 10) {
        property = 2;
        axis = type - 8;
      } else
        goto done; /* Visibility/path/effect callbacks are not TRS. */
      GcCurve *curve = &curves[property * 3 + axis];
      if (curve->key)
        goto done;
      float start =
          figa ? (float)(int16_t)pso_u16(r, desc + 2) : pso_float(r, desc + 8);
      if (!hsd_curve(r, pso_u32(r, desc + (figa ? 8 : 16)),
                     figa ? pso_u16(r, desc) : pso_u32(r, desc + 4),
                     pso_byte(r, desc + (figa ? 5 : 13)),
                     pso_byte(r, desc + (figa ? 6 : 14)), start, curve))
        goto done;
      if (property == 2)
        for (unsigned k = 0; k < curve->count; k++)
          if (fabsf(curve->key[k].value - 1) > 1e-6f || curve->key[k].in ||
              curve->key[k].out)
            goto done;
      if (figa)
        table += 12;
      else
        linked = pso_u32(r, desc);
    }
    for (unsigned property = 0; property < 3; property++)
      for (unsigned axis = 0; axis < 3; axis++) {
        GcCurve *curve = &curves[property * 3 + axis];
        if (!curve->key) {
          curve->key = imp_alloc(&c->gc.memory, sizeof(GcKey));
          curve->count = 1;
          if (!curve->key)
            goto done;
          curve->key->value = property == 0   ? c->joint[node].pose.t[axis]
                              : property == 1 ? c->joint[node].euler[axis]
                                              : 1;
        }
      }
    for (unsigned property = 0; property < 3; property++)
      if (!gc_track(&tracks, curves + property * 3, node, property, duration,
                    fps, 0))
        goto done;
    gc_curves_free(&c->gc.memory, curves, 9);
  }
  gc_clip(&c->gc, &tracks, duration / fps, c->joints * 3);
  ok = !r.memory->failed;
done:
  gc_curves_free(&c->gc.memory, curves, 9);
  imp_free(&c->gc.memory, tracks.data);
  imp_free(&c->gc.memory, aobjs);
  imp_free(&c->gc.memory, seen);
  return ok;
}
static int import_melee(const char *path, const char *animation_path, float fps,
                        int budget, int approximate, int executor) {
  HsdImport c = {0};
  gc_init(&c.gc, budget, approximate, executor);
  GcAsset model = {0}, animation = {0};
  HsdArchive motion = {0};
  int token = 0;
  if (!hsd_asset(&model, &c.archive, path, &c.gc.memory, 0))
    goto done;
  c.joint = imp_alloc(&c.gc.memory, 1024 * sizeof(*c.joint));
  if (!c.joint || !hsd_joints(&c, c.archive.root, -1, 0) || !hsd_geometry(&c))
    goto done;
  if (animation_path &&
      (!hsd_asset(&animation, &motion, animation_path, &c.gc.memory, 1) ||
       !hsd_animation(&c, motion, fps)))
    goto done;
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.joint);
  c.joint = NULL;
  token = gc_publish(&c.gc, budget);
done:
  gc_asset_free(&model, &c.gc.memory);
  gc_asset_free(&animation, &c.gc.memory);
  imp_free(&c.gc.memory, c.joint);
  gc_free(&c.gc);
  return token;
}
