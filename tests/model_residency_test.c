/* Execute the native residency state machine with tiny per-turn limits. The
 * host ARAM backend copies immediately but retains the production ticket,
 * completion, ownership, cancellation and publication rules. */
#include "dnd_model_internal.h"
#include "dnd_aram.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static bool frame_active;
bool dnd_device_active(void) { return frame_active; }
void dnd_fs_close(int token) { (void)token; assert(!"No fixture file handle."); }
int dnd_buffer_hold(int token) { (void)token; return -1; }
int dnd_buffer_unhold(int token) { (void)token; return -1; }
uint64_t dnd_buffer_held_address(int token,int offset,int count) {
  (void)token; (void)offset; (void)count; return 0;
}
enum { COPY=1, OFFSETS, RETIRE_SKINS, RETIRE_TEXTURES, FREE_PACKAGE,
       REBASE, TEXTURE_SEARCH, TEXTURE_COMPARE, TEXTURE_COPY, PREPARE_SKINS, PUBLISH };
static void put(unsigned char *p,unsigned value) {
  for(unsigned i=0;i<4;i++)p[i]=(unsigned char)(value>>(24-i*8));
}
static void number(unsigned char *p,unsigned word,unsigned value) { put(p+word*4,value); }
static void scalar(unsigned char *p,unsigned word,float value) {
  unsigned bits;memcpy(&bits,&value,4);number(p,word,bits);
}
static DndArray *array(unsigned n) {
  DndArray *a=calloc(1,sizeof(*a)+n);assert(a);a->length=n;a->element_size=1;return a;
}
static unsigned char *fixture_package(bool skinned,unsigned *length) {
  unsigned nodes=skinned?256:1;
  unsigned sizes[6]={4+nodes*108,4+44+3*80+12,36,96,skinned?8+256*68:4,4};
  unsigned offset[6],total=160;
  for(unsigned i=0;i<6;i++){offset[i]=total;total=(total+sizes[i]+31)&~31u;}
  /* Deliberately unaligned logical length exercises the final rounded DMA span. */
  *length=total+13;
  unsigned char *p=calloc(1,*length);assert(p);
  number(p,0,0x444e444d);number(p,1,1);number(p,2,*length);number(p,3,6);
  unsigned char *n=p+offset[0];number(n,0,nodes);
  for(unsigned i=0;i<nodes;i++){
    unsigned char *node=n+4+i*108;
    number(node,0,~0u);snprintf((char *)node+4,64,"joint-%u",i);
    scalar(node,17,(float)i);scalar(node,23,1);
    scalar(node,24,1);scalar(node,25,1);scalar(node,26,1);
  }
  unsigned char *g=p+offset[1];number(g,0,1);number(g,1,0);number(g,2,skinned?0:~0u);
  number(g,3,0);number(g,4,3);number(g,5,3);
  scalar(g,9,1);scalar(g,10,1);
  for(unsigned v=0;v<3;v++){
    unsigned char *vertex=g+48+v*80;
    scalar(vertex,0,v==1?1:0);scalar(vertex,1,v==2?1:0);scalar(vertex,5,1);
    for(unsigned c=8;c<12;c++)scalar(vertex,c,1);
    if(skinned){number(vertex,12,255);scalar(vertex,16,1);}
  }
  for(unsigned i=0;i<3;i++)number(g+48+3*80,i,i);
  unsigned char *m=p+offset[2];number(m,0,1);
  for(unsigned c=1;c<5;c++)scalar(m,c,1);
  number(m,5,0);number(m,6,0);scalar(m,7,0);number(m,8,0);
  unsigned char *t=p+offset[3];number(t,0,1);
  number(t,1,4);number(t,2,4);number(t,3,1);number(t,7,64);
  for(unsigned i=0;i<64;i++)t[32+i]=(unsigned char)(i*17+3);
  unsigned char *s=p+offset[4];number(s,0,skinned?1:0);
  if(skinned){number(s,1,256);for(unsigned i=0;i<256;i++){
    unsigned char *joint=s+8+i*68;number(joint,0,i);
    for(unsigned k=0;k<16;k++)scalar(joint,k+1,k%5==0?1:0);
  }}
  number(p+offset[5],0,0);
  memset(p+total,0xa7,13);
  for(unsigned i=0;i<6;i++){
    unsigned char *d=p+16+i*16;number(d,0,i+1);number(d,1,offset[i]);
    number(d,2,sizes[i]);number(d,3,dnd_model_hash(p+offset[i],sizes[i]));
  }
  return p;
}
static int load(const unsigned char *p,unsigned n) {
  int token=dnd_model_begin_data(p,n,8*1024*1024);assert(token);return token;
}
static int tick(int token,int cancel) {
  int rc=dnd_model_residency_step(token,32,1,cancel);
  assert(rc==0||rc==1||rc==2||rc==-1||rc==-2||rc==-3);
  assert(dnd_model_residency_stat(token,1)<=32);
  assert(dnd_model_residency_stat(token,2)<=1);
  return rc;
}
static int finish(int token,int cancel) {
  for(unsigned steps=0;steps<100000;steps++){
    int rc=tick(token,cancel);if(rc==1||rc<0)return rc;
  }
  assert(!"Residency did not reach terminal state.");return -99;
}
static void phase(int token,int wanted) {
  for(unsigned steps=0;steps<100000;steps++){
    if(dnd_model_residency_stat(token,0)==wanted)return;
    assert(tick(token,0)>=0);
  }
  assert(!"Residency did not enter requested phase.");
}
static int backing(unsigned length) {
  int token=dnd_aram_allocate((int)((length+31)&~31u));assert(token);return token;
}
static void begin(int token,int aram,int restore) {
  assert(dnd_model_residency_begin(token,aram,restore)==0);
  DndModel *m=dnd_model_find(token,true);assert(m&&m->residency);
  void *owner=m->residency;
  DndArray *a=array(32);
  assert(dnd_model_residency_begin(token,aram,restore)==-1);
  assert(dnd_model_copy(token,0,a,32,0)==-1);
  assert(dnd_model_copy(token,0,a,32,1)==-1);
  assert(dnd_model_unload(token)==-1);
  assert(dnd_model_restore_begin(token)==-1);
  assert(dnd_model_restore_end(token,0)==-1);
  assert(dnd_model_residency_step(token,33,1,0)==-1);
  assert(dnd_model_residency_step(token,32,0,0)==-1);
  frame_active=true;assert(tick(token,0)==0);frame_active=false;
  assert(m->residency==owner);free(a);
}
static void transfer(int token,unsigned offset,DndArray *bytes,int write) {
  assert(dnd_aram_retain(token)==0);
  assert(dnd_aram_begin(token,(int)offset,bytes,0,(int)bytes->length,write)==0);
  int rc;do{rc=dnd_aram_poll(bytes,0);}while(!rc);assert(rc==1);
  assert(dnd_aram_release(token)==0);
}
static void no_leaks(void) {
  assert(dnd_model_live_handles()==0);
  assert(dnd_aram_broker_stat(0)==0);
  assert(dnd_aram_stat(1)==0&&dnd_aram_stat(4)==0);
}
static void round_trip(const unsigned char *package,unsigned length) {
  int token=load(package,length),store=backing(length);
  DndModel *m=dnd_model_find(token,false);assert(m);
  int instance=dnd_model_instance(token,4*1024*1024);assert(instance);
  DndInstance *in=dnd_instance_find(instance);assert(in);
  if(m->clips>=2){
    assert(dnd_model_play(instance,0,1,0)==0);
    assert(dnd_model_control(instance,0,.375f)==0);
    assert(dnd_model_play(instance,1,1,.75f)==0);
    assert(dnd_model_control(instance,0,.125f)==0);
    assert(in->next>=0);
  }
  assert(dnd_model_control(instance,5,1.25f)==0);
  assert(dnd_model_control(instance,1,0)==0);
  float time=in->time,next_time=in->next_time,elapsed=in->elapsed;
  int clip=in->clip,next=in->next;
  unsigned before_model=m->bytes,before_instance=in->bytes;
  float expected[6];assert(dnd_model_vertex(instance,0,1,expected)==0);
  float *globals=malloc(m->nodes*64);assert(globals);memcpy(globals,in->global,m->nodes*64);

  begin(token,store,0);
  /* Cancellation with a request queued/in flight preserves the resident data. */
  assert(tick(token,0)==2);(void)dnd_aram_service_broker();
  assert(finish(token,1)==-3);
  assert(m->ready&&m->package&&!m->residency&&m->bytes==before_model);
  assert(in->bytes==before_instance);

  begin(token,store,0);phase(token,OFFSETS);
  assert(tick(token,0)==2);assert(!m->ready);
  /* Once retirement commits, late cancellation must finish valid and cold. */
  assert(finish(token,1)==1);
  assert(!m->ready&&!m->package&&m->residency_offsets&&m->residency_digest_valid);
  assert(in->bytes<=before_instance&&!in->workspace);
  unsigned cold=m->bytes,cold_instance=in->bytes;
  assert(dnd_model_restore_begin(token)==-1);
  begin(token,store,1);phase(token,REBASE);
  assert(tick(token,0)==2); /* One descriptor has been rebased, not published. */
  assert(!m->ready&&m->staged);
  assert(finish(token,1)==-3);
  assert(!m->ready&&!m->staged&&m->residency_offsets&&m->bytes==cold);
  assert(in->bytes==cold_instance);

  /* Simulated per-model admission pressure must reject without publishing or
   * leaking either the context or the staged package. */
  unsigned budget=m->budget;m->budget=m->bytes;
  assert(dnd_model_residency_begin(token,store,1)==-2);
  assert(!m->residency&&!m->staged&&m->bytes==cold);
  m->budget=budget;
  begin(token,store,1);m->budget=m->bytes;phase(token,REBASE);
  assert(finish(token,0)==-2);
  assert(!m->residency&&!m->staged&&m->bytes==cold&&m->residency_offsets);
  assert(in->bytes==cold_instance);m->budget=budget;

  /* Corruption must be rejected before descriptor/texture publication, and a
   * repaired snapshot must remain retryable after complete rollback. */
  DndArray *original=array(32),*broken=array(32);transfer(store,0,original,0);
  memcpy(broken->data,original->data,32);broken->data[17]^=0x80;transfer(store,0,broken,1);
  begin(token,store,1);assert(finish(token,0)==-1);
  assert(!m->ready&&!m->staged&&!m->residency&&m->residency_offsets&&m->bytes==cold);
  transfer(store,0,original,1);free(original);free(broken);
  begin(token,store,1);assert(finish(token,0)==1);
  assert(m->ready&&m->package&&!m->staged&&!m->residency_offsets);
  assert(!memcmp(m->package,package,length)&&m->bytes==before_model);
  assert(in->bytes==before_instance&&!memcmp(in->global,globals,m->nodes*64));free(globals);
  float actual[6];assert(dnd_model_vertex(instance,0,1,actual)==0);
  assert(!memcmp(actual,expected,sizeof(actual)));
  assert(in->time==time&&in->next_time==next_time&&in->elapsed==elapsed&&in->paused);
  assert(in->clip==clip&&in->next==next&&in->speed==1.25f);

  int reads=dnd_aram_broker_stat(3),writes=dnd_aram_broker_stat(4);
  begin(token,0,0);assert(finish(token,0)==1);
  assert(dnd_aram_broker_stat(3)==reads&&dnd_aram_broker_stat(4)==writes);
  begin(token,store,1);assert(finish(token,0)==1);
  assert(dnd_model_instance_dispose(instance)==0&&dnd_model_dispose(token)==0);
  assert(dnd_aram_dispose(store)==0);no_leaks();
}
static void shared_textures(const unsigned char *package,unsigned length) {
  (void)dnd_model_take_texture_publication();
  int a=load(package,length);assert(dnd_model_take_texture_publication());
  int b=load(package,length),store=backing(length);
  assert(!dnd_model_take_texture_publication()); /* Ready-cache reuse is coherent. */
  DndModel *first=dnd_model_find(a,false),*second=dnd_model_find(b,false);
  assert(first->textures&&first->texture[0].data==second->texture[0].data);
  void *shared=first->texture[0].data;
  int physical=dnd_model_stat(a,6);
  begin(b,store,0);assert(finish(b,0)==1);
  assert(dnd_model_stat(a,6)==physical&&first->texture[0].data==shared);
  unsigned cold=second->bytes;
  begin(b,store,1);phase(b,TEXTURE_COMPARE);assert(tick(b,0)==2);
  assert(finish(b,1)==-3&&second->bytes==cold);
  assert(dnd_model_stat(a,6)==physical&&first->texture[0].data==shared);
  begin(b,store,1);assert(finish(b,0)==1);
  assert(!dnd_model_take_texture_publication());
  assert(second->texture[0].data==shared&&dnd_model_stat(a,6)==physical);
  assert(dnd_model_dispose(a)==0);
  assert(dnd_model_stat(b,6)==physical&&second->texture[0].data==shared);
  begin(b,0,0);assert(finish(b,0)==1);assert(dnd_model_stat(b,6)==0);
  assert(dnd_model_dispose(b)==0&&dnd_aram_dispose(store)==0);no_leaks();

  /* A half-copied cache entry must not be observed by another model load. */
  a=load(package,length);first=dnd_model_find(a,false);store=backing(length);
  assert(dnd_model_take_texture_publication());
  begin(a,store,0);assert(finish(a,0)==1);cold=first->bytes;
  begin(a,store,1);phase(a,TEXTURE_COPY);assert(tick(a,0)==2);
  assert(!first->texture[0].data);
  assert(!dnd_model_take_texture_publication()); /* Partial copy is private. */
  b=load(package,length);second=dnd_model_find(b,false);shared=second->texture[0].data;
  assert(dnd_model_take_texture_publication());
  assert(dnd_model_stat(b,6)>physical);
  assert(finish(a,1)==-3&&first->bytes==cold);
  assert(second->texture[0].data==shared&&dnd_model_stat(b,6)==physical);
  begin(a,store,1);assert(finish(a,0)==1);
  assert(!dnd_model_take_texture_publication());
  assert(first->texture[0].data==shared&&dnd_model_stat(a,6)==physical);
  assert(dnd_model_dispose(b)==0);
  begin(a,0,0);assert(finish(a,0)==1);assert(dnd_model_stat(a,6)==0);
  assert(dnd_model_dispose(a)==0&&dnd_aram_dispose(store)==0);no_leaks();

  /* Completing a fresh restored copy marks publication before cache reuse. */
  a=load(package,length);assert(dnd_model_take_texture_publication());
  first=dnd_model_find(a,false);store=backing(length);
  begin(a,store,0);assert(finish(a,0)==1);
  begin(a,store,1);phase(a,TEXTURE_COPY);
  assert(!dnd_model_take_texture_publication());
  assert(tick(a,0)==2&&!dnd_model_take_texture_publication());
  assert(tick(a,0)==2&&dnd_model_take_texture_publication());
  assert(!first->ready&&first->texture[0].data);
  b=load(package,length);assert(!dnd_model_take_texture_publication());
  assert(dnd_model_find(b,false)->texture[0].data==first->texture[0].data);
  assert(finish(a,0)==1);
  assert(dnd_model_dispose(a)==0&&dnd_model_dispose(b)==0);
  assert(dnd_aram_dispose(store)==0);no_leaks();
}
static void closed_owner(const unsigned char *package,unsigned length) {
  int token=load(package,length),store=backing(length);
  begin(token,store,0);assert(tick(token,0)==2);
  (void)dnd_aram_service_broker();
  assert(dnd_model_dispose(token)==0&&dnd_aram_dispose(store)==0);
  assert(dnd_model_find(token,true)&&dnd_aram_stat(4)==1);
  assert(finish(token,0)==-3);
  assert(!dnd_model_find(token,true));no_leaks();
}
static void hash_collision(const unsigned char *package,unsigned length) {
  unsigned char *changed=malloc(length);assert(changed);memcpy(changed,package,length);
  unsigned texture=dnd_model_u32(changed+16+3*16+4);
  changed[texture+32]^=0x5a;
  put(changed+16+3*16+12,dnd_model_hash(changed+texture,dnd_model_u32(changed+16+3*16+8)));
  int a=load(package,length),b=load(changed,length),store=backing(length);
  DndModel *first=dnd_model_find(a,false),*second=dnd_model_find(b,false);
  assert(first->texture[0].data!=second->texture[0].data);
  /* Inject an equal lookup hash while keeping distinct validated package and
   * SHA contents. The cache must compare bytes rather than trust the hint. */
  second->texture[0].hash=first->texture[0].hash;
  begin(b,store,0);assert(finish(b,0)==1);
  int one=dnd_model_stat(a,6);
  begin(b,store,1);phase(b,TEXTURE_COMPARE);
  assert(finish(b,0)==1);
  assert(first->texture[0].data!=second->texture[0].data);
  assert(!memcmp(first->texture[0].data,package+texture+32,64));
  assert(!memcmp(second->texture[0].data,changed+texture+32,64));
  assert(dnd_model_stat(a,6)==2*one);
  assert(dnd_model_dispose(a)==0);
  begin(b,0,0);assert(finish(b,0)==1);assert(dnd_model_stat(b,6)==0);
  assert(dnd_model_dispose(b)==0&&dnd_aram_dispose(store)==0);free(changed);no_leaks();
}
int main(int argc,char **argv) {
  assert(dnd_aram_initialize(2*1024*1024));
  unsigned length;unsigned char *p=fixture_package(false,&length);
  round_trip(p,length);shared_textures(p,length);hash_collision(p,length);closed_owner(p,length);free(p);
  p=fixture_package(true,&length);round_trip(p,length);free(p);
  if(argc>1){
    char name[4096];snprintf(name,sizeof(name),"%s/sd/animated.dndmodel",argv[1]);
    FILE *f=fopen(name,"rb");assert(f);assert(!fseek(f,0,SEEK_END));long size=ftell(f);
    assert(size>=160&&!fseek(f,0,SEEK_SET));p=malloc((size_t)size);assert(p);
    assert(fread(p,1,(size_t)size,f)==(size_t)size);assert(!fclose(f));
    round_trip(p,(unsigned)size);free(p);
  }
  puts("Native model residency: bounded copy/rebase/skin, cancellation, SHA retry, shared textures and owner drainage passed.");
  return 0;
}
