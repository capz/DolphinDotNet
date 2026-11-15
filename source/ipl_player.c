#define _POSIX_C_SOURCE 200809L
#include "dnd_ipl_player.h"
#include "dnd_ipl_audio.h"
#include <limits.h>
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#ifdef DND_GAMECUBE_STORAGE
#include <ogc/lwp.h>
#include <ogc/mutex.h>
#include <malloc.h>
#include <unistd.h>
typedef mutex_t IplMutex;
typedef lwp_t IplThread;
static int mutex_init(IplMutex *m){return LWP_MutexInit(m,false);}
static void lock(IplMutex *m){LWP_MutexLock(*m);}
static void unlock(IplMutex *m){LWP_MutexUnlock(*m);}
static void mutex_free(IplMutex *m){LWP_MutexDestroy(*m);}
static void pause_producer(void){struct timespec t={0,1000000};nanosleep(&t,NULL);}
#else
#include <pthread.h>
typedef pthread_mutex_t IplMutex;
typedef pthread_t IplThread;
static int mutex_init(IplMutex *m){return pthread_mutex_init(m,NULL);}
static void lock(IplMutex *m){pthread_mutex_lock(m);}
static void unlock(IplMutex *m){pthread_mutex_unlock(m);}
static void mutex_free(IplMutex *m){pthread_mutex_destroy(m);}
static void pause_producer(void){struct timespec t={0,1000000};nanosleep(&t,NULL);}
#endif
#define IPL_CHUNKS 4u
#define IPL_CHUNK_BYTES 1280u
#define IPL_STACK_BYTES (16u*1024u)
/* Reserve opaque synchronization bookkeeping as part of the native budget. */
#define IPL_SYNC_RESERVE (512u+2304u)
typedef struct {
  int token,cache,ready,running,thread_started,mono;
  unsigned next_owner,startup,startup_seen;
  float volume;
  DndIplAudioContext *cache_context;
  IplMutex mutex;
  IplThread thread;
  atomic_uint produced,consumed,offset,underruns,streak,fault,stopping;
  _Alignas(32) uint8_t queue[IPL_CHUNKS][IPL_CHUNK_BYTES];
  _Alignas(32) uint8_t stack[IPL_STACK_BYTES];
} IplPlayer;
static IplPlayer *players[2],*active;
static int next_token=1;
static IplPlayer *find(int token) {
  for(unsigned i=0;i<2;i++)if(players[i]&&players[i]->token==token)return players[i];
  return NULL;
}
static void pull(void *context,uint8_t *out,unsigned frames) {
  IplPlayer *p=context;
  if(!out||!frames||frames>320)return;
  if(atomic_load_explicit(&p->fault,memory_order_relaxed)||atomic_load_explicit(&p->stopping,memory_order_relaxed)) {
    memset(out,0,frames*4);return;
  }
  unsigned done=0;
  while(done<frames) {
    unsigned read=atomic_load_explicit(&p->consumed,memory_order_relaxed);
    unsigned write=atomic_load_explicit(&p->produced,memory_order_acquire);
    if(read==write) {
      memset(out+done*4,0,(frames-done)*4);
      atomic_fetch_add_explicit(&p->underruns,1,memory_order_relaxed);
      if(atomic_fetch_add_explicit(&p->streak,1,memory_order_relaxed)>=2)
        atomic_store_explicit(&p->fault,2,memory_order_release);
      return;
    }
    unsigned offset=atomic_load_explicit(&p->offset,memory_order_relaxed),take=320-offset;
    if(take>frames-done)take=frames-done;
    memcpy(out+done*4,p->queue[read%IPL_CHUNKS]+offset*4,take*4);done+=take;offset+=take;
    if(offset==320) {
      atomic_store_explicit(&p->offset,0,memory_order_relaxed);
      atomic_store_explicit(&p->consumed,read+1,memory_order_release);
    } else atomic_store_explicit(&p->offset,offset,memory_order_relaxed);
  }
  atomic_store_explicit(&p->streak,0,memory_order_relaxed);
}
static bool produce(IplPlayer *p) {
  unsigned write=atomic_load_explicit(&p->produced,memory_order_relaxed);
  unsigned read=atomic_load_explicit(&p->consumed,memory_order_acquire);
  if(write-read>=IPL_CHUNKS)return false;
  lock(&p->mutex);
  bool ok=dnd_ipl_audio_render_context(p->cache_context,p->queue[write%IPL_CHUNKS],320);
  if(ok&&p->startup) {
    if(dnd_ipl_audio_startup_context(p->cache_context))p->startup_seen=1;
    else if(p->startup_seen) {
      ok=dnd_ipl_audio_ambience_context(p->cache_context,true);p->startup=0;
    }
  }
  unlock(&p->mutex);
  if(!ok){atomic_store_explicit(&p->fault,1,memory_order_release);return false;}
  atomic_store_explicit(&p->produced,write+1,memory_order_release);return true;
}
static void *producer(void *context) {
  IplPlayer *p=context;
  while(!atomic_load_explicit(&p->stopping,memory_order_acquire)) {
    if(atomic_load_explicit(&p->fault,memory_order_acquire)||!produce(p))pause_producer();
  }
  return NULL;
}
static int thread_start(IplPlayer *p) {
#ifdef DND_GAMECUBE_STORAGE
  /* A sleeping producer wakes above the application executors, fills at most
   * four short chunks, then sleeps again. It never enters the managed gate. */
  return LWP_CreateThread(&p->thread,producer,p,p->stack,sizeof(p->stack),80);
#else
  return pthread_create(&p->thread,NULL,producer,p);
#endif
}
static void thread_join(IplPlayer *p) {
#ifdef DND_GAMECUBE_STORAGE
  LWP_JoinThread(p->thread,NULL);
#else
  pthread_join(p->thread,NULL);
#endif
}
int dnd_ipl_player_begin(DndArray *packet,int budget) {
  unsigned overhead=(unsigned)sizeof(IplPlayer)+IPL_SYNC_RESERVE;
  if(budget<1024||(unsigned)budget<=overhead||next_token==INT_MAX)return 0;
  unsigned slot=0;for(;slot<2;slot++)if(!players[slot])break;if(slot==2)return 0;
  IplPlayer *p=NULL;
#ifdef DND_GAMECUBE_STORAGE
  p=memalign(32,sizeof(*p));
#else
  if(posix_memalign((void **)&p,32,sizeof(*p)))return 0;
#endif
  if(!p)return 0;
  memset(p,0,sizeof(*p));
  atomic_init(&p->offset,0);atomic_init(&p->produced,0);atomic_init(&p->consumed,0);atomic_init(&p->underruns,0);
  atomic_init(&p->streak,0);atomic_init(&p->fault,0);atomic_init(&p->stopping,0);
  if(mutex_init(&p->mutex)){free(p);return 0;}
  p->cache=dnd_ipl_audio_begin(packet,budget-(int)overhead);
  if(!p->cache){mutex_free(&p->mutex);free(p);return 0;}
  p->cache_context=dnd_ipl_audio_context(p->cache);
  p->volume=1;p->next_owner=1;p->token=next_token++;players[slot]=p;return p->token;
}
int dnd_ipl_player_step(int token) {
  IplPlayer *p=find(token);if(!p||p->running)return -1;
  int status=dnd_ipl_audio_step(p->cache);if(status==1)p->ready=1;return status;
}
void dnd_ipl_player_stop(int token) {
  IplPlayer *p=find(token);if(!p||!p->running)return;
  atomic_store_explicit(&p->stopping,1,memory_order_release);
  dnd_ipl_backend_stop(p);
  if(p->thread_started){thread_join(p);p->thread_started=0;}
  p->running=0;p->startup=0;p->startup_seen=0;if(active==p)active=NULL;
  /* Pending tails and every previous cue are invalidated at Stop. */
  if(!atomic_load(&p->fault))dnd_ipl_audio_reset(p->cache);
}
int dnd_ipl_player_start(int token,int startup) {
  IplPlayer *p=find(token);
  if(!p||!p->ready||startup< -1||startup>2||atomic_load(&p->fault)||(active&&active!=p))return 0;
  if(p->running)dnd_ipl_player_stop(token);
  if(!dnd_ipl_audio_reset(p->cache)||!dnd_ipl_audio_output(p->cache,p->mono,p->volume))return 0;
  if(!dnd_ipl_audio_port(p->cache,startup<0?0x21001:0x21002,0,startup<0?1u:(unsigned)startup))return 0;
  p->startup=startup>=0;p->startup_seen=0;
  atomic_store(&p->offset,0);atomic_store(&p->produced,0);atomic_store(&p->consumed,0);atomic_store(&p->stopping,0);atomic_store(&p->streak,0);
  for(unsigned i=0;i<IPL_CHUNKS;i++)if(!produce(p))return 0;
  active=p;p->running=1;
  if(thread_start(p)){p->running=0;active=NULL;return 0;}
  p->thread_started=1;
  if(!dnd_ipl_backend_start(p,pull)){dnd_ipl_player_stop(token);return 0;}
  return 1;
}
int dnd_ipl_player_play(int token,int kind,int value) {
  IplPlayer *p=find(token);
  if(!p||!p->running||atomic_load(&p->fault)||p->next_owner==INT_MAX||value<0)return 0;
  unsigned owner=p->next_owner++;
  lock(&p->mutex);
  static const unsigned menu_events[]={3,4,5,6,7,12,9,10,11,8,22,13,15,16,17,18,19,20,21};
  bool ok=kind==0?(value<19&&dnd_ipl_audio_cue(p->cache,menu_events[value],owner)):
    kind==1&&dnd_ipl_audio_sample(p->cache,(unsigned)value,owner);
  unlock(&p->mutex);return ok?(int)owner:0;
}
int dnd_ipl_player_control(int token,int kind,float value) {
  IplPlayer *p=find(token);if(!p||!p->ready||atomic_load(&p->fault)||!(value>=0&&value<=1))return 0;
  lock(&p->mutex);bool ok=false;
  if(kind==0||kind==1) {
    if(kind==0)p->volume=value;else p->mono=value!=0;
    ok=dnd_ipl_audio_output(p->cache,p->mono,p->volume);
  } else if(kind==2&&p->running)ok=dnd_ipl_audio_port(p->cache,0x21001,value!=0?0u:1u,value!=0);
  else if((kind==3||kind==4)&&p->running)ok=dnd_ipl_audio_motion(p->cache,kind==4,value);
  unlock(&p->mutex);return ok?1:0;
}
int dnd_ipl_player_stat(int token,int kind) {
  IplPlayer *p=find(token);if(!p)return -1;
  if(kind==0)return atomic_load(&p->fault)?3:p->running?2:p->ready?1:0;
  if(kind==1||kind==2) {
    int bytes=dnd_ipl_audio_stat(p->cache,kind);return bytes<0?bytes:bytes+(int)sizeof(*p)+(int)IPL_SYNC_RESERVE;
  }
  if(kind==7)return (int)atomic_load(&p->underruns);
  if(kind==8) {
    unsigned read=atomic_load(&p->consumed),offset=atomic_load(&p->offset),write=atomic_load(&p->produced);
    unsigned frames=(write-read)*320;return (int)(frames>=offset?frames-offset:0);
  }
  if(kind==9)return (int)atomic_load(&p->fault);
  lock(&p->mutex);int result=kind==10?(int)p->startup:dnd_ipl_audio_stat(p->cache,kind);unlock(&p->mutex);return result;
}
int dnd_ipl_player_owner(int token,int owner) {
  IplPlayer *p=find(token);if(!p||!p->running||owner<=0||atomic_load(&p->fault))return 0;
  lock(&p->mutex);bool result=dnd_ipl_audio_owner_playing(p->cache,(unsigned)owner);unlock(&p->mutex);return result;
}
void dnd_ipl_player_stop_owner(int token,int owner) {
  IplPlayer *p=find(token);if(!p||!p->running||owner<=0)return;
  lock(&p->mutex);dnd_ipl_audio_stop_owner(p->cache,(unsigned)owner);unlock(&p->mutex);
}
void dnd_ipl_player_dispose(int token) {
  IplPlayer *p=find(token);if(!p)return;
  dnd_ipl_player_stop(token);
  for(unsigned i=0;i<2;i++)if(players[i]==p)players[i]=NULL;
  dnd_ipl_audio_dispose(p->cache);mutex_free(&p->mutex);free(p);
}
void dnd_ipl_player_shutdown(void) {
  for(unsigned i=0;i<2;i++)if(players[i])dnd_ipl_player_dispose(players[i]->token);
}
