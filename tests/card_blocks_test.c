#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <errno.h>
#include <malloc.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
typedef uint32_t u32;
typedef struct{int len,filenum;}card_file;
typedef struct{int comment_addr,banner_fmt,icon_fmt,icon_speed;u32 icon_addr;}card_stat;
#define CARD_ERROR_NOFILE -4
#define CARD_ERROR_IOERROR -5
#define CARD_BANNER_RGB 2
#define CARD_ICON_RGB 2
static void *workareas[2]={(void*)1,(void*)1};
static unsigned char disk[32*8192];static int size,reads,writes,opened,closed,metadata;
static void identity(int slot){assert(slot==0||slot==1);}
static int card_errno(int rc){return rc<0?-EIO:0;}
static int CARD_GetSectorSize(int slot,u32 *n){(void)slot;*n=8192;return 0;}
static int CARD_Open(int slot,const char *name,card_file *f){assert(slot==0&&!strcmp(name,"AnimalTown"));if(!size)return CARD_ERROR_NOFILE;f->len=size;f->filenum=0;opened++;return 0;}
static int CARD_Create(int slot,const char *name,int bytes,card_file *f){(void)slot;(void)name;assert(!size&&bytes<=(int)sizeof disk);size=bytes;memset(disk,0xff,size);f->len=size;f->filenum=0;opened++;return 0;}
static int CARD_Close(card_file *f){(void)f;closed++;return 0;}
static int CARD_Read(card_file *f,void *data,int bytes,int offset){assert(bytes==8192&&offset%8192==0&&offset+bytes<=size&&((uintptr_t)data&31)==0);memcpy(data,disk+offset,bytes);f->len=bytes;reads++;return 0;}
static int CARD_Write(card_file *f,const void *data,int bytes,int offset){assert(bytes==8192&&offset%8192==0&&offset+bytes<=size&&((uintptr_t)data&31)==0);memcpy(disk+offset,data,bytes);f->len=bytes;writes++;return 0;}
static int CARD_GetStatus(int slot,int index,card_stat *s){(void)slot;(void)index;memset(s,0xff,sizeof *s);return 0;}
static int CARD_SetStatus(int slot,int index,card_stat *s){(void)slot;(void)index;assert(s->comment_addr==32&&s->banner_fmt==2&&s->icon_fmt==2&&s->icon_addr==96);metadata++;return 0;}
#include "dnd_card_blocks.inc"
int main(void){
  unsigned char header[16384]={0},block[8192]={0};
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",0,0,0,NULL,0)==0);
  assert(!dnd_gc_card_blocks_io(4,"AnimalTown",3,16,123,header,sizeof header));
  assert(metadata==1&&size==18*8192&&writes==18);
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",0,0,0,NULL,0)==16);
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",4,0,0,NULL,0)==123);
  memset(block,37,sizeof block);assert(!dnd_gc_card_blocks_io(4,"AnimalTown",2,15,123,block,sizeof block));
  memset(block,0,sizeof block);assert(!dnd_gc_card_blocks_io(4,"AnimalTown",1,15,123,block,sizeof block));assert(block[8191]==37);
  int before=writes;
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",2,15,124,block,sizeof block)==-EINVAL);
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",2,16,123,block,sizeof block)==-EINVAL);
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",2,-1,123,block,sizeof block)==-EINVAL);
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",2,1,123,block,1)==-EINVAL);
  assert(dnd_gc_card_blocks_io(4,"AnimalTown",3,16,123,header,sizeof header)==-EEXIST);
  assert(before==writes&&opened==closed);
  disk[16]^=1;assert(dnd_gc_card_blocks_io(4,"AnimalTown",2,0,123,block,sizeof block)==-EIO);assert(before==writes);
  puts("Card block adapter: aligned bounded I/O, physical file length, one artwork item, identity guard and refusal to replace existing files passed.");return 0;
}
