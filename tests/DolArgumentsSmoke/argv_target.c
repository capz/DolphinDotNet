#include <stdint.h>
/* Freestanding native consumer of the standard libogc entry _arg ABI. No game,
 * Swiss code, libc initialization or managed runtime is part of this payload. */
__asm__(".pushsection .text.entry,\"ax\"\n.global _start\n_start:\nb 1f\n.long 0x5f617267\n.space 24\n1:\nlis 1,0x817f\nori 1,1,0xff00\nstwu 1,-32(1)\nbl capture_arguments\n2: b 2b\n.popsection\n");
extern unsigned char _start[];
volatile uint32_t argv_result,argv_length,argv_count;
volatile unsigned char argv_capture[512];
void capture_arguments(void) {
    volatile uint32_t *header=(volatile uint32_t *)(_start+8);
    uint32_t magic=header[0],length=header[2];const unsigned char *source=(const unsigned char *)(uintptr_t)header[1];
    if(magic!=0x5f617267||length>sizeof(argv_capture)||length==0){argv_result=0xbad00001;return;}
    uint32_t count=0;for(uint32_t i=0;i<length;i++){argv_capture[i]=source[i];if(source[i]==0)count++;}
    argv_length=length;argv_count=count;argv_result=0x41524756;
}
