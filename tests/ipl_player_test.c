#define _POSIX_C_SOURCE 200809L
#define DND_IPL_CACHE_FIXTURE_ONLY
#include "ipl_audio_test.c"
#include "dnd_ipl_player.h"
#include <time.h>
static void *context;
static DndIplPull callback;
static unsigned starts,stops;
bool dnd_ipl_backend_start(void *p,DndIplPull pull) {
  assert(!context&&!callback);context=p;callback=pull;starts++;return true;
}
void dnd_ipl_backend_stop(void *p) {
  assert(context==p&&callback);context=NULL;callback=NULL;stops++;
}
static void wait_queue(int token) {
  for(unsigned i=0;dnd_ipl_player_stat(token,8)<960&&i<2000;i++) {
    struct timespec delay={0,1000000};nanosleep(&delay,NULL);
  }
  assert(dnd_ipl_player_stat(token,8)>=960);
}
static int prepare(void) {
  DndArray *a=packet();int p=dnd_ipl_player_begin(a,8*1024*1024);assert(p);
  memset(a->data,0,a->length);free(a);
  int s=0;for(unsigned i=0;!s&&i<100;i++)s=dnd_ipl_player_step(p);
  assert(s==1&&dnd_ipl_player_stat(p,0)==1);return p;
}
int main(void) {
  int first=prepare(),second=prepare();
  assert(dnd_ipl_player_start(first,-1));assert(!dnd_ipl_player_start(second,-1));
  assert(dnd_ipl_player_stat(first,8)==1280);
  int owner=dnd_ipl_player_play(first,1,0);assert(owner&&dnd_ipl_player_owner(first,owner));
  uint8_t output[1152];bool heard=false;unsigned absolute=0;
  /* Ten minutes of 288-frame backend pulls across 320-frame chunks with a generated embedded-loop source.
   * The fake consumes only published chunks: this is a deterministic queue,
   * ownership and producer-throughput test, not a hardware real-time claim. */
  for(unsigned chunk=0;chunk<66727;chunk++) {
    wait_queue(first);callback(context,output,288);
    for(unsigned i=0;i<sizeof(output);i++)if(output[i])heard=true;
    /* Independent sample-by-sample oracle catches padded, skipped or repeated
     * PCM at the backend's 288-frame / producer's 320-frame boundary. */
    for(unsigned frame=0;frame<288;frame++,absolute++)if(absolute<64000) {
      int expected=0;
      if(absolute>1280) {
        unsigned n=absolute-1280;int index=(int)(n*(double)(32000.0f/32028.5f))-1;
        if(index>=14)index=3+(index-14)%11;
        int source=index<0?0:(index+1)*1000-1;
        expected=n<80?(int)((float)source*(float)n/80):source;
      }
      for(unsigned channel=0;channel<2;channel++)assert(abs((int16_t)((unsigned)output[frame*4+channel*2]<<8|output[frame*4+channel*2+1])-expected)<=1);
    }
  }
  assert(heard&&dnd_ipl_player_owner(first,owner)&&!dnd_ipl_player_stat(first,7));
  assert(dnd_ipl_player_control(first,0,.5f)&&dnd_ipl_player_control(first,1,1));
  dnd_ipl_player_stop_owner(first,owner);assert(!dnd_ipl_player_owner(first,owner));
  dnd_ipl_player_stop(first);assert(!context&&dnd_ipl_player_stat(first,0)==1);
  assert(dnd_ipl_player_start(second,-1));dnd_ipl_player_dispose(second);assert(!context);
  assert(dnd_ipl_player_start(first,-1));assert(!dnd_ipl_player_owner(first,owner));
  dnd_ipl_player_dispose(first);dnd_ipl_player_dispose(first);
  assert(starts==3&&stops==3&&dnd_ipl_player_stat(first,0)==-1);
  dnd_ipl_player_shutdown();puts("IPL dedicated producer: ten-minute generated queue, ownership, stop/restart, single-session lease and joined disposal passed");
}
