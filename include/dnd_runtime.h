#ifndef DND_RUNTIME_H
#define DND_RUNTIME_H

#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

typedef enum {
    DND_TYPE_OBJECT = 1,
    DND_TYPE_STRING = 2,
    DND_TYPE_ARRAY = 3
} DndTypeId;

typedef struct {
    uint16_t type_id;
    uint16_t flags;
    uint32_t size;
} DndObjectHeader;

typedef struct {
    DndObjectHeader header;
    uint32_t length;
    char chars[];
} DndString;

typedef struct {
    DndObjectHeader header;
    uint32_t length;
    uint32_t element_size;
    uint8_t data[];
} DndArray;

typedef struct {
    uint8_t *start;
    size_t capacity;
    size_t used;
} DndHeap;

void dnd_heap_init(DndHeap *heap, void *memory, size_t size);
void dnd_heap_reset(DndHeap *heap);
void *dnd_alloc(DndHeap *heap, DndTypeId type, size_t payload_size);
DndString *dnd_string_new(DndHeap *heap, const char *utf8);
DndArray *dnd_array_new(DndHeap *heap, uint32_t length, uint32_t element_size);
void *dnd_array_at(DndArray *array, uint32_t index);

#endif
