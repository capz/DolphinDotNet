#include "dnd_platform.h"
#include "dnd_runtime.h"
#include "dnd_vm.h"
#include "../generated/generated_program.h"
#include <stdint.h>

static uint8_t managed_heap[256 * 1024];

int main(void) {
    DndHeap heap;
    DndVm vm;

    dnd_platform_init();
    dnd_heap_init(&heap, managed_heap, sizeof(managed_heap));

    DndString *hello = dnd_string_new(&heap, "Managed heap online.");
    dnd_platform_write(hello ? hello->chars : "Managed allocation failed.");
    dnd_platform_write("\n\n");

    dnd_vm_init(&vm, dnd_generated_code, dnd_generated_code_size,
                dnd_generated_strings, dnd_generated_string_count);

    if (!dnd_vm_run(&vm)) {
        dnd_platform_write("Runtime fault.\n");
    } else {
        dnd_platform_write("\nPress START to exit.\n");
    }

    dnd_platform_wait_forever();
    return vm.return_value;
}
