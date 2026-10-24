/* Stackless position-independent MEM1 handoff. r3=plan, r4=ELF bytes.
 * No libc, TOC, SDA, old stack or absolute code reference after copying starts. */
.section .text.dnd_launcher_stub,"ax",@progbits
.balign 32
.global dnd_launcher_stub_start
dnd_launcher_stub_start:
    lwz 11,4(3)
    addi 10,3,8
1:  lwz 5,4(10)
    lwz 6,8(10)
    lwz 7,0(10)
    add 7,4,7
    mr 8,5
    mr 9,6
    cmpwi 9,0
    beq 3f
2:  lbz 12,0(7)
    stb 12,0(8)
    addi 7,7,1
    addi 8,8,1
    addic. 9,9,-1
    bne 2b
3:  lwz 9,12(10)
    subf 9,6,9
    li 12,0
    cmpwi 9,0
    beq 5f
4:  stb 12,0(8)
    addi 8,8,1
    addic. 9,9,-1
    bne 4b
5:  lwz 6,12(10)
    add 6,5,6
    rlwinm 5,5,0,0,26
    mr 7,5
6:  dcbf 0,5
    addi 5,5,32
    cmplw 5,6
    blt 6b
    sync
7:  icbi 0,7
    addi 7,7,32
    cmplw 7,6
    blt 7b
    addi 10,10,16
    addic. 11,11,-1
    bne 1b
    sync
    isync
    lwz 12,0(3)
    mtctr 12
    li 3,0
    li 4,0
    li 5,0
    li 6,0
    bctr
.global dnd_launcher_stub_end
dnd_launcher_stub_end:
