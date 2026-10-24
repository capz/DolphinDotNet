#include "dnd_aram.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
/* No pinned native buffer exists in this managed-array ARAM fixture. Clang
 * requires an explicit host stub even for the weak native-buffer hook. */
uint64_t dnd_buffer_held_address(int token,int offset,int count) {
 (void)token;(void)offset;(void)count;return 0;
}
static DndArray *array(int size){DndArray *a=calloc(1,sizeof(*a)+(size_t)size);assert(a);a->length=(uint32_t)size;a->element_size=1;return a;}
static void finish(DndArray *a,int offset){int rc;do{rc=dnd_aram_poll(a,offset);}while(rc==0);assert(rc==1);}
int main(void){
 uint32_t used,total;
 assert(!dnd_aram_memory_usage(&used,&total));
 assert(!dnd_aram_initialize(-1));assert(!dnd_aram_initialize(1));
 assert(dnd_aram_initialize(16*1024*1024-0x4000-16384));
 assert(dnd_aram_stat(0)==16384);
 assert(dnd_aram_memory_usage(&used,&total));
 assert(total==16u*1024u*1024u && used==total-16384);
 int a=dnd_aram_allocate(4097),b=dnd_aram_allocate(4096),c=dnd_aram_allocate(4096);assert(a&&b&&c);
 assert(dnd_aram_stat(1)==12320 && dnd_aram_stat(3)==4064);
 assert(dnd_aram_memory_usage(&used,&total) && used==total-16384+12320);
 assert(dnd_aram_dispose(b)==0);assert(dnd_aram_stat(3)==4096);
 assert(dnd_aram_dispose(a)==0);assert(dnd_aram_stat(3)==8224);
 int d=dnd_aram_allocate(8000);assert(d && d!=a && d!=b);assert(dnd_aram_retain(a)==-EINVAL);
 assert(dnd_aram_retain(d)==0);
 DndArray *src=array(4096),*dst=array(4096);for(int i=0;i<4096;i++)((unsigned char*)src->data)[i]=(unsigned char)(i*7);
 assert(dnd_aram_begin(d,0,src,0,4096,1)==0);assert(dnd_aram_begin(d,0,src,0,32,1)==-EBUSY);finish(src,0);
 /* Unaligned patch must preserve both adjacent bytes and use aligned DMA. */
 ((unsigned char*)src->data)[0]=42;assert(dnd_aram_begin(d,31,src,0,1,1)==0);finish(src,0);
 assert(dnd_aram_begin(d,0,dst,0,4096,0)==0);finish(dst,0);
 for(int i=0;i<4096;i++)assert(((unsigned char*)dst->data)[i]==(i==31?42:(unsigned char)(i*7)));
 assert(dnd_aram_begin(d,7999,src,0,2,1)==-EINVAL);
 assert(dnd_aram_begin(d,0,src,4095,2,1)==-EINVAL);
 assert(dnd_aram_begin(d,4095,src,0,4096,1)==-EINVAL);
 assert(dnd_aram_begin(d,1,src,0,4096,1)==0);finish(src,0);
 assert(dnd_aram_begin(d,1,dst,0,4096,0)==0);finish(dst,0);assert(!memcmp(src->data,dst->data,4096));
 /* Disposed storage remains occupied until pending transfers are drained. */
 assert(dnd_aram_begin(d,0,dst,0,32,0)==0);assert(dnd_aram_dispose(d)==0);assert(dnd_aram_retain(d)==-EINVAL);finish(dst,0);
 assert(dnd_aram_stat(4)==2);assert(dnd_aram_release(d)==0);assert(dnd_aram_stat(4)==1);
 assert(dnd_aram_dispose(c)==0);assert(dnd_aram_stat(3)==16384);
 int e=dnd_aram_allocate(16384);assert(e && dnd_aram_allocate(1)==0);assert(dnd_aram_retain(e)==0);
 assert(dnd_aram_begin(e,0,dst,0,32,0)==0);dnd_aram_test_fail_next();assert(dnd_aram_poll(dst,0)==-EIO);
 assert(dnd_aram_begin(e,0,dst,0,32,0)==0);finish(dst,0);
 assert(dnd_aram_dispose(e)==0);assert(dnd_aram_release(e)==0);
 int handles[256];for(int i=0;i<256;i++){handles[i]=dnd_aram_allocate(1);assert(handles[i]);}assert(!dnd_aram_allocate(1));
 for(int i=0;i<256;i++)assert(dnd_aram_dispose(handles[i])==0);
 assert(dnd_aram_stat(2)==16384 && dnd_aram_stat(4)==0);free(src);free(dst);
 puts("ARAM allocation, fragmentation, staging, lifetime and fault tests passed");return 0;
}
