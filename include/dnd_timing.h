#ifndef DND_TIMING_H
#define DND_TIMING_H
#include <stdint.h>
int64_t dnd_timing_now(void); /* Monotonic microseconds, not UTC/calendar time. */
#endif
