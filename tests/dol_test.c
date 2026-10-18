#include "dnd_dol.h"
#include "dnd_storage.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned char image[10000];
static size_t file_position;
static bool supported=true, fail_read, throw_read;
static int opened, closed, launched;
static void word(unsigned char *p, unsigned n) { p[0]=(uint8_t)(n>>24);p[1]=(uint8_t)(n>>16);p[2]=(uint8_t)(n>>8);p[3]=(uint8_t)n; }
static void valid(void) {
    memset(image,0,sizeof(image));
    word(image,256);word(image+0x48,0x80004000);word(image+0x90,32);
    word(image+7*4,288);word(image+0x48+7*4,0x80005000);word(image+0x90+7*4,32);
    word(image+0xd8,0x80006000);word(image+0xdc,128);word(image+0xe0,0x80004000);
    image[256]=42;
}
bool dnd_dol_platform_supported(void) { return supported; }
size_t dnd_dol_platform_stub_size(void) { return 4; }
void dnd_dol_platform_stub_copy(void *p) { memset(p,0xab,4); }
void dnd_dol_platform_launch(DndDolPlan *plan, void *bytes, void *stub) {
    assert(plan->entry==0x80004000 && ((unsigned char *)bytes)[256]==42 && ((unsigned char *)stub)[0]==0xab);
    launched++;
}
bool dnd_fs_path_utf8_native(DndString *path, char *p, unsigned capacity) { (void)path;assert(capacity>4);strcpy(p,"dol");return true; }
int dnd_fs_open_utf8_native(const char *p,int mode,int access,int share) { assert(!strcmp(p,"dol") && mode==3 && access==1 && share==1);opened++;file_position=0;return 1; }
int64_t dnd_fs_length_native(int token) { assert(token==1);return sizeof(image); }
int dnd_fs_read_native(int token,void *p,int count) {
    assert(token==1);
    if (throw_read) dnd_exception_throw(DND_EXCEPTION_IO,"Injected DOL read exception");
    if (fail_read) return 0;
    if (count>17) count=17; /* File reads may be short without reaching EOF. */
    if ((size_t)count>sizeof(image)-file_position) count=(int)(sizeof(image)-file_position);
    memcpy(p,image+file_position,(size_t)count);file_position+=(size_t)count;return count;
}
void dnd_fs_close(int token) { assert(token==1);closed++; }
static int bytes(void) {
    DndArray *a=calloc(1,sizeof(DndArray)+sizeof(image));assert(a);
    a->element_size=1;a->length=sizeof(image);memcpy(a->data,image,sizeof(image));
    int token=dnd_dol_prepare(a);free(a);return token;
}
int main(void) {
    DndDolPlan plan;
    valid();assert(dnd_dol_validate(image,sizeof(image),&plan));assert(plan.count==2);
    assert(!dnd_dol_validate(image,255,&plan));
    assert(!dnd_dol_validate(image,DND_DOL_MAX_SIZE+1u,&plan));
    for (unsigned i=0;i<5;i++) { valid();word(image+0x48,(unsigned[]){0,0x800030fc,0xc0004000,0x817ffff0,0xfffffff0}[i]);assert(!dnd_dol_validate(image,sizeof(image),&plan)); }
    valid();word(image+0x90,0xffffffff);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image,0xfffffff0);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image,128);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image+0x48+7*4,0x80004010);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image+7*4,260);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    /* DOL BSS envelopes may include initialized bytes/padded section tails.
     * The trampoline clears BSS first, then copies the initialized sections. */
    valid();word(image+0xd8,0x80004000);assert(dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image+0xe0,0x80005000);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image+0xe0,0x80004001);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();word(image+0x90,1);assert(!dnd_dol_validate(image,sizeof(image),&plan));
    valid();assert(dnd_dol_validate(image,sizeof(image),&plan));
    assert(!dnd_dol_stage_safe(&plan,0x80003ff0,32));
    assert(!dnd_dol_stage_safe(&plan,0x80006040,64));
    assert(!dnd_dol_stage_safe(&plan,UINTPTR_MAX-15,32));
    assert(dnd_dol_stage_safe(&plan,0x81000000,4096));
    int t=bytes();assert(t>0 && dnd_dol_info(t,2)==2);
    image[256]=0;assert(dnd_dol_request(t)==0);assert(dnd_dol_dispose(t)==0);
    assert(dnd_dol_pending());assert(dnd_dol_request(t)==-7);
    dnd_dol_finish(true);assert(launched==1 && !dnd_dol_pending());
    assert(dnd_dol_info(t,1)==-6);
    valid();t=bytes();assert(t>0);assert(dnd_dol_request(t)==0);dnd_dol_finish(false);assert(launched==1);
    int handles[8];for(int i=0;i<8;i++){handles[i]=bytes();assert(handles[i]>0);}assert(bytes()==-5);
    for(int i=0;i<8;i++)assert(dnd_dol_dispose(handles[i])==0);
    assert(dnd_dol_dispose(handles[0])==-6);
    t=dnd_dol_begin(NULL);assert(t>0);assert(dnd_dol_request(t)==-6);
    assert(dnd_dol_step(t)==0);assert(dnd_dol_dispose(t)==0);assert(opened==closed);
    t=dnd_dol_begin(NULL);assert(t>0);while(dnd_dol_step(t)==0){}assert(opened==closed);assert(dnd_dol_dispose(t)==0);
    t=dnd_dol_begin(NULL);assert(t>0);fail_read=true;assert(dnd_dol_step(t)==-4);assert(opened==closed);fail_read=false;
    DndEhFrame guard;dnd_eh_push(&guard);throw_read=true;
    if(!setjmp(guard.environment)){(void)dnd_dol_begin(NULL);assert(false);}
    dnd_eh_pop(&guard);assert(opened==closed);dnd_exception_clear();throw_read=false;
    t=dnd_dol_begin(NULL);assert(t>0);throw_read=true;dnd_eh_push(&guard);
    if(!setjmp(guard.environment)){(void)dnd_dol_step(t);assert(false);}
    dnd_eh_pop(&guard);assert(opened==closed);dnd_exception_clear();throw_read=false;
    supported=false;assert(bytes()==-8);assert(dnd_dol_begin(NULL)==-8);
    puts("DOL validation, staging ownership, short reads, cancellation cleanup and failed-Main cancellation passed.");
    return 0;
}
