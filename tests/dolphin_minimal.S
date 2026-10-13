/* Minimal GameCube PowerPC executable with no libogc/CRT startup.
 * This verifies Dolphin's guest PC/breakpoint path independently of EXI.
 * 'main' is the ELF/DOL entry point and spins without touching hardware.
 */
    .section .text,"ax",@progbits
    .globl main
    .type main,@function
    .balign 32
main:
    b main
    .size main, .-main
