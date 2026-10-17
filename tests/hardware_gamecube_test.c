#include "dnd_hardware.h"
#include <gccore.h>
#include <sdcard/gcsd.h>
#include <assert.h>
#include <stdio.h>

static bool mounted[7];
static int result[2],probes,resets;
bool dnd_storage_is_mounted(int device){return mounted[device];}
int CARD_ProbeEx(int slot,s32 *blocks,s32 *sector){assert(slot>=0 && slot<2);probes++;*blocks=64;*sector=8192;return result[slot];}
int EXI_GetID(int channel,int device,u32 *id){(void)channel;(void)device;*id=0;return 0;}
u32 SI_Probe(int port){(void)port;return SI_ERROR_NO_RESPONSE;}
u32 SI_GetType(int port){(void)port;return SI_ERROR_NO_RESPONSE;}
u32 PAD_GetType(int port,u32 *type){(void)port;*type=0;return 0;}
int dnd_input_flags(int port){(void)port;return 0;}
int DVD_CheckDisk(void){return 0;}
int SYS_ResetButtonDown(void){return 0;}
void SYS_ResetSystem(int mode,int reset,int force){(void)mode;(void)reset;(void)force;resets++;}
DISC_INTERFACE *get_io_gcsda(void){return NULL;}
DISC_INTERFACE *get_io_gcsdb(void){return NULL;}
DISC_INTERFACE *get_io_gcsd2(void){return NULL;}
int main(void){
    assert(dnd_hardware_probe(2,4,0)==3 && probes==0);
    mounted[4]=true;
    assert(dnd_hardware_probe(2,4,0)==1 && probes==0); /* Safe remains cached. */
    result[0]=CARD_ERROR_NOCARD;
    assert(dnd_hardware_probe(2,4,1)==0 && probes==1); /* Mounted does not hide removal. */
    result[0]=0;assert(dnd_hardware_probe(2,4,1)==1);
    result[0]=CARD_ERROR_BUSY;assert(dnd_hardware_probe(2,4,1)==2);
    result[0]=CARD_ERROR_WRONGDEVICE;assert(dnd_hardware_probe(2,4,1)==0);
    result[0]=-99;assert(dnd_hardware_probe(2,4,1)==3);
    result[1]=CARD_ERROR_NOCARD;assert(dnd_hardware_probe(2,5,1)==0);
    result[1]=0;assert(dnd_hardware_probe(2,5,1)==1);
    int before=probes;mounted[0]=true;
    assert(dnd_hardware_probe(2,4,1)==2 && probes==before); /* No EXI probe while SD owns A. */
    assert(dnd_hardware_probe(2,0,1)==4 && probes==before);
    assert(resets==0);
    puts("Physical card probes, cached mounts, slot isolation and contention passed.");
}
