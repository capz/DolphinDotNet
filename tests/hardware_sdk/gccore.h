#ifndef HARDWARE_TEST_GCCORE_H
#define HARDWARE_TEST_GCCORE_H
#include <stdint.h>
#include <stdbool.h>
typedef uint32_t u32;
typedef int32_t s32;
#define FALSE 0
#define TRUE 1
#define SYS_RESTART 0
#define SYS_HOTRESET 1
#define SYS_RETURNTOMENU 2
#define EXI_CHANNEL_0 0
#define EXI_DEVICE_2 2
#define SI_GBA 0x00040000
#define SI_ERROR_BUSY 128
#define SI_ERROR_NO_RESPONSE 8
#define CARD_ERROR_NOCARD -3
#define CARD_ERROR_BUSY -1
#define CARD_ERROR_WRONGDEVICE -2
int CARD_ProbeEx(int,s32*,s32*);
int EXI_GetID(int,int,u32*);
u32 SI_Probe(int);
u32 SI_GetType(int);
u32 PAD_GetType(int,u32*);
int DVD_CheckDisk(void);
int SYS_ResetButtonDown(void);
void SYS_ResetSystem(int,int,int);
#endif
