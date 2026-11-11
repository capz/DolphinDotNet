/* DolphinDotNet GameCube homebrew apploader.
 * Originally Copyright (c) 2026 AnimalTown contributors. SPDX-License-Identifier: MIT
 * IPL callback protocol: each main call requests one disc read, final returns
 * the DOL entry point. No Nintendo apploader code is included. */
typedef unsigned int u32;
typedef void (*report_fn)(const char *, ...);
static u32 header[64] __attribute__((aligned(32)));
static int section;
static void *previous;
static u32 previous_size;
static void coherent(void *ptr, u32 size) {
    u32 a = (u32)ptr & ~31u, end = ((u32)ptr + size + 31u) & ~31u;
    for (; a < end; a += 32) __asm__ volatile("dcbst 0,%0"::"r"(a):"memory");
    __asm__ volatile("sync":::"memory");
    a = (u32)ptr & ~31u;
    for (; a < end; a += 32) __asm__ volatile("icbi 0,%0"::"r"(a):"memory");
    __asm__ volatile("sync; isync":::"memory");
}
static void init(report_fn report) { (void)report; section = -1; previous_size = 0; }
static int main_read(void **dst, int *size, int *offset) {
    if (previous_size) coherent(previous, previous_size);
    if (section == -1) {
        *dst = header; *size = 256; *offset = 0x10000; section = 0;
        previous = header; previous_size = 256; return 1;
    }
    while (section < 18) {
        int s = section++;
        if (!header[0x90 / 4 + s]) continue;
        *dst = (void *)header[0x48 / 4 + s];
        *size = (int)((header[0x90 / 4 + s] + 31u) & ~31u);
        *offset = 0x10000 + (int)header[s];
        previous = *dst; previous_size = (u32)*size; return 1;
    }
    previous_size = 0; return 0;
}
static void *final(void) {
    volatile unsigned char *bss = (volatile unsigned char *)header[0xd8 / 4];
    u32 length = header[0xdc / 4];
    for (u32 i = 0; i < length; i++) bss[i] = 0;
    coherent((void *)bss, length);
    return (void *)header[0xe0 / 4];
}
void entry(void (**initialize)(report_fn), int (**read)(void **, int *, int *), void *(**finish)(void)) {
    *initialize = init; *read = main_read; *finish = final;
}
