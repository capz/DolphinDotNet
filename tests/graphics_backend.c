#include "dnd_draw.h"
#include "dnd_graphics.h"
#include "dnd_gx_raw.h"
#include <assert.h>
int graphics_draws, graphics_vertices, graphics_raw_commands, graphics_effect_draws;
static int frames;
const unsigned char *graphics_expected_rgba;
int graphics_expected_width,graphics_expected_height;
void dnd_graphics_init(void){}
int dnd_graphics_width(void){return 640;}
int dnd_graphics_height(void){return 480;}
void dnd_graphics_begin_frame(float r,float g,float b,float a){(void)r;(void)g;(void)b;(void)a;assert(!frames);frames++;}
void dnd_graphics_present(void){assert(frames);frames--;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *world){assert(v&&n&&m&&world);assert(primitive==0x90);if(m->effect){assert(m->effect->count>=1&&m->effect->count<=16);if(m->effect->texture)assert(t);graphics_effect_draws++;}if(t){assert(t->data&&t->width==4&&t->height==4);const unsigned char *p=t->data;assert(p[0]==0&&p[1]==0);}graphics_draws++;graphics_vertices+=n;}
void dnd_draw_backend_camera(const float *v,const float *p){assert(v&&p);}
void dnd_draw_set_camera(const float *v,const float *p){dnd_draw_backend_camera(v,p);}
void dnd_draw_backend_2d(int w,int h){assert(w>0&&h>0);}
void dnd_draw_backend_viewport(int x,int y,int w,int h){assert(x>=0&&y>=0&&w>0&&h>0);}
void dnd_draw_backend_scissor(int x,int y,int w,int h){assert(x>=0&&y>=0&&w>0&&h>0);}
void dnd_draw_backend_copy(const DndDrawTexture *t,int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;
    if(graphics_expected_rgba) {
        assert(t&&t->format==6&&t->linear&&t->width==graphics_expected_width&&t->height==graphics_expected_height);
        const unsigned char *data=t->data;int blocks=(t->width+3)/4;
        for(int py=0;py<((t->height+3)/4)*4;py++)for(int px=0;px<blocks*4;px++) {
            int at=((py/4)*blocks+px/4)*64+((py&3)*4+(px&3))*2;
            const unsigned char *rgba=px<t->width&&py<t->height?graphics_expected_rgba+(py*t->width+px)*4:NULL;
            assert(data[at]==(rgba?rgba[3]:0)&&data[at+1]==(rgba?rgba[0]:0));
            assert(data[at+32]==(rgba?rgba[1]:0)&&data[at+33]==(rgba?rgba[2]:0));
        }
    }}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_abort(void){}
void dnd_draw_backend_external(void){}
void dnd_draw_backend_flush(void *p,int n,int invalidate){assert(p&&n>=0);(void)invalidate;}
uint64_t dnd_raw_record(int op,const uint64_t *args,int count){if(op==DND_RAW_GX_SETCOPYCLEAR){assert(count==2&&args[0]==0x11223344&&args[1]==0xffffff);}
if(op==DND_RAW_GX_SETTEVCOLORS10){assert(count==2&&args[1]==UINT64_C(0xffff0002fffd0004));}
if(op==DND_RAW_GUMTXIDENTITY){assert(count==1&&args[0]&&(args[0]&31)==0);}
graphics_raw_commands++;return 0;}

bool dnd_draw_backend_immediate_ready(void){return true;}

void dnd_draw_backend_tev(const DndDrawEffect *effect,bool texture_bound){(void)effect;(void)texture_bound;}
