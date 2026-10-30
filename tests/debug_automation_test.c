#include "dnd_debug_automation.h"
#include "input_test_backend.h"
#include <assert.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>

/* Host ABI fixture: the debugger reads this exact 96-byte, big-endian-on-PPC
 * mailbox through RSP. No controller hardware or GX frame is required. */
_Static_assert(sizeof(DndAutomationMailbox)==0x60,"Unexpected debugger mailbox size.");
_Static_assert(offsetof(DndAutomationMailbox,request_seq)==0x08,"request location");
_Static_assert(offsetof(DndAutomationMailbox,command)==0x14,"command location");
_Static_assert(offsetof(DndAutomationMailbox,input_polls)==0x40,"input count location");
_Static_assert(offsetof(DndAutomationMailbox,xfb_address)==0x48,"XFB location");

static void send(uint32_t seq, uint32_t command, uint32_t port, uint32_t polls,
                 uint32_t buttons) {
    dnd_automation_mailbox.command=command;
    dnd_automation_mailbox.port=port;
    dnd_automation_mailbox.polls=polls;
    dnd_automation_mailbox.connected=1;
    dnd_automation_mailbox.buttons=buttons;
    dnd_automation_mailbox.stick_x=80;
    dnd_automation_mailbox.stick_y=-20;
    dnd_automation_mailbox.cstick_x=0;
    dnd_automation_mailbox.cstick_y=0;
    dnd_automation_mailbox.trigger_l=180;
    dnd_automation_mailbox.trigger_r=0;
    dnd_automation_mailbox.request_seq=seq;
}
int main(void) {
    assert(dnd_automation_mailbox.magic==DND_AUTO_MAGIC);
    assert(dnd_automation_mailbox.version==1);
    DndInputSample physical={.connected=true,.held=0x200,.stick_x=-80};
    test_input_set(0,physical);
    physical.held=0x400;
    test_input_set(1,physical);
    dnd_input_poll();
    assert(dnd_input_buttons(0,0)==0x200);
    send(1,DND_AUTO_SET,0,2,0x100);
    dnd_input_poll();
    assert(dnd_automation_mailbox.ack_seq==1 && dnd_automation_mailbox.status==DND_AUTO_ACCEPTED);
    assert(dnd_input_buttons(0,0)==0x100 && dnd_input_buttons(0,1)==0x100 &&
           dnd_input_buttons(0,2)==0x200);
    assert(dnd_input_buttons(1,0)==0x400); /* port isolation */
    assert(dnd_input_axis(0,0)>0.95f && dnd_input_axis(0,1)<0);
    assert(dnd_input_axis(0,4)==1);
    dnd_input_poll();
    assert(dnd_input_buttons(0,0)==0x100 && dnd_input_buttons(0,1)==0);
    dnd_input_poll();
    assert(dnd_input_buttons(0,0)==0x200 && dnd_input_buttons(0,1)==0x200 &&
           dnd_input_buttons(0,2)==0x100); /* override expired */
    send(2,DND_AUTO_SET,2,1,0x100);
    dnd_automation_mailbox.polls=0;
    dnd_input_poll();
    assert(dnd_automation_mailbox.ack_seq==2 && dnd_automation_mailbox.status==DND_AUTO_REJECTED);
    assert(dnd_input_buttons(0,0)==0x200);
    send(3,DND_AUTO_SET,0,200,0x100);
    dnd_input_poll();
    assert(dnd_input_buttons(0,0)==0x100);
    send(4,DND_AUTO_CLEAR,4,1,0);
    dnd_input_poll();
    assert(dnd_automation_mailbox.status==DND_AUTO_ACCEPTED);
    assert(dnd_input_buttons(0,0)==0x200);
    /* Target sends only a validated live XFB pointer and exact stride/bytes.
     * No graphics allocation, malloc, or copy is involved. */
    dnd_automation_published_frame((void*)(uintptr_t)0xc0120000u,640,480);
    assert(dnd_automation_mailbox.frame_serial==1);
    assert(dnd_automation_mailbox.xfb_address==0xc0120000u);
    assert(dnd_automation_mailbox.xfb_width==640 &&
           dnd_automation_mailbox.xfb_height==480 &&
           dnd_automation_mailbox.xfb_stride==1280 &&
           dnd_automation_mailbox.xfb_bytes==614400 &&
           dnd_automation_mailbox.xfb_format==1);
    dnd_automation_frame_boundary();
    dnd_automation_published_frame(NULL,10000,120);
    assert(dnd_automation_mailbox.frame_serial==2 && dnd_automation_mailbox.xfb_address==0);
    assert(dnd_automation_mailbox.input_polls==7);
    dnd_input_shutdown();
    puts("debugger hardware automation protocol OK");
    return 0;
}
