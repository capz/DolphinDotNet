#ifndef DND_BCL_H
#define DND_BCL_H
#include "dnd_managed.h"
#include <stdint.h>

typedef struct {
    DndManagedHeap *heap;
    DndArray *items;
    uint32_t count;
    uint32_t element_size;
} DndList;

typedef struct {
    uint32_t state;
} DndRandom;

bool dnd_list_init(DndList *list, DndManagedHeap *heap, uint32_t element_size, uint32_t capacity);
bool dnd_list_add(DndList *list, const void *item);
void *dnd_list_get(DndList *list, uint32_t index);
uint32_t dnd_list_count(const DndList *list);

void dnd_random_init(DndRandom *random, int32_t seed);
int32_t dnd_random_next(DndRandom *random);
int32_t dnd_random_next_max(DndRandom *random, int32_t max_exclusive);

int32_t dnd_math_abs_i32(int32_t value);
int32_t dnd_math_min_i32(int32_t a, int32_t b);
int32_t dnd_math_max_i32(int32_t a, int32_t b);
#endif
