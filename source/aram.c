#include "dnd_aram.h"
#include "dnd_draw.h"
#ifndef DND_GAMECUBE_STORAGE
/* Some isolated ARAM host tests intentionally do not link the graphics module. */
extern uint64_t dnd_buffer_held_address(int token,int offset,int count) __attribute__((weak));
extern int dnd_buffer_hold(int token) __attribute__((weak));
extern int dnd_buffer_unhold(int token) __attribute__((weak));
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
#define REQUESTS 128
#define REQUEST_CHUNK 16384
/* No managed pointers survive a call into this module. */
static struct {int token,length,holds;unsigned offset,size;bool closed;} slots[SLOTS];
static unsigned first,end;
static int next_token=1;
static bool initialized;
static _Alignas(32) unsigned char staging[CHUNK+32];
static unsigned char patch[CHUNK];
static struct {bool active,write,direct;int phase,count,skip,owner;unsigned address,size;void *native;} transfer;
/* Broker service may finish legacy hardware, but its original caller still
 * consumes the result. A completed legacy read keeps staging reserved until
 * that caller copies it into its current managed array. Direct DMA never uses
 * staging, so foreground tickets can progress without another executor's poll. */
static int legacy_result,legacy_count,legacy_skip;
static bool legacy_direct,legacy_read;
typedef struct {
 int token,aram,offset,buffer,count,position,chunk,priority,result;
 bool write,cancel;
 unsigned char *memory;
 uint64_t order;
} AramRequest;
static AramRequest requests[REQUESTS];
static int next_request=1,request_peak,canceled_requests;
static uint64_t request_order,dma_read_bytes,dma_write_bytes;
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
 if(write)dma_write_bytes+=transfer.size;else dma_read_bytes+=transfer.size;
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
static void finish_legacy(int result){
 legacy_result=result;legacy_count=transfer.count;legacy_skip=transfer.skip;legacy_direct=transfer.direct;
 legacy_read=!transfer.direct&&!transfer.write;
 transfer.active=false;transfer.direct=false;transfer.native=NULL;
}
static void service_legacy(void){
 if(!transfer.active||transfer.owner||!completed)return;
#ifdef DND_ARAM_TEST
 if(fail_next){fail_next=false;finish_legacy(-EIO);return;}
#endif
#ifdef DND_GAMECUBE_STORAGE
 if(!transfer.write||(!transfer.direct&&transfer.phase==1))DCInvalidateRange(transfer.direct?transfer.native:staging,transfer.size);
#endif
 if(!transfer.direct&&transfer.phase==1&&transfer.write){
  memcpy(staging+transfer.skip,patch,(size_t)transfer.count);transfer.phase=2;submit(true);return;
 }
 finish_legacy(1);
}
static bool demand_waiting(void){for(int i=0;i<REQUESTS;i++)if(requests[i].token&&!requests[i].result&&!requests[i].cancel&&requests[i].priority==0)return true;return false;}
int dnd_aram_begin(int token,int offset,DndArray *a,int ao,int count,int write){
 int i=locate(token);if(i<0 || slots[i].holds<=0 || offset<0 || count<=0 || count>CHUNK || offset>slots[i].length || count>slots[i].length-offset || !range(a,ao,count))return -EINVAL;
 if(transfer.active||legacy_result||demand_waiting())return -EBUSY;
 unsigned address=slots[i].offset+(unsigned)offset;
 transfer.active=true;transfer.direct=false;transfer.native=NULL;transfer.owner=0;transfer.write=write!=0;transfer.phase=1;transfer.count=count;transfer.skip=(int)(address%ALIGN);transfer.address=address&~31u;transfer.size=((unsigned)transfer.skip+(unsigned)count+31u)&~31u;
 if(write&&transfer.skip==0&&((unsigned)count&31u)==0){
  memcpy(staging,(unsigned char*)a->data+ao,(size_t)count);transfer.phase=2;submit(true);
 }else{if(write)memcpy(patch,(unsigned char*)a->data+ao,(size_t)count);submit(false);}
 return 0;
}
int dnd_aram_poll(DndArray *a,int ao){
 service_legacy();
 if(legacy_result){
  if(legacy_direct)return -EINVAL;
  int result=legacy_result;
  if(result==1&&legacy_read){
   if(!range(a,ao,legacy_count))result=-EINVAL;
   else memcpy((unsigned char*)a->data+ao,staging+legacy_skip,(size_t)legacy_count);
  }
  legacy_result=0;return result;
 }
 return transfer.active&&!transfer.direct&&!transfer.owner?0:-EINVAL;
}

