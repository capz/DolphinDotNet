#include "dnd_fst.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static unsigned char image[4096];
static bool read_image(void *context,uint64_t offset,void *data,size_t size){(void)context;if(offset>sizeof(image)||size>sizeof(image)-offset)return false;memcpy(data,image+offset,size);return true;}
static void put(unsigned offset,uint32_t value){image[offset]=(unsigned char)(value>>24);image[offset+1]=(unsigned char)(value>>16);image[offset+2]=(unsigned char)(value>>8);image[offset+3]=(unsigned char)value;}
int main(void){
 put(0x1c,0xc2339f3d);put(0x424,0x500);put(0x428,72);put(0x500,0x01000000);put(0x508,4);
 put(0x50c,0x01000001);put(0x510,0);put(0x514,3);put(0x518,5);put(0x51c,0x800);put(0x520,5);put(0x524,10);put(0x528,0x900);put(0x52c,3);memcpy(image+0x530,"\0dir\0data\0top\0",14);memcpy(image+0x800,"hello",5);memcpy(image+0x900,"abc",3);
 DndFst fst={0};assert(dnd_fst_mount(&fst,read_image,NULL,sizeof(image)));assert(dnd_fst_find(&fst,"dvd:/dir/data")==2);assert(dnd_fst_find(&fst,"/top")==3);assert(dnd_fst_find(&fst,"/data")==-1);assert(dnd_fst_next(&fst,1)==3);char bytes[8]={0};assert(dnd_fst_read(&fst,2,1,bytes,8)==4&&!strcmp(bytes,"ello"));assert(dnd_fst_read(&fst,2,9,bytes,1)==0);dnd_fst_unmount(&fst);
 put(0x514,5);assert(!dnd_fst_mount(&fst,read_image,NULL,sizeof(image)));put(0x514,3);put(0x51c,4094);assert(!dnd_fst_mount(&fst,read_image,NULL,sizeof(image)));put(0x51c,0x800);image[0x532]='/';assert(!dnd_fst_mount(&fst,read_image,NULL,sizeof(image)));puts("original-disc FST parser passed");return 0;
}
