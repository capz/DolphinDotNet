#include "dnd_system_firmware.h"
#include <stdint.h>
#include <string.h>

#if defined(DND_GAMECUBE_STORAGE) || defined(DND_FIRMWARE_TEST)
#ifdef DND_FIRMWARE_TEST
extern int dnd_fw_lock(void),dnd_fw_select(void),dnd_fw_deselect(void);
extern void dnd_fw_unlock(void),dnd_fw_write(unsigned,uint32_t);
extern uint32_t dnd_fw_read(unsigned);
extern uint64_t dnd_fw_now(void);
#else
#include <gccore.h>
#include <ogc/exi.h>
#include <ogc/lwp_watchdog.h>
static int dnd_fw_lock(void){return EXI_Lock(EXI_CHANNEL_0,EXI_DEVICE_1,NULL);}
static int dnd_fw_select(void){return EXI_Select(EXI_CHANNEL_0,EXI_DEVICE_1,EXI_SPEED32MHZ);}
static int dnd_fw_deselect(void){return EXI_Deselect(EXI_CHANNEL_0);}
static void dnd_fw_unlock(void){EXI_Unlock(EXI_CHANNEL_0);}
static void dnd_fw_write(unsigned reg,uint32_t value){((volatile uint32_t *)0xcc006800)[reg]=value;__asm__ volatile("eieio" ::: "memory");}
static uint32_t dnd_fw_read(unsigned reg){return ((volatile uint32_t *)0xcc006800)[reg];}
static uint64_t dnd_fw_now(void){return ticks_to_microsecs(gettime());}
#endif
/* Pinned libogc2 system.c confirms channel 0/device 1 at 32 MHz and the
 * offset<<6 read command. __SYS_ReadROM is deliberately not called: its retry
 * loop is unbounded. Immediate transfers avoid DMA lifetime/alignment hazards.
 * EXI_Lock/Select preserve normal SRAM/RTC arbitration. We do not call EXI_Imm:
 * its private transfer flags would outlive a timed-out immediate transaction.
 * This driver owns the channel until deselect/unlock and changes no ROM bytes.
 * ROM bursts must stop at 1 KiB boundaries, as in libogc's __SYS_ReadROM.
 * A new command/chip-select cycle is required for the next page. Dolphin's
 * linear, pre-decoded ROM implementation does not expose this restriction. */
static int transfer(uint32_t value,unsigned bytes,unsigned write,uint32_t *result,uint64_t deadline)
{
    if(dnd_fw_now()>=deadline)return 0;
    dnd_fw_write(4,value);
    dnd_fw_write(3,((bytes-1)<<4)|(write?4u:0u)|1u);
    unsigned polls=0;
    while(dnd_fw_read(3)&1u)
        if(++polls>=4096||dnd_fw_now()>=deadline)return 0;
    if(result)*result=dnd_fw_read(4);
    return 1;
}
int dnd_firmware_read_raw(void *destination,unsigned offset,unsigned count)
{
    if(!destination||!count||count>4096||offset>0x200000||count>0x200000-offset)return -3;
    if(!dnd_fw_lock())return -1;
    uint64_t deadline=dnd_fw_now()+20000; /* 20 ms and finite poll-count cap. */
    int result=-2,selected=0;
    uint8_t *out=destination;
    for(unsigned at=0;at<count;){
        if(dnd_fw_now()>=deadline)goto cleanup;
        if(!dnd_fw_select()){result=-1;goto cleanup;}selected=1;
        unsigned address=offset+at,page_bytes=1024-(address&1023u);
        if(page_bytes>count-at)page_bytes=count-at;
        if(!transfer(address<<6,4,1,NULL,deadline))goto cleanup;
        for(unsigned done=0;done<page_bytes;){
            unsigned n=page_bytes-done;if(n>4)n=4;
            uint32_t value;
            if(!transfer(0xffffffffu,n,0,&value,deadline))goto cleanup;
            for(unsigned i=0;i<n;i++)out[at+done+i]=(uint8_t)(value>>(24-i*8));
            done+=n;
        }
        int deselected=dnd_fw_deselect();selected=0;
        if(!deselected){result=-1;goto cleanup;}
        at+=page_bytes;
    }
    result=(int)count;
cleanup:
    /* Immediate transfers never reference memory after return. Clear START on
     * timeout, then release chip select and the libogc lock on every path. */
    if(selected){if(result<0)dnd_fw_write(3,0);if(!dnd_fw_deselect()&&result>=0)result=-1;}
    dnd_fw_unlock();
    return result;
}
#else
int dnd_firmware_read_raw(void *destination,unsigned offset,unsigned count)
{(void)destination;(void)offset;(void)count;return -1;}
#endif
int dnd_firmware_read(DndArray *destination,int offset,int count)
{
    if(!destination||destination->element_size!=1||count<1||offset<0||
       (unsigned)count>destination->length)return -3;
    return dnd_firmware_read_raw(destination->data,(unsigned)offset,(unsigned)count);
}