/* Native DMA path requires 32-byte-aligned ranges; no read/modify/write.
 * The caller holds both the ARAM allocation and native buffer until poll ends. */
int dnd_aram_begin_native(int token,int offset,int buffer_token,int bo,int count,int write) {
 int i=locate(token);
 if(transfer.active||legacy_result||demand_waiting())return -EBUSY;
 if(i<0 || slots[i].holds<=0 || offset<0 || bo<0 ||
    count<=0 || count>65536 || ((offset|bo|count)&31) ||
    offset>slots[i].length || count>slots[i].length-offset)return -EINVAL;
#ifndef DND_GAMECUBE_STORAGE
 if(!dnd_buffer_held_address)return -EINVAL;
#endif
 uint64_t address=dnd_buffer_held_address(buffer_token,bo,count);
 if(!address || (address&31u))return -EINVAL;
 transfer.active=true;transfer.direct=true;transfer.native=(void*)(uintptr_t)address;transfer.owner=0;
 transfer.write=write!=0;transfer.phase=2;transfer.count=count;transfer.skip=0;
 transfer.address=slots[i].offset+(unsigned)offset;transfer.size=(unsigned)count;
 submit(write!=0);return 0;
}
int dnd_aram_poll_native(void) {
 service_legacy();
 if(legacy_result){if(!legacy_direct)return -EINVAL;int result=legacy_result;legacy_result=0;return result;}
 return transfer.active&&transfer.direct&&!transfer.owner?0:-EINVAL;
}

static AramRequest *find_request(int token){
 for(int i=0;i<REQUESTS;i++)if(token>0&&requests[i].token==token)return requests+i;
 return NULL;
}
static int request_count(void){int n=0;for(int i=0;i<REQUESTS;i++)n+=requests[i].token!=0;return n;}
static bool overlaps(uintptr_t a,unsigned an,uintptr_t b,unsigned bn){return a<=b?b-a<an:a-b<bn;}
static bool conflicts(const AramRequest *a,const AramRequest *b){
 if(a->aram==b->aram&&(a->write||b->write)&&
    overlaps((uintptr_t)(unsigned)a->offset,(unsigned)a->count,(uintptr_t)(unsigned)b->offset,(unsigned)b->count))return true;
 return (!a->write||!b->write)&&overlaps((uintptr_t)a->memory,(unsigned)a->count,(uintptr_t)b->memory,(unsigned)b->count);
}
static bool blocked(const AramRequest *r){
 for(int i=0;i<REQUESTS;i++){
  const AramRequest *other=requests+i;
  if(other->token&&other->token<r->token&&!other->result&&conflicts(other,r))return true;
 }
 return false;
}
static void request_done(AramRequest *r,int result){
 r->result=result;
 if(result==-ECANCELED&&canceled_requests<INT_MAX)canceled_requests++;
}
/* One completion and one new bounded DMA per call. A caller polling its own
 * ticket may advance other tickets, but only that ticket's poll consumes it. */
