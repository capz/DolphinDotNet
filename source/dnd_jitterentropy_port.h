#ifndef DND_JITTERENTROPY_PORT_H
#define DND_JITTERENTROPY_PORT_H
#include <sys/types.h>
#include <stdint.h>
#include <stddef.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#define DND_JENT_FAIL_CLOSED_STARTUP 1

/* These functions are owned by one serialized, bounded native request.
 * Timer values are raw PPC timebase ticks, never rounded microseconds or
 * manufactured nanoseconds. Returning zero marks a failed/expired source. */
uint64_t dnd_jent_timestamp(void);
void *dnd_jent_zalloc(size_t length);
void dnd_jent_zfree(void *pointer,size_t length);
void dnd_jent_wipe(void *pointer,size_t length);
static inline void jent_get_nstime(uint64_t *out){*out=dnd_jent_timestamp();}
static inline void *jent_zalloc(size_t length){return dnd_jent_zalloc(length);}
static inline void jent_zfree(void *pointer,size_t length){dnd_jent_zfree(pointer,length);}
static inline void jent_memset_secure(void *pointer,size_t length){dnd_jent_wipe(pointer,length);}
static inline int jent_fips_enabled(void){return 1;}
static inline long jent_ncpu(void){return 1;}
/* GC has a 32 KiB L1 data cache. The caller explicitly caps the memory source
 * at four times this size, with an independent allocator hard limit. */
static inline uint32_t jent_cache_size_roundup(int all_caches){(void)all_caches;return 32768;}
static inline void jent_yield(void){}
static inline uint64_t rol64(uint64_t x,int n){return (x<<(n&63))|(x>>((64-n)&63));}
#endif
