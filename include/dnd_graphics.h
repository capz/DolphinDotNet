#ifndef DND_GRAPHICS_H
#define DND_GRAPHICS_H
void dnd_graphics_init(void);
void dnd_graphics_model_begin(void);
void dnd_graphics_model_end(void);
void dnd_graphics_begin_frame(float r,float g,float b,float a);
void dnd_graphics_draw_demo(float rotation_degrees);
void dnd_graphics_begin_overlay(void);
void dnd_graphics_overlay_rect(float x,float y,float w,float h,float r,float g,float b,float a);
void dnd_graphics_overlay_glyph_begin(unsigned quads,unsigned char shade);
void dnd_graphics_overlay_glyph_quad(float x,float y,float size);
void dnd_graphics_overlay_glyph_end(void);
void dnd_graphics_end_frame(void);
int dnd_graphics_width(void);
int dnd_graphics_height(void);
#endif
