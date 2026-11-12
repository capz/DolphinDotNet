#include "dnd_aram.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define BYTES 65536
static _Alignas(32) unsigned char buffers[4][BYTES];
static int holds[4];
static bool closed[4];
int dnd_buffer_hold(int token){if(token<1||token>4||closed[token-1])return -EINVAL;holds[token-1]++;return 0;}
int dnd_buffer_unhold(int token){if(token<1||token>4||holds[token-1]<1)return -EINVAL;holds[token-1]--;return 0;}
uint64_t dnd_buffer_held_address(int token,int offset,int count){
 if(token<1||token>4||holds[token-1]<1||offset<0||count<0||offset>BYTES||count>BYTES-offset)return 0;
 return (uint64_t)(uintptr_t)(buffers[token-1]+offset);
}
static DndArray *array(int count){DndArray *a=calloc(1,sizeof(*a)+(size_t)count);assert(a);a->length=(unsigned)count;a->element_size=1;return a;}
static void done(int ticket){int result,steps=0;do{result=dnd_aram_request_poll(ticket);assert(++steps<1000);}while(!result);assert(result==1);}
static void idle(void){assert(dnd_aram_broker_stat(0)==0);for(int i=0;i<4;i++)assert(!holds[i]);}
static int put(int token,void *memory,int count,int priority){int ticket=dnd_aram_request_memory(token,0,memory,count,1,priority);assert(ticket>0);return ticket;}

