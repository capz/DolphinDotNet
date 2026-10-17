#ifndef DND_LINK_TEST_EXI_H
#define DND_LINK_TEST_EXI_H
#include <stdint.h>
typedef uint32_t u32;
#define EXI_CHANNEL_0 0
#define EXI_DEVICE_2 2
#define EXI_SPEED32MHZ 5
#define EXI_WRITE 1
#define EXI_READ 0
int EXI_GetID(int channel, int device, u32 *id);
int EXI_Lock(int channel, int device, void *callback);
int EXI_Select(int channel, int device, int speed);
int EXI_ImmEx(int channel, void *buffer, u32 length, u32 mode);
int EXI_Deselect(int channel);
int EXI_Unlock(int channel);
#endif
