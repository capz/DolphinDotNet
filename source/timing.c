#define _POSIX_C_SOURCE 200809L
#include "dnd_timing.h"
#include "dnd_managed.h"
#ifdef GEKKO
#include <ogc/timesupp.h>
#else
#include <time.h>
#endif
int64_t dnd_timing_now(void) {
#ifdef GEKKO
    return (int64_t)ticks_to_microsecs(gettime());
#else
    struct timespec now;
    if (clock_gettime(CLOCK_MONOTONIC,&now)!=0) {
        dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Monotonic clock unavailable.");
        return 0;
    }
    return (int64_t)now.tv_sec*1000000 + now.tv_nsec/1000;
#endif
}
