#include "dnd_ipl_font.h"
#include <stdlib.h>
#include <string.h>
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_IPL_FONT_TEST)
#if defined(DND_IPL_FONT_TEST)
#define font_allocate(bytes) aligned_alloc(32,(bytes))
#else
#include <malloc.h>
#define font_allocate(bytes) memalign(32,(bytes))
#endif
#include <ogc/system.h>
/* libogc retains pointers into SYS_InitFont's storage. Keep it alive until
 * graphics shutdown, rather than free it after copying an atlas. */
static sys_fontheader *firmware;
static int firmware_bytes;
static int firmware_encoding;
int dnd_ipl_font_cache_bytes(void){return firmware_bytes;}
void dnd_ipl_font_shutdown(void){free(firmware);firmware=NULL;firmware_bytes=0;}
int dnd_ipl_font_build(uint8_t *atlas,size_t bytes,int32_t *metadata,size_t count)
{
    if(!atlas||!metadata||bytes!=DND_IPL_WIDTH*DND_IPL_HEIGHT*2||count!=DND_IPL_METADATA)return 0;
    if(!firmware){
        int encoding=SYS_GetFontEncoding();
        int size=encoding==SYS_FONTENC_SJIS?SYS_FONTSIZE_SJIS:SYS_FONTSIZE_ANSI;
        sys_fontheader *data=font_allocate((size_t)size);
        if(!data)return 0;
        if(!SYS_InitFont(data)){free(data);return 0;}
        firmware=data;firmware_bytes=size;firmware_encoding=encoding;
    }
    if(!firmware->cell_width||firmware->cell_width>32||!firmware->cell_height||firmware->cell_height>32||!firmware->leading)return 0;
    uint8_t *i4=font_allocate(DND_IPL_WIDTH*DND_IPL_HEIGHT/2);
    if(!i4)return 0;
    memset(i4,0,DND_IPL_WIDTH*DND_IPL_HEIGHT/2);
    memset(metadata,0,count*sizeof(*metadata));
    metadata[0]=firmware->cell_width;metadata[1]=firmware->cell_height;
    metadata[2]=firmware->asc;metadata[3]=firmware->desc;metadata[4]=firmware->leading;
    metadata[5]=firmware_encoding;metadata[6]=firmware_bytes;metadata[7]=1;
    for(int i=0;i<DND_IPL_GLYPHS;i++){
        int x=i%16*32,y=i/16*32,width=0;
        SYS_GetFontTexel(i+DND_IPL_FIRST,i4+y*DND_IPL_WIDTH/2,x,DND_IPL_WIDTH/2,&width);
        if(width<0||width>32){free(i4);return 0;}
        int *m=metadata+8+i*5;m[0]=x;m[1]=y;m[2]=firmware->cell_width;m[3]=firmware->cell_height;m[4]=width;
    }
    /* GX I4 source is 8x8 tiled; IA8 output is 4x4 tiled. Preserve white RGB
     * at antialiased edges instead of multiplying coverage into RGB twice. */
    size_t at=0;
    for(int ty=0;ty<DND_IPL_HEIGHT;ty+=4)for(int tx=0;tx<DND_IPL_WIDTH;tx+=4)
        for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++){
            size_t src=((size_t)(y/8)*(DND_IPL_WIDTH/8)+x/8)*32+(y%8)*4+(x%8)/2;
            unsigned nibble=(x&1)?i4[src]&15:i4[src]>>4;
            atlas[at++]=(uint8_t)(nibble*17);atlas[at++]=255;
        }
    free(i4);return 1;
}
#else
int dnd_ipl_font_cache_bytes(void){return 0;}
void dnd_ipl_font_shutdown(void){}
int dnd_ipl_font_build(uint8_t *atlas,size_t bytes,int32_t *metadata,size_t count)
{(void)atlas;(void)bytes;(void)metadata;(void)count;return 0;}
#endif
