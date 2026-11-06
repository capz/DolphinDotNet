#ifndef DND_CARD_MANAGER_H
#define DND_CARD_MANAGER_H
#include "dnd_managed.h"
int dnd_cmgr_open(int slot);
int dnd_cmgr_refresh(int token);
int dnd_cmgr_stat(int token,int field);
int dnd_cmgr_entry(int token,int revision,int index,DndArray *metadata);
int dnd_cmgr_icon(int token,int revision,int index,int frame,DndArray *rgba);
int dnd_cmgr_delete(int token,int revision,int index);
int dnd_cmgr_format(int token);
int dnd_cmgr_close(int token);
int dnd_cmgr_copy_begin(int source,int revision,int index,int destination,int move);
int dnd_cmgr_copy_step(int token);
int dnd_cmgr_copy_abort(int token);
bool dnd_cmgr_active(int slot);
void dnd_cmgr_shutdown(void);
#endif
