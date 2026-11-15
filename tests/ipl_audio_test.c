/* Generated sources only. The existing fake supplies filesystem/ordinary
 * audio symbols; this suite exercises the independently owned session cache. */
#define main unused_format_fixture_main
#include "audio_formats_test.c"
#undef main
#include "dnd_ipl_audio.h"
static void put32(uint8_t *p,unsigned v) {
  p[0]=(uint8_t)(v>>24);p[1]=(uint8_t)(v>>16);p[2]=(uint8_t)(v>>8);p[3]=(uint8_t)v;
}
static DndArray *packet(void) {
  const unsigned seq=13,bank=0x8e0,start=352+seq+bank+128+512,size=start+12*(256+32);
  DndArray *a=calloc(1,sizeof(*a)+size);assert(a);a->length=size;a->element_size=1;
  uint8_t *p=a->data;memcpy(p,"DIA1",4);put32(p+4,size);put32(p+8,1);put32(p+12,12);
  put32(p+16,seq);put32(p+20,bank);put32(p+24,4);put32(p+28,512);
  const uint8_t idle[]={0xd0,0,2,0x10,1,0xe5,0x80,100,0xc8,0,0,0,6};memcpy(p+352,idle,seq);
  memcpy(p+352+seq,"IBNK",4);put32(p+352+seq+4,bank);
  for(unsigned i=0;i<4;i++){uint8_t *e=p+352+seq+bank+i*32;e[0]=2;put32(e+12,1);}
  for(unsigned phase=0;phase<64;phase++){unsigned at=352+seq+bank+128+phase*8+6;p[at]=127;p[at+1]=255;}
  for(unsigned i=0;i<12;i++) {
    unsigned at=start+i*288;uint8_t *e=p+64+i*24,*w=p+at;
    put32(e,at);put32(e+4,at+256);put32(e+8,32);put32(e+12,i);put32(e+16,60);put32(e+20,256);
    memcpy(w,"WSYS",4);put32(w+4,256);put32(w+16,32);memcpy(w+32,"WINF",4);
    put32(w+36,1);put32(w+40,64);memcpy(w+64,"generated.aw",13);put32(w+176,1);put32(w+180,192);
    w[193]=3;w[194]=60;put32(w+196,0x46fa0000);put32(w+204,32);put32(w+208,UINT32_MAX);
    put32(w+212,3);put32(w+216,14);put32(w+220,16);
    for(unsigned n=0;n<16;n++){w[256+n*2]=(uint8_t)((n+1)*1000>>8);w[257+n*2]=(uint8_t)((n+1)*1000);}
  }
  return a;
}
static void ready(int token) {
  int state=0;unsigned steps=0;
  while(!state&&steps++<100)state=dnd_ipl_audio_step(token);
  assert(state==1);
}
#ifndef DND_IPL_CACHE_FIXTURE_ONLY
int main(void) {
  DndArray *a=packet();int token=dnd_ipl_audio_begin(a,8*1024*1024);assert(token);
  memset(a->data,0,a->length);free(a);ready(token);
  unsigned exact=(unsigned)dnd_ipl_audio_stat(token,2);assert(exact>1000&&exact<8*1024*1024);
  uint8_t out[1280];assert(dnd_ipl_audio_sample(token,0,123));
  for(unsigned i=0;i<200;i++)assert(dnd_ipl_audio_render(token,out,320));
  assert(dnd_ipl_audio_owner_playing(token,123));dnd_ipl_audio_stop_owner(token,123);
  assert(!dnd_ipl_audio_owner_playing(token,123));dnd_ipl_audio_dispose(token);dnd_ipl_audio_dispose(token);
  assert(dnd_ipl_audio_stat(token,0)==-1&&!dnd_ipl_audio_render(token,out,320));
  a=packet();token=dnd_ipl_audio_begin(a,(int)exact);assert(token);ready(token);
  assert(dnd_ipl_audio_stat(token,2)==(int)exact);dnd_ipl_audio_dispose(token);
  token=dnd_ipl_audio_begin(a,(int)exact-1);
  if(token){assert(dnd_ipl_audio_step(token)>=0);while(dnd_ipl_audio_step(token)==0){}assert(dnd_ipl_audio_stat(token,0)==-1);dnd_ipl_audio_dispose(token);}
  /* Cancellation before decode completion releases both available slots. */
  int first=dnd_ipl_audio_begin(a,8*1024*1024),second=dnd_ipl_audio_begin(a,8*1024*1024);
  assert(first&&second&&first!=second&&!dnd_ipl_audio_begin(a,8*1024*1024));
  dnd_ipl_audio_dispose(first);token=dnd_ipl_audio_begin(a,8*1024*1024);assert(token&&token!=first);
  assert(!dnd_ipl_audio_sample(first,0,1));dnd_ipl_audio_dispose(second);dnd_ipl_audio_dispose(token);
  /* Extent overlap, foreign IDs, invalid source loop and truncated sequence. */
  put32(a->data+64,0);assert(!dnd_ipl_audio_begin(a,8*1024*1024));free(a);
  a=packet();put32(a->data+64+12,12);assert(!dnd_ipl_audio_begin(a,8*1024*1024));free(a);
  a=packet();unsigned header=((unsigned)a->data[64]<<24)|((unsigned)a->data[65]<<16)|((unsigned)a->data[66]<<8)|a->data[67];
  put32(a->data+header+216,17);assert(!dnd_ipl_audio_begin(a,8*1024*1024));free(a);
  a=packet();const uint8_t spin[]={0xc8,0,0,0,0};memcpy(a->data+352,spin,sizeof(spin));
  token=dnd_ipl_audio_begin(a,8*1024*1024);assert(token);ready(token);free(a);
  assert(!dnd_ipl_audio_render(token,out,80)&&dnd_ipl_audio_stat(token,0)==-1);dnd_ipl_audio_dispose(token);
  puts("IPL owned cache: source lifetime, automatic loop, exact budget, cancellation, slot reuse and malformed-input tests passed");
}

#endif
