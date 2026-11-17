#ifndef DND_LAUNCH_H
#define DND_LAUNCH_H
#include "dnd_managed.h"
/* Snapshot the CRT-provided incoming argv before application initialization. */
void dnd_launch_capture(int argc,const char *const *argv);
int dnd_launch_count(void);
DndString *dnd_launch_argument(DndManagedHeap *heap,int index);
#endif
