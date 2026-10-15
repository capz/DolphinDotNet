/* Unmodified ufbx implementation, using the pinned header and native
 * allocators. Filesystem access is supplied exclusively by model_source.c
 * callbacks. */
#define UFBX_HEADER_PATH "dnd_ufbx.h"
#define UFBX_NO_STDIO
#define UFBX_NO_MALLOC
/* This pin's UFBX_NO_MALLOC expression has void type in C. Supply the
 * documented global allocator hooks with correctly typed failure results. */
#define ufbx_malloc(n) ((void)(n), (void *)0)
#define ufbx_realloc(p, o, n) ((void)(p), (void)(o), (void)(n), (void *)0)
#define ufbx_free(p, n) ((void)(p), (void)(n))
#include "dnd_ufbx_impl.h"
