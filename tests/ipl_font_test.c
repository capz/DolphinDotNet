/* Synthetic font provider: no ROM/firmware font data is distributed. */
#include "dnd_ipl_font.h"
#include <ogc/system.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static int loads,encoding,fail,bad_cell;
uint16_t SYS_GetFontEncoding(void){return (uint16_t)encoding;}
uint32_t SYS_InitFont(sys_fontheader *font){loads++;if(fail)return 0;memset(font,0,sizeof(*font));font->cell_width=bad_cell?40:24;font->cell_height=24;font->asc=18;font->desc=6;font->leading=26;return 1;}
static unsigned value(int c,int x,int y){return (unsigned)(c+x+3*y)&15;}
void SYS_GetFontTexel(int c,void *image,int pos,int stride,int *width)
{
    uint8_t *out=image;int pixels=stride*2;
    for(int y=0;y<24;y++)for(int x=0;x<24;x++){
        int dx=pos+x;size_t at=((size_t)(y/8)*(pixels/8)+dx/8)*32+(y%8)*4+(dx%8)/2;
        unsigned v=value(c,x,y);if(dx&1)out[at]=(uint8_t)((out[at]&0xf0)|v);else out[at]=(uint8_t)((out[at]&15)|(v<<4));
    }
    *width=c==' '?8:11+c%8;
}
static unsigned alpha(const uint8_t *data,int x,int y)
{size_t at=((size_t)(y/4)*(DND_IPL_WIDTH/4)+x/4)*32+(y%4)*8+(x%4)*2;assert(data[at+1]==255);return data[at];}
int main(void)
{
    size_t bytes=DND_IPL_WIDTH*DND_IPL_HEIGHT*2;uint8_t *data=malloc(bytes);assert(data);int32_t metrics[DND_IPL_METADATA];
    assert(!dnd_ipl_font_build(NULL,bytes,metrics,DND_IPL_METADATA));assert(!dnd_ipl_font_build(data,bytes-1,metrics,DND_IPL_METADATA));assert(!dnd_ipl_font_build(data,bytes,metrics,DND_IPL_METADATA-1));assert(loads==0);
    assert(dnd_ipl_font_build(data,bytes,metrics,DND_IPL_METADATA));assert(loads==1&&metrics[0]==24&&metrics[1]==24&&metrics[2]==18&&metrics[3]==6&&metrics[4]==26&&metrics[5]==0&&metrics[6]==SYS_FONTSIZE_ANSI);
    for(int i=0;i<DND_IPL_GLYPHS;i++){int *m=metrics+8+i*5;assert(m[0]==i%16*32&&m[1]==i/16*32&&m[2]==24&&m[3]==24);for(int y=0;y<24;y++)for(int x=0;x<24;x++)assert(alpha(data,m[0]+x,m[1]+y)==value(i+32,x,y)*17);assert(alpha(data,m[0]+24,m[1])==0);}
    assert(dnd_ipl_font_build(data,bytes,metrics,DND_IPL_METADATA)&&loads==1);dnd_ipl_font_shutdown();assert(dnd_ipl_font_cache_bytes()==0);
    encoding=1;assert(dnd_ipl_font_build(data,bytes,metrics,DND_IPL_METADATA)&&metrics[5]==1&&metrics[6]==SYS_FONTSIZE_SJIS);dnd_ipl_font_shutdown();
    fail=1;assert(!dnd_ipl_font_build(data,bytes,metrics,DND_IPL_METADATA)&&dnd_ipl_font_cache_bytes()==0);fail=0;bad_cell=1;assert(!dnd_ipl_font_build(data,bytes,metrics,DND_IPL_METADATA));dnd_ipl_font_shutdown();free(data);
    puts("IPL font: ROM-provider failure, charset metadata, cache lifetime, proportional widths and I4/IA8 swizzle passed");return 0;
}
