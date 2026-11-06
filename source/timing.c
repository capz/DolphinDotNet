#define _POSIX_C_SOURCE 200809L
#include "dnd_timing.h"
#include "dnd_managed.h"
#ifdef GEKKO
#include <gccore.h>
#include <ogc/timesupp.h>
extern u32 __SYS_GetRTC(u32 *rtc);
extern u32 __SYS_SyncSram(void);
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
int64_t dnd_calendar_read(void) {
#if defined(GEKKO) && defined(HW_DOL)
    uint32_t rtc;
    if (!__SYS_GetRTC(&rtc)) return -1;
    /* IPL uses unsigned 32-bit modular addition, including negative bias. */
    uint32_t local = rtc + SYS_GetCounterBias();
    return INT64_C(946684800) + local;
#else
    return -1; /* A host build never reads or writes its operating-system clock. */
#endif
}
int dnd_calendar_write(int64_t seconds) {
#if defined(GEKKO) && defined(HW_DOL)
    if (seconds < INT64_C(946684800) || seconds >= INT64_C(4102444800)) return -1;
    uint32_t rtc;
    if (!__SYS_GetRTC(&rtc)) return -1;
    SYS_SetCounterBias((uint32_t)(seconds - INT64_C(946684800)) - rtc);
    /* Do not call settime/__SYS_SetTime: older libogc versions change the
     * hardware timebase, which would rewind the monotonic animation clock.
     * SystemClock reads RTC+bias directly; IPL and subsequent programs do too. */
    uint64_t deadline=gettime()+secs_to_ticks(1);
    while (!__SYS_SyncSram()) {
        if (gettime() >= deadline) return -2;
    }
    return 0;
#else
    (void)seconds;
    return -1;
#endif
}
int dnd_console_sound_read(void) {
#if defined(GEKKO) && defined(HW_DOL)
    return SYS_GetSoundMode()==SYS_SOUND_STEREO?0:1;
#else
    return -1;
#endif
}
int dnd_console_sound_write(int mode) {
#if defined(GEKKO) && defined(HW_DOL)
    if(mode<0||mode>1)return -1;
    SYS_SetSoundMode(mode==0?SYS_SOUND_STEREO:SYS_SOUND_MONO);
    uint64_t deadline=gettime()+secs_to_ticks(1);
    while(!__SYS_SyncSram())if(gettime()>=deadline)return -2;
    return 0;
#else
    (void)mode;return -1;
#endif
}
