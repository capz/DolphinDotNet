#include "dnd_platform.h"
#include "dnd_runtime.h"
#include "dnd_vm.h"
#include <stdint.h>

#define I32LE(v) \
    (uint8_t)((uint32_t)(v) & 0xff), \
    (uint8_t)(((uint32_t)(v) >> 8) & 0xff), \
    (uint8_t)(((uint32_t)(v) >> 16) & 0xff), \
    (uint8_t)(((uint32_t)(v) >> 24) & 0xff)

static uint8_t managed_heap[256 * 1024];

static const char *strings[] = {
    "DolphinDotNet",
    "Tiny managed runtime on Nintendo GameCube",
    "40 + 2 ="
};

static const uint8_t program[] = {
    DND_OP_LDC_I4, I32LE(0),
    DND_OP_CALL_INTERNAL, DND_ICALL_WRITE_LINE,

    DND_OP_LDC_I4, I32LE(1),
    DND_OP_CALL_INTERNAL, DND_ICALL_WRITE_LINE,

    DND_OP_LDC_I4, I32LE(2),
    DND_OP_CALL_INTERNAL, DND_ICALL_WRITE_LINE,

    DND_OP_LDC_I4, I32LE(40),
    DND_OP_LDC_I4, I32LE(2),
    DND_OP_ADD,
    DND_OP_CALL_INTERNAL, DND_ICALL_WRITE_INT,

    DND_OP_LDC_I4, I32LE(0),
    DND_OP_RET
};

int main(void) {
    DndHeap heap;
    DndVm vm;

    dnd_platform_init();
    dnd_heap_init(&heap, managed_heap, sizeof(managed_heap));

    DndString *hello = dnd_string_new(&heap, "Managed heap online.");
    dnd_platform_write(hello ? hello->chars : "Managed allocation failed.");
    dnd_platform_write("\n\n");

    dnd_vm_init(&vm, program, sizeof(program),
                strings, sizeof(strings) / sizeof(strings[0]));

    if (!dnd_vm_run(&vm)) {
        dnd_platform_write("Runtime fault.\n");
    } else {
        dnd_platform_write("\nPress START to exit.\n");
    }

    dnd_platform_wait_forever();
    return vm.return_value;
}
