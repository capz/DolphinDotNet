#ifndef DND_TIMING_H
#define DND_TIMING_H
#include <stdint.h>
int64_t dnd_timing_now(void); /* Monotonic microseconds, not UTC/calendar time. */
int64_t dnd_calendar_read(void); /* Local IPL calendar seconds since 1970, or -1. */
int dnd_calendar_write(int64_t seconds); /* 0 persisted, -1 unavailable, -2 SRAM pending. */
int dnd_console_sound_read(void);
int dnd_console_sound_write(int mode);
#endif
