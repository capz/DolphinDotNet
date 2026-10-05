#include "dnd_graphics.h"
#include <gccore.h>
#include <GL/gl.h>
#include <malloc.h>
#include <string.h>

static GXRModeObj *mode;
static void *xfb[2];
static unsigned fb;
static void *fifo;
static int first_frame=1;

void dnd_graphics_init(void) {
    VIDEO_Init();
    mode=VIDEO_GetPreferredMode(NULL);
    xfb[0]=MEM_K0_TO_K1(SYS_AllocateFramebuffer(mode));
    xfb[1]=MEM_K0_TO_K1(SYS_AllocateFramebuffer(mode));
    VIDEO_Configure(mode);
    VIDEO_SetNextFramebuffer(xfb[0]);
    VIDEO_SetBlack(TRUE);
    VIDEO_Flush(); VIDEO_WaitVSync();
    if(mode->viTVMode & VI_NON_INTERLACE) VIDEO_WaitVSync();
    fifo=memalign(32,256*1024); memset(fifo,0,256*1024);
    GX_Init(fifo,256*1024);
    GX_SetCopyClear((GXColor){0,0,0,255},0x00ffffff);
    GX_SetViewport(0,0,mode->fbWidth,mode->efbHeight,0,1);
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight,mode->xfbHeight));
    GX_SetScissor(0,0,mode->fbWidth,mode->efbHeight);
    GX_SetDispCopySrc(0,0,mode->fbWidth,mode->efbHeight);
    GX_SetDispCopyDst(mode->fbWidth,mode->xfbHeight);
    GX_SetCopyFilter(mode->aa,mode->sample_pattern,GX_TRUE,mode->vfilter);
    GX_SetFieldMode(mode->field_rendering,((mode->viHeight==2*mode->xfbHeight)?GX_ENABLE:GX_DISABLE));
    glViewport(0,0,mode->fbWidth,mode->efbHeight);
    glEnable(GL_DEPTH_TEST);
}
void dnd_graphics_begin_frame(float r,float g,float b,float a) {
    glViewport(0,0,mode->fbWidth,mode->efbHeight);
    glClearColor(r,g,b,a); glClear(GL_COLOR_BUFFER_BIT|GL_DEPTH_BUFFER_BIT);
    glMatrixMode(GL_PROJECTION); glLoadIdentity();
    glFrustum(-0.8,0.8,-0.6,0.6,1.0,100.0);
    glMatrixMode(GL_MODELVIEW); glLoadIdentity();
}
void dnd_graphics_draw_demo(float angle) {
    glPushMatrix(); glTranslatef(0,0,-4); glRotatef(angle,0.4f,1.0f,0.2f);
    glBegin(GL_TRIANGLES);
    glColor3f(1,0.25f,0.15f); glVertex3f(0,1,0); glColor3f(0.15f,0.8f,0.35f); glVertex3f(-1,-1,0); glColor3f(0.2f,0.45f,1); glVertex3f(1,-1,0);
    glEnd(); glPopMatrix();
}
void dnd_graphics_begin_overlay(void) {
    glDisable(GL_DEPTH_TEST); glEnable(GL_BLEND); glBlendFunc(GL_SRC_ALPHA,GL_ONE_MINUS_SRC_ALPHA);
    glMatrixMode(GL_PROJECTION); glPushMatrix(); glLoadIdentity();
    glOrtho(0,mode->fbWidth,mode->efbHeight,0,-1,1);
    glMatrixMode(GL_MODELVIEW); glPushMatrix(); glLoadIdentity();
}
void dnd_graphics_overlay_rect(float x,float y,float w,float h,float r,float g,float b,float a) {
    glColor4f(r,g,b,a); glBegin(GL_QUADS);
    glVertex2f(x,y); glVertex2f(x+w,y); glVertex2f(x+w,y+h); glVertex2f(x,y+h); glEnd();
}
void dnd_graphics_end_frame(void) {
    glMatrixMode(GL_MODELVIEW); glPopMatrix(); glMatrixMode(GL_PROJECTION); glPopMatrix();
    glDisable(GL_BLEND); glEnable(GL_DEPTH_TEST); glFlush();
    GX_SetZMode(GX_TRUE,GX_LEQUAL,GX_TRUE); GX_SetColorUpdate(GX_TRUE);
    GX_CopyDisp(xfb[fb],GX_TRUE); GX_DrawDone(); VIDEO_SetNextFramebuffer(xfb[fb]);
    if(first_frame){ first_frame=0; VIDEO_SetBlack(FALSE); }
    VIDEO_Flush(); VIDEO_WaitVSync(); fb^=1;
}
int dnd_graphics_width(void){return mode?mode->fbWidth:640;}
int dnd_graphics_height(void){return mode?mode->efbHeight:480;}
