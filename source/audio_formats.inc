/* Independently implemented data readers for documented GameCube audio formats.
 * Included by audio.c; codec mathematics is integer-only during playback. */
enum {
  AC_DSP = 1,
  AC_AFC,
  AC_AFC2,
  AC_ADX,
  AC_DTK,
  AC_PCM8,
  AC_PCM16,
  AC_PCM16LE
};
typedef struct {
  const uint8_t *data;
  unsigned size;
  int failed;
} AudioView;
static int av_range(AudioView *v, unsigned at, unsigned n) {
  if (at > v->size || n > v->size - at) {
    v->failed = 1;
    return 0;
  }
  return 1;
}
static unsigned av_be16(AudioView *v, unsigned a) {
  return av_range(v, a, 2) ? (unsigned)v->data[a] << 8 | v->data[a + 1] : 0;
}
static unsigned av_be32(AudioView *v, unsigned a) {
  return av_range(v, a, 4)
             ? (unsigned)v->data[a] << 24 | (unsigned)v->data[a + 1] << 16 |
                   (unsigned)v->data[a + 2] << 8 | v->data[a + 3]
             : 0;
}
static unsigned av_le32(AudioView *v, unsigned a) {
  return av_range(v, a, 4) ? le32(v->data + a) : 0;
}
static unsigned av_byte(AudioView *v, unsigned a) {
  return av_range(v, a, 1) ? v->data[a] : 0;
}
static int av_id(AudioView *v, unsigned a, const char *id, unsigned n) {
  return av_range(v, a, n) && !memcmp(v->data + a, id, n);
}
static unsigned ac_spf(unsigned codec) {
  return codec == AC_DSP                       ? 14
         : codec == AC_AFC || codec == AC_AFC2 ? 16
         : codec == AC_ADX                     ? 32
         : codec == AC_DTK                     ? 28
                                               : 1;
}
static unsigned ac_bytes(unsigned codec) {
  return codec == AC_DSP    ? 8
         : codec == AC_AFC  ? 9
         : codec == AC_AFC2 ? 5
         : codec == AC_ADX  ? 18
         : codec == AC_DTK  ? 32
         : codec == AC_PCM8 ? 1
                            : 2;
}
static unsigned ac_nibbles(unsigned n) {
  unsigned tail = n % 16;
  return n / 16 * 14 + (tail > 2 ? tail - 2 : 0);
}
static int ac_spans(DndAudioSource *s, unsigned n, unsigned available) {
  if (!n || n > 65536 || n > available / sizeof(DndAudioSpan))
    return 0;
  s->span = calloc(n, sizeof(*s->span));
  if (!s->span)
    return 0;
  s->span_count = n;
  return 1;
}
static void ac_coefficients(DndAudioSource *s, AudioView *v, unsigned at,
                            unsigned spacing, unsigned history) {
  for (unsigned c = 0; c < s->channels; c++) {
    for (unsigned i = 0; i < 16; i++)
      s->coefficients[c][i] = (int16_t)av_be16(v, at + c * spacing + i * 2);
    if (history) {
      s->history[c][0] = (int16_t)av_be16(v, history + c * spacing);
      s->history[c][1] = (int16_t)av_be16(v, history + c * spacing + 2);
    }
  }
}
static int ac_flat(DndAudioSource *s, unsigned offset, unsigned interleave,
                   unsigned samples, unsigned budget) {
  if (!ac_spans(s, 1, budget))
    return 0;
  s->samples = samples;
  DndAudioSpan *b = s->span;
  b->samples = samples;
  b->offset = offset;
  b->interleave = interleave ? interleave : ac_bytes(s->codec);
  b->stride = b->interleave * s->channels;
  for (unsigned c = 0; c < s->channels; c++)
    b->channel[c] = c * b->interleave;
  return 1;
}
static int ac_validate_source(DndAudioSource *s) {
  if (!s->data || !s->span || !s->samples || s->samples > INT_MAX / 4u ||
      s->rate < 5000 || s->rate > 48000 || s->channels < 1 || s->channels > 2)
    return 0;
  if (s->has_loop && (s->loop_start >= s->loop_end || s->loop_end > s->samples))
    return 0;
  unsigned end = 0, bytes = ac_bytes(s->codec), spf = ac_spf(s->codec);
  for (unsigned i = 0; i < s->span_count; i++) {
    DndAudioSpan *b = s->span + i;
    if (b->first != end || !b->samples || b->samples > s->samples - end ||
        !b->interleave || b->interleave % bytes || b->stride < b->interleave)
      return 0;
    uint64_t last = ((uint64_t)b->samples + spf - 1) / spf * bytes - 1;
    for (unsigned c = 0; c < (s->codec == AC_DTK ? 1 : s->channels); c++) {
      uint64_t at = (uint64_t)b->offset + last / b->interleave * b->stride +
                    b->channel[c] + last % b->interleave;
      if (at >= s->size)
        return 0;
    }
    end += b->samples;
  }
  if (!s->has_loop) {
    s->loop_start = 0;
    s->loop_end = s->samples;
  }
  return end == s->samples;
}
static int ac_dsp(DndAudioSource *s, AudioView *v, unsigned budget) {
  if (!av_range(v, 0, 0x60) || av_be16(v, 14) || av_be16(v, 60) ||
      av_be16(v, 12) > 1 || av_be16(v, 74) > 1)
    return 0;
  s->codec = AC_DSP;
  s->channels = 1;
  s->rate = av_be32(v, 8);
  unsigned samples = av_be32(v, 0), nibbles = av_be32(v, 4);
  if (ac_nibbles(nibbles) != samples || av_be32(v, 24) > 2)
    return 0;
  s->has_loop = av_be16(v, 12);
  s->loop_start = ac_nibbles(av_be32(v, 16));
  s->loop_end = ac_nibbles(av_be32(v, 20) + 1);
  ac_coefficients(s, v, 28, 0, 64);
  return ac_flat(s, 96, 0, samples, budget);
}
static int ac_adx(DndAudioSource *s, AudioView *v, unsigned budget) {
  unsigned offset = av_be16(v, 2) + 4, type = av_byte(v, 4),
           version = av_be16(v, 18), channels = av_byte(v, 7);
  if (av_be16(v, 0) != 0x8000 || offset < 26 ||
      !av_id(v, offset - 6, "(c)CRI", 6) || av_byte(v, 5) != 18 ||
      av_byte(v, 6) != 4 || channels < 1 || channels > 2 || type < 2 ||
      type > 4 || (version != 0x300 && version != 0x400 && version != 0x500))
    return 0;
  s->codec = AC_ADX;
  s->channels = channels;
  s->rate = av_be32(v, 8);
  s->adx_version = version;
  s->adx_type = type;
  unsigned cutoff = av_be16(v, 16);
  if (s->rate < 5000 || s->rate > 48000 || cutoff >= s->rate / 2)
    return 0;
  float z = cosf((float)(6.283185307179586 * cutoff / s->rate)),
        a = (float)(1.4142135623730951 - z),
        b = (float)(1.4142135623730951 - 1.0);
  float c = (a - sqrtf((a + b) * (a - b))) / b;
  if (!isfinite(c) || c < 0 || c > 1)
    return 0;
  for (unsigned ch = 0; ch < channels; ch++) {
    s->coefficients[ch][0] = (int)(c * 8192);
    s->coefficients[ch][1] = (int)(c * c * -4096);
  }
  unsigned loop = 20;
  if (version == 0x400) {
    unsigned history = channels > 1 ? 4 * channels : 8;
    if (offset < 24 + history + 6)
      return 0;
    for (unsigned ch = 0; ch < channels; ch++) {
      s->history[ch][0] = (int16_t)av_be16(v, 24 + ch * 4);
      s->history[ch][1] = (int16_t)av_be16(v, 26 + ch * 4);
    }
    loop = 24 + history;
    if (loop + 12 <= offset - 6 && av_id(v, loop + 4, "AINF", 4)) {
      unsigned size = av_be32(v, loop + 8);
      if (size > offset - 6 - loop)
        return 0;
      loop += size;
    }
  }
  if (version != 0x500 && loop + 24 <= offset - 6) {
    s->has_loop = av_be32(v, loop + 4) != 0;
    s->loop_start = av_be32(v, loop + 8);
    s->loop_end = av_be32(v, loop + 16);
  }
  return ac_flat(s, offset, 18, av_be32(v, 12), budget);
}
static int ac_afc(DndAudioSource *s, AudioView *v, unsigned budget) {
  if (!av_range(v, 0, 32) || av_be32(v, 0) > v->size || av_be16(v, 10) != 4 ||
      av_be16(v, 12) != 16 || av_be32(v, 16) > 1)
    return 0;
  s->codec = AC_AFC;
  s->channels = 2;
  s->rate = av_be16(v, 8);
  s->has_loop = av_be32(v, 16);
  s->loop_start = av_be32(v, 20);
  s->loop_end = av_be32(v, 4);
  return ac_flat(s, 32, 9, s->loop_end, budget);
}
static int ac_dtk(DndAudioSource *s, AudioView *v, unsigned budget) {
  if (!v->size || v->size % 32)
    return 0;
  s->codec = AC_DTK;
  s->channels = 2;
  s->rate = 48000;
  if (!ac_flat(s, 0, 32, v->size / 32 * 28, budget))
    return 0;
  s->span->stride = 32;
  s->span->channel[1] = 0;
  return 1;
}
static int ac_ssm(DndAudioSource *s, AudioView *v, unsigned entry,
                  unsigned budget) {
  unsigned head = av_be32(v, 0), size = av_be32(v, 4), count = av_be32(v, 8),
           at = 16;
  if (!count || count > 4096 || entry >= count || head > v->size - 16 ||
      size > v->size - 16 - head)
    return 0;
  unsigned data = v->size - size;
  for (unsigned i = 0; i < count; i++) {
    unsigned ch = av_be32(v, at);
    if (ch < 1 || ch > 2 || !av_range(v, at, 8 + ch * 64) ||
        at + 8 + ch * 64 > 16 + head)
      return 0;
    if (i == entry) {
      unsigned start = av_be32(v, at + 20), end = av_be32(v, at + 16),
               loop = av_be32(v, at + 12);
      if (end <= start || start % 16 > 15)
        return 0;
      s->codec = AC_DSP;
      s->channels = ch;
      s->rate = av_be32(v, at + 4);
      s->has_loop = av_be16(v, at + 8) != 0;
      if (s->has_loop && loop < start)
        return 0;
      s->loop_start = ac_nibbles(loop - start);
      s->loop_end = ac_nibbles(end - start);
      ac_coefficients(s, v, at + 24, 64, at + 60);
      unsigned interleave = 0;
      if (ch == 2) {
        unsigned second = av_be32(v, at + 84);
        if (second <= start || (second - start) % 16)
          return 0;
        interleave = (second - start) / 16 * 8;
      }
      if (!ac_flat(s, data + start / 16 * 8, interleave, s->loop_end, budget))
        return 0;
    }
    at += 8 + ch * 64;
  }
  return s->span != NULL;
}
static int ac_hps(DndAudioSource *s, AudioView *v, unsigned budget) {
  if (!av_id(v, 0, " HALPST\0", 8))
    return 0;
  s->codec = AC_DSP;
  s->rate = av_be32(v, 8);
  s->channels = av_be32(v, 12);
  if (s->channels < 1 || s->channels > 2)
    return 0;
  s->samples = ac_nibbles(av_be32(v, 24)) + 1;
  ac_coefficients(s, v, 32, 56, 0);
  unsigned at = 128, count = 0, total = 0, loop_offset = UINT_MAX;
  while (1) {
    if (!av_range(v, at, 32) || ++count > 65536)
      return 0;
    unsigned size = av_be32(v, at), last = av_be32(v, at + 4),
             next = av_be32(v, at + 8);
    unsigned n = ac_nibbles(last + 1);
    if (!n || size % s->channels || !av_range(v, at + 32, size) ||
        n > s->samples - total)
      return 0;
    total += n;
    if (next == UINT_MAX)
      break;
    if (next <= at) {
      loop_offset = next;
      break;
    }
    if (next < at + 32 + size)
      return 0;
    at = next;
  }
  if (total != s->samples || !ac_spans(s, count, budget))
    return 0;
  at = 128;
  total = 0;
  for (unsigned i = 0; i < count; i++) {
    DndAudioSpan *b = s->span + i;
    unsigned size = av_be32(v, at) / s->channels,
             n = ac_nibbles(av_be32(v, at + 4) + 1);
    *b = (DndAudioSpan){.first = total,
                        .samples = n,
                        .offset = at + 32,
                        .interleave = size,
                        .stride = size * s->channels,
                        .channel = {0, size}};
    b->reset_history = 1;
    for (unsigned channel = 0; channel < s->channels; channel++) {
      b->history[channel][0] = (int16_t)av_be16(v, at + 14 + channel * 8);
      b->history[channel][1] = (int16_t)av_be16(v, at + 16 + channel * 8);
      if (av_be16(v, at + 18 + channel * 8))
        return 0;
    }
    if (at == loop_offset) {
      s->has_loop = 1;
      s->loop_start = total;
      s->loop_end = s->samples;
    }
    total += n;
    at = av_be32(v, at + 8);
  }
  return loop_offset == UINT_MAX || s->has_loop;
}
static int ac_ast(DndAudioSource *s, AudioView *v, unsigned budget) {
  if (!av_id(v, 0, "STRM", 4) || av_be32(v, 4) != v->size - 64 ||
      av_be16(v, 10) != 16)
    return 0;
  unsigned type = av_be16(v, 8);
  if (type > 1)
    return 0;
  s->codec = type ? AC_PCM16 : AC_AFC;
  s->channels = av_be16(v, 12);
  if (s->channels < 1 || s->channels > 2)
    return 0;
  s->rate = av_be32(v, 16);
  s->samples = av_be32(v, 20);
  s->has_loop = av_be16(v, 14) != 0;
  s->loop_start = av_be32(v, 24);
  s->loop_end = av_be32(v, 28);
  unsigned at = 64, count = 0, total = 0, framebytes = ac_bytes(s->codec),
           spf = ac_spf(s->codec);
  while (total < s->samples) {
    if (!av_id(v, at, "BLCK", 4) || ++count > 65536)
      return 0;
    unsigned size = av_be32(v, at + 4);
    if (!size || size % framebytes || size > (v->size - at - 32) / s->channels)
      return 0;
    unsigned n = size / framebytes * spf;
    if (n > s->samples - total)
      n = s->samples - total;
    total += n;
    at += 32 + size * s->channels;
  }
  if (!ac_spans(s, count, budget))
    return 0;
  at = 64;
  total = 0;
  for (unsigned i = 0; i < count; i++) {
    unsigned size = av_be32(v, at + 4), n = size / framebytes * spf;
    if (n > s->samples - total)
      n = s->samples - total;
    s->span[i] = (DndAudioSpan){.first = total,
                                .samples = n,
                                .offset = at + 32,
                                .interleave = size,
                                .stride = size * s->channels,
                                .channel = {0, size}};
    total += n;
    at += 32 + size * s->channels;
  }
  return 1;
}
static int ac_rws809(DndAudioSource *s, AudioView *v, unsigned entry,
                     unsigned budget) {
  if (av_le32(v, 0) != 0x809 || av_le32(v, 12) != 0x80a ||
      av_le32(v, 4) != v->size - 12)
    return 0;
  unsigned at = 24 + av_le32(v, 16);
  if (av_le32(v, at) != 0x80c)
    return 0;
  unsigned end = at + 12 + av_le32(v, at + 4), count = av_be32(v, at + 12);
  if (end != v->size || !count || count > 4096 || entry >= count)
    return 0;
  at += 16;
  for (unsigned i = 0; i < count; i++) {
    if (av_le32(v, at) != 0x802)
      return 0;
    unsigned size = av_le32(v, at + 4);
    if (!av_range(v, at + 12, size))
      return 0;
    if (i == entry) {
      unsigned h = at + 12, hs = av_le32(v, h + 4), data = h + 12 + hs;
      if (av_le32(v, h) != 0x803 || av_le32(v, data) != 0x804 ||
          av_byte(v, h + 29) != 1)
        return 0;
      unsigned bytes = av_be32(v, h + 24), codec = av_be32(v, h + 44);
      if (av_le32(v, data + 4) != bytes || data + 12 + bytes > at + 12 + size)
        return 0;
      s->rate = av_be32(v, h + 16);
      s->channels = 1;
      if (codec == 0xf86215b0) {
        s->codec = AC_DSP;
        if (av_be32(v, h + 36) != 96)
          return 0;
        ac_coefficients(s, v, h + 60 + 28, 0, h + 60 + 64);
        if (bytes % 8)
          return 0;
      } else if (codec == 0xd01bd217) {
        s->codec = AC_PCM16;
        if (bytes % 2)
          return 0;
      } else
        return 0;
      if (!ac_flat(s, data + 12, 0,
                   bytes / ac_bytes(s->codec) * ac_spf(s->codec), budget))
        return 0;
    }
    at += 12 + size;
  }
  return at == end && s->span != NULL;
}
/* Strings in RWS 0x80d headers are padded to 16 bytes, including the NUL. */
static int ac_rws_string(AudioView *v, unsigned *at, unsigned end) {
  unsigned start = *at;
  while (*at < end && av_byte(v, *at))
    (*at)++;
  if (*at == end)
    return 0;
  unsigned n = (*at - start + 16) & ~15u;
  if (n > end - start)
    return 0;
  *at = start + n;
  return 1;
}
static int ac_rws(DndAudioSource *s, AudioView *v, unsigned entry,
                  unsigned budget) {
  if (av_le32(v, 0) != 0x80d || av_le32(v, 4) != v->size - 12 ||
      av_le32(v, 12) != 0x80e)
    return 0;
  unsigned end = 24 + av_le32(v, 16);
  if (av_le32(v, end) != 0x80f || av_le32(v, end + 4) != v->size - end - 12)
    return 0;
  unsigned segments = av_be32(v, 24 + 32), layers = av_be32(v, 24 + 40),
           at = 24 + 80;
  if (!segments || !layers || segments > 1024 || layers > 256 ||
      segments * layers > 4096 || entry >= segments * layers ||
      !ac_rws_string(v, &at, end))
    return 0;
  unsigned target_segment = entry / layers, target_layer = entry % layers,
           segment_bytes = 0, segment_offset = 0;
  for (unsigned i = 0; i < segments; i++) {
    if (i == target_segment) {
      segment_bytes = av_be32(v, at + 24);
      segment_offset = av_be32(v, at + 28);
    }
    at += 32;
  }
  unsigned usable = 0;
  for (unsigned i = 0; i < segments * layers; i++) {
    if (i == entry)
      usable = av_be32(v, at);
    at += 4;
  }
  at += 16 * segments;
  for (unsigned i = 0; i < segments; i++)
    if (!ac_rws_string(v, &at, end))
      return 0;
  unsigned block = 0, stride = 0, layer_start = 0;
  for (unsigned i = 0; i < layers; i++) {
    unsigned pad = av_be32(v, at + 16);
    if (pad > UINT_MAX - stride)
      return 0;
    stride += pad;
    if (i == target_layer) {
      block = av_be32(v, at + 32);
      layer_start = av_be32(v, at + 36);
    }
    at += 40;
  }
  for (unsigned i = 0; i < layers; i++) {
    unsigned codec = av_be32(v, at + 28), config = at;
    at += 44;
    if (i == target_layer) {
      s->rate = av_be32(v, config);
      s->channels = av_byte(v, config + 13);
      if (s->channels < 1 || s->channels > 2)
        return 0;
      if (codec == 0xf86215b0) {
        s->codec = AC_DSP;
        ac_coefficients(s, v, at + 28, 0, at + 64);
      } else if (codec == 0xd01bd217)
        s->codec = AC_PCM16;
      else
        return 0;
    }
    if (codec == 0xf86215b0)
      at += 96;
    at += 4;
  }
  if (at > end || !block || !stride || block > stride || block % s->channels ||
      !usable || usable % s->channels || segment_offset > v->size - end - 12 ||
      segment_bytes > v->size - end - 12 - segment_offset ||
      layer_start > stride - block)
    return 0;
  unsigned channel = block / s->channels;
  if (channel % ac_bytes(s->codec) ||
      usable % (s->channels * ac_bytes(s->codec)))
    return 0;
  if ((uint64_t)(usable / block + (usable % block != 0)) * stride >
      segment_bytes)
    return 0;
  if (!ac_flat(s, end + 12 + segment_offset + layer_start, channel,
               usable / s->channels / ac_bytes(s->codec) * ac_spf(s->codec),
               budget))
    return 0;
  s->span->stride = stride;
  return 1;
}
static int ac_re4(DndAudioSource *s, AudioView *h, AudioView *body,
                  unsigned entry, unsigned budget) {
  unsigned count = av_be32(h, 0), headers = av_be32(h, 4),
           table = av_be32(h, 8);
  if (!count || count > 4096 || entry >= count ||
      !av_range(h, table, count * 16))
    return 0;
  int index = (int16_t)av_be16(h, table + entry * 16);
  if (index < 0 || index >= 4096)
    return 0;
  unsigned relative = av_be32(h, headers + (unsigned)index * 4);
  if (relative > h->size - headers)
    return 0;
  unsigned at = headers + relative;
  if (!av_range(h, at, 124))
    return 0;
  unsigned flags = av_be32(h, at);
  if (flags & ~15u)
    return 0;
  s->codec = AC_DSP;
  s->channels = flags & 1 ? 2 : 1;
  s->samples = av_be32(h, at + 4);
  s->rate = av_be32(h, at + 12);
  if (av_be32(h, at + 16) > 2 || av_be16(h, at + 96) || av_be16(h, at + 98))
    return 0;
  s->has_loop = !(flags & 4);
  s->loop_start = ac_nibbles(av_be32(h, at + 20));
  s->loop_end = ac_nibbles(av_be32(h, at + 24) + 1);
  for (unsigned c = 0; c < s->channels; c++) {
    for (unsigned k = 0; k < 16; k++)
      s->coefficients[c][k] = (int16_t)av_be16(h, at + 32 + c * 32 + k * 2);
    s->history[c][0] = (int16_t)av_be16(h, at + 104 + c * 2);
    s->history[c][1] = (int16_t)av_be16(h, at + 108 + c * 2);
  }
  s->data = body->data;
  s->size = body->size;
  return ac_flat(s, av_be32(h, at + 28), s->channels == 2 ? 0x4000 : 0x8000,
                 s->samples, budget);
}
static int ac_wsys(DndAudioSource *s, AudioView *h, AudioView *body,
                   unsigned base, unsigned *entry, unsigned budget) {
  if (!av_id(h, base, "WSYS", 4))
    return 0;
  unsigned size = av_be32(h, base + 4);
  if (!size || !av_range(h, base, size))
    return 0;
  unsigned winf = av_be32(h, base + 16);
  if (winf > size - 8)
    return 0;
  winf += base;
  if (!av_id(h, winf, "WINF", 4))
    return 0;
  unsigned banks = av_be32(h, winf + 4);
  if (!banks || banks > 256)
    return 0;
  for (unsigned i = 0; i < banks; i++) {
    unsigned bank = av_be32(h, winf + 8 + i * 4);
    if (bank > size - 116)
      return 0;
    bank += base;
    unsigned count = av_be32(h, bank + 112);
    if (count > 65536)
      return 0;
    if (*entry >= count) {
      *entry -= count;
      continue;
    }
    unsigned wave = av_be32(h, bank + 116 + *entry * 4);
    if (wave > size - 40)
      return 0;
    wave += base;
    unsigned codec = av_byte(h, wave + 1), rate_bits = av_be32(h, wave + 4);
    float rate;
    memcpy(&rate, &rate_bits, 4);
    if (!isfinite(rate) || rate < 5000 || rate > 48000)
      return 0;
    s->codec = codec == 0   ? AC_AFC
               : codec == 1 ? AC_AFC2
               : codec == 2 ? AC_PCM8
               : codec == 3 ? AC_PCM16
                            : 0;
    if (!s->codec)
      return 0;
    s->channels = 1;
    s->rate = (unsigned)rate;
    s->has_loop = av_be32(h, wave + 16) == UINT_MAX;
    s->loop_start = av_be32(h, wave + 20);
    s->loop_end = av_be32(h, wave + 24);
    s->data = body->data;
    s->size = body->size;
    unsigned offset = av_be32(h, wave + 8), bytes = av_be32(h, wave + 12),
             samples = av_be32(h, wave + 28);
    if (!av_range(body, offset, bytes) ||
        (uint64_t)(samples + ac_spf(s->codec) - 1) / ac_spf(s->codec) *
                ac_bytes(s->codec) >
            bytes)
      return 0;
    return ac_flat(s, offset, 0, samples, budget);
  }
  return 2; /* no matching wave in this WSYS */
}
static int ac_jaudio(DndAudioSource *s, AudioView *h, AudioView *body,
                     unsigned entry, unsigned budget) {
  if (av_id(h, 0, "WSYS", 4))
    return ac_wsys(s, h, body, 0, &entry, budget) == 1;
  unsigned at = 0;
  for (unsigned commands = 0; commands < 4096 && at < h->size; commands++) {
    unsigned id = av_be32(h, at);
    at += 4;
    if (!id)
      return 0;
    if (id == 1 || id == 4 || id == 5 || id == 6 || id == 7) {
      if (!av_range(h, at, 12))
        return 0;
      at += 12;
      continue;
    }
    if (id != 2 && id != 3)
      return 0;
    for (unsigned i = 0; i < 4096; i++) {
      unsigned offset = av_be32(h, at);
      at += 4;
      if (!offset)
        break;
      if (!av_range(h, at, 8))
        return 0;
      at += 8;
      if (id == 3) {
        int found = ac_wsys(s, h, body, offset, &entry, budget);
        if (found != 2)
          return found;
      }
      if (i == 4095)
        return 0;
    }
  }
  return 0;
}
static int ac_afs(DndAudioSource *s, AudioView *v, unsigned entry,
                  unsigned budget) {
  if (!av_id(v, 0, "AFS\0", 4))
    return 0;
  unsigned count = av_le32(v, 4);
  if (!count || count > 65536 || entry >= count || !av_range(v, 8, count * 8))
    return 0;
  unsigned at = av_le32(v, 8 + entry * 8), size = av_le32(v, 12 + entry * 8);
  if (!av_range(v, at, size))
    return 0;
  AudioView member = {v->data + at, size, 0};
  s->data = member.data;
  s->size = member.size;
  int ok = ac_adx(s, &member, budget);
  return ok && !member.failed;
}
static int ac_awd(DndAudioSource *s, AudioView *v, unsigned entry,
                  unsigned budget) {
  if (av_be32(v, 0) != 0x809 || av_be32(v, 24) != 0xfd9d32d3)
    return 0;
  unsigned data = av_be32(v, 8), dictionary = av_be32(v, 12),
           header = av_be32(v, 40);
  if (data != header || data > v->size || dictionary > header ||
      header - dictionary < 20)
    return 0;
  unsigned sentinel = dictionary + 12, next = av_be32(v, sentinel + 4),
           visited[1024], count = 0;
  while (next != sentinel) {
    if (count == 1024 || next < dictionary || next > header ||
        header - next < 12)
      return 0;
    for (unsigned i = 0; i < count; i++)
      if (visited[i] == next)
        return 0;
    visited[count] = next;
    unsigned info = av_be32(v, next + 8);
    if (info < dictionary || info > header || header - info < 80)
      return 0;
    if (count == entry) {
      unsigned bytes = av_be32(v, info + 24), extra = av_be32(v, info + 32),
               offset = av_be32(v, info + 76);
      if (av_be32(v, info + 20) != 3 || av_byte(v, info + 29) != 1 || !bytes ||
          bytes % 8 || extra > header || header - extra < 96 ||
          offset > v->size - data || bytes > v->size - data - offset)
        return 0;
      s->codec = AC_DSP;
      s->channels = 1;
      s->rate = av_be32(v, info + 16);
      ac_coefficients(s, v, extra + 28, 0, extra + 64);
      if (!ac_flat(s, data + offset, 0, bytes / 8 * 14, budget))
        return 0;
    }
    count++;
    next = av_be32(v, next + 4);
  }
  return entry < count && s->span != NULL;
}
/* Explicit format values match AudioFormat. Auto recognizes signatures;
 * headerless DTK, DSP and paired banks always require an explicit format. */
