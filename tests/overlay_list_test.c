#include "dnd_overlay_list.h"
#include <assert.h>
#include <stdio.h>
static uint32_t word(const unsigned char *p){return (uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];}
static float real(const unsigned char *p){uint32_t bits=word(p);float value;memcpy(&value,&bits,4);return value;}
int main(void) {
 DndOverlayList cache={0};unsigned char supported[128]={0};supported['A']=supported['B']=1;
 const char *text[2]={"A B","BA"};
 assert(dnd_overlay_list_update(&cache,text,2,4,8,208,supported,1)==1);
 assert(cache.bytes==416&&((uintptr_t)cache.data&31)==0);
 assert(cache.data[0]==0x80&&cache.data[1]==0&&cache.data[2]==16);
 const unsigned char *v=cache.data+3;
 assert(real(v)==4&&real(v+4)==8&&real(v+8)==0&&word(v+12)==0xd0d0d0ffu);
 assert(real(v+16)==1.f/16&&real(v+20)==.5f);
 assert(real(v+24)==4+5*1.35f&&real(v+24+20)==.5f);
 assert(real(v+96)==4+2*(6*1.35f)); /* Unsupported space preserves character pitch. */
 for(unsigned i=3+16*24;i<cache.bytes;i++)assert(cache.data[i]==0); /* GX NOP padding. */
 unsigned char saved[416];memcpy(saved,cache.data,sizeof saved);
 assert(dnd_overlay_list_update(&cache,text,2,4,8,208,supported,1)==0);
 const char *changed[2]={"B","A"};
 assert(dnd_overlay_list_update(&cache,changed,2,4,8,208,supported,1)==-1);
 assert(!memcmp(saved,cache.data,sizeof saved)); /* Never rewrite in-flight commands. */
 assert(dnd_overlay_list_update(&cache,changed,2,4,8,208,supported,2)==1);
 assert(cache.bytes==224&&cache.capacity==416);
 assert(dnd_overlay_list_update(&cache,changed,2,5,8,208,supported,2)==-1);
 assert(dnd_overlay_list_update(&cache,changed,2,5,8,208,supported,3)==1);
 char too_long[80];memset(too_long,'A',79);too_long[79]=0;const char *oversize[]={too_long};
 assert(dnd_overlay_list_update(&cache,oversize,1,4,8,208,supported,4)==-1);
 assert(dnd_overlay_list_update(&cache,text,11,4,8,208,supported,4)==-1);
 const char *blank[]={" ?"};assert(dnd_overlay_list_update(&cache,blank,1,4,8,208,supported,4)==1&&cache.bytes==0);
 dnd_overlay_list_clear(&cache);dnd_overlay_list_clear(&cache);assert(!cache.data&&!cache.valid);
 puts("Console command cache: GX byte order/layout, pitch, padding, key invalidation, bounds, lifetime and cleanup passed.");
}
