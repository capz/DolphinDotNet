#ifndef DND_IPL_AUDIO_H
#define DND_IPL_AUDIO_H
#include "dnd_managed.h"
#include <stdbool.h>
#include <stdint.h>
/* Private native preparation/render contract. The managed facade owns tokens;
 * source addresses, ports and original event numbers are never managed APIs. */
typedef struct DndIplAudioContext DndIplAudioContext;
DndIplAudioContext *dnd_ipl_audio_context(int);
bool dnd_ipl_audio_render_context(DndIplAudioContext *,uint8_t *,unsigned);
int dnd_ipl_audio_startup_context(DndIplAudioContext *);
bool dnd_ipl_audio_ambience_context(DndIplAudioContext *,bool);
int dnd_ipl_audio_begin(DndArray *,int);
int dnd_ipl_audio_step(int);
int dnd_ipl_audio_stat(int,int);
void dnd_ipl_audio_dispose(int);
bool dnd_ipl_audio_render(int,uint8_t *,unsigned);
bool dnd_ipl_audio_sample(int,unsigned,unsigned);
bool dnd_ipl_audio_cue(int,unsigned,unsigned);
bool dnd_ipl_audio_port(int,unsigned,unsigned,unsigned);
bool dnd_ipl_audio_owner_playing(int,unsigned);
void dnd_ipl_audio_stop_owner(int,unsigned);
bool dnd_ipl_audio_reset(int);
bool dnd_ipl_audio_output(int,int,float);
bool dnd_ipl_audio_motion(int,int,float);
#endif
