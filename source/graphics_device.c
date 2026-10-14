#include "dnd_draw.h"
#include "dnd_graphics.h"
#include "dnd_performance.h"
#include "dnd_async.h"
#include <math.h>
static bool active,external;
static int pass, raw_depth;
static int viewport[4], scissor[4];
static bool finite_color(float x) { return isfinite(x) && x>=0 && x<=1; }
int dnd_device_begin(float r,float g,float b,float a) {
    dnd_async_require_application_thread();
    if(external || active || raw_depth || !finite_color(r)||!finite_color(g)||!finite_color(b)||!finite_color(a)) return -1;
    active=true; pass=0;
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_GRAPHICS_TEST)
    dnd_graphics_begin_frame(r,g,b,a);
#endif
    if(dnd_exception_pending()){active=false;return -1;}
    viewport[0]=viewport[1]=scissor[0]=scissor[1]=0;
    viewport[2]=scissor[2]=dnd_device_width();viewport[3]=scissor[3]=dnd_device_height();
    dnd_performance_begin_frame();
    return 0;
}
int dnd_device_end(void) {
    dnd_async_require_application_thread();
    if(!active || pass || raw_depth) return -1;
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_GRAPHICS_TEST)
    dnd_graphics_present();
#endif
    active=false;
    dnd_draw_collect();
    return 0;
}
int dnd_device_pass_begin(int next) {
    dnd_async_require_application_thread();
    if(!active || pass || next<1 || next>4 || raw_depth) return -1;
    if(next!=3){dnd_draw_backend_viewport(viewport[0],viewport[1],viewport[2],viewport[3]);dnd_draw_backend_scissor(scissor[0],scissor[1],scissor[2],scissor[3]);}
    pass=next; return 0;
}
int dnd_device_pass_end(int expected) {
    dnd_async_require_application_thread();
    if(!active || pass!=expected || raw_depth) return -1;
    pass=0; return 0;
}
int dnd_device_pass(void) { return pass; }
bool dnd_device_active(void) { return active; }
int dnd_device_raw_begin(void) { return dnd_device_pass_begin(3); }
int dnd_device_raw_end(void) { return dnd_device_pass_end(3); }
int dnd_device_width(void) {
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_GRAPHICS_TEST)
    if(!external)dnd_graphics_init();
    return dnd_graphics_width();
#else
    return 640;
#endif
}
int dnd_device_height(void) {
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_GRAPHICS_TEST)
    if(!external)dnd_graphics_init();
    return dnd_graphics_height();
#else
    return 480;
#endif
}
static bool rectangle(int x,int y,int w,int h) {
    return x>=0 && y>=0 && w>0 && h>0 && x<=dnd_device_width()-w && y<=dnd_device_height()-h;
}
int dnd_device_viewport(int x,int y,int w,int h) {
    dnd_async_require_application_thread();
    if(!active || pass || !rectangle(x,y,w,h)) return -1;
    viewport[0]=x;viewport[1]=y;viewport[2]=w;viewport[3]=h;dnd_draw_backend_viewport(x,y,w,h); return 0;
}
int dnd_device_scissor(int x,int y,int w,int h) {
    dnd_async_require_application_thread();
    if(!active || pass || !rectangle(x,y,w,h)) return -1;
    scissor[0]=x;scissor[1]=y;scissor[2]=w;scissor[3]=h;dnd_draw_backend_scissor(x,y,w,h); return 0;
}
int dnd_device_wait(void) {
    dnd_async_require_application_thread();
    if(raw_depth) return -1;
    dnd_draw_backend_wait(); return 0;
}
int dnd_device_claim_external(void) {
    if(active || raw_depth) return -1;
    external=true;dnd_draw_backend_external();return 0;
}
int dnd_raw_enter(int kind) {
    dnd_async_require_application_thread();
    if((kind!=1&&kind!=2&&kind!=4) || (raw_depth&kind) || (kind!=1&&raw_depth) || (active&&pass!=3)) return -1;
    if(!raw_depth)dnd_async_enter_critical();
    raw_depth|=kind;return 0;
}
int dnd_raw_leave(int kind) {
    dnd_async_require_application_thread();
    if(!(raw_depth&kind) || (kind!=1&&(raw_depth&1)))return -1;
    raw_depth&=~kind;if(!raw_depth)dnd_async_leave_critical();return 0;
}
int dnd_raw_depth(void) { return raw_depth; }
void dnd_device_shutdown(void) {
    /* Native shutdown never executes user callbacks and retains pending errors. */
    if(raw_depth) { dnd_draw_backend_abort();raw_depth=0; dnd_async_leave_critical(); }
    dnd_draw_backend_wait(); active=false; pass=0; dnd_draw_shutdown(); external=false;
}
void dnd_immediate_begin(float r,float g,float b,float a) {
    if(dnd_device_begin(r,g,b,a)<0 || dnd_device_pass_begin(4)<0)
        dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Graphics frame already active or clear color invalid.");
}
void dnd_immediate_end(void) {
    if(!dnd_draw_backend_immediate_ready() || dnd_device_pass_end(4)<0 || dnd_device_end()<0)
        dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No complete immediate frame.");
}
