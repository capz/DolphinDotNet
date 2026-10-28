#ifndef DND_FRAME_CLEAR_H
#define DND_FRAME_CLEAR_H
#include <stdbool.h>
#include <stdint.h>
/* EndFrame clears the EFB with BeginFrame's packed color and completes GX.
 * Raw/external ownership invalidates that proof; never infer from GPU timing. */
typedef struct { uint32_t color; bool clean,trusted; } DndFrameClear;
static inline bool dnd_frame_clear_begin(DndFrameClear *s,bool reuse,uint32_t color) {
    bool needed=!reuse||!s->clean||s->color!=color;
    s->color=color;s->clean=false;s->trusted=true;return needed;
}
static inline void dnd_frame_clear_end(DndFrameClear *s){s->clean=s->trusted;}
static inline void dnd_frame_clear_invalidate(DndFrameClear *s){s->clean=s->trusted=false;}
#endif
