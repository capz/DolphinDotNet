#include "dnd_platform_entropy.h"
#include <stdlib.h>
#include <string.h>
#include <stdatomic.h>

static void wipe(void *pointer,size_t length){
    volatile unsigned char *p=(volatile unsigned char *)pointer;
    if(p)while(length--)*p++=0;
}

#ifdef DND_PLATFORM_ENTROPY
#include "jitterentropy/jitterentropy.h"
#ifndef DND_PLATFORM_ENTROPY_TEST
#if !defined(GEKKO)
#error "The production entropy provider requires the GameCube timebase."
#endif
#include <ogc/lwp_watchdog.h>
#endif

#define ENTROPY_MAX_ALLOCATION (128u*1024u)
#define ENTROPY_MEMORY_LIMIT (192u*1024u)
#define ENTROPY_SAMPLE_LIMIT UINT64_C(1000000)
#define ENTROPY_ALLOCATION_SLOTS 8
typedef struct {void *pointer;size_t length;} EntropyAllocation;
static struct {
    uint64_t started,deadline,samples;
    size_t live,peak;
    int expired,allocation_failed,invalid_state;
    EntropyAllocation allocations[ENTROPY_ALLOCATION_SLOTS];
} request;
static atomic_flag busy=ATOMIC_FLAG_INIT;

static uint64_t monotonic_ms(void){
#ifdef DND_PLATFORM_ENTROPY_TEST
    return dnd_platform_entropy_test_monotonic_ms();
#else
    return gettime()/UINT64_C(40500);
#endif
}
static uint64_t raw_timebase(void){
#ifdef DND_PLATFORM_ENTROPY_TEST
    return dnd_platform_entropy_test_clock();
#else
    return gettime();
#endif
}
static int expired(void){
    uint64_t now=monotonic_ms();
    if(now<request.started||now>=request.deadline||request.samples>=ENTROPY_SAMPLE_LIMIT)
        request.expired=1;
    return request.expired;
}
uint64_t dnd_jent_timestamp(void){
    if(expired())return 0;
    request.samples++;
    if(expired())return 0;
    return raw_timebase();
}
void dnd_jent_wipe(void *pointer,size_t length){wipe(pointer,length);}
void *dnd_jent_zalloc(size_t length){
    if(expired())return NULL;
    int slot=-1;
    for(int i=0;i<ENTROPY_ALLOCATION_SLOTS;i++)if(!request.allocations[i].pointer){slot=i;break;}
    if(!length||length>ENTROPY_MAX_ALLOCATION||request.live>ENTROPY_MEMORY_LIMIT-length||slot<0){
        request.allocation_failed=1;return NULL;
    }
#ifdef DND_PLATFORM_ENTROPY_TEST
    void *pointer=dnd_platform_entropy_test_alloc(length);
#else
    void *pointer=malloc(length);
#endif
    if(!pointer){request.allocation_failed=1;return NULL;}
    memset(pointer,0,length);
    request.allocations[slot]=(EntropyAllocation){pointer,length};
    request.live+=length;
    if(request.live>request.peak)request.peak=request.live;
    return pointer;
}
void dnd_jent_zfree(void *pointer,size_t length){
    if(!pointer)return;
    for(int i=0;i<ENTROPY_ALLOCATION_SLOTS;i++)if(request.allocations[i].pointer==pointer){
        size_t actual=request.allocations[i].length;
        if(actual!=length)request.invalid_state=1;
        wipe(pointer,actual);
#ifdef DND_PLATFORM_ENTROPY_TEST
        dnd_platform_entropy_test_free(pointer,actual);
#else
        free(pointer);
#endif
        request.live-=actual;
        request.allocations[i]=(EntropyAllocation){0};return;
    }
    request.invalid_state=1;
}
int dnd_platform_entropy_fill(unsigned char *output,size_t length,uint32_t timeout_ms){
    if(!output||length<48||length>64)return DND_ENTROPY_INVALID;
    wipe(output,length);
    if(timeout_ms<1||timeout_ms>30000)return DND_ENTROPY_INVALID;
    if(atomic_flag_test_and_set_explicit(&busy,memory_order_acquire))return DND_ENTROPY_BUSY;
    memset(&request,0,sizeof(request));
    request.started=monotonic_ms();request.deadline=request.started+timeout_ms;
    int result=DND_ENTROPY_SOURCE;
    struct rand_data *collector=NULL;
    const unsigned flags=JENT_FORCE_FIPS|JENT_DISABLE_INTERNAL_TIMER|JENT_MAX_MEMSIZE_128kB;
    if(request.deadline<request.started)request.expired=1;
    if(!expired()&&jent_entropy_init_ex(16,flags)==0&&!expired()){
        collector=jent_entropy_collector_alloc(16,flags);
        if(collector&&!expired()&&jent_read_entropy(collector,(char *)output,length)==(ssize_t)length&&!expired())
            result=DND_ENTROPY_OK;
    }
    jent_entropy_collector_free(collector);
    /* Defensive cleanup also covers a future upstream early-error path. */
    for(int i=0;i<ENTROPY_ALLOCATION_SLOTS;i++)if(request.allocations[i].pointer)
        dnd_jent_zfree(request.allocations[i].pointer,request.allocations[i].length);
    if(expired())result=DND_ENTROPY_TIMEOUT;
    else if(request.allocation_failed)result=DND_ENTROPY_MEMORY;
    else if(request.invalid_state)result=DND_ENTROPY_SOURCE;
    if(result!=DND_ENTROPY_OK)wipe(output,length);
    /* Collector/hash/memory source were wiped before releasing serialization.
     * Retain only public test counters; no noise samples or secret seed state. */
    request.started=request.deadline=0;
    atomic_flag_clear_explicit(&busy,memory_order_release);
    return result;
}
#ifdef DND_PLATFORM_ENTROPY_TEST
size_t dnd_platform_entropy_test_live_bytes(void){return request.live;}
size_t dnd_platform_entropy_test_peak_bytes(void){return request.peak;}
uint64_t dnd_platform_entropy_test_sample_count(void){return request.samples;}
#endif
#else
int dnd_platform_entropy_fill(unsigned char *output,size_t length,uint32_t timeout_ms){
    if(!output||length<48||length>64)return DND_ENTROPY_INVALID;
    wipe(output,length);
    return timeout_ms<1||timeout_ms>30000?DND_ENTROPY_INVALID:DND_ENTROPY_SOURCE;
}
#endif