int main(void){
 /* Native requests may borrow cached or uncached aliases of the same MEM1.
  * Test their dependency keys without ever dereferencing console addresses. */
#ifdef GEKKO
 assert(dnd_aram_test_memory_conflict(0x80010000u,64,0,0xc0010020u,32,0));
 assert(dnd_aram_test_memory_conflict(0xc0010000u,32,1,0x80010000u,32,0));
 assert(dnd_aram_test_memory_conflict(0x817fffe0u,32,0,0xc17fffe0u,32,0));
#else
 assert(!dnd_aram_test_memory_conflict(0x80010000u,64,0,0xc0010020u,32,0));
#endif
 assert(!dnd_aram_test_memory_conflict(0x80010000u,32,0,0xc0010020u,32,0));
 assert(!dnd_aram_test_memory_conflict(0x80010000u,32,1,0xc0010000u,32,1));
 assert(!dnd_aram_test_memory_conflict(0x81800000u,32,0,0xc1800000u,32,0));
 assert(!dnd_aram_test_memory_conflict(0x817fffe0u,64,0,0xc17fffe0u,64,0));
 assert(!dnd_aram_test_memory_conflict(0x80010000u,32,0,0xcc010000u,32,0));
 assert(!dnd_aram_test_memory_conflict(0x00010000u,32,0,0xc0010000u,32,0));
#if UINTPTR_MAX > UINT32_MAX
 assert(!dnd_aram_test_memory_conflict(UINT64_C(0x180010000),32,0,UINT64_C(0x1c0010000),32,0));
#endif
 assert(dnd_aram_initialize(2*1024*1024));
 int a=dnd_aram_allocate(BYTES),b=dnd_aram_allocate(BYTES);assert(a&&b);
 memset(buffers[0],0x11,BYTES);memset(buffers[1],0x22,BYTES);
 int before=dnd_aram_broker_stat(4),background=put(a,buffers[0],BYTES,2);
 assert(dnd_aram_request_poll(background)==0);
 assert(dnd_aram_broker_stat(4)-before==16384);
 int demand=put(b,buffers[1],32,0);
 assert(dnd_aram_request_poll(background)==0);
 assert(dnd_aram_broker_stat(4)-before==16384+32);
 assert(dnd_aram_poll_native()==-EINVAL); /* Cannot steal broker completion. */
 assert(dnd_aram_request_poll(demand)==1);
 done(background);idle();
 assert(dnd_aram_request_poll(demand)==-EINVAL); /* Tickets never alias reuse. */
 done(put(b,buffers[1],BYTES,1));

 /* The shared background driver advances chunks without consuming the owner's
  * terminal ticket, then quiesces instead of spinning on that live result. */
 int first=put(a,buffers[0],BYTES,2);
 int driver_steps=0;
 while(dnd_aram_service_broker())assert(++driver_steps<20);
 assert(dnd_aram_broker_stat(0)==1&&dnd_aram_broker_stat(1)==0);
 assert(!dnd_aram_service_broker());assert(dnd_aram_request_poll(first)==1);idle();

 /* Equal-priority independent reads alternate at chunk boundaries. Inspecting
  * partial buffers here is test instrumentation, not application usage. */
 memset(buffers[2],0,BYTES);memset(buffers[3],0,BYTES);
 first=dnd_aram_request_memory(a,0,buffers[2],BYTES,0,1);
 int second=dnd_aram_request_memory(b,0,buffers[3],BYTES,0,1);assert(first>0&&second>0);
 assert(!dnd_aram_request_poll(first));assert(buffers[2][0]==0x11&&buffers[2][16384]==0);
 assert(!dnd_aram_request_poll(first));assert(buffers[3][0]==0x22&&buffers[2][16384]==0);
 done(first);done(second);idle();

 /* A demand read must not bypass the preceding background snapshot of the
  * same ARAM range. Dependency ordering wins over priority when bytes alias. */
 memset(buffers[0],0x55,BYTES);memset(buffers[2],0,BYTES);
 first=put(a,buffers[0],32768,2);
 second=dnd_aram_request_memory(a,0,buffers[2],32768,0,0);assert(second>0);
 assert(!dnd_aram_request_poll(second));assert(buffers[2][0]==0);
 assert(!dnd_aram_request_poll(second));assert(buffers[2][0]==0);
 done(second);done(first);for(int i=0;i<32768;i++)assert(buffers[2][i]==0x55);idle();

 /* Reads into an overlapping MEM1 destination also retain submission order. */
 memset(buffers[2],0,BYTES);
 first=dnd_aram_request_memory(a,0,buffers[2],32768,0,2);
 second=dnd_aram_request_memory(b,0,buffers[2],32768,0,0);assert(first>0&&second>0);
 assert(!dnd_aram_request_poll(second));assert(buffers[2][0]==0x55);
 assert(!dnd_aram_request_poll(second));assert(buffers[2][16384]==0x55);
 done(second);done(first);for(int i=0;i<32768;i++)assert(buffers[2][i]==0x22);idle();

 /* Completed legacy managed reads retain their staging bytes, not the shared
  * channel. An application-thread terrain drain need not wait for a managed
  * executor to consume that legacy result. */
 DndArray *managed=array(128);
 assert(!dnd_aram_retain(a));assert(!dnd_aram_begin(a,31,managed,0,32,0));
 demand=put(b,buffers[1],32,0);assert(!dnd_aram_request_poll(demand));
 assert(dnd_aram_poll_native()==-EINVAL);
 assert(dnd_aram_begin(a,0,managed,0,32,0)==-EBUSY);
 done(demand);assert(dnd_aram_poll(managed,0)==1);
 for(int i=0;i<32;i++)assert(managed->data[i]==0x55);
 assert(!dnd_aram_release(a));idle();

 /* Legacy unaligned read/modify/write can complete in broker service, with its
  * result still available to its owner and neighboring bytes preserved. */
 managed->data[0]=0x99;assert(!dnd_aram_retain(a));assert(!dnd_aram_begin(a,31,managed,0,1,1));
 demand=put(b,buffers[1],32,0);done(demand);assert(dnd_aram_poll(managed,0)==1);
 assert(!dnd_aram_begin(a,0,managed,0,64,0));while(dnd_aram_poll(managed,0)==0){}
 for(int i=0;i<64;i++)assert(managed->data[i]==(i==31?0x99:0x55));
 assert(!dnd_aram_release(a));idle();

 /* Fully aligned managed writes skip the old unnecessary read DMA. */
 assert(!dnd_aram_retain(a));int reads=dnd_aram_broker_stat(3),writes=dnd_aram_broker_stat(4);
 memset(managed->data,0x77,32);assert(!dnd_aram_begin(a,0,managed,0,32,1));
 while(dnd_aram_poll(managed,0)==0){}
 assert(dnd_aram_broker_stat(3)==reads&&dnd_aram_broker_stat(4)==writes+32);
 assert(!dnd_aram_release(a));idle();

 /* Existing direct callers consume their own preserved completion while a new
  * broker request is active; a second legacy poll cannot consume its DMA. */
 assert(!dnd_aram_retain(a));assert(!dnd_buffer_hold(1));
 assert(!dnd_aram_begin_native(a,0,1,0,32,1));demand=put(b,buffers[1],32,0);
 assert(!dnd_aram_request_poll(demand));assert(dnd_aram_poll_native()==1);
 assert(dnd_aram_poll_native()==-EINVAL);done(demand);
 assert(!dnd_buffer_unhold(1));assert(!dnd_aram_release(a));idle();

 /* Optional jobs cannot consume the reserved promotion/terrain capacity. */
 int tickets[128];for(int i=0;i<96;i++)tickets[i]=put(a,buffers[0],32,2);
 assert(dnd_aram_request_memory(a,0,buffers[0],32,1,2)==-EAGAIN);
 for(int i=96;i<112;i++)tickets[i]=put(a,buffers[0],32,1);
 assert(dnd_aram_request_memory(a,0,buffers[0],32,1,1)==-EAGAIN);
 for(int i=112;i<128;i++)tickets[i]=put(a,buffers[0],32,0);
 assert(dnd_aram_request_memory(a,0,buffers[0],32,1,0)==-EAGAIN);
 for(int i=0;i<128;i++){assert(!dnd_aram_request_cancel(tickets[i]));assert(dnd_aram_request_poll(tickets[i])==-ECANCELED);}
 idle();assert(dnd_aram_broker_stat(2)==128);

 /* Failed/invalid submissions leak neither owner. A failed completed DMA is
  * terminal, and later requests still progress. */
 assert(dnd_aram_request_native(a,0,1,1,32,1,0)==-EINVAL);idle();
 assert(dnd_aram_request_memory(a,0,buffers[0],32,1,3)==-EINVAL);idle();
 demand=dnd_aram_request_native(a,0,1,0,32,1,0);assert(demand>0&&holds[0]==1);
 assert(!dnd_aram_request_poll(demand));dnd_aram_test_fail_next();assert(dnd_aram_request_poll(demand)==-EIO);idle();
 done(put(a,buffers[0],32,0));

 /* Canceling in-flight DMA retains both closed owners until its ticket drains,
  * and does not submit the remaining chunks. */
 int c=dnd_aram_allocate(BYTES);assert(c);writes=dnd_aram_broker_stat(4);
 demand=dnd_aram_request_native(c,0,3,0,BYTES,1,2);assert(demand>0);
 assert(!dnd_aram_request_poll(demand));assert(holds[2]==1);
 closed[2]=true;assert(!dnd_aram_dispose(c));assert(!dnd_aram_request_cancel(demand));
 assert(dnd_aram_stat(4)==3&&holds[2]==1);
 assert(dnd_aram_request_poll(demand)==-ECANCELED);
 assert(dnd_aram_stat(4)==2&&holds[2]==0);
 assert(dnd_aram_broker_stat(4)-writes==16384);idle();
 assert(!dnd_aram_dispose(a)&&!dnd_aram_dispose(b));free(managed);
 assert(!dnd_aram_stat(4));
 puts("ARAM broker priority, fairness, conflicts, legacy coexistence, reserved capacity, cancellation and lifetime passed.");
 return 0;
}
