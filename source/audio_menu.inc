/* Private offline preparation of qualified IPL menu recipes. This produces a
 * normal owned PCM16 stereo Clip; it adds no real-time mixer or BMS API. All
 * data is copied before returning to managed code. Decoder/render slices are
 * bounded so cancellation uses the ordinary AudioStep/AudioDispose contract. */
typedef struct {
  double phase;
  float step, left, right;
} MenuNote;
typedef struct {
  int16_t *ring;
  unsigned delay,position;
  int coefficient[8];
  int history[8];
  unsigned output;
  float gain;
} MenuReverb;
struct MenuPreparation {
  uint8_t *packet, *decoded[2];
  unsigned size, tracks, notes, curves, sources, decoding, position, peak;
  const uint8_t *track, *note, *curve;
  DndAudioSource source[2];
  DndAudioDecoder decoder;
  MenuNote voice[32];
  MenuReverb reverb[2];
  unsigned wet;
  const uint8_t *coefficients;
};
static unsigned menu_u32(const uint8_t *p) {
  return (unsigned)p[0]<<24|(unsigned)p[1]<<16|(unsigned)p[2]<<8|p[3];
}
static int menu_i32(const uint8_t *p) { return (int32_t)menu_u32(p); }
static void audio_menu_free(MenuPreparation *m) {
  if(!m)return;
  for(unsigned i=0;i<2;i++){free(m->source[i].span);free(m->decoded[i]);}
  for(unsigned i=0;i<2;i++)free(m->reverb[i].ring);
  free(m->packet);free(m);
}
static float menu_value(const MenuPreparation *m,unsigned track,unsigned param,float tick) {
  float value=param==3?0.5f:0.0f;
  for(unsigned i=0;i<m->curves;i++) {
    const uint8_t *p=m->curve+i*20;
    if(menu_u32(p)!=track||menu_u32(p+4)!=param)continue;
    float start=(float)menu_u32(p+8),duration=(float)menu_u32(p+12);
    if(tick<start)break;
    float target=(float)menu_i32(p+16)/32768.0f;
    /* Qualified recipes never overlap a parameter's timed updates. */
    if(duration>0&&tick<start+duration)return value+(target-value)*(tick-start)/duration;
    value=target;
  }
  return value;
}
static float menu_attack(const uint8_t *t,float age) {
  if(!menu_u32(t+24))return 1;
  float a=(float)menu_u32(t+8),d=(float)menu_u32(t+12),s=(float)menu_u32(t+16);
  float level=(float)menu_u32(t+20)/32767.0f;
  if(a>0&&age<a)return age/a;
  age-=a+d;
  if(age<=0)return 1;
  if(s>0&&age<s)return 1+(level-1)*age/s;
  return level;
}
static float menu_envelope(const uint8_t *t,float age,float held) {
  if(age<held)return menu_attack(t,age);
  float release=(float)menu_u32(t+28),elapsed=age-held;
  if(release<=0||elapsed>=release)return 0;
  float fraction=elapsed/release,curve=1-fraction;
  /* This early JAudio dialect applies the curve to the interpolated value:
   * mode 1 squares it; mode 2 takes its square root. Later JAS reverses these
   * identities and uses lookup tables, and must not be substituted here. */
  if(menu_u32(t+32)==1)curve*=curve;
  else if(menu_u32(t+32)==2)curve=sqrtf(curve);
  return menu_attack(t,held)*curve;
}
int dnd_audio_begin_menu(DndArray *recipe,int budget) {
  if(!recipe||recipe->elements_are_references||recipe->element_size!=1||
     recipe->length<64||recipe->length>262144||budget<1024||budget>MAX_PCM_BYTES||next_clip==INT_MAX)return 0;
  const uint8_t *p=recipe->data;unsigned size=recipe->length;
  unsigned frames=menu_u32(p+8),tracks=menu_u32(p+12),notes=menu_u32(p+16),curves=menu_u32(p+20),sources=menu_u32(p+48);
  if(memcmp(p,"DME1",4)||menu_u32(p+4)!=size||!frames||frames>256000||!tracks||tracks>3||!notes||notes>32||curves>64||!sources||sources>2)return 0;
  unsigned wet=menu_u32(p+52),tail=menu_u32(p+56),resampling=menu_u32(p+60);
  if(wet>1||tail!=(wet?32000u:0u)||resampling>1)return 0;
  unsigned table_end=64+tracks*36+notes*32+curves*20+wet*64+resampling*512,output=(frames*4+31)&~31u;
  if(table_end>size||sizeof(MenuPreparation)+size+output>(unsigned)budget)return 0;
  dnd_audio_init();Clip *c=NULL;
  for(int i=0;i<CLIPS;i++)if(!clips[i].token){c=&clips[i];break;}
  if(!c)return 0;
  memset(c,0,sizeof(*c));c->token=next_clip++;c->budget=(unsigned)budget;
  MenuPreparation *m=calloc(1,sizeof(*m));c->menu=m;
  if(!m)goto fail;
  m->packet=malloc(size);if(!m->packet)goto fail;
  memcpy(m->packet,p,size);p=m->packet;
  m->size=size;m->tracks=tracks;m->notes=notes;m->curves=curves;m->sources=sources;
  m->track=p+64;m->note=m->track+tracks*36;m->curve=m->note+notes*32;
  m->wet=wet;
  if(resampling)m->coefficients=p+table_end-512;
  unsigned spent=(unsigned)sizeof(*m)+size+output,next=table_end;
  for(unsigned i=0;i<wet*2;i++) {
    const uint8_t *q=m->curve+curves*20+i*32;MenuReverb *r=&m->reverb[i];
    unsigned blocks=menu_u32(q+12),dest=((unsigned)q[2]<<8)|q[3];
    if(q[0]!=1||dest!=0||!blocks||blocks>100||q[6]!=0||q[7]!=1||
       (i==0&&(q[8]||q[9]))||(i==1&&(q[4]||q[5])))goto fail;
    r->delay=blocks*80;r->output=i;r->gain=(float)(((unsigned)q[4+i*4]<<8)|q[5+i*4])/32768;
    unsigned need=r->delay*2;if(need>(unsigned)budget-spent)goto fail;spent+=need;
    r->ring=calloc(r->delay,2);if(!r->ring)goto fail;
    for(unsigned j=0;j<8;j++)r->coefficient[j]=(int16_t)((unsigned)q[16+j*2]<<8|q[17+j*2]);
  }
  for(unsigned i=0;i<sources;i++) {
    unsigned header=menu_u32(p+24+i*12),body=menu_u32(p+28+i*12),bytes=menu_u32(p+32+i*12);
    if(header!=next||header>size||256>size-header||body!=header+256||body>size||!bytes||bytes>size-body)goto fail;
    AudioView h={p+header,256,0},aw={p+body,bytes,0};
    if(!ac_parse(&m->source[i],&h,&aw,7,0,(unsigned)budget-spent))goto fail;
    DndAudioSource *s=&m->source[i];
    if(s->channels!=1||!s->samples||s->samples>100000||s->rate<8000||s->rate>48000)goto fail;
    unsigned need=s->samples*2+s->span_count*(unsigned)sizeof(DndAudioSpan);
    if(need>(unsigned)budget-spent)goto fail;
    spent+=need;
    m->decoded[i]=malloc(s->samples*2);if(!m->decoded[i])goto fail;
    next=body+bytes;
  }
  if(next!=size)goto fail;
  unsigned last=0;
  for(unsigned i=0;i<tracks;i++) {
    const uint8_t *t=m->track+i*36;
    if(menu_u32(t)>32768||menu_u32(t+4)>24||menu_u32(t+8)>6000||menu_u32(t+12)>6000||menu_u32(t+16)>32767||
       menu_u32(t+20)>32767||menu_u32(t+24)>1||menu_u32(t+28)>6000||menu_u32(t+32)>2)goto fail;
  }
  for(unsigned i=0;i<notes;i++) {
    const uint8_t *n=m->note+i*32;unsigned track=menu_u32(n),on=menu_u32(n+16),off=menu_u32(n+20);
    if(track>=tracks||menu_u32(n+4)>=sources||menu_u32(n+8)>127||menu_u32(n+12)>127||on>=off||off>2000||menu_u32(n+24)>127||menu_u32(n+28)>32768)goto fail;
    unsigned release=menu_u32(m->track+track*36+28),end=(unsigned)(((uint64_t)off*64057+47999)/48000+((uint64_t)release*64057+95999)/96000)*80;
    if(end>last)last=end;
  }
  if(last+tail!=frames)goto fail;
  for(unsigned i=0;i<curves;i++) {
    const uint8_t *q=m->curve+i*20;unsigned track=menu_u32(q),param=menu_u32(q+4),tick=menu_u32(q+8),duration=menu_u32(q+12);
    int value=menu_i32(q+16);
    if(track>=tracks||(param!=1&&param!=3)||tick>2000||duration>255||value< -32768||value>32767||(param==3&&value<0))goto fail;
    for(unsigned j=0;j<i;j++) {
      const uint8_t *old=m->curve+j*20;
      if(menu_u32(old)==track&&menu_u32(old+4)==param&&menu_u32(old+8)+menu_u32(old+12)>tick)goto fail;
    }
  }
  c->pcm=audio_memalign(32,output);if(!c->pcm)goto fail;memset(c->pcm,0,output);
  c->length=frames*4;c->rate=32000;c->format=3;m->peak=spent;c->preparation_peak=spent;
  dnd_audio_decoder_init(&m->decoder,&m->source[0],false);
  return c->token;
fail:
  dnd_audio_dispose(c->token);return 0;
}
static int menu_sample(const MenuPreparation *m,unsigned src,int position) {
  const DndAudioSource *s=&m->source[src];if(position<0)return 0;
  unsigned at=(unsigned)position;
  if(s->has_loop&&at>=s->loop_end)at=s->loop_start+(at-s->loop_end)%(s->loop_end-s->loop_start);
  if(at>=s->samples)return 0;
  const uint8_t *p=m->decoded[src]+at*2;return (int16_t)((unsigned)p[0]<<8|p[1]);
}
static int audio_menu_step(Clip *c) {
  MenuPreparation *m=c->menu;
  if(m->decoding<m->sources) {
    DndAudioSource *s=&m->source[m->decoding];unsigned at=m->decoder.state.position;
    unsigned got=dnd_audio_decode(&m->decoder,m->decoded[m->decoding]+at*2,512);
    if(m->decoder.failed||(!got&&at<s->samples))return -1;
    if(m->decoder.state.position==s->samples) {
      m->decoding++;if(m->decoding<m->sources)dnd_audio_decoder_init(&m->decoder,&m->source[m->decoding],false);
    }
    return 0;
  }
  unsigned end=m->position+256,frames=c->length/4;if(end>frames)end=frames;
  const float parent=90.0f/128.0f;
  for(unsigned frame=m->position;frame<end;frame++) {
    unsigned block=frame/80;
    float tick=(float)block*(24000.0f/32028.5f),left=0,right=0,send_left=0,send_right=0;
    for(unsigned i=0;i<m->notes;i++) {
      const uint8_t *n=m->note+i*32;unsigned track=menu_u32(n),src=menu_u32(n+4),on=menu_u32(n+16),off=menu_u32(n+20);
      const uint8_t *t=m->track+track*36;MenuNote *v=&m->voice[i];DndAudioSource *s=&m->source[src];
      unsigned on_block=(unsigned)(((uint64_t)on*64057+47999)/48000),off_block=(unsigned)(((uint64_t)off*64057+47999)/48000);
      unsigned release_blocks=(unsigned)(((uint64_t)menu_u32(t+28)*64057+95999)/96000);
      if(block<on_block||block>=off_block+release_blocks)continue;
      if(frame%80==0||v->step==0) {
        float parameter_tick=floorf(tick),bend=menu_value(m,track,1,parameter_tick)*4*(float)menu_u32(t+4);
        bend=floorf(bend*64)/64;
        float semitones=(float)menu_u32(n+8)-(float)menu_u32(n+24)+bend;
        float step=(float)s->rate/32028.5f*powf(2,semitones/12);
        if(!(step>0&&step<16))return -1;
        v->step=floorf(step*4096)/4096;
        float volume=(float)menu_u32(t)/32768.0f,vel=(float)menu_u32(n+12)/127;
        float env=menu_envelope(t,(float)(block-on_block)*(48000.0f/32028.5f),(float)(off_block-on_block)*(48000.0f/32028.5f));
        float gain=parent*volume*vel*vel*env*(13107.0f/16384.0f);
        float pan=menu_value(m,track,3,parameter_tick);if(pan<0)pan=0;if(pan>1)pan=1;
        v->left=gain*cosf(pan*1.5707963267948966f);v->right=gain*sinf(pan*1.5707963267948966f);
      }
      float sample;
      if(m->coefficients) {
        /* Original DSP 4.12 phase accumulator and its firmware-provided
         * 64-phase four-tap table. At >=4:1 it advances and selects directly.
         * Four initial zero-history samples are part of the DSP contract. */
        if(v->step>=4) {v->phase+=v->step;sample=(float)menu_sample(m,src,(int)v->phase-4);}
        else {
          unsigned phase=(unsigned)((v->phase-(int)v->phase)*4096),which=(phase>>6)*8;
          int64_t sum=0;
          for(unsigned j=0;j<4;j++) {
            int coefficient=(int16_t)((unsigned)m->coefficients[which+j*2]<<8|m->coefficients[which+j*2+1]);
            sum+=(int64_t)coefficient*menu_sample(m,src,(int)v->phase+(int)j-4);
          }
          int value=(int)(sum>>15);if(value>32767)value=32767;if(value< -32768)value= -32768;
          sample=(float)value;v->phase+=v->step;
        }
      } else {
        unsigned stop=s->has_loop?s->loop_end:s->samples;
        if(s->has_loop&&v->phase>=stop)v->phase=s->loop_start+fmod(v->phase-stop,(double)(stop-s->loop_start));
        if(v->phase>=stop)continue;
        unsigned pos=(unsigned)v->phase,next=pos+1;
        if(next>=stop)next=s->has_loop?s->loop_start:pos;
        float a=(float)menu_sample(m,src,(int)pos),b=(float)menu_sample(m,src,(int)next);
        sample=a+(b-a)*(float)(v->phase-pos);v->phase+=v->step;
      }
      left+=sample*v->left;right+=sample*v->right;
      if(m->wet) {
        float send=sinf((float)menu_u32(n+28)/32768.0f*1.5707963267948966f);
        send_left+=sample*v->left*send;send_right+=sample*v->right*send;
      }
    }
    for(unsigned i=0;i<m->wet*2;i++) {
      MenuReverb *r=&m->reverb[i];int value=r->ring[r->position];int64_t sum=0;
      /* Causal 8-tap FIR: the firmware addresses the previous eight samples,
       * with its final tap one sample behind the current delay read. */
      for(unsigned j=0;j<8;j++)sum+=(int64_t)r->history[j]*r->coefficient[j];
      for(unsigned j=0;j<7;j++)r->history[j]=r->history[j+1];
      r->history[7]=value;
      int filtered=(int)(sum>>15);if(filtered>32767)filtered=32767;if(filtered< -32768)filtered= -32768;
      float input=i==0?send_left:send_right;
      int feedback=filtered+(int)input;if(feedback>32767)feedback=32767;if(feedback< -32768)feedback= -32768;
      r->ring[r->position]=(int16_t)feedback;r->position=(r->position+1)%r->delay;
      if(i==0)left+=(float)filtered*r->gain;else right+=(float)filtered*r->gain;
    }
    int l=(int)left,r=(int)right;if(l>32767)l=32767;if(l< -32768)l= -32768;if(r>32767)r=32767;if(r< -32768)r= -32768;
    c->pcm[frame*4]=(uint8_t)(l>>8);c->pcm[frame*4+1]=(uint8_t)l;c->pcm[frame*4+2]=(uint8_t)(r>>8);c->pcm[frame*4+3]=(uint8_t)r;
  }
  m->position=end;
  if(end<frames)return 0;
  audio_menu_free(m);c->menu=NULL;c->ready=1;return 1;
}
