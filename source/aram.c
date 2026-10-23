#include "dnd_aram.h"
#include "dnd_draw.h"
#ifndef DND_GAMECUBE_STORAGE
/* Some isolated ARAM host tests intentionally do not link the graphics module. */
extern uint64_t dnd_buffer_held_address(int token,int offset,int count) __attribute__((weak));
#endif
#include <errno.h>
#include <limits.h>
#include <stdlib.h>
#include <string.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <ogc/aram.h>
#include <ogc/arqueue.h>
#endif
#define SLOTS 256
#define CHUNK 4096
#define ALIGN 32u
#define OWNER 0x444e4441u
/* No managed pointers survive a call into this module. */
static struct {int token,length,holds;unsigned offset,size;bool closed;} slots[SLOTS];
static unsigned first,end;
static int next_token=1;
static bool initialized;
static _Alignas(32) unsigned char staging[CHUNK+32];
static unsigned char patch[CHUNK];
static struct {bool active,write,direct;int phase,count,skip;unsigned address,size;void *native;} transfer;
static volatile int completed;
#ifdef DND_GAMECUBE_STORAGE
static ARQRequest request;
static void finished(ARQRequest *r){(void)r;completed=1;}
#else
static unsigned char *memory;
#ifdef DND_ARAM_TEST
static bool fail_next;
void dnd_aram_test_fail_next(void){fail_next=true;}
#endif
#endif
bool dnd_aram_initialize(int reserved){
 if(reserved<0 || (unsigned)reserved%ALIGN)return false;
 if(initialized)return first==0x4000u+(unsigned)reserved;
#ifdef DND_GAMECUBE_STORAGE
 /* Existing owners cannot safely be inferred. Never reset someone else's AR/ARQ. */
 if(AR_CheckInit() || ARQ_CheckInit())return false;
 unsigned base=AR_Init(NULL,0); unsigned size=AR_GetInternalSize();
 if(base!=0x4000u || size<=base || (unsigned)reserved>=size-base)return false;
 ARQ_Init();
#else
 unsigned base=0x4000u,size=16u*1024u*1024u;
 if((unsigned)reserved>=size-base)return false;
 memory=calloc(1,size);if(!memory)return false;
#endif
 first=base+(unsigned)reserved;end=size;initialized=true;return true;
}
bool dnd_aram_available(void){return initialized;}
bool dnd_aram_memory_usage(uint32_t *used,uint32_t *total){
 if(!initialized || !used || !total)return false;
 unsigned allocated=first;
 for(int i=0;i<SLOTS;i++)if(slots[i].token)allocated+=slots[i].size;
 *used=allocated;*total=end;return true;
}
static int locate(int token){for(int i=0;i<SLOTS;i++)if(token>0 && slots[i].token==token)return i;return -1;}
/* Walking occupied blocks in address order implicitly coalesces free holes. */
static unsigned hole(unsigned need,unsigned *largest){
 unsigned cursor=first,result=UINT_MAX;*largest=0;
 while(cursor<end){unsigned nearest=end,stop=end;
  for(int i=0;i<SLOTS;i++)if(slots[i].token && slots[i].offset>=cursor && slots[i].offset<nearest){nearest=slots[i].offset;stop=nearest+slots[i].size;}
  unsigned gap=nearest-cursor;if(gap>*largest)*largest=gap;
  if(result==UINT_MAX && gap>=need)result=cursor;
  if(nearest==end)break;
  cursor=stop;
 }
 return result;
}
int dnd_aram_stat(int kind){
 if(!initialized)return 0;
 unsigned allocated=0,largest;int count=0;for(int i=0;i<SLOTS;i++)if(slots[i].token){allocated+=slots[i].size;count++;}
 (void)hole(UINT_MAX,&largest);
 switch(kind){case 0:return (int)(end-first);case 1:return (int)allocated;case 2:return (int)(end-first-allocated);case 3:return (int)largest;case 4:return count;default:return -EINVAL;}
}
int dnd_aram_allocate(int length){
 if(!initialized || length<=0 || (unsigned)length>end-first || next_token==INT_MAX)return 0;
 unsigned size=((unsigned)length+31u)&~31u,largest;int free_slot=-1;
 for(int i=0;i<SLOTS;i++)if(!slots[i].token){free_slot=i;break;}
 unsigned address=hole(size,&largest);if(free_slot<0 || address==UINT_MAX)return 0;
 slots[free_slot].token=next_token++;slots[free_slot].offset=address;slots[free_slot].size=size;slots[free_slot].length=length;
 return slots[free_slot].token;
}
int dnd_aram_retain(int token){int i=locate(token);if(i<0 || slots[i].closed)return -EINVAL;if(slots[i].holds==INT_MAX)return -EOVERFLOW;slots[i].holds++;return 0;}
int dnd_aram_release(int token){int i=locate(token);if(i<0 || slots[i].holds<=0)return -EINVAL;if(--slots[i].holds==0 && slots[i].closed)memset(slots+i,0,sizeof(slots[i]));return 0;}
int dnd_aram_dispose(int token){int i=locate(token);if(i<0)return -EINVAL;slots[i].closed=true;if(!slots[i].holds)memset(slots+i,0,sizeof(slots[i]));return 0;}
static void submit(bool write){
 completed=0;
#ifdef DND_GAMECUBE_STORAGE
 void *address=transfer.direct?transfer.native:staging;
 if(write)DCFlushRange(address,transfer.size);else DCInvalidateRange(address,transfer.size);
 ARQ_PostRequestAsync(&request,OWNER,write?ARQ_MRAMTOARAM:ARQ_ARAMTOMRAM,ARQ_PRIO_LO,transfer.address,(u32)address,transfer.size,finished);
#else
 void *address=transfer.direct?transfer.native:staging;
 if(write)memcpy(memory+transfer.address,address,transfer.size);else memcpy(address,memory+transfer.address,transfer.size);
 completed=1;
#endif
}
static bool range(DndArray *a,int offset,int count){return a && a->element_size==1 && offset>=0 && count>=0 && (size_t)offset<=a->length && (size_t)count<=a->length-(size_t)offset;}
int dnd_aram_begin(int token,int offset,DndArray *a,int ao,int count,int write){
 int i=locate(token);if(i<0 || slots[i].holds<=0 || offset<0 || count<=0 || count>CHUNK || offset>slots[i].length || count>slots[i].length-offset || !range(a,ao,count))return -EINVAL;
 if(transfer.active)return -EBUSY;
 unsigned address=slots[i].offset+(unsigned)offset;
 transfer.active=true;transfer.direct=false;transfer.native=NULL;transfer.write=write!=0;transfer.phase=1;transfer.count=count;transfer.skip=(int)(address%ALIGN);transfer.address=address&~31u;transfer.size=((unsigned)transfer.skip+(unsigned)count+31u)&~31u;
 if(write)memcpy(patch,(unsigned char*)a->data+ao,(size_t)count);
 submit(false);return 0;
}
int dnd_aram_poll(DndArray *a,int ao){
 if(!transfer.active || transfer.direct)return -EINVAL;
 if(!completed)return 0;
#ifdef DND_ARAM_TEST
 if(fail_next){fail_next=false;transfer.active=false;return -EIO;}
#endif
 if(transfer.phase==1){
#ifdef DND_GAMECUBE_STORAGE
  DCInvalidateRange(staging,transfer.size);
#endif
  if(transfer.write){memcpy(staging+transfer.skip,patch,(size_t)transfer.count);transfer.phase=2;submit(true);return 0;}
  if(!range(a,ao,transfer.count)){transfer.active=false;return -EINVAL;}
  memcpy((unsigned char*)a->data+ao,staging+transfer.skip,(size_t)transfer.count);
 }
 transfer.active=false;return 1;
}

