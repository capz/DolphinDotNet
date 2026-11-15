#include "dnd_ipl_mixer.h"
#include <limits.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

static unsigned u32(const uint8_t *p) {return (unsigned)p[0]<<24|(unsigned)p[1]<<16|(unsigned)p[2]<<8|p[3];}
static unsigned u16(const uint8_t *p) {return (unsigned)p[0]<<8|p[1];}
static float f32(const uint8_t *p) {unsigned bits=u32(p);float v;memcpy(&v,&bits,4);return v;}
static bool range(DndIplMixer *m,unsigned at,unsigned bytes) {
  if(at>m->bank_bytes||bytes>m->bank_bytes-at){m->fault=1;return false;}return true;
}
static int saturate(int v) {return v>32767?32767:v< -32768?-32768:v;}
static float attack(const DndIplMixVoice *v,float age) {
  if(!v->adsr)return 1;
  float a=(float)v->envelope[0],d=(float)v->envelope[1],s=(float)v->envelope[2];
  float sustain=(float)v->envelope[3]/32767;
  if(a>0&&age<a)return age/a;
  age-=a+d;
  if(age<=0)return 1;
  if(s>0&&age<s)return 1+(sustain-1)*age/s;
  return sustain;
}
static float envelope(const DndIplMixer *m,const DndIplMixVoice *v) {
  float clock=m->early?1:48000.0f/32028.5f;
  if(!v->released)return attack(v,(float)(m->blocks-v->born)*clock);
  float elapsed=(float)(m->blocks-v->off)*clock;
  if(!v->release_units||elapsed>=(float)v->release_units)return 0;
  float remain=1-elapsed/(float)v->release_units;
  if(v->release_mode==1)remain*=remain;
  else if(v->release_mode==2)remain=sqrtf(remain);
  return v->held*remain;
}
static void release(DndIplMixer *m,DndIplMixVoice *v) {
  if(!v->active||v->released||v->raw)return;
  v->held=envelope(m,v);v->off=m->blocks;v->released=true;
}
static bool instrument(DndIplMixer *m,DndIplMixVoice *v,const DndIplTrack *t) {
  unsigned program=t->reg[6];
  if(program>9||program==4||!range(m,36+program*4,4))return false;
  unsigned inst=u32(m->bank+36+program*4);
  if(!range(m,inst,48)||memcmp(m->bank+inst,"INST",4))return false;
  unsigned count=u32(m->bank+inst+40);
  if(!count||count>2||!range(m,inst+44,count*4))return false;
  unsigned key=0;
  for(unsigned i=0;i<count;i++) {
    unsigned at=u32(m->bank+inst+44+i*4);
    if(!range(m,at,12))return false;
    if(v->key<=m->bank[at]){key=at;break;}
  }
  if(!key||u32(m->bank+key+4)!=1)return false;
  unsigned vel=u32(m->bank+key+8);
  if(!range(m,vel,16)||v->velocity>m->bank[vel])return false;
  v->source=u32(m->bank+vel+4);
  if(v->source>=DND_IPL_WAVES)return false;
  v->source_gain=f32(m->bank+inst+8)*f32(m->bank+vel+8);
  v->tuning=f32(m->bank+inst+12)*f32(m->bank+vel+12);
  if(!isfinite(v->source_gain)||v->source_gain<0||v->source_gain>4||
     !isfinite(v->tuning)||v->tuning<=0||v->tuning>4)return false;
  if(t->has_adsr||t->release_override) {
    v->adsr=t->has_adsr;memcpy(v->envelope,t->adsr,sizeof(v->envelope));
    v->release_units=t->adsr[4]&16383;v->release_mode=t->adsr[4]>>14;
  } else {
    unsigned oscillator=u32(m->bank+inst+16);
    if(!range(m,oscillator,16))return false;
    unsigned rel=u32(m->bank+oscillator+12);
    if(!range(m,rel,6))return false;
    v->release_mode=u16(m->bank+rel);v->release_units=u16(m->bank+rel+2);
  }
  if(!m->early&&t->oscillator_enabled&&t->oscillator_slot==0&&t->simple_oscillator==1) {
    v->adsr=false;v->release_units=16;v->release_mode=0;
  }
  return v->release_units<=6000&&v->release_mode<=2;
}
static void sequence_event(void *context,const DndIplEvent *e) {
  DndIplMixer *m=context;
  if(e->kind==DND_IPL_NOTE_OFF||e->kind==DND_IPL_TRACK_CLOSED) {
    for(unsigned i=0;i<DND_IPL_VOICES;i++) {
      DndIplMixVoice *v=&m->voice[i];
      if(v->active&&!v->raw&&v->track==e->track&&v->generation==e->generation&&
         (e->kind==DND_IPL_TRACK_CLOSED||v->slot==e->slot))release(m,v);
      if(e->kind==DND_IPL_TRACK_CLOSED&&v->active&&!v->raw&&v->manager==e->track&&v->manager_generation==e->generation) {
        int parent=m->sequence.track[e->track].parent;
        v->manager=parent>=0?(unsigned)parent:UINT_MAX;
        v->manager_generation=parent>=0?m->sequence.track[parent].generation:0;
      }
    }
    return;
  }
  if(e->kind==DND_IPL_TRACK_UPDATED&&e->release) {
    for(unsigned i=0;i<DND_IPL_VOICES;i++) {
      DndIplMixVoice *v=&m->voice[i];
      if(v->active&&!v->raw&&v->manager==e->track&&v->manager_generation==e->generation&&
          (v->released||e->release==2))v->active=false;
    }
    return;
  }
  if(e->kind!=DND_IPL_NOTE_ON)return;
  unsigned at=0;
  for(;at<DND_IPL_VOICES;at++)if(!m->voice[at].active)break;
  if(at==DND_IPL_VOICES){m->fault=1;return;}
  for(unsigned i=0;i<DND_IPL_VOICES;i++) {
    DndIplMixVoice *v=&m->voice[i];
    if(v->active&&!v->raw&&v->track==e->track&&v->generation==e->generation&&e->slot!=0&&v->slot==e->slot)release(m,v);
  }
  DndIplMixVoice *v=&m->voice[at];memset(v,0,sizeof(*v));
  v->manager=e->track;v->manager_generation=e->generation;
  v->track=e->track;v->generation=e->generation;v->owner=e->owner;v->slot=e->slot;
  v->key=e->key;v->velocity=e->velocity;v->born=m->blocks;v->gate=UINT64_MAX;
  const DndIplTrack *t=&m->sequence.track[e->track];
  memcpy(v->routing,t->mix,sizeof(v->routing));
  if(!instrument(m,v,t)){m->fault=1;return;}
  if(e->duration!=UINT_MAX) {
    double length=(double)e->duration*(double)e->gate_percent/100;
    if(t->time_relative)length/=dnd_ipl_sequence_tick_rate(&m->sequence,e->track);
    else length*=120.0/(double)t->timebase*(m->early?.8:1);
    if(length<0||length>1000000){m->fault=1;return;}
    v->gate=m->blocks+(uint64_t)length;
  }
  v->active=true;
}
bool dnd_ipl_mixer_init(DndIplMixer *m,const DndIplWave *waves,const uint8_t *sequence,unsigned sequence_bytes,
                       const uint8_t *bank,unsigned bank_bytes,const uint8_t *coefficients,const uint8_t *effects,
                       bool early,unsigned budget) {
  if(!m||!waves||!sequence||!bank||bank_bytes!=0x8e0||!effects||budget<sizeof(*m))return false;
  memset(m,0,sizeof(*m));memcpy(m->wave,waves,sizeof(m->wave));
  m->bank=bank;m->bank_bytes=bank_bytes;m->coefficients=coefficients;m->early=early;m->volume=1;
  m->bytes=(unsigned)sizeof(*m);
  if(coefficients)for(unsigned phase=0;phase<64;phase++) {
    unsigned total=0;
    for(unsigned j=0;j<4;j++) {
      int c=(int16_t)u16(coefficients+phase*8+j*2);
      m->resampler[phase][j]=(int16_t)c;total+=(unsigned)abs(c);
    }
    /* With signed 16-bit PCM this bound makes the four-tap MAC fit int32. */
    if(total>65535)goto fail;
  }
  for(unsigned i=0;i<DND_IPL_WAVES;i++) {
    const DndIplWave *w=&m->wave[i];
    if(!w->pcm||!w->samples||w->samples>200000||w->rate<5000||w->rate>48000||w->base_key>127||
       (w->loop&&(w->loop_start>=w->loop_end||w->loop_end>w->samples)))goto fail;
  }
  for(unsigned i=0;i<4;i++) {
    const uint8_t *p=effects+i*32;DndIplMixEffect *e=&m->effect[i];
    unsigned blocks=u32(p+12);e->mode=p[0];
    if(e->mode>2||blocks>100||(!blocks&&e->mode))goto fail;
    e->destination[0]=u16(p+2);e->destination[1]=u16(p+6);
    if(e->destination[0]>11||e->destination[1]>11)goto fail;
    if(!e->mode)continue;
    e->length=blocks*80;
    if(e->length*2>budget-m->bytes)goto fail;
    e->ring=calloc(e->length,2);if(!e->ring)goto fail;m->bytes+=e->length*2;
    e->left=(float)u16(p+4)/32768;e->right=(float)u16(p+8)/32768;
    for(unsigned tap=0;tap<8;tap++)e->coefficient[tap]=(int16_t)u16(p+16+tap*2);
  }
  if(!dnd_ipl_sequence_init(&m->sequence,sequence,sequence_bytes,sequence_event,m))goto fail;
  m->sequence.tick_divisor=early?25200:24021.375f;dnd_ipl_sequence_modern(&m->sequence,!early);
  return true;
fail:
  dnd_ipl_mixer_dispose(m);return false;
}
void dnd_ipl_mixer_dispose(DndIplMixer *m) {
  if(!m)return;
  for(unsigned i=0;i<4;i++){free(m->effect[i].ring);m->effect[i].ring=NULL;}
  m->bytes=0;
}
bool dnd_ipl_mixer_reset(DndIplMixer *m) {
  if(!m)return false;
  const uint8_t *sequence=m->sequence.sequence;unsigned bytes=m->sequence.length;
  memset(m->voice,0,sizeof(m->voice));m->fault=0;m->blocks=0;
  for(unsigned i=0;i<4;i++) {
    DndIplMixEffect *e=&m->effect[i];
    if(e->length)memset(e->ring,0,e->length*2);
    memset(e->history,0,sizeof(e->history));e->position=0;
  }
  bool ok=dnd_ipl_sequence_init(&m->sequence,sequence,bytes,sequence_event,m);
  m->sequence.tick_divisor=m->early?25200:24021.375f;dnd_ipl_sequence_modern(&m->sequence,!m->early);return ok;
}
static int source_sample(const DndIplWave *w,int at) {
  if(at<0)return 0;
  unsigned index=(unsigned)at;
  if(w->loop&&index>=w->loop_end)index=w->loop_start+(index-w->loop_end)%(w->loop_end-w->loop_start);
  if(index>=w->samples)return 0;
  return (int16_t)u16(w->pcm+index*2);
}
static float sample(DndIplMixer *m,DndIplMixVoice *v) {
  const DndIplWave *w=&m->wave[v->source];
  if(w->loop&&v->phase>=(double)w->loop_end+4)
    v->phase=w->loop_start+4+fmod(v->phase-w->loop_end-4,(double)(w->loop_end-w->loop_start));
  if(!w->loop&&v->phase>=(double)w->samples+4){v->active=false;return 0;}
  float result;
  if(m->coefficients) {
    if(v->step>=4){v->phase+=v->step;result=(float)source_sample(w,(int)v->phase-4);}
    else {
      unsigned phase=(unsigned)((v->phase-(int)v->phase)*4096),which=phase>>6;
      int32_t sum=0;
      for(unsigned j=0;j<4;j++)sum+=(int32_t)m->resampler[which][j]*source_sample(w,(int)v->phase+(int)j-4);
      result=(float)saturate((int)(sum>>15));v->phase+=v->step;
    }
  } else {
    int index=(int)v->phase;float a=(float)source_sample(w,index),b=(float)source_sample(w,index+1);
    result=a+(b-a)*(float)(v->phase-index);v->phase+=v->step;
  }
  return result;
}
/* Bounded linear/curved envelope traversal. The preset and loop targets are
 * read from the caller-owned firmware snapshot, never compiled-in tables. */
