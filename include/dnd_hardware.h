#ifndef DND_HARDWARE_H
#define DND_HARDWARE_H
/* HardwareKind: 0 controller, 1 GBA link, 2 memory card, 3 SD slot,
 * 4 SD2SP2, 5 BBA, 6 modem, 7 game DVD, 8 GC Loader, 9 GBP.
 * Ports: 0-3 controller, 4/5 slots A/B, 6 SP1, 7 SP2, 8 DVD, 9 HSP.
 * Presence: 0 absent, 1 present, 2 busy, 3 unknown, 4 unsupported.
 */
int dnd_hardware_probe(int kind,int port,int extended);
int dnd_hardware_id(int kind,int port);
int dnd_platform_reboot(int mode);
int dnd_platform_reset_button(void);
#endif
