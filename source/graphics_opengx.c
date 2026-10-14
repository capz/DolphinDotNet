#include "dnd_console.h"
#include "dnd_graphics.h"
#include "dnd_performance.h"
#include "dnd_aram.h"
#include <GL/gl.h>
/* OpenGX requires explicit state initialization after GX_Init. */
extern void ogx_initialize(void);
#include <gccore.h>
#include <malloc.h>
#include <ogc/timesupp.h>
#include <string.h>

static GXRModeObj *mode;
static void *xfb[2];
static unsigned fb;
static void *fifo;
static int first_frame = 1;
static int native_model_frame;
static unsigned glyph_quads, glyph_batch;
static unsigned char glyph_shade;

static void sample_performance_memory(void) {
  if (!dnd_performance_visible()) return;
  uint64_t now = ticks_to_microsecs(gettime());
  if (!dnd_performance_memory_due(now)) return;
  uint32_t total = SYS_GetPhysicalMem1Size();
  struct mallinfo info = mallinfo();
  // Count code, static storage, framebuffers, stacks and native heap allocations.
  uint64_t free_bytes = (uint64_t)SYS_GetArena1Size() + info.fordblks;
  uint32_t used = free_bytes < total ? total - (uint32_t)free_bytes : 0;
  uint32_t aram_used = 0, aram_total = 0;
  bool aram_available = dnd_aram_memory_usage(&aram_used, &aram_total);
  dnd_performance_memory(now, used, total, aram_available, aram_used, aram_total);
}

static void performance_presented(void) {
  if (dnd_performance_visible())
    dnd_performance_presented(ticks_to_microsecs(gettime()));
}