static int ac_parse(DndAudioSource *s, AudioView *h, AudioView *body,
                    unsigned format, unsigned entry, unsigned budget) {
  s->data = h->data;
  s->size = h->size;
  int ok = 0;
  if (!format) {
    if (av_be32(h, 0) == 0x809)
      format = 12;
    else if (av_id(h, 0, " HALPST\0", 8))
      format = 4;
    else if (av_id(h, 0, "STRM", 4))
      format = 6;
    else if (av_id(h, 0, "AFS\0", 4))
      format = 9;
    else if (av_be16(h, 0) == 0x8000)
      format = 2;
    else if (av_le32(h, 0) == 0x809 || av_le32(h, 0) == 0x80d)
      format = 8;
    else
      return 0;
  }
  if (entry && format != 5 && format != 7 && format != 8 && format != 9 &&
      format != 10 && format != 12)
    return 0;
  switch (format) {
  case 12:
    ok = ac_awd(s, h, entry, budget);
    break;
  case 1:
    ok = ac_dsp(s, h, budget);
    break;
  case 2:
    ok = ac_adx(s, h, budget);
    break;
  case 3:
    ok = ac_afc(s, h, budget);
    break;
  case 4:
    ok = ac_hps(s, h, budget);
    break;
  case 5:
    ok = ac_ssm(s, h, entry, budget);
    break;
  case 6:
    ok = ac_ast(s, h, budget);
    break;
  case 7:
    ok = body->data && ac_jaudio(s, h, body, entry, budget);
    break;
  case 8:
    ok = av_le32(h, 0) == 0x809 ? ac_rws809(s, h, entry, budget)
                                : ac_rws(s, h, entry, budget);
    break;
  case 9:
    ok = ac_afs(s, h, entry, budget);
    break;
  case 10:
    ok = body->data && ac_re4(s, h, body, entry, budget);
    break;
  case 11:
    ok = ac_dtk(s, h, budget);
    break;
  default:
    return 0;
  }
  return ok && !h->failed && !body->failed && ac_validate_source(s);
}
static const int ac_afc_coeff[16][2] = {
    {0, 0},        {2048, 0},     {0, 2048},     {1024, 1024},
    {4096, -2048}, {3584, -1536}, {3072, -1024}, {4608, -2560},
    {4200, -2248}, {4800, -2300}, {5120, -3072}, {2048, -2048},
    {1024, -1024}, {-1024, 1024}, {-1024, 0},    {-2048, 0}};
