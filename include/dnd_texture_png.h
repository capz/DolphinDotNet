/* Private to draw_resources.c. A separate bounded decoder keeps model-import
 * allocator state independent from texture creation on the graphics owner. */
#ifndef DND_TEXTURE_PNG_H
#define DND_TEXTURE_PNG_H
#define PNG_SCRATCH_LIMIT (8u*1024u*1024u)
typedef union { max_align_t alignment; size_t size; } PngAllocation;
static size_t png_scratch;
static void *png_alloc(size_t n) {
    if(n>PNG_SCRATCH_LIMIT-png_scratch)return NULL;
    PngAllocation *p=malloc(sizeof(*p)+n);
    if(!p)return NULL;
    p->size=n;png_scratch+=n;return p+1;
}
static void png_free(void *ptr) {
    if(!ptr)return;
    PngAllocation *p=(PngAllocation*)ptr-1;
    png_scratch-=p->size;free(p);
}
static void *png_resize(void *ptr,size_t old,size_t n) {
    void *next=png_alloc(n);
    if(next){if(ptr)memcpy(next,ptr,old<n?old:n);png_free(ptr);}
    return next;
}
#define STB_IMAGE_STATIC

#define STBI_MALLOC(n) png_alloc(n)
#define STBI_FREE(p) png_free(p)
#define STBI_REALLOC_SIZED(p,o,n) png_resize(p,o,n)
#define STBI_NO_STDIO
#define STBI_NO_THREAD_LOCALS
#define STBI_ONLY_PNG
#define STBI_MAX_DIMENSIONS 1024
#define STB_IMAGE_IMPLEMENTATION
/* GCC diagnoses stb's disabled static API declarations at end of the
 * translation unit, so this one diagnostic must remain disabled here. */
#if defined(__GNUC__)
#pragma GCC diagnostic ignored "-Wunused-function"
#endif
#include "dnd_stb_image.h"
static uint32_t png_be32(const unsigned char *p) {
    return (uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];
}
/* stb deliberately ignores chunk CRCs and tolerates missing IEND; validate the
 * complete PNG envelope before handing untrusted network bytes to it. */
static int png_envelope(const unsigned char *p,size_t size) {
    static const unsigned char signature[8]={137,80,78,71,13,10,26,10};
    if(size<45 || memcmp(p,signature,8))return 0;
    size_t at=8;int header=0,idat=0,ended_idat=0;
    while(size-at>=12) {
        uint32_t n=png_be32(p+at);const unsigned char *type=p+at+4;
        if(n>size-at-12)return 0;
        uint32_t crc=~0u;
        for(size_t i=0;i<(size_t)n+4;i++) {
            crc^=type[i];
            for(int bit=0;bit<8;bit++)crc=(crc>>1)^((0u-(crc&1))&0xedb88320u);
        }
        if((crc^~0u)!=png_be32(type+4+n))return 0;
        if(!header) {
            if(memcmp(type,"IHDR",4) || n!=13)return 0;
            if(!png_be32(type+4) || png_be32(type+4)>1024 || !png_be32(type+8) || png_be32(type+8)>1024)return 0;
            header=1;
        } else if(!memcmp(type,"IHDR",4))return 0;
        else if(!memcmp(type,"IDAT",4)){if(ended_idat)return 0;idat=1;}
        else {if(idat)ended_idat=1;}
        at+=(size_t)n+12;
        if(!memcmp(type,"IEND",4))return idat && n==0 && at==size;
    }
    return 0;
}
#endif
