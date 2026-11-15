#include "dnd_ipl_audio.h"
#include "dnd_ipl_mixer.h"
#include "dnd_audio_codec.h"
#include <limits.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

#define IPL_BUDGET_LIMIT (16u*1024u*1024u)
typedef struct DndIplAudioContext {
  int token,state;
  uint8_t *packet,*pcm[DND_IPL_WAVES];
  unsigned size,budget,bytes,peak,decoding,sequence_bytes,early,bank_offset,effect_offset,coefficient_offset;
  DndAudioSource source[DND_IPL_WAVES];
  unsigned base_key[DND_IPL_WAVES];
  DndAudioDecoder decoder;
  DndIplMixer *mixer;
} IplAudio;
static IplAudio *sessions[2];
static int next_session=1;
static unsigned be32(const uint8_t *p){return (unsigned)p[0]<<24|(unsigned)p[1]<<16|(unsigned)p[2]<<8|p[3];}
static IplAudio *find(int token) {
  for(unsigned i=0;i<2;i++)if(sessions[i]&&sessions[i]->token==token)return sessions[i];
  return NULL;
}
static bool charge(IplAudio *s,unsigned bytes) {
  if(bytes>s->budget-s->bytes)return false;
  s->bytes+=bytes;if(s->bytes>s->peak)s->peak=s->bytes;return true;
}
static void destroy(IplAudio *s) {
  if(!s)return;
  if(s->mixer){dnd_ipl_mixer_dispose(s->mixer);free(s->mixer);}
  for(unsigned i=0;i<DND_IPL_WAVES;i++){dnd_audio_source_dispose(&s->source[i]);free(s->pcm[i]);}
  free(s->packet);free(s);
}
int dnd_ipl_audio_begin(DndArray *input,int budget) {
  if(!input||input->elements_are_references||input->element_size!=1||input->length<352||input->length>2*1024*1024||
     budget<1024||budget>(int)IPL_BUDGET_LIMIT||next_session==INT_MAX)return 0;
  unsigned slot=0;for(;slot<2;slot++)if(!sessions[slot])break;if(slot==2)return 0;
  const uint8_t *p=input->data;
  unsigned seq=be32(p+16),bank=be32(p+20);
  if(memcmp(p,"DIA1",4)||be32(p+4)!=input->length||(be32(p+8)!=1&&be32(p+8)!=2)||be32(p+12)!=12||
     !seq||seq>4096||bank!=0x8e0||be32(p+24)!=4||be32(p+28)!=512)return 0;
  unsigned begin=352+seq+bank+128+512;
  if(begin>input->length||(unsigned)budget<sizeof(IplAudio)+sizeof(DndIplMixer)+input->length)return 0;
  IplAudio *s=calloc(1,sizeof(*s));if(!s)return 0;s->budget=(unsigned)budget;
  if(!charge(s,(unsigned)sizeof(*s)+input->length+(unsigned)sizeof(DndIplMixer)))goto fail;
  s->packet=malloc(input->length);s->mixer=calloc(1,sizeof(*s->mixer));if(!s->packet||!s->mixer)goto fail;
  memcpy(s->packet,p,input->length);s->size=input->length;p=s->packet;
  s->early=be32(p+8)==1;s->sequence_bytes=seq;s->bank_offset=352+seq;s->effect_offset=s->bank_offset+bank;s->coefficient_offset=s->effect_offset+128;
  if(memcmp(p+s->bank_offset,"IBNK",4)||be32(p+s->bank_offset+4)!=bank)goto fail;
  unsigned next=begin;
  for(unsigned i=0;i<12;i++) {
    const uint8_t *entry=p+64+i*24;
    unsigned header=be32(entry),body=be32(entry+4),bytes=be32(entry+8),id=be32(entry+12),key=be32(entry+16),header_bytes=be32(entry+20);
    if(header!=next||header_bytes!=256||body!=header+256||body>s->size||!bytes||bytes>s->size-body||id!=i||key>127)goto fail;
    if(!dnd_audio_source_parse_memory(&s->source[i],p+header,256,p+body,bytes,7,0,s->budget-s->bytes))goto fail;
    const DndAudioSource *source=&s->source[i];
    if(source->channels!=1||!source->samples||source->samples>200000||!source->span_count||source->span_count>16)goto fail;
    unsigned need=source->samples*2+source->span_count*(unsigned)sizeof(DndAudioSpan);
    if(!charge(s,need))goto fail;
    s->pcm[i]=malloc(source->samples*2);if(!s->pcm[i])goto fail;
    s->base_key[i]=key;next=body+bytes;
  }
  if(next!=s->size)goto fail;
  dnd_audio_decoder_init(&s->decoder,&s->source[0],false);
  s->token=next_session++;sessions[slot]=s;return s->token;
fail:
  destroy(s);return 0;
}
int dnd_ipl_audio_step(int token) {
  IplAudio *s=find(token);if(!s||s->state<0)return -1;if(s->state==1)return 1;
  if(s->decoding<12) {
    const DndAudioSource *source=&s->source[s->decoding];unsigned at=s->decoder.state.position;
    unsigned frames=dnd_audio_decode(&s->decoder,s->pcm[s->decoding]+at*2,512);
    if(s->decoder.failed||(!frames&&at<source->samples)){s->state=-1;return -1;}
    if(s->decoder.state.position==source->samples) {
      s->decoding++;if(s->decoding<12)dnd_audio_decoder_init(&s->decoder,&s->source[s->decoding],false);
    }
    return 0;
  }
  DndIplWave waves[12];
  for(unsigned i=0;i<12;i++) {
    const DndAudioSource *w=&s->source[i];
    waves[i]=(DndIplWave){s->pcm[i],w->samples,w->rate,s->base_key[i],w->loop_start,w->loop_end,w->has_loop!=0};
  }
  const uint8_t *p=s->packet;
  unsigned budget=s->budget-s->bytes+(unsigned)sizeof(DndIplMixer);
  if(!dnd_ipl_mixer_init(s->mixer,waves,p+352,s->sequence_bytes,p+s->bank_offset,0x8e0,
      p+s->coefficient_offset,p+s->effect_offset,s->early!=0,budget)){s->state=-1;return -1;}
  if(!charge(s,s->mixer->bytes-(unsigned)sizeof(DndIplMixer))){s->state=-1;return -1;}
  s->state=1;return 1;
}
int dnd_ipl_audio_stat(int token,int kind) {
  const IplAudio *s=find(token);if(!s)return -1;
  if(kind==0)return s->state;
  if(kind==1)return (int)s->bytes;
  if(kind==2)return (int)s->peak;
  if(kind==3) {
    unsigned voices=0;for(unsigned i=0;i<DND_IPL_VOICES;i++)if(s->mixer->voice[i].active)voices++;return (int)voices;
  }
  if(kind==4)return (int)s->mixer->blocks;
  if(kind==5)return (int)s->mixer->fault;
  if(kind==6)return (int)s->mixer->sequence.fault_pc;
  if(kind==10)return dnd_ipl_audio_startup_context((IplAudio *)s);
  return -1;
}
void dnd_ipl_audio_dispose(int token) {
  for(unsigned i=0;i<2;i++)if(sessions[i]&&sessions[i]->token==token) {
    IplAudio *s=sessions[i];sessions[i]=NULL;destroy(s);return;
  }
}
DndIplAudioContext *dnd_ipl_audio_context(int token) {return find(token);}
bool dnd_ipl_audio_render(int token,uint8_t *output,unsigned frames) {
  return dnd_ipl_audio_render_context(find(token),output,frames);
}
bool dnd_ipl_audio_render_context(DndIplAudioContext *s,uint8_t *output,unsigned frames) {
  if(!s||s->state!=1||!output||!frames||frames>320||frames%80)return false;
  for(unsigned at=0;at<frames;at+=80)if(!dnd_ipl_mixer_block(s->mixer,output+at*4)){s->state=-1;return false;}
  return true;
}
bool dnd_ipl_audio_sample(int token,unsigned source,unsigned owner) {
  IplAudio *s=find(token);return s&&s->state==1&&dnd_ipl_mixer_sample(s->mixer,source,owner);
}
bool dnd_ipl_audio_cue(int token,unsigned event,unsigned owner) {
  IplAudio *s=find(token);if(!s||s->state!=1||event<3||event>22||!owner)return false;
  unsigned table=(s->early?0x1b4:0x1bc)+event*3;
  if(table>s->sequence_bytes||3>s->sequence_bytes-table)return false;
  const uint8_t *p=s->packet+352+table;
  unsigned target=(unsigned)p[0]<<16|(unsigned)p[1]<<8|p[2];
  int cue=dnd_ipl_sequence_open(&s->mixer->sequence,target,owner);
  if(cue<0)return false;
  /* Standalone cue roots inherit the verified event-parent gain without
   * competing with the original event parent's control loop. */
  DndIplTrack *t=&s->mixer->sequence.track[cue];
  for(unsigned i=0;i<DND_IPL_TRACKS;i++) {
    DndIplTrack *parent=&s->mixer->sequence.track[i];
    if(parent->active&&parent->identifier==0x20002) {
      t->parent=(int)i;memcpy(t->reg,parent->reg,sizeof(t->reg));break;
    }
  }
  return true;
}
bool dnd_ipl_audio_port(int token,unsigned identifier,unsigned port,unsigned value) {
  IplAudio *s=find(token);return s&&s->state==1&&dnd_ipl_sequence_port(&s->mixer->sequence,identifier,port,value);
}
bool dnd_ipl_audio_owner_playing(int token,unsigned owner) {
  const IplAudio *s=find(token);return s&&s->state==1&&dnd_ipl_mixer_owner_playing(s->mixer,owner);
}
void dnd_ipl_audio_stop_owner(int token,unsigned owner) {
  IplAudio *s=find(token);if(s&&s->state==1)dnd_ipl_mixer_stop_owner(s->mixer,owner);
}