static int ac_clamp(int64_t n) {
  return n > 32767 ? 32767 : n < -32768 ? -32768 : (int)n;
}
/* Defined arithmetic shift for negative prediction values, including host C. */
static int64_t ac_shift(int64_t n, unsigned shift) {
  int64_t divisor = (int64_t)1 << shift;
  return n >= 0 ? n / divisor : -((-n + divisor - 1) / divisor);
}
static int ac_frame(DndAudioDecoder *d) {
  const DndAudioSource *s = d->source;
  DndAudioDecodeState *t = &d->state;
  while (t->span < s->span_count &&
         t->position >= s->span[t->span].first + s->span[t->span].samples)
    t->span++;
  if (t->span == s->span_count)
    return 0;
  const DndAudioSpan *b = s->span + t->span;
  if (b->reset_history && t->position == b->first)
    memcpy(t->history, b->history, sizeof(t->history));
  unsigned spf = ac_spf(s->codec), bytes = ac_bytes(s->codec),
           local = t->position - b->first, frame = local / spf;
  uint64_t byte = (uint64_t)frame * bytes;
  for (unsigned ch = 0; ch < s->channels; ch++) {
    uint64_t at = (uint64_t)b->offset + byte / b->interleave * b->stride +
                  b->channel[ch] + byte % b->interleave;
    if (at > s->size || bytes > s->size - at)
      return 0;
    const uint8_t *p = s->data + at;
    int h1 = t->history[ch][0], h2 = t->history[ch][1], scale = 0, a = 0, c = 0;
    if (s->codec == AC_DSP) {
      unsigned predictor = p[0] >> 4;
      if (predictor > 7)
        return 0;
      scale = 1 << (p[0] & 15);
      a = s->coefficients[ch][predictor * 2];
      c = s->coefficients[ch][predictor * 2 + 1];
    } else if (s->codec == AC_AFC || s->codec == AC_AFC2) {
      scale = 1 << (p[0] >> 4);
      a = ac_afc_coeff[p[0] & 15][0];
      c = ac_afc_coeff[p[0] & 15][1];
    } else if (s->codec == AC_ADX) {
      unsigned word = (unsigned)p[0] << 8 | p[1];
      if (s->adx_type == 2) {
        static const int coeff[4][2] = {
            {0, 0}, {3840, 0}, {7360, -3328}, {6272, -3520}};
        unsigned predictor = word >> 13;
        if (predictor > 3)
          return 0;
        a = coeff[predictor][0];
        c = coeff[predictor][1];
        scale = (word & 8191) + 1;
      } else {
        if (word & 0x8000)
          return 0;
        a = s->coefficients[ch][0];
        c = s->coefficients[ch][1];
        if (s->adx_type == 4) {
          if (word > 12)
            return 0;
          scale = 1 << (12 - word);
        } else
          scale = word + 1;
      }
    } else if (s->codec == AC_DTK) {
      unsigned header = p[ch];
      if (header != p[ch + 2] || (header >> 4) > 3 || (header & 15) > 12)
        return 0;
      static const int coeff[4][2] = {{0, 0}, {60, 0}, {115, 52}, {98, 55}};
      a = coeff[header >> 4][0];
      c = coeff[header >> 4][1];
      scale = header & 15;
    }
    for (unsigned i = 0; i < spf; i++) {
      int value = 0;
      int64_t prediction = (int64_t)a * h1 + (int64_t)c * h2;
      if (s->codec == AC_PCM8)
        value = (int8_t)p[0] * 256;
      else if (s->codec == AC_PCM16)
        value = (int16_t)((unsigned)p[0] << 8 | p[1]);
      else if (s->codec == AC_PCM16LE)
        value = (int16_t)((unsigned)p[1] << 8 | p[0]);
      else if (s->codec == AC_DTK) {
        int n = (p[4 + i] >> (ch ? 4 : 0)) & 15;
        if (n >= 8)
          n -= 16;
        int64_t hist = ac_shift((int64_t)h1 * a - (int64_t)h2 * c + 32, 6);
        if (hist > 2097151)
          hist = 2097151;
        if (hist < -2097152)
          hist = -2097152;
        int next = (int)(ac_shift(n * 4096, scale) * 64 + hist);
        h2 = h1;
        h1 = next;
        value = ac_clamp(ac_shift(next, 6));
      } else {
        int n;
        if (s->codec == AC_AFC2) {
          n = p[1 + i / 4] >> (6 - (i % 4) * 2) & 3;
          if (n >= 2)
            n -= 4;
        } else {
          unsigned skip = s->codec == AC_ADX ? 2 : 1;
          n = p[skip + i / 2] >> (i % 2 ? 0 : 4) & 15;
          if (n >= 8)
            n -= 16;
        }
        if (s->codec == AC_ADX) {
          int64_t pred = s->adx_version == 0x300
                             ? ac_shift((int64_t)a * h1, 12) +
                                   ac_shift((int64_t)c * h2, 12)
                             : ac_shift(prediction, 12);
          value = ac_clamp(n * scale + pred);
        } else if (s->codec == AC_DSP)
          value = ac_clamp(
              ac_shift((int64_t)n * scale * 2048 + prediction + 1024, 11));
        else if (s->codec == AC_AFC2)
          value =
              ac_clamp(ac_shift((int64_t)n * scale * 8192 + prediction, 11));
        else
          value =
              ac_clamp(ac_shift((int64_t)n * scale * 2048 + prediction, 11));
        h2 = h1;
        h1 = value;
      }
      t->frame[ch][i] = (int16_t)value;
    }
    t->history[ch][0] = h1;
    t->history[ch][1] = h2;
  }
  t->cursor = local % spf;
  t->cached = spf;
  if (t->cached > b->samples - frame * spf)
    t->cached = b->samples - frame * spf;
  return 1;
}
void dnd_audio_decoder_init(DndAudioDecoder *d, const DndAudioSource *s,
                            bool loop) {
  memset(d, 0, sizeof(*d));
  d->source = s;
  d->loop = loop;
  d->loop_start = s->loop_start;
  d->loop_end = s->loop_end;
  memcpy(d->state.history, s->history, sizeof(s->history));
}
unsigned dnd_audio_decode(DndAudioDecoder *d, uint8_t *out, unsigned frames) {
  if (!d || !d->source || d->failed || !out)
    return 0;
  unsigned produced = 0;
  const DndAudioSource *s = d->source;
  while (produced < frames) {
    if (d->loop && d->state.position == d->loop_end) {
      if (!d->saved) {
        d->failed = 1;
        break;
      }
      d->state = d->loop_state;
    }
    if (d->state.position >= s->samples)
      break;
    if (d->loop && !d->saved && d->state.position == d->loop_start) {
      d->loop_state = d->state;
      d->saved = 1;
    }
    if (d->state.cursor == d->state.cached && !ac_frame(d)) {
      d->failed = 1;
      break;
    }
    for (unsigned c = 0; c < s->channels; c++) {
      unsigned v = (uint16_t)d->state.frame[c][d->state.cursor];
      *out++ = (uint8_t)(v >> 8);
      *out++ = (uint8_t)v;
    }
    produced++;
    d->state.position++;
    d->state.cursor++;
  }
  return produced;
}
