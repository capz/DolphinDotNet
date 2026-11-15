#ifndef DND_IPL_MIXER_H
#define DND_IPL_MIXER_H
#include "dnd_ipl_sequence.h"
#define DND_IPL_WAVES 12
#define DND_IPL_VOICES 64
typedef struct {
  const uint8_t *pcm;
  unsigned samples,rate,base_key,loop_start,loop_end;
  bool loop;
} DndIplWave;
typedef struct {
  bool active,raw,released,adsr;
  unsigned source,track,generation,owner,slot,key,velocity;
  uint64_t born,off,gate;
  unsigned envelope[5],release_units,release_mode,routing[6];
  float held,source_gain,tuning,step,left,right,send,pan,fx;
  float current[12],target[12];
  double phase;
} DndIplMixVoice;
typedef struct {
  int16_t *ring;
  unsigned length,position,mode,destination[2];
  int history[8],coefficient[8];
  float left,right;
} DndIplMixEffect;
typedef struct {
  DndIplSequence sequence;
  DndIplWave wave[DND_IPL_WAVES];
  DndIplMixVoice voice[DND_IPL_VOICES];
  DndIplMixEffect effect[4];
  const uint8_t *bank,*coefficients,*oscillators;
  unsigned bank_bytes,fault,early,bytes,mono;
  float volume;
  uint64_t blocks;
} DndIplMixer;
/* All PCM, bank and coefficient data stay native-owned for the mixer lifetime.
 * Initialization allocates bounded delay storage; rendering allocates nothing. */
bool dnd_ipl_mixer_init(DndIplMixer *,const DndIplWave *,const uint8_t *,unsigned,
                       const uint8_t *,unsigned,const uint8_t *,const uint8_t *,
                       bool,unsigned);
void dnd_ipl_mixer_dispose(DndIplMixer *);
bool dnd_ipl_mixer_reset(DndIplMixer *);
bool dnd_ipl_mixer_block(DndIplMixer *,uint8_t *);
bool dnd_ipl_mixer_sample(DndIplMixer *,unsigned,unsigned);
void dnd_ipl_mixer_stop_owner(DndIplMixer *,unsigned);
bool dnd_ipl_mixer_owner_playing(const DndIplMixer *,unsigned);
#endif
