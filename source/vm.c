#include "dnd_vm.h"
#include "dnd_platform.h"

static int push(DndVm *vm, int32_t value) {
    if (vm->sp >= sizeof(vm->stack) / sizeof(vm->stack[0])) return 0;
    vm->stack[vm->sp++] = value;
    return 1;
}

static int pop(DndVm *vm, int32_t *value) {
    if (vm->sp == 0) return 0;
    *value = vm->stack[--vm->sp];
    return 1;
}

static int read_i32(DndVm *vm, int32_t *value) {
    if (vm->ip + 4 > vm->code_size) return 0;
    const uint8_t *p = vm->code + vm->ip;
    *value = (int32_t)((uint32_t)p[0] |
                       ((uint32_t)p[1] << 8) |
                       ((uint32_t)p[2] << 16) |
                       ((uint32_t)p[3] << 24));
    vm->ip += 4;
    return 1;
}

void dnd_vm_init(DndVm *vm, const uint8_t *code, size_t code_size,
                 const char **strings, size_t string_count) {
    vm->sp = 0;
    vm->code = code;
    vm->code_size = code_size;
    vm->ip = 0;
    vm->strings = strings;
    vm->string_count = string_count;
    vm->return_value = 0;
    vm->faulted = 0;
}

static int binary(DndVm *vm, DndOpcode op) {
    int32_t a, b;
    if (!pop(vm, &b) || !pop(vm, &a)) return 0;
    switch (op) {
        case DND_OP_ADD: return push(vm, a + b);
        case DND_OP_SUB: return push(vm, a - b);
        case DND_OP_MUL: return push(vm, a * b);
        case DND_OP_DIV:
            if (b == 0) return 0;
            return push(vm, a / b);
        default: return 0;
    }
}

static int internal_call(DndVm *vm, uint8_t call) {
    int32_t value;
    switch ((DndInternalCall)call) {
        case DND_ICALL_WRITE_LINE:
            if (!pop(vm, &value) || value < 0 ||
                (size_t)value >= vm->string_count) return 0;
            dnd_platform_write(vm->strings[value]);
            dnd_platform_write("\n");
            return 1;
        case DND_ICALL_WRITE_INT:
            if (!pop(vm, &value)) return 0;
            dnd_platform_write_int(value);
            dnd_platform_write("\n");
            return 1;
        default:
            return 0;
    }
}

int dnd_vm_run(DndVm *vm) {
    while (vm->ip < vm->code_size) {
        DndOpcode op = (DndOpcode)vm->code[vm->ip++];
        int32_t value;

        switch (op) {
            case DND_OP_NOP:
                break;
            case DND_OP_LDC_I4:
                if (!read_i32(vm, &value) || !push(vm, value)) goto fault;
                break;
            case DND_OP_ADD:
            case DND_OP_SUB:
            case DND_OP_MUL:
            case DND_OP_DIV:
                if (!binary(vm, op)) goto fault;
                break;
            case DND_OP_CALL_INTERNAL:
                if (vm->ip >= vm->code_size ||
                    !internal_call(vm, vm->code[vm->ip++])) goto fault;
                break;
            case DND_OP_POP:
                if (!pop(vm, &value)) goto fault;
                break;
            case DND_OP_RET:
                if (vm->sp > 0) pop(vm, &vm->return_value);
                return 1;
            default:
                goto fault;
        }
    }

fault:
    vm->faulted = 1;
    return 0;
}
