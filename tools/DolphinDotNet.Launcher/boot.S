#include "resident.h"

/* Outer DOL: no CRT, stack, allocator or writable globals. Preserve a pristine
 * receiver before its first CRT invocation mutates initialized data. */
.section .text.boot,"ax",@progbits
.global dnd_launcher_boot
dnd_launcher_boot:
    mfmsr 0
    rlwinm 0,0,0,17,15
    mtmsr 0
    isync
    lis 3,resident_image@h
    ori 3,3,resident_image@l
    lis 4,DND_RESIDENT_BASE@h
    ori 4,4,DND_RESIDENT_BASE@l
    lis 5,resident_image_end@h
    ori 5,5,resident_image_end@l
1:  lbz 6,0(3)
    stb 6,0(4)
    addi 3,3,1
    addi 4,4,1
    cmplw 3,5
    blt 1b
    lis 3,DND_RESIDENT_BASE@h
    ori 3,3,DND_RESIDENT_BASE@l
    bl flush_code
    lis 3,return_stub@h
    ori 3,3,return_stub@l
    lis 4,DND_RETURN_STUB@h
    ori 4,4,DND_RETURN_STUB@l
    li 5,return_stub_end-return_stub
    mtctr 5
2:  lbz 6,0(3)
    stb 6,0(4)
    addi 3,3,1
    addi 4,4,1
    bdnz 2b
    lis 3,DND_RETURN_STUB@h
    ori 3,3,DND_RETURN_STUB@l
    bl flush_code
    lis 12,DND_RETURN_STUB@h
    ori 12,12,DND_RETURN_STUB@l
    mtctr 12
    bctr

/* r3=start (32-aligned), r4=end. Flush writes before invalidating code. */
flush_code:
    mr 5,3
3:  dcbf 0,5
    addi 5,5,32
    cmplw 5,4
    blt 3b
    sync
4:  icbi 0,3
    addi 3,3,32
    cmplw 3,4
    blt 4b
    sync
    isync
    blr

/* libogc2 reload ABI: branch, "STUBHAXX", stackless entry. The pinned runtime
 * stops devices and multitasking before invoking this on main return / exit(). */
.balign 32
return_stub:
    b 5f
    .ascii "STUBHAXX"
5:  mfmsr 0
    rlwinm 0,0,0,17,15
    mtmsr 0
    isync
    lis 3,0x8000
    lis 4,DND_RESIDENT_BASE@h
    ori 4,4,DND_RESIDENT_BASE@l
    /* Bound the arena before libogc allocates its kernel heap on each entry. */
    stw 4,0x34(3)
    stw 4,0x38(3)
    stw 4,0xec(3)
    li 5,0
    stw 5,0x30(3)
    stw 5,0xf4(3)
    dcbf 0,3
    addi 5,3,0x20
    dcbf 0,5
    addi 5,3,0xe0
    dcbf 0,5
    sync
    addi 3,4,DND_RESIDENT_PLAN
    addi 12,4,DND_RESIDENT_STUB
    addi 4,4,DND_RESIDENT_IMAGE
    mtctr 12
    bctr
return_stub_end:

.section .rodata.resident,"a",@progbits
.balign 32
resident_image:
    .incbin "build/resident.bin"
resident_image_end:
