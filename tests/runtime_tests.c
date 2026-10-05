#include "dnd_runtime.h"
#include <assert.h>
#include <stdint.h>
#include <string.h>

int main(void) {
    uint8_t memory[1024];
    DndHeap heap;
    dnd_heap_init(&heap, memory, sizeof(memory));

    DndString *s = dnd_string_new(&heap, "GameCube");
    assert(s != NULL);
    assert(s->length == 8);
    assert(strcmp(s->chars, "GameCube") == 0);

    DndArray *a = dnd_array_new(&heap, 4, sizeof(int32_t));
    assert(a != NULL);
    assert(a->length == 4);
    *(int32_t *)dnd_array_at(a, 2) = 42;
    assert(*(int32_t *)dnd_array_at(a, 2) == 42);
    assert(dnd_array_at(a, 4) == NULL);

    dnd_heap_reset(&heap);
    assert(heap.used == 0);
    return 0;
}
