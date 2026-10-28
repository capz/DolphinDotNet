#ifndef DND_GRAPHICS_H
#define DND_GRAPHICS_H
#include <stdbool.h>
void dnd_graphics_init(void);
void dnd_graphics_model_begin(void);
void dnd_graphics_model_end(void);
void dnd_graphics_begin_frame(float r,float g,float b,float a);
void dnd_graphics_frustum(float left,float right,float bottom,float top,float near,float far);
void dnd_graphics_translate(float x,float y,float z);
void dnd_graphics_rotate(float degrees,float x,float y,float z);
void dnd_graphics_begin_triangles(void);
void dnd_graphics_color(float r,float g,float b);
void dnd_graphics_vertex(float x,float y,float z);
void dnd_graphics_end_triangles(void);
void dnd_graphics_present(void);
void dnd_graphics_begin_overlay(void);
void dnd_graphics_overlay_rect(float x,float y,float w,float h,float r,float g,float b,float a);
void dnd_graphics_overlay_glyph_begin(unsigned quads,unsigned char shade);
void dnd_graphics_overlay_glyph_rect(float x,float y,float width,float height);
void dnd_graphics_overlay_glyph_quad(float x,float y,float size);
void dnd_graphics_overlay_glyph_end(void);
void dnd_graphics_overlay_atlas_begin(unsigned quads,unsigned char shade);
void dnd_graphics_overlay_atlas_glyph(unsigned char character,float x,float y,float scale);
bool dnd_graphics_overlay_cached_text(const char *const *text,int rows,float left,float top,unsigned char shade);
void dnd_graphics_end_frame(void);
int dnd_graphics_width(void);
int dnd_graphics_height(void);
#endif
