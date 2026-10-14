#include "dnd_input.h"
#include <gccore.h>
#include <assert.h>
#include <stdio.h>
unsigned input_test_interrupt_depth;
static unsigned init_count, alarm_count, motors[4];
static bool fail_create, fail_set;
static struct { bool allocated, active; struct timespec due; alarmcallback callback; void *argument; } alarms[8];
u32 PAD_Init(void) { init_count++; return 1; }
u32 PAD_ScanPads(void) { return PAD_CHAN_BIT(0)|PAD_CHAN_BIT(1); }
u32 PAD_GetType(s32 port,u32 *type) { *type=port==1?SI_GC_NOMOTOR:0; return 1; }
u32 PAD_ButtonsHeld(s32 port) { return port==0?0x100:0; }
int8_t PAD_StickX(s32 port) { (void)port; return 80; }
int8_t PAD_StickY(s32 port) { (void)port; return 0; }
int8_t PAD_SubStickX(s32 port) { (void)port; return 0; }
int8_t PAD_SubStickY(s32 port) { (void)port; return 65; }
uint8_t PAD_TriggerL(s32 port) { (void)port; return 180; }
uint8_t PAD_TriggerR(s32 port) { (void)port; return 0; }
void PAD_ControlMotor(s32 port,u32 command) { motors[port]=command; }
s32 SYS_CreateAlarm(syswd_t *alarm) {
    assert(input_test_interrupt_depth>0);
    if (fail_create) return -1;
    for (unsigned i=1;i<8;i++) if (!alarms[i].allocated) { alarms[i].allocated=true; alarm_count++; *alarm=i; return 0; }
    return -1;
}
s32 SYS_SetAlarm(syswd_t alarm,const struct timespec *due,alarmcallback callback,void *argument) {
    assert(input_test_interrupt_depth>0 && alarms[alarm].allocated);
    if (fail_set) return -1;
    alarms[alarm].due=*due; alarms[alarm].callback=callback; alarms[alarm].argument=argument; alarms[alarm].active=true;
    return 0;
}
s32 SYS_CancelAlarm(syswd_t alarm) { alarms[alarm].active=false; return 0; }
s32 SYS_RemoveAlarm(syswd_t alarm) { assert(alarms[alarm].allocated && !alarms[alarm].active); alarms[alarm].allocated=false; alarm_count--; return 0; }
int main(void) {
    dnd_input_backend_init(); assert(init_count==1);
    DndInputSample pads[4]={0}; dnd_input_backend_sample(pads);
    assert(pads[0].connected && pads[0].supports_rumble && pads[0].held==0x100 && pads[0].stick_x==80);
    assert(pads[1].connected && !pads[1].supports_rumble && !pads[2].connected);
    fail_create=true; assert(!dnd_input_backend_rumble(0,100)); assert(alarm_count==0 && input_test_interrupt_depth==0);
    fail_create=false; fail_set=true; assert(!dnd_input_backend_rumble(0,100)); assert(motors[0]==PAD_MOTOR_STOP_HARD);
    fail_set=false; assert(dnd_input_backend_rumble(0,1500000));
    assert(alarms[1].due.tv_sec==1 && alarms[1].due.tv_nsec==500000000 && motors[0]==PAD_MOTOR_RUMBLE);
    alarms[1].callback(1,alarms[1].argument); assert(motors[0]==PAD_MOTOR_STOP_HARD);
    assert(dnd_input_backend_rumble(0,100)); assert(alarm_count==1); // Alarm is reused.
    assert(dnd_input_backend_rumble(3,200)); assert(alarm_count==2);
    alarms[2].callback(2,alarms[2].argument); assert(motors[3]==PAD_MOTOR_STOP_HARD);
    dnd_input_backend_stop_rumble(0); assert(!alarms[1].active && motors[0]==PAD_MOTOR_STOP_HARD);
    dnd_input_backend_shutdown(); assert(alarm_count==0 && input_test_interrupt_depth==0);
    dnd_input_backend_shutdown(); assert(alarm_count==0);
    puts("GameCube input backend capability, alarm failure, callback and cleanup contracts passed.");
}
