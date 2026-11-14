#include "dnd_system_firmware.h"
#include <stdint.h>
#include <string.h>

#if defined(DND_GAMECUBE_STORAGE) || defined(DND_FIRMWARE_TEST)
#ifdef DND_FIRMWARE_TEST
extern int dnd_fw_lock(void),dnd_fw_select(void),dnd_fw_deselect(void);
extern void dnd_fw_unlock(void),dnd_fw_write(unsigned,uint32_t);
extern uint32_t dnd_fw_read(unsigned);
extern uint64_t dnd_fw_now(void);
extern uint32_t dnd_fw_dma_prepare(void *,unsigned);
extern void dnd_fw_dma_complete(void *,unsigned),dnd_fw_dma_flush(void *,unsigned);
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
static uint32_t dnd_fw_dma_prepare(void *buffer,unsigned count)
{DCInvalidateRange(buffer,count);return (uint32_t)MEM_VIRTUAL_TO_PHYSICAL(buffer);}
static void dnd_fw_dma_complete(void *buffer,unsigned count)
{__asm__ volatile("sync" ::: "memory");DCInvalidateRange(buffer,count);}
static void dnd_fw_dma_flush(void *buffer,unsigned count){DCFlushRange(buffer,count);}
#endif
/* Pinned libogc2 system.c confirms channel 0/device 1 at 32 MHz and the
 * offset<<6 read command. __SYS_ReadROM is deliberately not called: its retry
 * loop is unbounded. Match its continuous DMA transfer for each 1 KiB page,
 * rather than hundreds of separately scheduled immediate reads. EXI_Lock/Select
 * preserve SRAM/RTC arbitration. Direct registers avoid libogc transfer flags
 * that could outlive a timeout. The aligned DMA buffer has static lifetime;
 * caller/managed memory is never a DMA destination. After a DMA timeout it is
 * quarantined for the rest of the process, including any late device writes. */
static uint8_t dma_page[1024] __attribute__((aligned(32)));
static int dma_quarantined;
static int wait_transfer(uint64_t deadline,unsigned limit)
{
    unsigned polls=0;
    while(dnd_fw_read(3)&1u)
        if(++polls>=limit||dnd_fw_now()>=deadline)return 0;
    return 1;
}
int dnd_firmware_read_raw(void *destination,unsigned offset,unsigned count)
{
    if(!destination||!count||count>4096||offset>0x200000||count>0x200000-offset)return -3;
    if(!dnd_fw_lock())return -1;
    if(dma_quarantined){dnd_fw_unlock();return -2;}
    uint64_t deadline=dnd_fw_now()+20000; /* 20 ms and finite poll-count cap. */
    int result=-2,selected=0;
    uint8_t *out=destination;
    for(unsigned at=0;at<count;){
        if(dnd_fw_now()>=deadline)goto cleanup;
        if(!dnd_fw_select()){result=-1;goto cleanup;}selected=1;
        unsigned address=offset+at,in_page=address&1023u,page_bytes=1024-in_page;
        if(page_bytes>count-at)page_bytes=count-at;
        dnd_fw_write(4,(address&~1023u)<<6);
        dnd_fw_write(3,0x35); /* Four-byte immediate address command. */
        if(!wait_transfer(deadline,4096)||dnd_fw_now()>=deadline)goto cleanup;
        dnd_fw_write(1,dnd_fw_dma_prepare(dma_page,sizeof(dma_page)));
        dnd_fw_write(2,sizeof(dma_page));
        dnd_fw_write(3,3); /* DMA read: one uninterrupted, aligned ROM page. */
        if(!wait_transfer(deadline,1000000)){dma_quarantined=1;goto cleanup;}
        dnd_fw_dma_complete(dma_page,sizeof(dma_page));
        memcpy(out+at,dma_page+in_page,page_bytes);
        int deselected=dnd_fw_deselect();selected=0;
        if(!deselected){result=-1;goto cleanup;}
        at+=page_bytes;
    }
    result=(int)count;
cleanup:
    /* Clear START and release the bus on every path. A DMA timeout must neither
     * clear nor reuse its buffer: hardware can still reference static storage. */
    if(selected){if(result<0)dnd_fw_write(3,0);if(!dnd_fw_deselect()&&result>=0)result=-1;}
    if(!dma_quarantined){memset(dma_page,0,sizeof(dma_page));dnd_fw_dma_flush(dma_page,sizeof(dma_page));}
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
