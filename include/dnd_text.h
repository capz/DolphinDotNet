#ifndef DND_TEXT_H
#define DND_TEXT_H
#include "dnd_managed.h"
DndArray *dnd_text_encode(DndManagedHeap *,DndString *,int,bool);
DndString *dnd_text_decode(DndManagedHeap *,DndArray *,int,int,int,bool);
DndString *dnd_text_from_chars(DndManagedHeap *,DndArray *,int,int);
#endif
