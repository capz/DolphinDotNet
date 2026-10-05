#include "dnd_managed.h"
#include "dnd_bcl.h"
#include <assert.h>
#include <stdint.h>
#include <stdio.h>

static int invoked;
static void callback(void *target, void *arg) {
    (void)target; invoked = *(int *)arg;
}

int main(void) {
    uint8_t memory[16384];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, memory, sizeof(memory));

    DndString *hello = dnd_string_from_utf8(&heap, "Hello");
    DndString *space = dnd_string_from_utf8(&heap, " ");
    DndString *world = dnd_string_from_utf8(&heap, "World");
    DndString *hello2 = dnd_string_from_utf8(&heap, "Hello");
    assert(hello && hello->length == 5);
    assert(dnd_string_equals(hello, hello2));
    DndString *left = dnd_string_concat(&heap, hello, space);
    DndString *sentence = dnd_string_concat(&heap, left, world);
    assert(sentence && sentence->length == 11);

    DndArray *array = dnd_managed_array_new(&heap, 4, sizeof(int32_t));
    *(int32_t *)dnd_managed_array_at(array, 2) = 42;
    assert(*(int32_t *)dnd_managed_array_at(array, 2) == 42);
    assert(dnd_managed_array_at(array, 9) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_INDEX_OUT_OF_RANGE);
    dnd_exception_clear();

    DndList list;
    assert(dnd_list_init(&list, &heap, sizeof(int32_t), 2));
    for (int32_t i = 0; i < 10; i++) assert(dnd_list_add(&list, &i));
    assert(dnd_list_count(&list) == 10);
    assert(*(int32_t *)dnd_list_get(&list, 7) == 7);

    DndRandom random;
    dnd_random_init(&random, 1234);
    int32_t value = dnd_random_next_max(&random, 10);
    assert(value >= 0 && value < 10);
    assert(dnd_math_abs_i32(-12) == 12);
    assert(dnd_math_min_i32(2, 9) == 2);
    assert(dnd_math_max_i32(2, 9) == 9);

    DndDelegate *delegate = dnd_delegate_new(&heap, NULL, callback);
    int n = 73; dnd_delegate_invoke(delegate, &n);
    assert(invoked == 73);

    DndObject *root = (DndObject *)hello;
    DndObject **slots[1];
    DndRootSet roots;
    dnd_roots_init(&roots, slots, 1);
    assert(dnd_root_add(&roots, &root));
    size_t used = heap.used;
    dnd_gc_collect(&heap, &roots);
    assert(heap.used == used);
    root = NULL;
    dnd_gc_collect(&heap, &roots);
    assert(heap.used == 0);

    puts("managed runtime + core BCL tests passed");
    return 0;
}
