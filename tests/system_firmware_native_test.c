#include "dnd_system_firmware.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static unsigned reg[5],position,operations,polls,locked,selected,releases;
static int busy,select_fail,stall,deselect_fail;
static uint64_t now;
int dnd_fw_lock(void){if(busy)return 0;assert(!locked);locked=1;return 1;}
int dnd_fw_select(void){if(select_fail)return 0;selected=1;return 1;}
int dnd_fw_deselect(void){assert(selected);selected=0;return !deselect_fail;}
void dnd_fw_unlock(void){assert(locked);locked=0;releases++;}
uint64_t dnd_fw_now(void){return ++now;}
void dnd_fw_write(unsigned r,uint32_t value)
{
    assert(locked&&selected);reg[r]=value;
    if(r!=3||!(value&1))return;
    operations++;
    if(stall)return;
    if(value&4)position=reg[4]>>6;
    else {unsigned n=((value>>4)&3)+1;reg[4]=0;for(unsigned i=0;i<n;i++)reg[4]|=((position++^0xa5)&255)<<(24-i*8);}
    reg[3]=0;
}
uint32_t dnd_fw_read(unsigned r){if(r==3)polls++;return reg[r];}
int main(void)
{
    unsigned char out[4100];memset(out,0xcc,sizeof(out));
    assert(dnd_firmware_read_raw(out+2,0x123,4096)==4096);
    assert(out[0]==0xcc&&out[1]==0xcc&&out[4098]==0xcc);
    for(unsigned i=0;i<4096;i++)assert(out[i+2]==(((i+0x123)^0xa5)&255));
    assert(!locked&&!selected&&releases==1&&operations==1025);
    for(unsigned i=1;i<=7;i++)assert(dnd_firmware_read_raw(out,0x200000-i,i)==(int)i);
    assert(dnd_firmware_read_raw(out,0x1fffff,2)==-3);
    assert(dnd_firmware_read_raw(out,0,4097)==-3);
    assert(dnd_firmware_read_raw(NULL,0,1)==-3);
    busy=1;assert(dnd_firmware_read_raw(out,0,1)==-1);busy=0;
    select_fail=1;assert(dnd_firmware_read_raw(out,0,1)==-1);select_fail=0;assert(!locked);
    stall=1;unsigned before=polls;assert(dnd_firmware_read_raw(out,0,4)==-2);
    assert(polls-before<=4096&&!locked&&!selected&&reg[3]==0);stall=0;
    deselect_fail=1;assert(dnd_firmware_read_raw(out,0,4)==-1);assert(!locked&&!selected);
    puts("Firmware EXI: bounded ranges, contention, timeout and cleanup passed.");
}