static void service_requests(void){
 service_legacy();
 if(transfer.active){
  if(!transfer.owner||!completed)return;
  AramRequest *r=find_request(transfer.owner);
  /* A broker ticket cannot disappear while a DMA owns its native pointer. */
  if(!r)return;
#ifdef DND_ARAM_TEST
  if(fail_next){fail_next=false;request_done(r,-EIO);}
  else
#endif
  {
#ifdef DND_GAMECUBE_STORAGE
   if(!transfer.write)DCInvalidateRange(transfer.native,transfer.size);
#endif
   r->position+=r->chunk;
   if(r->cancel)request_done(r,-ECANCELED);
   else if(r->position==r->count)request_done(r,1);
   else r->order=++request_order;
  }
  transfer.active=false;transfer.owner=0;transfer.direct=false;transfer.native=NULL;
 }
 AramRequest *next=NULL;
 for(int i=0;i<REQUESTS;i++){
  AramRequest *r=requests+i;
  if(!r->token||r->result)continue;
  if(r->cancel){request_done(r,-ECANCELED);continue;}
  if(blocked(r))continue;
  if(!next||r->priority<next->priority||(r->priority==next->priority&&r->order<next->order))next=r;
 }
 if(!next)return;
 int slot=locate(next->aram);
 if(slot<0){request_done(next,-EINVAL);return;}
 next->chunk=next->count-next->position;
 if(next->chunk>REQUEST_CHUNK)next->chunk=REQUEST_CHUNK;
 transfer.active=true;transfer.direct=true;transfer.write=next->write;transfer.owner=next->token;
 transfer.native=next->memory+next->position;transfer.phase=2;transfer.count=next->chunk;transfer.skip=0;
 transfer.address=slots[slot].offset+(unsigned)next->offset+(unsigned)next->position;
 transfer.size=(unsigned)next->chunk;
 submit(next->write);
}
static int request_memory(int token,int offset,void *address,int count,int write,int priority,int buffer){
 int slot=locate(token),live=request_count();
 if(slot<0||slots[slot].closed||offset<0||count<=0||priority<0||priority>2||
    ((uintptr_t)address&31u)||!address||((offset|count)&31)||
    offset>slots[slot].length||count>slots[slot].length-offset||
    (uintptr_t)address>UINTPTR_MAX-(unsigned)count)return -EINVAL;
 /* Leave room for foreground restoration even when optional work is full. */
 int limit=priority==0?REQUESTS:priority==1?REQUESTS-16:REQUESTS-32;
 if(live>=limit)return -EAGAIN;
 if(next_request==INT_MAX)return -EOVERFLOW;
 int index=0;while(index<REQUESTS&&requests[index].token)index++;
 if(index==REQUESTS)return -EAGAIN;
 if(dnd_aram_retain(token))return -EINVAL;
 AramRequest *r=requests+index;
 r->token=next_request++;r->aram=token;r->offset=offset;r->memory=address;r->buffer=buffer;
 r->count=count;r->write=write!=0;r->priority=priority;r->order=++request_order;
 if(live+1>request_peak)request_peak=live+1;
 return r->token;
}
int dnd_aram_request_memory(int token,int offset,void *address,int count,int write,int priority){
 return request_memory(token,offset,address,count,write,priority,0);
}
int dnd_aram_request_native(int token,int offset,int buffer,int bo,int count,int write,int priority){
#ifndef DND_GAMECUBE_STORAGE
 if(!dnd_buffer_hold||!dnd_buffer_unhold||!dnd_buffer_held_address)return -EINVAL;
#endif
 if(buffer<=0||bo<0||dnd_buffer_hold(buffer))return -EINVAL;
 uint64_t address=dnd_buffer_held_address(buffer,bo,count);
 int result=request_memory(token,offset,(void*)(uintptr_t)address,count,write,priority,buffer);
 if(result<0)(void)dnd_buffer_unhold(buffer);
 return result;
}
int dnd_aram_request_poll(int token){
 AramRequest *r=find_request(token);if(!r)return -EINVAL;
 if(!r->result)service_requests();
 if(!r->result)return 0;
 int result=r->result,aram=r->aram,buffer=r->buffer;
 memset(r,0,sizeof(*r));
 if(buffer)(void)dnd_buffer_unhold(buffer);
 (void)dnd_aram_release(aram);
 return result;
}
int dnd_aram_request_cancel(int token){
 AramRequest *r=find_request(token);if(!r)return -EINVAL;
 if(!r->result){r->cancel=true;if(!transfer.active||transfer.owner!=token)request_done(r,-ECANCELED);}
 return 0;
}
int dnd_aram_service_broker(void){
 bool pending=false;
 for(int i=0;i<REQUESTS;i++)if(requests[i].token&&!requests[i].result){pending=true;break;}
 if(!pending)return 0;
 service_requests();
 for(int i=0;i<REQUESTS;i++)if(requests[i].token&&!requests[i].result)return 1;
 return 0;
}
static int counter_value(uint64_t n){return n>INT_MAX?INT_MAX:(int)n;}
int dnd_aram_broker_stat(int kind){
 if(kind==0)return request_count();
 if(kind==1){uint64_t bytes=0;for(int i=0;i<REQUESTS;i++)if(requests[i].token&&!requests[i].result)bytes+=(unsigned)(requests[i].count-requests[i].position);return counter_value(bytes);}
 if(kind==2)return request_peak;
 if(kind==3)return counter_value(dma_read_bytes);
 if(kind==4)return counter_value(dma_write_bytes);
 if(kind==5)return canceled_requests;
 return -EINVAL;
}
