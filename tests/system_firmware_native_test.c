#include "dnd_system_firmware.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static unsigned reg[5],position,page,operations,polls,locked,selected,releases,commands,selects,deselects;
static unsigned select_fail_at,stall_command;
static int busy,select_fail,stall,deselect_fail;
static uint64_t now;
static unsigned clock_step=1;
/* Include upper address bits: a byte-periodic pattern would hide a repeated
 * 1 KiB ROM page, which is the real-console failure this fixture models. */
static unsigned rom_byte(unsigned address){return (address^(address>>8)^(address>>13)^0xa5)&255;}
int dnd_fw_lock(void){if(busy)return 0;assert(!locked);locked=1;return 1;}
int dnd_fw_select(void){assert(locked&&!selected);selects++;if(select_fail||selects==select_fail_at)return 0;selected=1;return 1;}
int dnd_fw_deselect(void){assert(selected);deselects++;selected=0;return !deselect_fail;}
void dnd_fw_unlock(void){assert(locked);locked=0;releases++;}
uint64_t dnd_fw_now(void){now+=clock_step;return now;}
void dnd_fw_write(unsigned r,uint32_t value)
{
    assert(locked&&selected);reg[r]=value;
    if(r!=3||!(value&1))return;
    operations++;
    if(value&4)commands++;
    if(stall||commands==stall_command)return;
    if(value&4){position=(reg[4]>>6)&1023;page=(reg[4]>>6)&~1023u;}
    else {unsigned n=((value>>4)&3)+1;reg[4]=0;for(unsigned i=0;i<n;i++)reg[4]|=rom_byte(page+(position++&1023))<<(24-i*8);}
    reg[3]=0;
}
uint32_t dnd_fw_read(unsigned r){if(r==3)polls++;return reg[r];}
int main(void)
{
    unsigned char out[4100];memset(out,0xcc,sizeof(out));
    assert(dnd_firmware_read_raw(out+2,0x123,4096)==4096);
    assert(out[0]==0xcc&&out[1]==0xcc&&out[4098]==0xcc);
    for(unsigned i=0;i<4096;i++)assert(out[i+2]==rom_byte(i+0x123));
    assert(!locked&&!selected&&releases==1&&commands==5&&selects==5&&deselects==5);
    /* Every possible starting position in a ROM page, with reads crossing up
     * to four boundaries. Guard bytes catch an overrun on partial words. */
    for(unsigned low=0;low<1024;low++){
        unsigned offset=0x8000+low,count=1+(low*17)%4096;
        memset(out,0xcc,sizeof(out));
        assert(dnd_firmware_read_raw(out+2,offset,count)==(int)count);
        for(unsigned i=0;i<count;i++)assert(out[i+2]==rom_byte(offset+i));
        assert(out[0]==0xcc&&out[1]==0xcc&&out[count+2]==0xcc&&out[count+3]==0xcc);
        assert(!locked&&!selected);
    }
    for(unsigned i=1;i<=7;i++)assert(dnd_firmware_read_raw(out,0x200000-i,i)==(int)i);
    assert(dnd_firmware_read_raw(out,0x1fffff,2)==-3);
    assert(dnd_firmware_read_raw(out,0,4097)==-3);
    assert(dnd_firmware_read_raw(NULL,0,1)==-3);
    busy=1;assert(dnd_firmware_read_raw(out,0,1)==-1);busy=0;
    select_fail=1;assert(dnd_firmware_read_raw(out,0,1)==-1);select_fail=0;assert(!locked);
    stall=1;unsigned before=polls;assert(dnd_firmware_read_raw(out,0,4)==-2);
    assert(polls-before<=4096&&!locked&&!selected&&reg[3]==0);stall=0;
    /* A later page can fail after earlier bytes were read; release the whole
     * transaction and leave the next caller able to acquire the channel. */
    select_fail_at=selects+2;before=releases;
    assert(dnd_firmware_read_raw(out,0,4096)==-1&&!locked&&!selected&&releases==before+1);select_fail_at=0;
    stall_command=commands+2;before=polls;
    assert(dnd_firmware_read_raw(out,0,4096)==-2&&!locked&&!selected&&reg[3]==0);
    assert(polls-before<=4096+257);stall_command=0;
    clock_step=50;uint64_t started=now;
    assert(dnd_firmware_read_raw(out,0,4096)==-2&&!locked&&!selected&&reg[3]==0);
    assert(now-started>=20000&&now-started<=20200);clock_step=1;
    assert(dnd_firmware_read_raw(out,1023,7)==7);
    for(unsigned i=0;i<7;i++)assert(out[i]==rom_byte(1023+i));
    deselect_fail=1;assert(dnd_firmware_read_raw(out,0,4)==-1);assert(!locked&&!selected);
    puts("Firmware EXI: 1 KiB page wrap, unaligned ranges, later-page errors, contention, timeout and cleanup passed.");
}
