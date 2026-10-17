#include "dnd_input.h"
#include <gccore.h>
#include <ogc/machine/processor.h>
#include <time.h>

static syswd_t alarms[4];
static bool alarm_ready[4];
static void stop_alarm(syswd_t alarm, void *argument) {
    (void)alarm;
    PAD_ControlMotor((int)(uintptr_t)argument, PAD_MOTOR_STOP_HARD);
}
void dnd_input_backend_init(void) { PAD_Init(); }
void dnd_input_backend_sample(DndInputSample samples[4]) {
    u32 connected=PAD_ScanPads();
    for (unsigned i=0; i<4; i++) {
        u32 type=0;
        /* PAD_ScanPads returns low bits 0..3, unlike PAD_Read/PAD_Reset's
         * high-bit PAD_CHAN_BIT mask. */
        samples[i].connected=(connected & (1u<<i))!=0;
        samples[i].supports_rumble=samples[i].connected && PAD_GetType((int)i, &type) && !(type & SI_GC_NOMOTOR);
        samples[i].held=(uint16_t)PAD_ButtonsHeld((int)i);
        samples[i].stick_x=PAD_StickX((int)i); samples[i].stick_y=PAD_StickY((int)i);
        samples[i].cstick_x=PAD_SubStickX((int)i); samples[i].cstick_y=PAD_SubStickY((int)i);
        samples[i].trigger_l=PAD_TriggerL((int)i); samples[i].trigger_r=PAD_TriggerR((int)i);
    }
}
bool dnd_input_backend_rumble(int port, uint32_t microseconds) {
    u32 level;
    _CPU_ISR_Disable(level);
    if (!alarm_ready[port]) {
        if (SYS_CreateAlarm(&alarms[port])!=0) { _CPU_ISR_Restore(level); return false; }
        alarm_ready[port]=true;
    }
    SYS_CancelAlarm(alarms[port]);
    struct timespec duration={microseconds/1000000, (long)(microseconds%1000000)*1000};
    bool success=SYS_SetAlarm(alarms[port], &duration, stop_alarm, (void *)(uintptr_t)port)==0;
    PAD_ControlMotor(port, success ? PAD_MOTOR_RUMBLE : PAD_MOTOR_STOP_HARD);
    _CPU_ISR_Restore(level);
    return success;
}
void dnd_input_backend_stop_rumble(int port) {
    u32 level;
    _CPU_ISR_Disable(level);
    if (alarm_ready[port]) SYS_CancelAlarm(alarms[port]);
    PAD_ControlMotor(port, PAD_MOTOR_STOP_HARD);
    _CPU_ISR_Restore(level);
}
void dnd_input_backend_shutdown(void) {
    u32 level;
    _CPU_ISR_Disable(level);
    for (unsigned i=0; i<4; i++) if (alarm_ready[i]) {
        SYS_CancelAlarm(alarms[i]);
        SYS_RemoveAlarm(alarms[i]);
        alarm_ready[i]=false;
    }
    _CPU_ISR_Restore(level);
}
