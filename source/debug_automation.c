#include "dnd_debug_automation.h"
#ifdef DND_DEBUG_AUTOMATION
#include <stddef.h>
#include <string.h>

/* Explicit, opt-in debug-build symbol for the standalone RSP test runner.
 * The target must be halted while writing commands or reading XFB memory.
 * Never issue GX or PAD calls from debugger callbacks. */
DndAutomationMailbox dnd_automation_mailbox __attribute__((used,aligned(32))) = {
    .magic=DND_AUTO_MAGIC, .version=DND_AUTO_VERSION
};

typedef struct {
    DndInputSample sample;
    uint32_t remaining;
} DndOverride;
static DndOverride overrides[4];

static int valid(const DndAutomationMailbox *m) {
    if (m->command == DND_AUTO_CLEAR)
        return m->port <= 4;
    if (m->command != DND_AUTO_SET || m->port >= 4 ||
        m->polls == 0 || m->polls > 3600 || m->connected > 1 ||
        (m->buttons & ~0x1f7fu) != 0 ||
        m->stick_x < -127 || m->stick_x > 127 ||
        m->stick_y < -127 || m->stick_y > 127 ||
        m->cstick_x < -127 || m->cstick_x > 127 ||
        m->cstick_y < -127 || m->cstick_y > 127 ||
        m->trigger_l > 255 || m->trigger_r > 255)
        return 0;
    return 1;
}

void dnd_automation_input(DndInputSample samples[4]) {
    DndAutomationMailbox *m=&dnd_automation_mailbox;
    if (m->request_seq != m->ack_seq) {
        if (valid(m)) {
            if (m->command == DND_AUTO_CLEAR) {
                if (m->port == 4) memset(overrides,0,sizeof overrides);
                else memset(&overrides[m->port],0,sizeof overrides[0]);
            } else {
                DndOverride *o=&overrides[m->port];
                o->sample=(DndInputSample){
                    .connected=m->connected != 0, .supports_rumble=false,
                    .held=(uint16_t)m->buttons,
                    .stick_x=(int8_t)m->stick_x, .stick_y=(int8_t)m->stick_y,
                    .cstick_x=(int8_t)m->cstick_x, .cstick_y=(int8_t)m->cstick_y,
                    .trigger_l=(uint8_t)m->trigger_l, .trigger_r=(uint8_t)m->trigger_r
                };
                o->remaining=m->polls;
            }
            m->status=DND_AUTO_ACCEPTED;
        } else m->status=DND_AUTO_REJECTED;
        /* Publish ACK last. Invalid commands never change active overrides. */
        m->ack_seq=m->request_seq;
    }
    for (unsigned port=0;port<4;port++) {
        DndOverride *o=&overrides[port];
        if(o->remaining) {
            samples[port]=o->sample;
            o->remaining--;
        }
    }
    m->input_polls++;
}

void dnd_automation_published_frame(const void *xfb,uint32_t width,uint32_t height) {
    DndAutomationMailbox *m=&dnd_automation_mailbox;
    /* XFB must remain live and uncached while the GDB target is stopped.
     * Dimensions are bounded before exposing any RSP-read length. */
    if(xfb && width && width<=720 && height && height<=576) {
        m->xfb_address=(uint32_t)(uintptr_t)xfb;
        m->xfb_width=width; m->xfb_height=height;
        m->xfb_stride=width*2;
        m->xfb_bytes=width*height*2;
        m->xfb_format=DND_AUTO_XFB_YUYV;
    } else {
        m->xfb_address=0;m->xfb_width=0;m->xfb_height=0;
        m->xfb_stride=0;m->xfb_bytes=0;m->xfb_format=0;
    }
    m->frame_serial++;
}

__attribute__((noinline,used,externally_visible))
void dnd_automation_frame_boundary(void) {
    /* A stable address for GDB Z0; the barrier prevents tail/inlined removal. */
    __asm__ __volatile__("" ::: "memory");
}
#endif
