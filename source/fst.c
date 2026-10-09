#include "dnd_fst.h"
#include <limits.h>
#include <stdlib.h>
#include <string.h>
static uint32_t be(const uint8_t *p) {
  return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) |
         ((uint32_t)p[2] << 8) | p[3];
}
static const uint8_t *entry(const DndFst *f, uint32_t i) {
  return f->table + 12 * i;
}
bool dnd_fst_directory(const DndFst *f, uint32_t i) {
  return f && f->table && i < f->count && entry(f, i)[0] == 1;
}
const char *dnd_fst_name(const DndFst *f, uint32_t i) {
  if (!f || !f->table || i >= f->count)
    return NULL;
  return (const char *)f->table + 12 * f->count + (be(entry(f, i)) & 0xffffff);
}
uint32_t dnd_fst_length(const DndFst *f, uint32_t i) {
  return f && f->table && i < f->count ? be(entry(f, i) + 8) : 0;
}
uint32_t dnd_fst_next(const DndFst *f, uint32_t i) {
  return dnd_fst_directory(f, i) ? dnd_fst_length(f, i) : i + 1;
}
void dnd_fst_unmount(DndFst *f) {
  if (!f)
    return;
  free(f->table);
  memset(f, 0, sizeof(*f));
}
bool dnd_fst_mount(DndFst *f, DndDiscRead read, void *context,
                   uint64_t capacity) {
  if (!f || !read || f->table)
    return false;
  uint8_t header[0x440];
  if (capacity < sizeof(header) || !read(context, 0, header, sizeof(header)) ||
      be(header + 0x1c) != 0xc2339f3d)
    return false;
  uint32_t offset = be(header + 0x424), size = be(header + 0x428);
  if (size < 12 || size > 16 * 1024 * 1024 || offset > capacity ||
      size > capacity - offset)
    return false;
  uint8_t *table = malloc(size);
  if (!table)
    return false;
  if (!read(context, offset, table, size)) {
    free(table);
    return false;
  }
  uint32_t count = be(table + 8);
  if (table[0] != 1 || be(table + 4) != 0 || !count || count > size / 12) {
    free(table);
    return false;
  }
  DndFst result = {table, count, size, read, context, capacity};
  uint32_t *stack = malloc((size_t)count * sizeof(uint32_t));
  if (!stack) {
    free(table);
    return false;
  }
  uint32_t depth = 1;
  stack[0] = 0;
  bool valid = true;
  for (uint32_t i = 1; i < count; i++) {
    while (depth > 1 && i >= dnd_fst_next(&result, stack[depth - 1]))
      depth--;
    const uint8_t *p = entry(&result, i);
    uint32_t name = be(p) & 0xffffff;
    uint32_t strings = size - 12 * count;
    if (p[0] > 1 || name >= strings) {
      valid = false;
      break;
    }
    const char *s = (const char *)table + 12 * count + name;
    const char *end = memchr(s, 0, strings - name);
    if (!end || end == s || (size_t)(end - s) > 255 || strchr(s, '/') ||
        strchr(s, '\\') || strchr(s, ':') || !strcmp(s, ".") ||
        !strcmp(s, "..")) {
      valid = false;
      break;
    }
    if (p[0]) {
      uint32_t next = be(p + 8);
      if (be(p + 4) != stack[depth - 1] || next <= i ||
          next > dnd_fst_next(&result, stack[depth - 1])) {
        valid = false;
        break;
      }
      stack[depth++] = i;
    } else {
      uint32_t start = be(p + 4), length = be(p + 8);
      if (start > capacity || length > capacity - start) {
        valid = false;
        break;
      }
    }
  }
  free(stack);
  if (!valid) {
    free(table);
    return false;
  }
  *f = result;
  return true;
}
int dnd_fst_find(const DndFst *f, const char *path) {
  if (!f || !f->table || !path)
    return -1;
  const char *colon = strchr(path, ':');
  if (colon)
    path = colon + 1;
  while (*path == '/')
    path++;
  uint32_t parent = 0;
  while (*path) {
    const char *end = strchr(path, '/');
    size_t length = end ? (size_t)(end - path) : strlen(path);
    bool found = false;
    if (!dnd_fst_directory(f, parent))
      return -1;
    for (uint32_t i = parent + 1; i < dnd_fst_next(f, parent);
         i = dnd_fst_next(f, i)) {
      const char *name = dnd_fst_name(f, i);
      if (strlen(name) == length && !memcmp(name, path, length)) {
        parent = i;
        found = true;
        break;
      }
    }
    if (!found)
      return -1;
    if (!end)
      break;
    path = end + 1;
    while (*path == '/')
      path++;
  }
  return (int)parent;
}
int dnd_fst_read(const DndFst *f, uint32_t i, uint32_t offset, void *data,
                 size_t length) {
  if (!f || !f->table || i >= f->count || dnd_fst_directory(f, i) ||
      (!data && length))
    return -1;
  uint32_t size = dnd_fst_length(f, i);
  if (offset >= size)
    return 0;
  if (length > size - offset)
    length = size - offset;
  if (length > INT_MAX)
    length = INT_MAX;
  return f->read(f->context, (uint64_t)be(entry(f, i) + 4) + offset, data,
                 length)
             ? (int)length
             : -1;
}
