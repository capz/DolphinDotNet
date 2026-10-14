#include "dnd_draw.h"
void dnd_draw_backend_external(void){}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_abort(void){}
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){(void)data;(void)bytes;(void)invalidate;}
void dnd_draw_backend_viewport(int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_scissor(int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_camera(const float *view,const float *projection){(void)view;(void)projection;}
void dnd_draw_backend_2d(int w,int h){(void)w;(void)h;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *world){(void)v;(void)n;(void)primitive;(void)t;(void)m;(void)world;}
void dnd_draw_backend_copy(const DndDrawTexture *t,int x,int y,int w,int h){(void)t;(void)x;(void)y;(void)w;(void)h;}
