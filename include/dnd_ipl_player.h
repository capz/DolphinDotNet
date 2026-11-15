#ifndef DND_IPL_PLAYER_H
#define DND_IPL_PLAYER_H
#include "dnd_managed.h"
#include <stdbool.h>
/* A dedicated native producer owns the sequencer/mixer. The DSP callback only
 * copies up to 320 ready frames across chunk boundaries, or supplies silence
 * on a missed deadline. */
int dnd_ipl_player_begin(DndArray *,int);
int dnd_ipl_player_step(int);
int dnd_ipl_player_start(int,int);
int dnd_ipl_player_play(int,int,int);
int dnd_ipl_player_control(int,int,float);
int dnd_ipl_player_stat(int,int);
int dnd_ipl_player_owner(int,int);
void dnd_ipl_player_stop_owner(int,int);
void dnd_ipl_player_stop(int);
void dnd_ipl_player_dispose(int);
void dnd_ipl_player_shutdown(void);
/* Backend lifetime: stop unregisters/quiesces the callback before returning. */
typedef void (*DndIplPull)(void *,uint8_t *,unsigned);
bool dnd_ipl_backend_start(void *,DndIplPull);
void dnd_ipl_backend_stop(void *);
#endif
