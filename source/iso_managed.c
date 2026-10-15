#include "dnd_iso_file.h"
#include "dnd_managed.h"
#include <limits.h>
#include <stdint.h>
#include <string.h>

#define ISO_SLOTS 4
#define ISO_PATH 1024
static DndIsoFile images[ISO_SLOTS];
static bool active[ISO_SLOTS];

/* Explicit UTF-16 to UTF-8 conversion: image paths may contain Unicode. */
static bool image_path(DndString *string, char out[ISO_PATH]) {
  if (!string) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL, "Image path is null.");
    return false;
  }
  unsigned n = 0;
  for (uint32_t i = 0; i < string->length; ++i) {
    uint32_t c = string->chars[i];
    if (!c) goto bad;
    if (c >= 0xd800 && c <= 0xdbff) {
      if (++i >= string->length || string->chars[i] < 0xdc00 ||
          string->chars[i] > 0xdfff) goto bad;
      c = 0x10000 + ((c-0xd800)<<10) + string->chars[i]-0xdc00;
    } else if (c >= 0xdc00 && c <= 0xdfff) goto bad;
    unsigned count = c < 128 ? 1 : c < 2048 ? 2 : c < 65536 ? 3 : 4;
    if (n + count >= ISO_PATH) {
      dnd_exception_throw(DND_EXCEPTION_PATH_TOO_LONG, "Image path is too long.");
      return false;
    }
    if (count == 1) out[n++] = (char)c;
    else {
      out[n++] = (char)((count==2 ? 0xc0 : count==3 ? 0xe0 : 0xf0) |
                         (c >> (6*(count-1))));
      for (unsigned j = count-1; j; --j)
        out[n++] = (char)(0x80 | ((c >> (6*(j-1))) & 63));
    }
  }
  out[n]=0;
  return true;
bad:
  dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Invalid image path characters.");
  return false;
}
static DndIsoFile *session(int token) {
  int i=token-1;
  if (i<0 || i>=ISO_SLOTS || !active[i]) {
    dnd_exception_throw(DND_EXCEPTION_OBJECT_DISPOSED, "ISO handle is closed.");
    return NULL;
  }
  return &images[i];
}
int dnd_iso_open(DndString *path) {
  char name[ISO_PATH];
  if (!image_path(path,name)) return 0;
  for (int i=0;i<ISO_SLOTS;i++) {
    if (active[i]) continue;
    if (!dnd_iso_file_open(&images[i],name)) return 0;
    active[i]=true;
    return i+1;
  }
  dnd_exception_throw(DND_EXCEPTION_IO, "Too many GameCube ISO images.");
  return 0;
}
void dnd_iso_close(int token) {
  DndIsoFile *image=session(token);
  if (!image) return;
  dnd_iso_file_close(image);
  active[token-1]=false;
}
int dnd_iso_find(int token, DndString *path) {
  DndIsoFile *image=session(token);
  char name[ISO_PATH];
  if (!image || !image_path(path,name)) return -1;
  return dnd_iso_file_entry(image,name);
}
int dnd_iso_directory(int token,int index) {
  DndIsoFile *image=session(token);
  if (!image) return 0;
  return index>=0 && (uint32_t)index<image->fst.count &&
         dnd_fst_directory(&image->fst,(uint32_t)index);
}
int dnd_iso_next(int token,int index) {
  DndIsoFile *image=session(token);
  if (!image) return -1;
  if (index<0 || (uint32_t)index>=image->fst.count) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE, "Invalid FST index.");
    return -1;
  }
  return (int)dnd_fst_next(&image->fst,(uint32_t)index);
}
int dnd_iso_length(int token,int index) {
  DndIsoFile *image=session(token);
  if (!image) return -1;
  if (index<0 || (uint32_t)index>=image->fst.count ||
      dnd_fst_directory(&image->fst,(uint32_t)index) ||
      dnd_fst_length(&image->fst,(uint32_t)index)>INT_MAX) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Not a supported file.");
    return -1;
  }
  return (int)dnd_fst_length(&image->fst,(uint32_t)index);
}
DndString *dnd_iso_name(DndManagedHeap *heap,int token,int index) {
  DndIsoFile *image=session(token);
  if (!image) return NULL;
  if (index<0 || (uint32_t)index>=image->fst.count) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE, "Invalid FST index.");
    return NULL;
  }
  return dnd_string_from_utf8(heap,dnd_fst_name(&image->fst,(uint32_t)index));
}
int dnd_iso_read(int token,int index,int offset,DndArray *buffer,
                 int destination,int length) {
  DndIsoFile *image=session(token);
  if (!image) return -1;
  if (!buffer) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL, "Buffer is null.");
    return -1;
  }
  if (index<0 || (uint32_t)index>=image->fst.count || offset<0 ||
      destination<0 || length<0 || buffer->element_size!=1 ||
      buffer->elements_are_references || (uint32_t)destination>buffer->length ||
      (uint32_t)length>buffer->length-(uint32_t)destination) {
    dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Invalid ISO read arguments.");
    return -1;
  }
  int n=dnd_iso_file_read(image,(uint32_t)index,(uint32_t)offset,
                          buffer->data+destination,(size_t)length);
  if (n<0) dnd_exception_throw(DND_EXCEPTION_IO, "GameCube ISO read failed.");
  return n;
}
