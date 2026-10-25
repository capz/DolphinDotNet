#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static int active,raw,errors;
bool dnd_device_active(void){return active!=0;}
int dnd_raw_depth(void){return raw;}
void dnd_draw_backend_wait(void){}
void dnd_exception_throw(DndExceptionKind kind,const char *message){(void)kind;(void)message;errors++;}
static uint32_t reference(const unsigned char *p,int n,uint32_t crc){
    crc=~crc;
    while(n--){crc^=*p++;for(int j=0;j<8;j++)crc=(crc>>1)^((crc&1)?0xedb88320u:0);}
    return ~crc;
}
int main(void){
    int token=dnd_buffer_create(4096,32);assert(token);
    unsigned char *p=(unsigned char*)(uintptr_t)dnd_buffer_address(token,0);
    memcpy(p,"123456789",9);
    assert(dnd_buffer_crc32(token,0,9,0)==0xcbf43926u);
    uint32_t first=dnd_buffer_crc32(token,0,4,0);
    assert(dnd_buffer_crc32(token,4,5,first)==0xcbf43926u);
    assert(dnd_buffer_crc32(token,4096,0,first)==first);
    for(int i=0;i<4096;i++)p[i]=(unsigned char)(i*37+11);
    for(int offset=0;offset<64;offset++)for(int count=0;count<4000;count+=137)
        assert(dnd_buffer_crc32(token,offset,count,0x12345678u)==reference(p+offset,count,0x12345678u));
    (void)dnd_buffer_crc32(token,4090,10,0);assert(errors==1);
    active=1;(void)dnd_buffer_crc32(token,0,8,0);assert(errors==2);active=0;
    raw=1;(void)dnd_buffer_crc32(token,0,8,0);assert(errors==3);raw=0;
    assert(dnd_draw_release(token)==0);
    (void)dnd_buffer_crc32(token,0,0,0);assert(errors==4);
    assert(dnd_draw_total_bytes()==0);
    puts("Native buffer CRC32: ranges, incremental identity, stale handles and frame exclusion passed.");
}
