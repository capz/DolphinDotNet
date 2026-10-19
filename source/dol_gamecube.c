#include "dnd_dol.h"
#include <string.h>
#if defined(GEKKO) && defined(HW_DOL)
#include <gccore.h>

/* Position-independent, stackless PowerPC trampoline. r3=plan, r4=image.
 * All branches are local; no TOC, small-data, libc or old runtime references.
 * Clear BSS, copy sections, flush data/instruction cache lines, then branch to
 * the DOL entry. The new executable's CRT establishes its own stack/runtime. */
__asm__(
    ".pushsection .text.dnd_dol_stub,\"ax\",@progbits\n"
    ".balign 32\n"
    ".global dnd_dol_stub_start\n"
    "dnd_dol_stub_start:\n"
    "lwz 5,4(3)\n"
    "lwz 6,8(3)\n"
    "li 7,0\n"
    "cmpwi 6,0\n"
    "beq 3f\n"
    "mr 8,5\n"
    "mr 9,6\n"
    "1: stb 7,0(8)\n"
    "addi 8,8,1\n"
    "addic. 9,9,-1\n"
    "bne 1b\n"
    "add 6,5,6\n"
    "rlwinm 5,5,0,0,26\n"
    "2: dcbf 0,5\n"
    "addi 5,5,32\n"
    "cmplw 5,6\n"
    "blt 2b\n"
    "sync\n"
    "3: addi 10,3,16\n"
    "li 11,18\n"
    "4: lwz 6,8(10)\n"
    "cmpwi 6,0\n"
    "beq 8f\n"
    "lwz 5,4(10)\n"
    "lwz 7,0(10)\n"
    "add 7,4,7\n"
    "mr 8,5\n"
    "mr 9,6\n"
    "5: lbz 12,0(7)\n"
    "stb 12,0(8)\n"
    "addi 7,7,1\n"
    "addi 8,8,1\n"
    "addic. 9,9,-1\n"
    "bne 5b\n"
    "add 6,5,6\n"
    "rlwinm 5,5,0,0,26\n"
    "mr 7,5\n"
    "6: dcbf 0,5\n"
    "addi 5,5,32\n"
    "cmplw 5,6\n"
    "blt 6b\n"
    "sync\n"
    "7: icbi 0,7\n"
    "addi 7,7,32\n"
    "cmplw 7,6\n"
    "blt 7b\n"
    "8: addi 10,10,12\n"
    "addic. 11,11,-1\n"
    "bne 4b\n"
    "sync\n"
    "isync\n"
    "lwz 12,0(3)\n"
    "mtctr 12\n"
    "li 3,0\n"
    "li 4,0\n"
    "li 5,0\n"
    "li 6,0\n"
    "bctr\n"
    ".global dnd_dol_stub_end\n"
    "dnd_dol_stub_end:\n"
    ".popsection\n");
extern const uint8_t dnd_dol_stub_start[], dnd_dol_stub_end[];
_Static_assert(sizeof(DndDolSection)==12 && offsetof(DndDolPlan, sections)==16,
               "DOL trampoline descriptor ABI");
bool dnd_dol_platform_supported(void) { return true; }
size_t dnd_dol_platform_stub_size(void) { return (size_t)(dnd_dol_stub_end-dnd_dol_stub_start); }
void dnd_dol_platform_stub_copy(void *destination) {
    memcpy(destination, dnd_dol_stub_start, dnd_dol_platform_stub_size());
}
void dnd_dol_platform_launch(DndDolPlan *plan, void *image, void *stub) {
    DCFlushRange(stub, (u32)dnd_dol_platform_stub_size());
    ICInvalidateRange(stub, (u32)dnd_dol_platform_stub_size());
    /* Registered libogc reset callbacks stop threads, DSP/DMA and devices while
     * the current thread remains alive. Do this before disabling interrupts. */
    SYS_ResetSystem(SYS_SHUTDOWN, 0, 0);
    IRQ_Disable();
    ((void (*)(DndDolPlan *, void *))stub)(plan, image);
    __builtin_unreachable();
}
#else
bool dnd_dol_platform_supported(void) { return false; }
size_t dnd_dol_platform_stub_size(void) { return 0; }
void dnd_dol_platform_stub_copy(void *destination) { (void)destination; }
void dnd_dol_platform_launch(DndDolPlan *plan, void *image, void *stub) {
    (void)plan; (void)image; (void)stub;
}
#endif
