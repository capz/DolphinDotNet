#include "dnd_managed.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static unsigned lines;
void dnd_console_write_line(const char *text) {
    static const char *expected[] = {
        "Hello from real C#!", "Running through DolphinDotNet.", "42"
    };
    assert(lines < 3 && strcmp(text, expected[lines++]) == 0);
}
int main(void) {
    unsigned char memory[64 * 1024];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, memory, sizeof(memory));
    dnd_gc_set_stress(1);
    assert(dnd_aot_entry(&heap) == 0 && !dnd_exception_pending());
    assert(lines == 3);
    puts("HelloGameCube AOT greeting and integer formatting passed.");
    return 0;
}
