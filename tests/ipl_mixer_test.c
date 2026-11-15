#include "dnd_ipl_mixer.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static void be32(uint8_t *p,unsigned v){p[0]=(uint8_t)(v>>24);p[1]=(uint8_t)(v>>16);p[2]=(uint8_t)(v>>8);p[3]=(uint8_t)v;}
static void be16(uint8_t *p,unsigned v){p[0]=(uint8_t)(v>>8);p[1]=(uint8_t)v;}
static int pcm(const uint8_t *p){return (int16_t)((unsigned)p[0]<<8|p[1]);}
static void fixture(uint8_t *bank,uint8_t *effects,uint8_t *wave,DndIplWave *waves) {
  memset(bank,0,0x8e0);memset(effects,0,128);
  be32(bank+36,100);memcpy(bank+100,"INST",4);be32(bank+108,0x3f800000);be32(bank+112,0x3f800000);
  be32(bank+116,180);be32(bank+140,1);be32(bank+144,200);be32(bank+192,240);
  bank[200]=127;be32(bank+204,1);be32(bank+208,220);bank[220]=127;
  be32(bank+228,0x3f800000);be32(bank+232,0x3f800000);be16(bank+240,1);be16(bank+242,10);
  for(unsigned i=0;i<4;i++){effects[i*32]=2;be32(effects+i*32+12,1);}
  for(unsigned i=0;i<16;i++)be16(wave+i*2,(i+1)*1000);
  for(unsigned i=0;i<12;i++)waves[i]=(DndIplWave){wave,16,32000,60,3,14,true};
}
int main(void) {
  uint8_t bank[0x8e0],effects[128],wave[32],out[320];DndIplWave waves[12];
  fixture(bank,effects,wave,waves);
  DndIplMixer *m=calloc(1,sizeof(*m));assert(m);
  const uint8_t idle[]={0x80,100,0xc8,0,0,0,0};
  unsigned exact=(unsigned)sizeof(*m)+640;
  assert(!dnd_ipl_mixer_init(m,waves,idle,sizeof(idle),bank,sizeof(bank),NULL,effects,true,exact-1));
  assert(dnd_ipl_mixer_init(m,waves,idle,sizeof(idle),bank,sizeof(bank),NULL,effects,true,exact));
  /* No repeat flag or endpoints at playback. The wave descriptor alone makes
   * the intro [0,14) play once, followed by the eleven-sample [3,14) loop. */
  assert(dnd_ipl_mixer_sample(m,0,77));
  unsigned error=0;
  for(unsigned block=0;block<8;block++) {
    assert(dnd_ipl_mixer_block(m,out));
    for(unsigned frame=0;frame<80;frame++) {
      unsigned absolute=block*80+frame;
      double phase=absolute*(double)(32000.0f/32028.5f);unsigned whole=(unsigned)phase;
      unsigned index=whole<14?whole:3+(whole-14)%11,next=index+1<14?index+1:3;
      double value=(index+1)*1000+((double)next-index)*1000*(phase-whole);
      int expected=(int)(value*(block?1:(double)frame/80));
      for(unsigned channel=0;channel<2;channel++) {
        unsigned delta=(unsigned)abs(pcm(out+frame*4+channel*2)-expected);if(delta>error)error=delta;
      }
    }
  }
  assert(error<=1&&dnd_ipl_mixer_owner_playing(m,77));
  dnd_ipl_mixer_stop_owner(m,77);assert(!dnd_ipl_mixer_owner_playing(m,77));
  assert(dnd_ipl_mixer_block(m,out));for(unsigned i=0;i<sizeof(out);i++)assert(!out[i]);
  dnd_ipl_mixer_dispose(m);
  /* A generated four-tap filter has an independent fixed-point oracle.
   * An excessive coefficient norm is rejected before rendering can overflow. */
  uint8_t coefficients[512]={0};
  for(unsigned phase=0;phase<64;phase++)be16(coefficients+phase*8+6,32767);
  assert(dnd_ipl_mixer_init(m,waves,idle,sizeof(idle),bank,sizeof(bank),coefficients,effects,true,exact));
  assert(dnd_ipl_mixer_sample(m,0,79));
  double filtered_position=0;
  for(unsigned block=0;block<4;block++) {
    assert(dnd_ipl_mixer_block(m,out));
    for(unsigned frame=0;frame<80;frame++) {
      int index=(int)filtered_position-1;
      if(index>=14)index=3+(index-14)%11;
      int source=index<0?0:(index+1)*1000;
      int expected=(int)((float)((source*32767)>>15)*(block?1:(float)frame/80));
      assert(pcm(out+frame*4)==expected&&pcm(out+frame*4+2)==expected);
      filtered_position+=(double)(32000.0f/32028.5f);
    }
  }
  dnd_ipl_mixer_dispose(m);
  for(unsigned j=0;j<4;j++)be16(coefficients+j*2,32767);
  assert(!dnd_ipl_mixer_init(m,waves,idle,sizeof(idle),bank,sizeof(bank),coefficients,effects,true,exact));
  /* A source without an embedded loop finishes once under the same Play API. */
  waves[0].loop=false;
  assert(dnd_ipl_mixer_init(m,waves,idle,sizeof(idle),bank,sizeof(bank),NULL,effects,true,exact));
  assert(dnd_ipl_mixer_sample(m,0,78));assert(dnd_ipl_mixer_block(m,out));
  assert(!dnd_ipl_mixer_owner_playing(m,78));dnd_ipl_mixer_dispose(m);waves[0].loop=true;
  /* Mathematical oracle for an authored finite note. It independently folds
   * the source coordinate into the loop, applies constant-power center pan,
   * the early velocity law, DSP ratio quantization and squared release. */
  const uint8_t note[]={0x98,0,64,60,1,127,0x80,2,0x81,0xff};
  assert(dnd_ipl_mixer_init(m,waves,note,sizeof(note),bank,sizeof(bank),NULL,effects,true,exact));
  double position=0,previous=0;error=0;
  const double step=4092.0/4096,gain=.5*(13107.0/16384)/sqrt(2.0);
  for(unsigned block=0;block<20;block++) {
    assert(dnd_ipl_mixer_block(m,out));
    double env=block<3?1:block>=13?0:pow(1-(block-3)/10.0,2),target=gain*env;
    for(unsigned frame=0;frame<80;frame++) {
      int expected=0;
      if(block<13) {
        if(position>=14)position=3+fmod(position-14,11);
        unsigned at=(unsigned)position,next=at+1<14?at+1:3;
        double source=(at+1)*1000+((double)next-at)*1000*(position-at);
        expected=(int)(source*(previous+(target-previous)*frame/80));position+=step;
      }
      for(unsigned channel=0;channel<2;channel++) {
        unsigned delta=(unsigned)abs(pcm(out+frame*4+channel*2)-expected);if(delta>error)error=delta;
      }
    }
    previous=target;
  }
  assert(error<=3);dnd_ipl_mixer_dispose(m);
  /* Slot zero is polyphonic: an authored absolute gate may outlive the
   * score's wait before the next note. Independent timers, not another Play,
   * determine when the preceding note begins releasing. */
  uint8_t overlapping[64]={0xc1,0,0,0,20,0x88,255,255,0xc8,0,0,0,5};
  const uint8_t overlap_child[]={0xd5,0,0xfe,0,40,60,8,100,100,4,64,8,100,100,4,0x80,100,0xff};
  memcpy(overlapping+20,overlap_child,sizeof(overlap_child));
  assert(dnd_ipl_mixer_init(m,waves,overlapping,sizeof(overlapping),bank,sizeof(bank),NULL,effects,true,exact));
  for(unsigned i=0;i<7;i++)assert(dnd_ipl_mixer_block(m,out));
  assert(m->voice[0].active&&m->voice[1].active&&!m->voice[0].released);
  assert(m->voice[0].gate==9&&m->voice[1].gate==15);
  for(unsigned i=0;i<3;i++)assert(dnd_ipl_mixer_block(m,out));
  assert(m->voice[0].released&&!m->voice[1].released);dnd_ipl_mixer_dispose(m);
  const uint8_t absolute[]={0xd5,0,0xfe,0,80,60,8,100,100,4,0x80,100,0xff};
  assert(dnd_ipl_mixer_init(m,waves,absolute,sizeof(absolute),bank,sizeof(bank),NULL,effects,false,exact));
  assert(dnd_ipl_mixer_block(m,out)&&m->voice[0].gate==6);dnd_ipl_mixer_dispose(m);
  /* Closed child channels join the parent's release list. Flush-release
   * removes those tails while preserving an active note on that parent. */
  uint8_t flush[64]={0xc1,0,0,0,32,64,2,127,0x80,3,0xe9,0x80,100,0xff};
  const uint8_t released_child[]={60,1,127,0x80,1,0xff};
  memcpy(flush+32,released_child,sizeof(released_child));
  assert(dnd_ipl_mixer_init(m,waves,flush,sizeof(flush),bank,sizeof(bank),NULL,effects,true,exact));
  for(unsigned i=0;i<3;i++)assert(dnd_ipl_mixer_block(m,out));
  assert(m->voice[0].active&&m->voice[0].released&&m->voice[0].manager==0);
  for(unsigned i=0;i<3;i++)assert(dnd_ipl_mixer_block(m,out));
  assert(!m->voice[0].active&&m->voice[1].active);dnd_ipl_mixer_dispose(m);
  /* A post-filter path returns the delayed input before applying its zero
   * feedback filter. A nonzero delay is audible, rather than discarded. */
  for(unsigned i=0;i<16;i++)be16(wave+i*2,1000);
  be16(effects+64+4,16384);
  const uint8_t wet[]={0xdd,2,0x54,60,1,127,0x80,100,0xff};
  assert(dnd_ipl_mixer_init(m,waves,wet,sizeof(wet),bank,sizeof(bank),NULL,effects,true,exact));
  assert(dnd_ipl_mixer_block(m,out));assert(pcm(out)==0);
  assert(dnd_ipl_mixer_block(m,out));int first=pcm(out);
  assert(dnd_ipl_mixer_block(m,out));int repeated=pcm(out);
  assert(repeated>first&&pcm(out+2)==pcm(out+318));
  dnd_ipl_mixer_dispose(m);
  /* Modern firmware has an enabled post-filter route to auxiliary bus 10,
   * and a disabled fourth path. The auxiliary center is folded equally into front L/R at the output stage. */
  memset(effects+96,0,32);effects[64+3]=10;
  const uint8_t auxiliary[]={0xdd,0,0,0,0xdd,1,0,0,0xdd,2,5,0,60,1,127,0x80,100,0xff};
  assert(dnd_ipl_mixer_init(m,waves,auxiliary,sizeof(auxiliary),bank,sizeof(bank),NULL,effects,false,exact));
  assert(dnd_ipl_mixer_block(m,out));assert(pcm(out)==0);
  assert(dnd_ipl_mixer_block(m,out));assert(dnd_ipl_mixer_block(m,out));
  assert(pcm(out)>0&&pcm(out)==pcm(out+2));
  assert(dnd_ipl_mixer_reset(m));dnd_ipl_mixer_dispose(m);
  /* Modern stereo ambience uses a subtractive auxiliary send. Mono routes
   * that send to front right before equal-channel downmix, preserving the dry
   * source instead of cancelling it. */
  const uint8_t spatial[]={0xdd,0,1,0x50,0xdd,1,9,0x10,0xdd,2,0,0,0xdd,3,0,0,60,1,127,0x80,100,0xff};
  assert(dnd_ipl_mixer_init(m,waves,spatial,sizeof(spatial),bank,sizeof(bank),NULL,effects,false,exact));
  assert(dnd_ipl_mixer_block(m,out)&&dnd_ipl_mixer_block(m,out));
  assert(pcm(out)>0&&pcm(out+2)<0&&abs(pcm(out)+pcm(out+2))<=1);
  m->mono=1;assert(dnd_ipl_mixer_block(m,out)&&dnd_ipl_mixer_block(m,out));
  assert(pcm(out)>400&&pcm(out)==pcm(out+2));dnd_ipl_mixer_dispose(m);
  /* Original generated modulation fixture: a caller-owned envelope traverses
   * both half cycles and loops without replay calls or embedded firmware data. */
  uint8_t oscillators[128]={0};uint8_t *osc=oscillators+64;
  be32(osc+4,0x3f800000);be32(osc+8,0x3e800000);be32(osc+12,0x3f800000);
  be16(osc+20,16384);be16(osc+24,4);be16(osc+26,(unsigned)(uint16_t)-16384);
  be16(osc+30,4);be16(osc+32,16384);be16(osc+34,13);be16(osc+38,1);
  const uint8_t tremolo[]={0xd6,1,0x9c,9,0x20,0,0x9c,10,0x40,0,60,1,127,0x80,100,0xff};
  assert(dnd_ipl_mixer_init(m,waves,tremolo,sizeof(tremolo),bank,sizeof(bank),NULL,effects,true,exact));
  m->oscillators=oscillators;
  for(unsigned block=0;block<40;block++) {
    assert(dnd_ipl_mixer_block(m,out));double phase=fmod(block*.5,8);
    double triangle=phase<4?.5-phase*.25:-.5+(phase-4)*.25;
    double expected=(1+triangle*.25)*(13107.0/16384)/sqrt(2.0);
    assert(fabs(m->voice[0].left-expected)<.000001);
  }
  be16(osc+38,3);assert(!dnd_ipl_mixer_block(m,out)&&m->fault);
  dnd_ipl_mixer_dispose(m);free(m);

  printf("IPL mixer: automatic partial-frame source loops, one-shots, bounded allocation, finite PCM oracle (%u LSB), and post-filter delay passed\n",error);
}
