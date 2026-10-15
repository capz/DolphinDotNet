#define _POSIX_C_SOURCE 200809L
#include "dnd_iso_file.h"
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

static unsigned char bytes[4096];
static void big32(unsigned at,uint32_t v) {
  bytes[at]=(unsigned char)(v>>24);
  bytes[at+1]=(unsigned char)(v>>16);
  bytes[at+2]=(unsigned char)(v>>8);
  bytes[at+3]=(unsigned char)v;
}
static void prepare(void) {
  memset(bytes,0,sizeof(bytes));
  big32(0x1c,0xc2339f3d);
  big32(0x424,0x500);
  big32(0x428,72);
  big32(0x500,0x01000000);
  big32(0x508,4);
  big32(0x50c,0x01000001);
  big32(0x510,0);
  big32(0x514,3);
  big32(0x518,5);
  big32(0x51c,0x800);
  big32(0x520,5);
  big32(0x524,10);
  big32(0x528,0x900);
  big32(0x52c,3);
  memcpy(bytes+0x530,"\0dir\0data\0top\0",14);
  memcpy(bytes+0x800,"hello",5);
  memcpy(bytes+0x900,"abc",3);
}
static void write_image(const char *name) {
  FILE *fp=fopen(name,"wb");
  assert(fp);
  assert(fwrite(bytes,1,sizeof(bytes),fp)==sizeof(bytes));
  assert(!fclose(fp));
}
int main(void) {
  char temp[]="/tmp/dnd-gc-iso-XXXXXX";
  int fd=mkstemp(temp);
  assert(fd>=0);
  close(fd);
  prepare();
  write_image(temp);
  DndIsoFile a={0},b={0};
  assert(dnd_iso_file_open(&a,temp));
  assert(dnd_iso_file_open(&b,temp));
  assert(a.fst.count==4);
  int dir=dnd_iso_file_entry(&a,"/dir");
  int file=dnd_iso_file_entry(&a,"/dir/data");
  assert(dir==1 && file==2 && dnd_iso_file_entry(&a,"/top")==3);
  assert(dnd_fst_directory(&a.fst,(uint32_t)dir));
  assert(dnd_fst_next(&a.fst,1)==3);
  char output[8]={0};
  assert(dnd_iso_file_read(&a,(unsigned)file,1,output,5)==4);
  assert(!strcmp(output,"ello"));
  assert(dnd_iso_file_read(&a,(unsigned)file,5,output,2)==0);
  assert(dnd_iso_file_read(&b,3,0,output,3)==3);
  assert(!memcmp(output,"abc",3));
  assert(dnd_iso_file_read(&a,1,0,output,3)==-1);
  dnd_iso_file_close(&a);
  assert(dnd_iso_file_entry(&b,"/top")==3);
  dnd_iso_file_close(&b);
  assert(dnd_iso_file_entry(&a,"/top")==-1);

  big32(0x1c,0);
  write_image(temp);
  assert(!dnd_iso_file_open(&a,temp));
  prepare();
  big32(0x51c,4094); // Extent outside the image.
  write_image(temp);
  assert(!dnd_iso_file_open(&a,temp));
  prepare();
  bytes[0x532]='/'; // Invalid FST name.
  write_image(temp);
  assert(!dnd_iso_file_open(&a,temp));
  unlink(temp);
  puts("GameCube ISO read-only FST and corruption tests passed");
  return 0;
}
