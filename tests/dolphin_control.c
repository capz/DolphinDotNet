#include <gccore.h>

/* Minimal libogc-only program to separate emulator/runner failures from
 * problems specific to DolphinDotNet's generated DOL. */
int main(void) {
    SYS_Report("DND_EMULATOR_CONTROL_BOOTED\n");
    VIDEO_Init();
    for (;;) VIDEO_WaitVSync();
    return 0;
}
