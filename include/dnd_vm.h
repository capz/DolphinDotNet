#ifndef DND_VM_H
#define DND_VM_H

#include "dnd_runtime.h"
#include <stddef.h>
#include <stdint.h>

typedef enum {
    DND_OP_NOP = 0x00,
    DND_OP_LDC_I4 = 0x01,
    DND_OP_ADD = 0x02,
    DND_OP_SUB = 0x03,
    DND_OP_MUL = 0x04,
    DND_OP_DIV = 0x05,
    DND_OP_CALL_INTERNAL = 0x06,
    DND_OP_POP = 0x07,
    DND_OP_RET = 0x08
} DndOpcode;

typedef enum {
    DND_ICALL_WRITE_LINE = 1,
    DND_ICALL_WRITE_INT = 2
} DndInternalCall;

typedef struct {
    int32_t stack[64];
    size_t sp;
    const uint8_t *code;
    size_t code_size;
    size_t ip;
    const char **strings;
    size_t string_count;
    int32_t return_value;
    int faulted;
} DndVm;

void dnd_vm_init(DndVm *vm, const uint8_t *code, size_t code_size,
                 const char **strings, size_t string_count);
int dnd_vm_run(DndVm *vm);

#endif
