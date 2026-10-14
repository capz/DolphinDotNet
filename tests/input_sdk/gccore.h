#ifndef INPUT_TEST_GCCORE_H
#define INPUT_TEST_GCCORE_H
#include <stdint.h>
#include <time.h>
typedef uint32_t u32;
typedef int32_t s32;
typedef uint32_t syswd_t;
typedef void (*alarmcallback)(syswd_t,void *);
#define PAD_CHAN_BIT(i) (0x80000000u>>(i))
#define SI_GC_NOMOTOR 0x20000000u
#define PAD_MOTOR_RUMBLE 1
#define PAD_MOTOR_STOP_HARD 2
u32 PAD_Init(void);
u32 PAD_ScanPads(void);
u32 PAD_GetType(s32,u32 *);
u32 PAD_ButtonsHeld(s32);
int8_t PAD_StickX(s32); int8_t PAD_StickY(s32);
int8_t PAD_SubStickX(s32); int8_t PAD_SubStickY(s32);
uint8_t PAD_TriggerL(s32); uint8_t PAD_TriggerR(s32);
void PAD_ControlMotor(s32,u32);
s32 SYS_CreateAlarm(syswd_t *);
s32 SYS_SetAlarm(syswd_t,const struct timespec *,alarmcallback,void *);
s32 SYS_CancelAlarm(syswd_t);
s32 SYS_RemoveAlarm(syswd_t);
#endif
