#include <gccore.h>
#include <ogc/exi.h>
#include <stdint.h>
#include <string.h>

/*
 * libogc2's SYS_Report() routes through InitializeUART(), which disables
 * UART writes when SYS_GetConsoleType() reports a retail GameCube.  For this
 * CI-only control executable, write directly to Dolphin's emulated IPL UART.
 * Do not copy this diagnostic into hardware-targeted application code.
 */
static void dolphin_debug_uart(char *message) {
    size_t remaining = strlen(message);
    char *cursor = message;

    if (!EXI_Lock(EXI_CHANNEL_0, EXI_DEVICE_1, NULL))
        return;
    while (remaining) {
        uint32_t command = 0xa0010000;
        unsigned int length = remaining < 12 ? (unsigned int)remaining : 12;
        if (!EXI_Select(EXI_CHANNEL_0, EXI_DEVICE_1, EXI_SPEED8MHZ))
            break;
        if (!EXI_ImmEx(EXI_CHANNEL_0, &command, sizeof(command), EXI_WRITE) ||
            !EXI_ImmEx(EXI_CHANNEL_0, cursor, length, EXI_WRITE)) {
            EXI_Deselect(EXI_CHANNEL_0);
            break;
        }
        EXI_Deselect(EXI_CHANNEL_0);
        cursor += length;
        remaining -= length;
    }
    EXI_Unlock(EXI_CHANNEL_0);
}

int main(void) {
    char marker[] = "DND_EMULATOR_CONTROL_BOOTED\r";
    dolphin_debug_uart(marker);
    VIDEO_Init();
    for (;;)
        VIDEO_WaitVSync();
    return 0;
}