#ifndef DND_PLATFORM_ENTROPY_STANDALONE
#include "dnd_async.h"
int dnd_platform_entropy_fill_array(DndArray *output,int32_t timeout_ms){
    if(!output||output->element_size!=1||output->elements_are_references)return DND_ENTROPY_INVALID;
    wipe(output->data,output->length);
    if(output->length<48||output->length>64||timeout_ms<1||timeout_ms>30000)return DND_ENTROPY_INVALID;
    if(dnd_async_running()&&dnd_async_context()!=1)return DND_ENTROPY_BUSY;
    /* Root the output across gate release; no managed pointer is accessed until
     * native_end has resumed the service context. Seed stays on native stack. */
    DndObject *root=(DndObject *)output;
    DndObject **roots[]={&root};DndGcFrame frame;dnd_gc_frame_push(&frame,roots,1);
    size_t length=output->length;unsigned char seed[64]={0};
    int context=dnd_async_native_begin();
    int result=dnd_platform_entropy_fill(seed,length,(uint32_t)timeout_ms);
    dnd_async_native_end(context);
    output=(DndArray *)root;
    if(result==DND_ENTROPY_OK)memcpy(output->data,seed,length);
    else wipe(output->data,output->length);
    wipe(seed,sizeof(seed));dnd_gc_frame_pop(&frame);return result;
}
#endif
