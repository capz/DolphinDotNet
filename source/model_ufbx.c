/* Unmodified ufbx implementation, using the pinned header and native
 * allocators. Filesystem access is supplied exclusively by model_source.c
 * callbacks. */
#define UFBX_HEADER_PATH "dnd_ufbx.h"
#define UFBX_NO_STDIO
#define UFBX_NO_MALLOC
/* GCC does not reliably advertise -fsanitize=undefined to upstream's feature
 * detection. Always select ufbx's documented UB-safe paths: in particular,
 * mask packed Huffman shift counts instead of relying on x86 shift wrapping. */
#ifndef UFBX_UBSAN
#define UFBX_UBSAN 1
#endif
/* This pin's UFBX_NO_MALLOC expression has void type in C. Supply the
 * documented global allocator hooks with correctly typed failure results. */
#define ufbx_malloc(n) ((void)(n), (void *)0)
#define ufbx_realloc(p, o, n) ((void)(p), (void)(o), (void)(n), (void *)0)
#define ufbx_free(p, n) ((void)(p), (void)(n))
#include "dnd_ufbx_impl.h"