void dnd_graphics_init(void) {
  VIDEO_Init();
  mode = VIDEO_GetPreferredMode(NULL);
  xfb[0] = MEM_K0_TO_K1(SYS_AllocateFramebuffer(mode));
  xfb[1] = MEM_K0_TO_K1(SYS_AllocateFramebuffer(mode));
  VIDEO_Configure(mode);
  VIDEO_SetNextFramebuffer(xfb[0]);
  VIDEO_SetBlack(TRUE);
  VIDEO_Flush();
  VIDEO_WaitVSync();
  if (mode->viTVMode & VI_NON_INTERLACE)
    VIDEO_WaitVSync();
  fifo = memalign(32, 256 * 1024);
  memset(fifo, 0, 256 * 1024);
  GX_Init(fifo, 256 * 1024);
  ogx_initialize();
  GX_SetCopyClear((GXColor){0, 0, 0, 255}, 0x00ffffff);
  GX_SetViewport(0, 0, mode->fbWidth, mode->efbHeight, 0, 1);
  GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight, mode->xfbHeight));
  GX_SetScissor(0, 0, mode->fbWidth, mode->efbHeight);
  GX_SetDispCopySrc(0, 0, mode->fbWidth, mode->efbHeight);
  GX_SetDispCopyDst(mode->fbWidth, mode->xfbHeight);
  GX_SetCopyFilter(mode->aa, mode->sample_pattern, GX_TRUE, mode->vfilter);
  GX_SetFieldMode(
      mode->field_rendering,
      ((mode->viHeight == 2 * mode->xfbHeight) ? GX_ENABLE : GX_DISABLE));
  glViewport(0, 0, mode->fbWidth, mode->efbHeight);
  glEnable(GL_DEPTH_TEST);
}
void dnd_graphics_begin_frame(float r, float g, float b, float a) {
  dnd_performance_begin_frame();
  glViewport(0, 0, mode->fbWidth, mode->efbHeight);
  glClearColor(r, g, b, a);
  glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
  glMatrixMode(GL_PROJECTION);
  glLoadIdentity();
  glMatrixMode(GL_MODELVIEW);
  glLoadIdentity();
}
void dnd_graphics_frustum(float left, float right, float bottom, float top,
                          float near, float far) {
  glMatrixMode(GL_PROJECTION);
  glLoadIdentity();
  glFrustum(left, right, bottom, top, near, far);
  glMatrixMode(GL_MODELVIEW);
}
void dnd_graphics_translate(float x, float y, float z) { glTranslatef(x, y, z); }
void dnd_graphics_rotate(float degrees, float x, float y, float z) {
  glRotatef(degrees, x, y, z);
}
static unsigned triangle_vertices;
void dnd_graphics_begin_triangles(void) {
  triangle_vertices = 0;
  glBegin(GL_TRIANGLES);
}
void dnd_graphics_color(float r, float g, float b) { glColor3f(r, g, b); }
void dnd_graphics_vertex(float x, float y, float z) {
  glVertex3f(x, y, z);
  triangle_vertices++;
}
void dnd_graphics_end_triangles(void) {
  glEnd();
  dnd_performance_add_triangles(triangle_vertices / 3);
}
void dnd_graphics_present(void) {
  dnd_graphics_begin_overlay();
  dnd_console_render();
  dnd_graphics_end_frame();
}
void dnd_graphics_begin_overlay(void) {
  sample_performance_memory();
  if (native_model_frame) {
    Mtx44 projection;
    Mtx view;
    guMtxIdentity(view);
    guOrtho(projection, 0, mode->efbHeight, 0, mode->fbWidth, -1, 1);
    GX_LoadProjectionMtx(projection, GX_ORTHOGRAPHIC);
    GX_LoadPosMtxImm(view, GX_PNMTX0);
    GX_SetCurrentMtx(GX_PNMTX0);
    GX_SetZMode(GX_FALSE, GX_ALWAYS, GX_FALSE);
    GX_SetCullMode(GX_CULL_NONE);
    GX_SetAlphaCompare(GX_ALWAYS, 0, GX_AOP_AND, GX_ALWAYS, 0);
    GX_SetBlendMode(GX_BM_BLEND, GX_BL_SRCALPHA, GX_BL_INVSRCALPHA,
                    GX_LO_CLEAR);
    GX_SetNumTexGens(0);
    GX_SetNumChans(1);
    GX_SetNumTevStages(1);
    GX_SetChanCtrl(GX_COLOR0A0, GX_DISABLE, GX_SRC_REG, GX_SRC_VTX,
                   GX_LIGHTNULL, GX_DF_NONE, GX_AF_NONE);
    GX_SetTevOrder(GX_TEVSTAGE0, GX_TEXCOORDNULL, GX_TEXMAP_NULL, GX_COLOR0A0);
    GX_SetTevOp(GX_TEVSTAGE0, GX_PASSCLR);
    GX_ClearVtxDesc();
    GX_SetVtxDesc(GX_VA_POS, GX_DIRECT);
    GX_SetVtxDesc(GX_VA_CLR0, GX_DIRECT);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_POS, GX_POS_XY, GX_F32, 0);
    GX_SetVtxAttrFmt(GX_VTXFMT0, GX_VA_CLR0, GX_CLR_RGBA, GX_RGBA8, 0);
    return;
  }
  glDisable(GL_DEPTH_TEST);
  glEnable(GL_BLEND);
  glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
  glMatrixMode(GL_PROJECTION);
  glPushMatrix();
  glLoadIdentity();
  glOrtho(0, mode->fbWidth, mode->efbHeight, 0, -1, 1);
  glMatrixMode(GL_MODELVIEW);
  glPushMatrix();
  glLoadIdentity();
}
void dnd_graphics_overlay_rect(float x, float y, float w, float h, float r,
                               float g, float b, float a) {
  if (native_model_frame) {
    GX_Begin(GX_QUADS, GX_VTXFMT0, 4);
    GX_Position2f32(x, y);
    GX_Color4u8((u8)(r * 255), (u8)(g * 255), (u8)(b * 255), (u8)(a * 255));
    GX_Position2f32(x + w, y);
    GX_Color4u8((u8)(r * 255), (u8)(g * 255), (u8)(b * 255), (u8)(a * 255));
    GX_Position2f32(x + w, y + h);
    GX_Color4u8((u8)(r * 255), (u8)(g * 255), (u8)(b * 255), (u8)(a * 255));
    GX_Position2f32(x, y + h);
    GX_Color4u8((u8)(r * 255), (u8)(g * 255), (u8)(b * 255), (u8)(a * 255));
    GX_End();
    return;
  }
  glColor4f(r, g, b, a);
  glBegin(GL_QUADS);
  glVertex2f(x, y);
  glVertex2f(x + w, y);
  glVertex2f(x + w, y + h);
  glVertex2f(x, y + h);
  glEnd();
}
void dnd_graphics_end_frame(void) {
  glMatrixMode(GL_MODELVIEW);
  glPopMatrix();
  glMatrixMode(GL_PROJECTION);
  glPopMatrix();
  glDisable(GL_BLEND);
  glEnable(GL_DEPTH_TEST);
  glFlush();
  GX_SetZMode(GX_TRUE, GX_LEQUAL, GX_TRUE);
  GX_SetColorUpdate(GX_TRUE);
  GX_CopyDisp(xfb[fb], GX_TRUE);
  GX_DrawDone();
  VIDEO_SetNextFramebuffer(xfb[fb]);
  if (first_frame) {
    first_frame = 0;
    VIDEO_SetBlack(FALSE);
  }
  VIDEO_Flush();
  VIDEO_WaitVSync();
  fb ^= 1;
  performance_presented();
}
int dnd_graphics_width(void) { return mode ? mode->fbWidth : 640; }
int dnd_graphics_height(void) { return mode ? mode->efbHeight : 480; }

