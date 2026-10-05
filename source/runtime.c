#include "dnd_runtime.h"
#include <string.h>

static size_t align8(size_t value) {
    return (value + 7u) & ~(size_t)7u;
}

void dnd_heap_init(DndHeap *heap, void *memory, size_t size) {
    heap->start = (uint8_t *)memory;
    heap->capacity = size;
    heap->used = 0;
}

void dnd_heap_reset(DndHeap *heap) {
    heap->used = 0;
}

void *dnd_alloc(DndHeap *heap, DndTypeId type, size_t payload_size) {
    size_t total = align8(sizeof(DndObjectHeader) + payload_size);
    if (!heap || total > heap->capacity - heap->used) {
        return NULL;
    }

    DndObjectHeader *object = (DndObjectHeader *)(heap->start + heap->used);
    heap->used += total;
    object->type_id = (uint16_t)type;
    object->flags = 0;
    object->size = (uint32_t)total;

    memset((uint8_t *)object + sizeof(DndObjectHeader), 0,
           total - sizeof(DndObjectHeader));
    return object;
}

DndString *dnd_string_new(DndHeap *heap, const char *utf8) {
    if (!utf8) return NULL;
    size_t length = strlen(utf8);
    DndString *str = (DndString *)dnd_alloc(
        heap, DND_TYPE_STRING, sizeof(uint32_t) + length + 1);
    if (!str) return NULL;
    str->length = (uint32_t)length;
    memcpy(str->chars, utf8, length + 1);
    return str;
}

DndArray *dnd_array_new(DndHeap *heap, uint32_t length, uint32_t element_size) {
    if (element_size != 0 && length > (UINT32_MAX / element_size)) return NULL;
    size_t bytes = (size_t)length * element_size;
    DndArray *array = (DndArray *)dnd_alloc(
        heap, DND_TYPE_ARRAY, sizeof(uint32_t) * 2 + bytes);
    if (!array) return NULL;
    array->length = length;
    array->element_size = element_size;
    return array;
}

void *dnd_array_at(DndArray *array, uint32_t index) {
    if (!array || index >= array->length) return NULL;
    return array->data + ((size_t)index * array->element_size);
}