bool dnd_ipl_audio_reset(int token) {
  IplAudio *s=find(token);if(!s||s->state!=1||!dnd_ipl_mixer_reset(s->mixer))return false;
  uint8_t prime[320];return dnd_ipl_audio_render(token,prime,80);
}
bool dnd_ipl_audio_output(int token,int mono,float volume) {
  IplAudio *s=find(token);if(!s||s->state!=1||(mono!=0&&mono!=1)||!(volume>=0&&volume<=1))return false;
  s->mixer->mono=(unsigned)mono;s->mixer->volume=volume;return true;
}

bool dnd_ipl_audio_motion(int token,int submenu,float progress) {
  IplAudio *s=find(token);if(!s||s->state!=1||(submenu!=0&&submenu!=1)||!(progress>=0&&progress<=1))return false;
  float root=sqrtf(progress);
  unsigned gain=submenu?0:(unsigned)((1-root)*32767);
  if(!dnd_ipl_audio_port(token,0x21002,1,gain))return false;
  if(root<=0)return true;
  float interval=fminf(10/root,100);
  float scale=root+(submenu?.15f:.1f);
  unsigned low=(unsigned)(2048*scale*(submenu?3.6f:1));
  unsigned high=(unsigned)(2048*scale*(submenu?3:1.6f));
  if(!dnd_ipl_audio_port(token,0x21003,2,low)||!dnd_ipl_audio_port(token,0x21003,3,high))return false;
  return root<=.6f||dnd_ipl_audio_port(token,0x21003,0,(unsigned)interval&255);
}

int dnd_ipl_audio_startup_context(DndIplAudioContext *s) {
  if(!s||s->state!=1)return 0;
  for(unsigned i=0;i<DND_IPL_VOICES;i++) {
    const DndIplMixVoice *v=&s->mixer->voice[i];
    if(v->active&&!v->raw&&!v->owner&&(v->source<4||v->source==7||v->source==8))return 1;
  }
  return 0;
}
bool dnd_ipl_audio_ambience_context(DndIplAudioContext *s,bool enabled) {
  return s&&s->state==1&&dnd_ipl_sequence_port(&s->mixer->sequence,0x21001,enabled?0:1,enabled?1:0);
}
