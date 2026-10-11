#ifndef DND_ARAM_H
#define DND_ARAM_H
#include "dnd_managed.h"
/* Application-thread API; DMA callbacks touch only a native completion flag. */
bool dnd_aram_initialize(int reserved_bytes);
bool dnd_aram_available(void);
int dnd_aram_stat(int kind); /* 0 usable, 1 allocated, 2 free, 3 largest, 4 allocations */
int dnd_aram_allocate(int length); /* 0 means exhausted; tokens never reused */
int dnd_aram_retain(int token);
int dnd_aram_release(int token);
int dnd_aram_dispose(int token);
int dnd_aram_begin(int token,int offset,DndArray *array,int array_offset,int count,int write);
int dnd_aram_poll(DndArray *array,int array_offset);
#ifdef DND_ARAM_TEST
void dnd_aram_test_fail_next(void);
#endif
#endif
