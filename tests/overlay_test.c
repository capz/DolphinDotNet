#include "dnd_console.h"
#include "dnd_graphics.h"
#include "dnd_performance.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

static unsigned rectangles, expected_quads, emitted_quads;
static float panel_left, panel_top, panel_width, panel_height;
static int screen_height = 480;
static bool performance_panel = true;
int dnd_graphics_width(void) { return 640; }
int dnd_graphics_height(void) { return screen_height; }
void dnd_graphics_overlay_rect(float x, float y, float w, float h,
                               float r, float g, float b, float a) {
  assert(r == 0 && g == 0 && b == 0);
  panel_left = x; panel_top = y; panel_width = w; panel_height = h;
  if (performance_panel) {
    assert(a == 1 && x == 8 && fabsf(y + h + 8 - screen_height) < 0.001f);
    assert(y > screen_height / 2 && x + w <= 640);
  } else assert(a == .62f && x == 0 && y == 0);
  rectangles++;
}
void dnd_graphics_overlay_glyph_begin(unsigned quads, unsigned char shade) {
  assert(shade == (performance_panel ? 208 : 255) && quads > 0);
  expected_quads = quads;
  emitted_quads = 0;
}
void dnd_graphics_overlay_glyph_quad(float x, float y, float size) {
  assert(x >= panel_left && y >= panel_top);
  assert(x + size <= panel_left + panel_width && y + size <= panel_top + panel_height);
  emitted_quads++;
}
void dnd_graphics_overlay_glyph_end(void) { assert(emitted_quads == expected_quads); }

int main(void) {
  char lines[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS];
  assert(!dnd_performance_visible());
  dnd_console_init();
  dnd_console_set_visible(0);
  dnd_console_render();
  assert(rectangles == 0);
  dnd_performance_set_visible(1);
  dnd_performance_begin_frame();
  dnd_performance_add_triangles(10);
  dnd_performance_add_triangles(12);
  assert(dnd_performance_stats()->triangles == 22);
  dnd_performance_format(lines);
  assert(strstr(lines[0], "--") && strcmp(lines[1], "TRIS: 22") == 0);
  assert(strcmp(lines[3], "ARAM: NOT INITIALIZED") == 0);
  uint64_t now = 0;
  dnd_performance_presented(now);
  for (int i = 0; i < 40; i++) { now += 20000; dnd_performance_presented(now); }
  assert(dnd_performance_stats()->fps_tenths == 500);
  assert(dnd_performance_stats()->frame_centimilliseconds == 2000);
  // Reassigning an enabled toggle must preserve the current timing window.
  dnd_performance_set_visible(1);
  assert(dnd_performance_stats()->timing_valid);
  assert(dnd_performance_memory_due(now));
  dnd_performance_memory(now, 6 * 1048576, 24 * 1048576, true, 2 * 1048576, 16 * 1048576);
  assert(!dnd_performance_memory_due(now + 249999));
  assert(dnd_performance_memory_due(now + 250000));
  dnd_performance_format(lines);
  assert(strcmp(lines[0], "FPS: 50.0  FRAME: 20.00 MS") == 0);
  assert(strcmp(lines[2], "MAIN: 6.00 / 24.00 MIB") == 0);
  assert(strcmp(lines[3], "ARAM: 2.00 / 16.00 MIB") == 0);
  dnd_console_render();
  assert(rectangles == 1);
  screen_height = 576;
  dnd_console_render();
  assert(rectangles == 2);
  for (int i = 0; i < 40; i++) { now += 40000; dnd_performance_presented(now); }
  assert(dnd_performance_stats()->fps_tenths == 250);
  assert(dnd_performance_stats()->frame_centimilliseconds == 4000);
  dnd_performance_add_triangles(UINT32_MAX);
  assert(dnd_performance_stats()->triangles == UINT32_MAX);
  dnd_performance_begin_frame();
  assert(dnd_performance_stats()->triangles == 0);
  dnd_performance_presented(1); // Clock reset is a new baseline, never an underflow.
  assert(!dnd_performance_stats()->timing_valid);
  dnd_performance_presented(UINT64_MAX);
  assert(dnd_performance_stats()->frame_centimilliseconds == UINT32_MAX);
  dnd_performance_set_visible(0);
  dnd_console_render();
  assert(rectangles == 2);
  dnd_performance_set_visible(1);
  assert(!dnd_performance_stats()->timing_valid && dnd_performance_memory_due(1));
  // Hiding the performance panel must leave the independent text log usable.
  dnd_performance_set_visible(0);
  performance_panel = false;
  dnd_console_set_visible(1);
  dnd_console_write_line("LOG STILL VISIBLE");
  dnd_console_render();
  assert(rectangles == 3);
  puts("Performance timing, counters, toggle, color and bottom-left layout passed.");
  return 0;
}