/* Native DMA path requires 32-byte-aligned ranges; no read/modify/write.
 * The caller holds both the ARAM allocation and native buffer until poll ends. */
int dnd_aram_begin_native(int token,int offset,int buffer_token,int bo,int count,int write) {
 int i=locate(token);
 if(transfer.active)return -EBUSY;
 if(i<0 || slots[i].holds<=0 || offset<0 || bo<0 ||
    count<=0 || count>65536 || ((offset|bo|count)&31) ||
    offset>slots[i].length || count>slots[i].length-offset)return -EINVAL;
#ifndef DND_GAMECUBE_STORAGE
 if(!dnd_buffer_held_address)return -EINVAL;
#endif
 uint64_t address=dnd_buffer_held_address(buffer_token,bo,count);
 if(!address || (address&31u))return -EINVAL;
 transfer.active=true;transfer.direct=true;transfer.native=(void*)(uintptr_t)address;
 transfer.write=write!=0;transfer.phase=2;transfer.count=count;transfer.skip=0;
 transfer.address=slots[i].offset+(unsigned)offset;transfer.size=(unsigned)count;
 submit(write!=0);return 0;
}
int dnd_aram_poll_native(void) {
 if(!transfer.active || !transfer.direct)return -EINVAL;
 if(!completed)return 0;
#ifdef DND_ARAM_TEST
 if(fail_next){fail_next=false;transfer.active=false;transfer.direct=false;return -EIO;}
#endif
#ifdef DND_GAMECUBE_STORAGE
 if(!transfer.write)DCInvalidateRange(transfer.native,transfer.size);
#endif
 transfer.active=false;transfer.direct=false;transfer.native=NULL;return 1;
}