static float oscillator_value(DndIplMixer *m,DndIplTrack *t,unsigned *target) {
  *target=0;
  if(!m->oscillators||!t->oscillator_enabled)return 1;
  const uint8_t *p=m->oscillators+t->simple_oscillator*64;
  *target=u32(p);if(*target>4){m->fault=1;return 1;}
  unsigned base=t->oscillator_slot?9:6;
  float rate=(t->parameter_written&(1u<<(base+1)))?t->parameter[base+1].value:f32(p+4);
  float depth=(t->parameter_written&(1u<<base))?t->parameter[base].value:f32(p+8);
  float bias=(t->parameter_written&(1u<<(base+2)))?t->parameter[base+2].value:f32(p+12);
  if(!isfinite(rate)||!isfinite(depth)||!isfinite(bias)||rate<0||rate>16){m->fault=1;return 1;}
  float time=t->oscillator_time,previous=0;
  for(unsigned i=0,work=0;i<6&&work<20;work++) {
    const uint8_t *record=p+16+i*6;unsigned mode=u16(record),duration=u16(record+2);
    float value=(float)(int16_t)u16(record+4)/32768;
    if(mode==13) {
      unsigned loop=u16(record+4);float period=0;
      if(loop>=i){m->fault=1;return 1;}
      for(unsigned j=loop;j<i;j++)period+=(float)u16(p+16+j*6+2);
      if(period<=0){m->fault=1;return 1;}
      time=fmodf(time,period);i=loop;continue;
    }
    if(mode==14||mode==15)return previous*depth+bias;
    if(mode>2){m->fault=1;return 1;}
    if(duration&&time<(float)duration) {
      float fraction=time/(float)duration;
      if(mode==1)fraction*=fraction;else if(mode==2)fraction=sqrtf(fraction);
      return (previous+(value-previous)*fraction)*depth+bias;
    }
    time-=(float)duration;previous=value;i++;
  }
  m->fault=1;return 1;
}
static void update_voice(DndIplMixer *m,DndIplMixVoice *v) {
  const DndIplWave *w=&m->wave[v->source];
  if(v->raw) {
    v->step=(float)w->rate/32028.5f;v->target[0]=v->target[1]=1;return;
  }
  if(!v->released&&m->blocks>=v->gate)release(m,v);
  float env=envelope(m,v);if(env<=0&&v->released){v->active=false;return;}
  DndIplTrack *t=&m->sequence.track[v->track];
  if(t->generation==v->generation&&t->active) {
    float volume=t->parameter[0].value,pitch=t->parameter[1].value*(float)t->reg[7]*4,fx=t->parameter[2].value;
    float pan=t->parameter[3].value;
    unsigned oscillator_target;float modulation=oscillator_value(m,t,&oscillator_target),pitch_scale=1;
    if(oscillator_target==0)volume*=modulation;
    else if(oscillator_target==1)pitch_scale*=modulation;
    else if(oscillator_target==2)pan*=modulation;
    else if(oscillator_target==3)fx*=modulation;
    for(int parent=t->independent?-1:t->parent;parent>=0;) {
      DndIplTrack *p=&m->sequence.track[parent];unsigned target;float mod=oscillator_value(m,p,&target);
      volume*=p->parameter[0].value*(target==0?mod:1);
      if(target==1)pitch_scale*=mod;
      fx+=p->parameter[2].value;
      pitch+=p->parameter[1].value*(float)p->reg[7]*4;parent=p->independent?-1:p->parent;
    }
    pitch=floorf(pitch*64)/64;
    if(v->step==0||pitch!=v->last_pitch||pitch_scale!=v->last_pitch_scale) {
      float ratio=(float)w->rate/32028.5f*v->tuning*pitch_scale*powf(2,((float)v->key-(float)w->base_key+pitch)/12);
      if(!(ratio>0&&ratio<16)){m->fault=1;return;}
      v->step=floorf(ratio*4096)/4096;v->last_pitch=pitch;v->last_pitch_scale=pitch_scale;
    }
    if(!isfinite(volume)){m->fault=1;return;}
    if(pan<0)pan=0;
    if(pan>1)pan=1;
    float velocity=(float)v->velocity/127;
    float gain=volume*velocity*(m->early?1:velocity)*v->source_gain*(13107.0f/16384.0f);
    if((v->pan_left==0&&v->pan_right==0)||pan!=v->pan) {
      v->pan_left=cosf(pan*1.5707963267948966f);v->pan_right=sinf(pan*1.5707963267948966f);
    }
    v->left=gain*v->pan_left;v->right=gain*v->pan_right;
    if(fx<0)fx=0;
    if(fx>1)fx=1;
    if(v->fx!=fx)v->send=sinf(fx*(m->early?.125f:.25f)*1.5707963267948966f);
    v->pan=pan;v->fx=fx;
    memcpy(v->routing,t->mix,sizeof(v->routing));
  }
  memset(v->target,0,sizeof(v->target));
  float modern_gain=m->early?0:sqrtf(v->left*v->left+v->right*v->right);
  for(unsigned i=0;i<6;i++) {
    unsigned route=v->routing[i];
    if(route&0x1000000u) {
      unsigned destination=(route>>8)&255;
      if(!destination)continue;
      if(destination>11||destination==10){m->fault=1;return;}
      destination=destination==9?(m->mono?1u:11u):destination==11?10:destination-1;
      float gain=modern_gain;
      for(unsigned shift=0;shift<=4;shift+=4) {
        unsigned mode=(route>>shift)&15;float control;
        if(!mode)continue;
        if(mode==1||mode==5)control=v->pan;
        else if(mode==2||mode==6)control=v->fx*.25f;
        else {m->fault=1;return;}
        if(mode>=5)control=1-control;
        if(control<0)control=0;
        if(control>1)control=1;
        gain*=sinf(control*1.5707963267948966f);
      }
      v->target[destination]+=gain*env;continue;
    }
    unsigned destination=route>>4;if(!destination)continue;
    if(destination>12){m->fault=1;return;}
    float gain=(i&1)?v->right:v->left;
    if((route&15)==5)gain*=v->send;
    v->target[destination-1]+=gain*env;
  }
}
bool dnd_ipl_mixer_block(DndIplMixer *m,uint8_t *out) {
  if(!m||!out||m->fault||!dnd_ipl_sequence_block(&m->sequence))return false;
  for(unsigned i=0;i<DND_IPL_VOICES;i++)if(m->voice[i].active)update_voice(m,&m->voice[i]);
  if(m->fault)return false;
  /* Mix each live voice once across the block. Keep the original summation
   * order, but avoid scanning inactive voices and unused buses per sample. */
  float buses[80][12]={0};
  for(unsigned i=0;i<DND_IPL_VOICES;i++) {
    DndIplMixVoice *v=&m->voice[i];if(!v->active)continue;
    unsigned destinations[12],count=0;
    for(unsigned b=0;b<12;b++)if(v->current[b]!=0||v->target[b]!=0)destinations[count++]=b;
    for(unsigned frame=0;frame<80&&v->active;frame++) {
      float value=sample(m,v);
      for(unsigned j=0;j<count;j++) {
        unsigned b=destinations[j];
        buses[frame][b]+=value*(v->current[b]+(v->target[b]-v->current[b])*(float)frame/80);
      }
    }
  }
  for(unsigned frame=0;frame<80;frame++) {
    float *bus=buses[frame];
    for(unsigned i=0;i<4;i++) {
      DndIplMixEffect *e=&m->effect[i];if(!e->mode)continue;int delayed=e->ring[e->position];int64_t sum=0;
      for(unsigned j=0;j<8;j++)sum+=(int64_t)e->history[j]*e->coefficient[j];
      for(unsigned j=0;j<7;j++)e->history[j]=e->history[j+1];
      e->history[7]=delayed;
      int filtered=saturate((int)(sum>>15)),returned=e->mode==1?filtered:delayed;
      e->ring[e->position]=(int16_t)saturate(filtered+(int)bus[i+2]);
      e->position=(e->position+1)%e->length;
      bus[e->destination[0]]+=(float)returned*e->left;bus[e->destination[1]]+=(float)returned*e->right;
    }
    if(!m->early){bus[0]+=bus[10]*(23170.0f/32768);bus[1]+=bus[10]*(23170.0f/32768)-bus[11];}
    if(m->mono)bus[0]=bus[1]=(bus[0]+bus[1])*.5f;
    int left=saturate((int)(bus[0]*m->volume)),right=saturate((int)(bus[1]*m->volume));
    out[frame*4]=(uint8_t)(left>>8);out[frame*4+1]=(uint8_t)left;
    out[frame*4+2]=(uint8_t)(right>>8);out[frame*4+3]=(uint8_t)right;
  }
  for(unsigned i=0;i<DND_IPL_VOICES;i++)if(m->voice[i].active)memcpy(m->voice[i].current,m->voice[i].target,sizeof(m->voice[i].target));
  for(unsigned i=0;i<DND_IPL_TRACKS;i++) {
    DndIplTrack *t=&m->sequence.track[i];
    if(t->active&&t->oscillator_enabled&&m->oscillators) {
      const uint8_t *p=m->oscillators+t->simple_oscillator*64;
      unsigned base=t->oscillator_slot?9:6;
  float rate=(t->parameter_written&(1u<<(base+1)))?t->parameter[base+1].value:f32(p+4);
      t->oscillator_time+=rate*(m->early?1:48000.0f/32028.5f);
      for(unsigned at=0;at<6;at++)if(u16(p+16+at*6)==13) {
        unsigned loop=u16(p+16+at*6+4);float prefix=0,period=0;
        if(loop>=at){m->fault=1;break;}
        for(unsigned j=0;j<at;j++) {
          float duration=(float)u16(p+16+j*6+2);
          if(j<loop)prefix+=duration;else period+=duration;
        }
        if(period<=0){m->fault=1;break;}
        if(t->oscillator_time>=prefix+period)t->oscillator_time=prefix+fmodf(t->oscillator_time-prefix,period);
        break;
      }
    }
  }
  m->blocks++;return !m->fault;
}
bool dnd_ipl_mixer_sample(DndIplMixer *m,unsigned source,unsigned owner) {
  if(!m||m->fault||source>=DND_IPL_WAVES||!owner)return false;
  for(unsigned i=0;i<DND_IPL_VOICES;i++)if(!m->voice[i].active) {
    DndIplMixVoice *v=&m->voice[i];memset(v,0,sizeof(*v));v->raw=true;v->source=source;v->owner=owner;v->active=true;return true;
  }
  return false;
}
void dnd_ipl_mixer_stop_owner(DndIplMixer *m,unsigned owner) {
  if(!m||!owner)return;
  dnd_ipl_sequence_stop_owner(&m->sequence,owner);
  for(unsigned i=0;i<DND_IPL_VOICES;i++)if(m->voice[i].owner==owner)m->voice[i].active=false;
}
bool dnd_ipl_mixer_owner_playing(const DndIplMixer *m,unsigned owner) {
  if(!m||!owner)return false;
  for(unsigned i=0;i<DND_IPL_VOICES;i++)if(m->voice[i].active&&m->voice[i].owner==owner)return true;
  for(unsigned i=0;i<DND_IPL_TRACKS;i++)if(m->sequence.track[i].active&&m->sequence.track[i].owner==owner)return true;
  return false;
}