/* Explicit GX frame path. OpenGX overlay state is re-established at frame end.
 */
void dnd_graphics_model_begin(void) {
  native_model_frame = 1;
  glFlush();
  GX_DrawDone();
  GX_SetViewport(0, 0, mode->fbWidth, mode->efbHeight, 0, 1);
  GX_SetScissor(0, 0, mode->fbWidth, mode->efbHeight);
  GX_SetCopyClear((GXColor){6, 9, 15, 255}, 0x00ffffff);
  GX_SetColorUpdate(GX_TRUE);
  GX_SetAlphaUpdate(GX_TRUE);
  GX_SetNumIndStages(0);
  GX_SetTevDirect(GX_TEVSTAGE0);
  GX_SetZMode(GX_TRUE, GX_LEQUAL, GX_TRUE);
  GX_CopyDisp(xfb[fb], GX_TRUE);
  GX_DrawDone();
}
void dnd_graphics_model_end(void) {
  dnd_graphics_begin_overlay();
  dnd_console_render();
  GX_DrawDone();
  native_model_frame = 0;
  /* Model state is entirely GX-owned during the frame. Present before allowing
   * mutation. */
  GX_SetZMode(GX_TRUE, GX_LEQUAL, GX_TRUE);
  GX_SetColorUpdate(GX_TRUE);
  GX_CopyDisp(xfb[fb], GX_TRUE);
  GX_DrawDone();
  VIDEO_SetNextFramebuffer(xfb[fb]);
  if (first_frame) {
    first_frame = 0;
    VIDEO_SetBlack(FALSE);
  }
  VIDEO_Flush();
  VIDEO_WaitVSync();
  fb ^= 1;
  performance_presented();
}

void dnd_graphics_overlay_glyph_begin(unsigned quads, unsigned char shade) {
  glyph_quads = quads;
  glyph_batch = 0;
  glyph_shade = shade;
  if (!native_model_frame) {
    float gray = shade / 255.0f;
    glColor4f(gray, gray, gray, 1);
    glBegin(GL_QUADS);
  }
}
void dnd_graphics_overlay_glyph_quad(float x, float y, float size) {
  if (native_model_frame) {
    if (!glyph_batch) {
      glyph_batch = glyph_quads > 16383 ? 16383 : glyph_quads;
      GX_Begin(GX_QUADS, GX_VTXFMT0, (u16)(glyph_batch * 4));
    }
    GX_Position2f32(x, y);
    GX_Color4u8(glyph_shade, glyph_shade, glyph_shade, 255);
    GX_Position2f32(x + size, y);
    GX_Color4u8(glyph_shade, glyph_shade, glyph_shade, 255);
    GX_Position2f32(x + size, y + size);
    GX_Color4u8(glyph_shade, glyph_shade, glyph_shade, 255);
    GX_Position2f32(x, y + size);
    GX_Color4u8(glyph_shade, glyph_shade, glyph_shade, 255);
    glyph_quads--;
    if (--glyph_batch == 0)
      GX_End();
  } else {
    glVertex2f(x, y);
    glVertex2f(x + size, y);
    glVertex2f(x + size, y + size);
    glVertex2f(x, y + size);
  }
}
void dnd_graphics_overlay_glyph_end(void) {
  if (!native_model_frame)
    glEnd();
}
