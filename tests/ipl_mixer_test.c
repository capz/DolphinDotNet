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
      unsigned absolute=block*80+frame,index=absolute<14?absolute:3+(absolute-14)%11;
      int expected=(int)((index+1)*1000*(block?1:(double)frame/80));
      for(unsigned channel=0;channel<2;channel++) {
        unsigned delta=(unsigned)abs(pcm(out+frame*4+channel*2)-expected);if(delta>error)error=delta;
      }
    }
  }
  assert(error<=1&&dnd_ipl_mixer_owner_playing(m,77));
  dnd_ipl_mixer_stop_owner(m,77);assert(!dnd_ipl_mixer_owner_playing(m,77));
  assert(dnd_ipl_mixer_block(m,out));for(unsigned i=0;i<sizeof(out);i++)assert(!out[i]);
  dnd_ipl_mixer_dispose(m);
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
   * and a disabled fourth path. Neither becomes an accidental front return. */
  memset(effects+96,0,32);effects[64+3]=10;
  assert(dnd_ipl_mixer_init(m,waves,wet,sizeof(wet),bank,sizeof(bank),NULL,effects,false,exact));
  for(unsigned i=0;i<5;i++){assert(dnd_ipl_mixer_block(m,out));for(unsigned j=0;j<sizeof(out);j++)assert(!out[j]);}
  assert(dnd_ipl_mixer_reset(m));dnd_ipl_mixer_dispose(m);free(m);
  printf("IPL mixer: automatic partial-frame source loops, one-shots, bounded allocation, finite PCM oracle (%u LSB), and post-filter delay passed\n",error);
}
