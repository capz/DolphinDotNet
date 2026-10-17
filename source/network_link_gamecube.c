#ifdef GEKKO
#include "dnd_network_platform.h"
#ifdef HW_DOL
#include <ogc/exi.h>
#include <stddef.h>

int dnd_np_link(void) {
    /* Same NWAYS register/bits as pinned libogc2 gcif.c. Only the original
       Nintendo BBA is supported; never interpret another adapter's registers.
       EXI_GetID on serial port 1 uses its cached ID or a non-waiting lock. */
    u32 id = 0;
    if (!EXI_GetID(EXI_CHANNEL_0, EXI_DEVICE_2, &id) || id != 0x04020200)
        return -1;
    if (!EXI_Lock(EXI_CHANNEL_0, EXI_DEVICE_2, NULL)) return -1;
    int link = -1;
    if (EXI_Select(EXI_CHANNEL_0, EXI_DEVICE_2, EXI_SPEED32MHZ)) {
        u32 command = 0x80003100; /* Read BBA_NWAYS (0x31). */
        unsigned char value = 0;
        int read = EXI_ImmEx(EXI_CHANNEL_0, &command, sizeof(command), EXI_WRITE) &&
                   EXI_ImmEx(EXI_CHANNEL_0, &value, sizeof(value), EXI_READ);
        int deselected = EXI_Deselect(EXI_CHANNEL_0);
        if (read && deselected && value != 0xff) link = (value & 3) != 0;
    }
    EXI_Unlock(EXI_CHANNEL_0);
    return link;
}
#else
int dnd_np_link(void) { return -1; }
#endif
#endif
