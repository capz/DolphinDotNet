#ifndef DND_OVERLAY_LIST_H
#define DND_OVERLAY_LIST_H
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
enum {DND_TEXT_ROWS=10,DND_TEXT_COLUMNS=78};
typedef struct {
    void *allocation;
    unsigned char *data;
    unsigned capacity,bytes,serial;
    bool valid;
    int rows;
    float left,top;
    unsigned char shade;
    char text[DND_TEXT_ROWS][DND_TEXT_COLUMNS+1];
} DndOverlayList;
static inline void dnd_overlay_list_clear(DndOverlayList *c){free(c->allocation);memset(c,0,sizeof(*c));}
static inline void dnd_overlay_word(unsigned char **p,uint32_t value){(*p)[0]=value>>24;(*p)[1]=value>>16;(*p)[2]=value>>8;(*p)[3]=value;*p+=4;}
static inline void dnd_overlay_float(unsigned char **p,float value){uint32_t bits;memcpy(&bits,&value,4);dnd_overlay_word(p,bits);}
static inline void dnd_overlay_vertex(unsigned char **p,float x,float y,uint32_t color,float u,float v){
    dnd_overlay_float(p,x);dnd_overlay_float(p,y);dnd_overlay_float(p,0);dnd_overlay_word(p,color);dnd_overlay_float(p,u);dnd_overlay_float(p,v);
}
/* Geometry-only GX QUADS list: pipeline state stays outside the cache. Return
 * -1 to use immediate rendering, 0 on reuse, 1 when CPU data needs flushing.
 * A serial advances only after DrawDone: never overwrite an in-flight list. */
static inline int dnd_overlay_list_update(DndOverlayList *c,const char *const *text,int rows,float left,float top,
                                          unsigned char shade,const unsigned char supported[128],unsigned serial) {
    if(rows<0||rows>DND_TEXT_ROWS)return -1;
    bool same=c->valid&&rows==c->rows&&left==c->left&&top==c->top&&shade==c->shade;
    if(same){for(int row=0;row<rows;row++)if(strcmp(c->text[row],text[row])){same=false;break;}if(same)return 0;}
    if(c->valid&&c->serial==serial)return -1;
    unsigned quads=0;
    for(int row=0;row<rows;row++){
        unsigned col=0;while(col<=DND_TEXT_COLUMNS&&text[row][col]){
            unsigned char ch=(unsigned char)text[row][col];if(ch<128&&supported[ch])quads++;col++;
        }
        if(col>DND_TEXT_COLUMNS)return -1;
    }
    unsigned bytes=quads?(quads*4*24+3+31)&~31u:0;
    if(bytes>c->capacity){
        void *allocation=malloc(bytes+31);if(!allocation)return -1;
        free(c->allocation);c->allocation=allocation;c->data=(unsigned char*)(((uintptr_t)allocation+31)&~(uintptr_t)31);c->capacity=bytes;
    }
    if(bytes){
        memset(c->data,0,bytes);unsigned char *p=c->data;
        *p++=0x80;*p++=(unsigned char)(quads*4>>8);*p++=(unsigned char)(quads*4);
        const float scale=1.35f,cw=6*scale,ch=9*scale;
        uint32_t color=(uint32_t)shade<<24|(uint32_t)shade<<16|(uint32_t)shade<<8|255;
        for(int row=0;row<rows;row++)for(int col=0;text[row][col];col++){
            unsigned char character=(unsigned char)text[row][col];if(character>=128||!supported[character])continue;
            float x=left+col*cw,y=top+row*ch,u=(character&15)*.0625f,v=(character>>4)*.125f;
            dnd_overlay_vertex(&p,x,y,color,u,v);
            dnd_overlay_vertex(&p,x+5*scale,y,color,u+5.f/128,v);
            dnd_overlay_vertex(&p,x+5*scale,y+7*scale,color,u+5.f/128,v+7.f/64);
            dnd_overlay_vertex(&p,x,y+7*scale,color,u,v+7.f/64);
        }
    }
    for(int row=0;row<rows;row++)memcpy(c->text[row],text[row],strlen(text[row])+1);
    c->rows=rows;c->left=left;c->top=top;c->shade=shade;c->serial=serial;c->bytes=bytes;c->valid=true;return 1;
}
#endif
