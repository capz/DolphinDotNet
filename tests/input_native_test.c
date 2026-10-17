#include "input_test_backend.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#ifdef DND_SAMPLE_CONTROLS
#include "dnd_performance.h"
#endif
static void close_to(float a,float b) { assert(fabsf(a-b)<0.0001f); }
int main(void) {
    DndInputSample raw={.connected=true,.supports_rumble=true,.held=0x100,.stick_x=80,.cstick_y=65,.trigger_l=180};
    test_input_set(0,raw); dnd_input_poll();
    assert(dnd_input_flags(0)==(DND_INPUT_CONNECTED|DND_INPUT_CONNECTED_NOW|DND_INPUT_RUMBLE));
    assert(dnd_input_buttons(0,0)==0x100 && dnd_input_buttons(0,1)==0);
    close_to(dnd_input_axis(0,0),1); close_to(dnd_input_axis(0,3),1); close_to(dnd_input_axis(0,4),1);
    DndGamePad saved=*dnd_input_gamepad(0);
    assert(dnd_input_rumble(0,0.1f)); test_input_advance(99999); assert(test_input_motors()==1);
    test_input_advance(1); assert(test_input_motors()==0); /* no input update needed */
    raw.held=0x200; raw.stick_x=5; raw.cstick_y=0; raw.trigger_l=105; test_input_set(0,raw); dnd_input_poll();
    assert(dnd_input_buttons(0,1)==0x200 && dnd_input_buttons(0,2)==0x100);
    close_to(dnd_input_axis(0,0),0); close_to(dnd_input_axis(0,4),0.5f);
    assert(saved.held==0x100 && saved.axes[0]==1);
    for (int i=0;i<10;i++) { assert(dnd_input_buttons(0,1)==0x200); (void)dnd_input_axis(0,0); }
    assert(test_input_polls()==2);
    raw.stick_x=80; raw.stick_y=80; test_input_set(0,raw); dnd_input_poll();
    float x=dnd_input_axis(0,0), y=dnd_input_axis(0,1); close_to(x*x+y*y,1);
    dnd_input_configure(0,0,0,100,100,0,200); raw.stick_y=0; raw.trigger_l=100; test_input_set(0,raw); dnd_input_poll();
    close_to(dnd_input_axis(0,0),0.8f); close_to(dnd_input_axis(0,4),0.5f);
    assert(dnd_input_rumble(0,1)); assert(dnd_input_rumble(0,0.2f)); test_input_advance(200000); assert(test_input_motors()==0);
    assert(dnd_input_rumble(0,1)); raw.connected=false; test_input_set(0,raw); dnd_input_poll();
    assert(dnd_input_flags(0)==DND_INPUT_DISCONNECTED_NOW && dnd_input_buttons(0,2)==0 && test_input_motors()==0);
    dnd_input_poll(); assert(dnd_input_flags(0)==0);
    raw.connected=true; raw.supports_rumble=false; test_input_set(0,raw); dnd_input_poll();
    assert(dnd_input_buttons(0,1)==0 && !dnd_input_rumble(0,1));
    raw.supports_rumble=true; test_input_set(3,raw); dnd_input_poll(); assert(dnd_input_rumble(3,1));
    dnd_input_shutdown(); assert(test_input_motors()==0 && dnd_input_flags(3)==0);
    assert(!dnd_input_rumble(-1,1) && !dnd_input_rumble(4,1));
#ifdef DND_SAMPLE_CONTROLS
    dnd_performance_set_visible(0);
    raw=(DndInputSample){.connected=true,.held=0x10};
    test_input_set(0,raw); dnd_input_poll();
    assert(!dnd_performance_visible()); /* Connect-held is a baseline. */
    raw.held=0; test_input_set(0,raw); dnd_input_poll();
    raw.held=0x10; test_input_set(0,raw); dnd_input_poll();
    assert(dnd_performance_visible() && dnd_input_buttons(0,1)==0x10);
    for(int i=0;i<10;i++)dnd_input_poll();
    assert(dnd_performance_visible()); /* Holding cannot flicker the overlay. */
    raw.held=0; test_input_set(0,raw); dnd_input_poll();
    assert(dnd_performance_visible()); /* Release does not toggle. */
    raw.held=0x10; test_input_set(0,raw); dnd_input_poll();
    assert(!dnd_performance_visible());
    raw.connected=false; test_input_set(0,raw); dnd_input_poll();
    raw.connected=true; test_input_set(0,raw); dnd_input_poll();
    assert(!dnd_performance_visible()); /* Reconnect-held is also a baseline. */
    for(int port=1;port<4;port++) {
        raw.held=0; test_input_set(port,raw); dnd_input_poll();
        raw.held=0x10; test_input_set(port,raw); dnd_input_poll();
        assert(!dnd_performance_visible()); /* Only controller 1 owns the UI. */
    }
    dnd_input_shutdown();
#endif
    puts("Controller transitions, calibration, snapshots, timed rumble and cleanup passed.");
}
