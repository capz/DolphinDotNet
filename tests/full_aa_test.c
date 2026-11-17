#include "dnd_full_aa.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
static float screen_y(const float *p,int part,int y,int perspective){
    float in[16];memcpy(in,p,sizeof in);
    int viewport[4]={0,0,640,480};float out[4][4];
    dnd_full_aa_projection(out,in,part,viewport);
    /* Feed a clip-space point corresponding to a particular logical row. */
    float w=perspective?3:1,clip_y=(1-y/240.f)*w;
    float cropped=out[1][1]*clip_y+out[1][3]*w;
    return (1-cropped/w)*DND_AA_HEIGHT/2+DND_AA_ORIGIN(part);
}
int main(void){
    float identity[16]={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    for(int type=0;type<2;type++)for(int part=0;part<2;part++)
        for(int row=0;row<480;row++)assert(fabsf(screen_y(identity,part,row,type)-row)<.0001f);
    unsigned char xfb[480+32];memset(xfb,0xa5,sizeof xfb);
    /* Top resolves 248 rows; bottom replaces the overlap and stops at 480. */
    for(int part=0;part<2;part++){
        int source=part?8:0,rows=part?240:DND_AA_HEIGHT,destination=part?240:0;
        for(int y=0;y<rows;y++)xfb[destination+y]=(unsigned char)(DND_AA_ORIGIN(part)+source+y);
        /* Full and half captures land on whole GX 4-row tiled blocks. */
        for(int scale=1;scale<=2;scale++){
            int start=DND_AA_ORIGIN(part)/scale,height=DND_AA_HEIGHT/scale;
            assert(start%4==0&&height%4==0&&start+height<=480/scale);
        }
    }
    for(int y=0;y<480;y++)assert(xfb[y]==(unsigned char)y);
    for(unsigned y=480;y<sizeof xfb;y++)assert(xfb[y]==0xa5);
    puts("Full AA: all 480 rows, perspective/orthographic join, overlap, capture block alignment and framebuffer canaries passed.");
}
