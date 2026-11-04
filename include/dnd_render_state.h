#ifndef DND_RENDER_STATE_H
#define DND_RENDER_STATE_H
#include <math.h>
#include <stdbool.h>
static inline bool dnd_render_integer(float x,int max){return isfinite(x)&&x>=0&&x<=max&&x==(int)x;}
static inline bool dnd_render_style_valid(const float *p){
    for(int i=0;i<40;i++)if(!isfinite(p[i]))return false;
    for(int s=0;s<2;s++){
        const float *q=p+s*20;
        if(!dnd_render_integer(q[0],2)||!dnd_render_integer(q[1],3)||!dnd_render_integer(q[2],3)||!dnd_render_integer(q[3],1))return false;
        if(!dnd_render_integer(q[10],5)||!dnd_render_integer(q[11],1)||q[12]<0||q[13]<q[12]||q[13]>10||q[14]<-4||q[14]>3.99f||!dnd_render_integer(q[15],1)||!dnd_render_integer(q[16],1)||!dnd_render_integer(q[17],2)||q[18]!=0||q[19]!=0)return false;
    }
    return true;
}
static inline bool dnd_render_scene_valid(const float *p){
    for(int i=0;i<136;i++)if(!isfinite(p[i]))return false;
    if(p[0]!=1||!dnd_render_integer(p[4],3)||!dnd_render_integer(p[12],1)||!dnd_render_integer(p[13],8)||p[10]<=0||p[11]<=p[10])return false;
    for(int i=1;i<=3;i++)if(!dnd_render_integer(p[i],255)||!dnd_render_integer(p[i+6],255))return false;
    if(p[4]&&(p[5]<0||p[6]<=p[5]))return false;
    for(int i=0;i<(int)p[13];i++){const float *l=p+16+i*15;
        if(!dnd_render_integer(l[0],2))return false;
        for(int c=1;c<=3;c++)if(!dnd_render_integer(l[c],255))return false;
        float n=l[7]*l[7]+l[8]*l[8]+l[9]*l[9];
        if(l[0]!=1&&(!(n>0)||!isfinite(n)))return false;
        if(l[0]!=0&&l[10]<=0)return false;
        if(l[0]==2&&(l[11]<=0||l[11]>90))return false;
    }return true;
}
#endif
