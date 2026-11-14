#ifndef DND_PLATFORM_ENTROPY_H
#define DND_PLATFORM_ENTROPY_H
#include <stddef.h>
#include <stdint.h>
enum {
    DND_ENTROPY_OK=0,DND_ENTROPY_INVALID=-1,DND_ENTROPY_BUSY=-2,
    DND_ENTROPY_MEMORY=-3,DND_ENTROPY_SOURCE=-4,DND_ENTROPY_TIMEOUT=-5
};
/* Fresh 48..64-byte seed only; timeout 1..30000 ms. Operational failures clear
 * output. Invalid lengths/null are rejected without accessing the buffer.
 * No persistent seed or public build/clock/device value substitutes entropy. */
int dnd_platform_entropy_fill(unsigned char *output,size_t length,uint32_t timeout_ms);
#ifndef DND_PLATFORM_ENTROPY_STANDALONE
#include "dnd_managed.h"
int dnd_platform_entropy_fill_array(DndArray *output,int32_t timeout_ms);
#endif
#ifdef DND_PLATFORM_ENTROPY_TEST
uint64_t dnd_platform_entropy_test_clock(void);
uint64_t dnd_platform_entropy_test_monotonic_ms(void);
void *dnd_platform_entropy_test_alloc(size_t length);
void dnd_platform_entropy_test_free(void *pointer,size_t length);
size_t dnd_platform_entropy_test_live_bytes(void);
size_t dnd_platform_entropy_test_peak_bytes(void);
uint64_t dnd_platform_entropy_test_sample_count(void);
#endif
#endif
