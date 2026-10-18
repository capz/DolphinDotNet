#ifndef DND_TEXT_H
#define DND_TEXT_H
#include "dnd_managed.h"
DndArray *dnd_text_encode(DndManagedHeap *, DndString *, int, bool);
DndString *dnd_text_decode(DndManagedHeap *, DndArray *, int, int, int, bool);
DndString *dnd_text_from_chars(DndManagedHeap *, DndArray *, int, int);
int dnd_text_compare(DndString *, DndString *, bool);
int dnd_text_hash(DndString *, bool);
bool dnd_text_range_equal(DndString *,int,DndString *,int,int,bool);
DndString *dnd_text_case(DndManagedHeap *,DndString *,bool);
int dnd_text_array_length(DndObject *);
DndObject *dnd_text_array_item(DndManagedHeap *,DndObject *,int);
DndString *dnd_text_from_array(DndManagedHeap *,DndArray *);
#endif
