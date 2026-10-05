#include "dnd_bcl.h"
#include <string.h>

bool dnd_list_init(DndList *list, DndManagedHeap *heap, uint32_t element_size, uint32_t capacity) {
    if (!list || !heap || !element_size) return false;
    if (capacity == 0) capacity = 4;
    list->heap = heap; list->count = 0; list->element_size = element_size;
    list->items = dnd_managed_array_new(heap, capacity, element_size);
    return list->items != NULL;
}

static bool grow(DndList *list) {
    uint32_t old_capacity = list->items->length;
    uint32_t next = old_capacity < UINT32_MAX / 2 ? old_capacity * 2 : UINT32_MAX;
    if (next <= old_capacity) return false;
    DndArray *replacement = dnd_managed_array_new(list->heap, next, list->element_size);
    if (!replacement) return false;
    memcpy(replacement->data, list->items->data, (size_t)list->count * list->element_size);
    list->items = replacement; return true;
}

bool dnd_list_add(DndList *list, const void *item) {
    if (!list || !item) return false;
    if (list->count == list->items->length && !grow(list)) return false;
    memcpy(list->items->data + (size_t)list->count * list->element_size, item, list->element_size);
    list->count++; return true;
}

void *dnd_list_get(DndList *list, uint32_t index) {
    if (!list || index >= list->count) {
        dnd_exception_throw(DND_EXCEPTION_INDEX_OUT_OF_RANGE, "List index out of range.");
        return NULL;
    }
    return list->items->data + (size_t)index * list->element_size;
}
uint32_t dnd_list_count(const DndList *list) { return list ? list->count : 0; }

void dnd_random_init(DndRandom *r, int32_t seed) { r->state = seed ? (uint32_t)seed : 0x6d2b79f5u; }
int32_t dnd_random_next(DndRandom *r) {
    uint32_t x = r->state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; r->state = x;
    return (int32_t)(x & 0x7fffffffu);
}
int32_t dnd_random_next_max(DndRandom *r, int32_t max) {
    if (max <= 0) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "maxExclusive must be positive."); return 0; }
    return dnd_random_next(r) % max;
}
int32_t dnd_math_abs_i32(int32_t v) { return v < 0 && v != INT32_MIN ? -v : v; }
int32_t dnd_math_min_i32(int32_t a, int32_t b) { return a < b ? a : b; }
int32_t dnd_math_max_i32(int32_t a, int32_t b) { return a > b ? a : b; }
