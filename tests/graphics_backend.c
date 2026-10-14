#include "dnd_draw.h"
#include "dnd_graphics.h"
#include <assert.h>
int graphics_draws, graphics_vertices, graphics_raw_commands;
static int frames;
void dnd_graphics_init(void){}
int dnd_graphics_width(void){return 640;}
int dnd_graphics_height(void){return 480;}
void dnd_graphics_begin_frame(float r,float g,float b,float a){(void)r;(void)g;(void)b;(void)a;assert(!frames);frames++;}
void dnd_graphics_present(void){assert(frames);frames--;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *world){assert(v&&n&&m&&world);assert(primitive==0x90);if(t){assert(t->data&&t->width==4&&t->height==4);const unsigned char *p=t->data;assert(p[0]==0&&p[1]==0);}graphics_draws++;graphics_vertices+=n;}
void dnd_draw_backend_camera(const float *v,const float *p){assert(v&&p);}
void dnd_draw_set_camera(const float *v,const float *p){dnd_draw_backend_camera(v,p);}
void dnd_draw_backend_2d(int w,int h){assert(w>0&&h>0);}
void dnd_draw_backend_viewport(int x,int y,int w,int h){assert(x>=0&&y>=0&&w>0&&h>0);}
void dnd_draw_backend_scissor(int x,int y,int w,int h){assert(x>=0&&y>=0&&w>0&&h>0);}
void dnd_draw_backend_copy(const DndDrawTexture *t,int x,int y,int w,int h){(void)t;(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_abort(void){}
void dnd_draw_backend_external(void){}
void dnd_draw_backend_flush(void *p,int n,int invalidate){assert(p&&n>=0);(void)invalidate;}
uint64_t dnd_raw_record(int op,const uint64_t *args,int count){(void)op;(void)args;(void)count;graphics_raw_commands++;return 0;}
