#ifndef DND_DEBUG_AUTOMATION_H
#define DND_DEBUG_AUTOMATION_H
#include <stdint.h>
#include "dnd_input.h"

/* GameCube Debug ELF only: standard RSP m/M/Z0 access, no extra EXI protocol.
 * All fields are naturally aligned, big-endian uint32, frozen while the
 * debugger is stopped at dnd_automation_frame_boundary. */
enum {
    DND_AUTO_MAGIC = 0x444e4441u, /* "DNDA" */
    DND_AUTO_VERSION = 1,
    DND_AUTO_SET = 1,
    DND_AUTO_CLEAR = 2,
    DND_AUTO_ACCEPTED = 1,
    DND_AUTO_REJECTED = 2,
    DND_AUTO_XFB_YUYV = 1
};
typedef struct {
    volatile uint32_t magic;          /* +00 */
    volatile uint32_t version;        /* +04 */
    volatile uint32_t request_seq;    /* +08, host writes LAST */
    volatile uint32_t ack_seq;        /* +0c */
    volatile uint32_t status;         /* +10 */
    volatile uint32_t command;        /* +14 */
    volatile uint32_t port;           /* +18: 0-3; 4 means all for CLEAR */
    volatile uint32_t polls;          /* +1c: bounded input polls, not VI frames */
    volatile uint32_t connected;      /* +20 */
    volatile uint32_t buttons;        /* +24: physical PAD bits */
    volatile int32_t stick_x;         /* +28 */
    volatile int32_t stick_y;         /* +2c */
    volatile int32_t cstick_x;        /* +30 */
    volatile int32_t cstick_y;        /* +34 */
    volatile uint32_t trigger_l;      /* +38 */
    volatile uint32_t trigger_r;      /* +3c */
    volatile uint32_t input_polls;    /* +40: progress counter */
    volatile uint32_t frame_serial;   /* +44: completed GX + VI presentation */
    volatile uint32_t xfb_address;    /* +48: uncached MEM2/MEM1 EXTERNAL XFB */
    volatile uint32_t xfb_width;      /* +4c */
    volatile uint32_t xfb_height;     /* +50 */
    volatile uint32_t xfb_stride;     /* +54, bytes */
    volatile uint32_t xfb_bytes;      /* +58 */
    volatile uint32_t xfb_format;     /* +5c: Y0 U Y1 V */
} DndAutomationMailbox;

#ifdef DND_DEBUG_AUTOMATION
extern DndAutomationMailbox dnd_automation_mailbox;
void dnd_automation_input(DndInputSample samples[4]);
void dnd_automation_published_frame(const void *xfb, uint32_t width, uint32_t height);
/* Set a GDB software breakpoint at this *function* symbol, not the mailbox.
 * It is called AFTER GPU completion, VI wait and final framebuffer flip. */
void dnd_automation_frame_boundary(void);
#endif
#endif
