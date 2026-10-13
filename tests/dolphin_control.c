#include <gccore.h>
#include <stdint.h>

/* Independent libogc control: Dolphin CI reads this guest-written value via
 * the GDB stub. It is set by main(), never by the loader or CRT startup. */
volatile uint32_t dnd_control_main_reached;

int main(void) {
    dnd_control_main_reached = 0x444e4443u; /* "DNDC" */
    VIDEO_Init();
    for (;;)
        VIDEO_WaitVSync();
    return 0;
}
