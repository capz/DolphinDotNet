#ifndef IPL_TEST_SYSTEM_H
#define IPL_TEST_SYSTEM_H
#include <stdint.h>
#define SYS_FONTENC_ANSI 0
#define SYS_FONTENC_SJIS 1
#define SYS_FONTSIZE_ANSI (288+131072)
#define SYS_FONTSIZE_SJIS (3840+1179648)
typedef struct { uint16_t cell_width,cell_height,asc,desc,leading; } sys_fontheader;
uint16_t SYS_GetFontEncoding(void);
uint32_t SYS_InitFont(sys_fontheader *font);
void SYS_GetFontTexel(int c,void *image,int pos,int stride,int *width);
#endif
