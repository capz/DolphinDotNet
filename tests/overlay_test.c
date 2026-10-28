#include "dnd_console.h"
#include "dnd_graphics.h"
#include "dnd_performance.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

static unsigned char raster[640*576],reference_raster[640*576];
static unsigned total_glyph_quads;
static unsigned rectangles, expected_quads, emitted_quads;
static float panel_left, panel_top, panel_width, panel_height;
static int screen_height = 480;
static bool performance_panel = true;
#ifdef DND_DEBUG_TELEMETRY
/* Reporting layout tests do not configure a live UDP channel. */
int dnd_np_monitor_open(void) { assert(0); return -2; }
int dnd_np_monitor_read(int fd,void *b,int n,uint32_t *ip,int *port) {(void)fd;(void)b;(void)n;(void)ip;(void)port;assert(0);return -1;}
int dnd_np_monitor_write(int fd,const void *b,int n,uint32_t ip,int port) {(void)fd;(void)b;(void)n;(void)ip;(void)port;assert(0);return -2;}
void dnd_np_close(int fd) {(void)fd;assert(0);}
#endif
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
  total_glyph_quads+=quads;
  assert(shade == (performance_panel ? 208 : 255) && quads > 0);
  expected_quads = quads;
  emitted_quads = 0;
}
void dnd_graphics_overlay_glyph_rect(float x,float y,float width,float height) {
  for(int py=(int)ceilf(y-.5f);py<(int)ceilf(y+height-.5f);py++)
    for(int px=(int)ceilf(x-.5f);px<(int)ceilf(x+width-.5f);px++)
      if(px>=0&&px<640&&py>=0&&py<576)raster[py*640+px]=1;
  emitted_quads++;
}
void dnd_graphics_overlay_glyph_quad(float x,float y,float size) {
  assert(x>=0&&y>=0&&fabsf(size-1.35f)<.001f);
  dnd_graphics_overlay_glyph_rect(x,y,size,size);
}
void dnd_graphics_overlay_glyph_end(void) { assert(emitted_quads == expected_quads); }

int main(void) {
  dnd_performance_compact_glyphs(0);
  char lines[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS];
  assert(!dnd_performance_visible());
  assert(!dnd_performance_connectivity_due(0));
  dnd_console_init();
  dnd_console_set_visible(0);
  dnd_console_render();
  assert(rectangles == 0);
  dnd_performance_set_visible(1);
  dnd_performance_frame_waits(1200, 16000);
  assert(dnd_performance_render_stat(3)==1200);
  assert(dnd_performance_render_stat(4)==16000);
  dnd_performance_begin_frame();
  assert(dnd_performance_render_stat(3)==0 && dnd_performance_render_stat(4)==0);

  dnd_performance_add_triangles(10);
  dnd_performance_add_triangles(12);
  assert(dnd_performance_stats()->triangles == 22);
  assert(dnd_performance_format(lines) == 5);
  assert(dnd_performance_connectivity_due(0));
  dnd_performance_connectivity_sampled(0, 1);
  assert(!dnd_performance_connectivity_due(1999999));
  assert(dnd_performance_connectivity_due(2000000));
  dnd_performance_connectivity_sampled(2000000, 0);
  assert(!dnd_performance_connectivity_due(3999999));
  assert(dnd_performance_connectivity_due(4000000));
  assert(dnd_performance_connectivity_due(1)); /* Clock reset. */
  dnd_performance_set_visible(0);
  assert(!dnd_performance_connectivity_due(UINT64_MAX));
  dnd_performance_set_visible(1);
  assert(dnd_performance_stats()->network_link == -1);
  assert(dnd_performance_connectivity_due(0)); /* Fresh sample on show. */
  dnd_performance_connectivity_sampled(0, -1);
  dnd_performance_set_visible(1);
  assert(!dnd_performance_connectivity_due(1)); /* Reassigning true preserves cadence. */
#ifdef DND_DEBUG_TELEMETRY
  assert(dnd_debugger_telemetry[0] == DND_DEBUGGER_TELEMETRY_MAGIC);
  assert(strcmp(lines[4], "DEBUG LAST: UNKNOWN") == 0);
  dnd_debugger_telemetry[1] = 1;
  dnd_performance_format(lines);
  assert(strcmp(lines[4], "DEBUG LAST: CONNECTED") == 0);
  dnd_debugger_telemetry[1] = 2;
  dnd_performance_format(lines);
  assert(strcmp(lines[4], "DEBUG LAST: DISCONNECTED") == 0);
#else
  assert(strcmp(lines[4], "DEBUG LAST: UNAVAILABLE") == 0);
#endif
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
  dnd_performance_network(0, true, 0, 0, 0);
  assert(dnd_performance_format(lines) == 9);
  assert(strcmp(lines[4], "BBA: CONFIGURING - DHCP") == 0 && strcmp(lines[5], "IP: --") == 0);
  dnd_performance_network(1, true, UINT32_C(0xc0a8012a), UINT32_C(0xffffff00), UINT32_C(0xc0a80101));
  dnd_performance_connectivity_sampled(now, 1);
  assert(dnd_performance_format(lines) == 9);
  assert(strcmp(lines[4], "BBA: READY - DHCP - LINK UP") == 0);
  assert(strcmp(lines[5], "IP: 192.168.1.42") == 0);
  assert(strcmp(lines[6], "MASK: 255.255.255.0") == 0);
  assert(strcmp(lines[7], "GW: 192.168.1.1") == 0);
  dnd_performance_connectivity_sampled(now, 0);
  dnd_performance_format(lines);
  assert(strcmp(lines[4], "BBA: READY - DHCP - LINK DOWN") == 0);
  assert(strcmp(lines[5], "IP: 192.168.1.42") == 0); /* Keep configuration on cable loss. */
  dnd_performance_connectivity_sampled(now, -1);
  dnd_performance_format(lines);
  assert(strcmp(lines[4], "BBA: READY - DHCP - LINK ?") == 0);
  dnd_console_render();
  assert(rectangles == 1);
  screen_height = 576;
  dnd_console_render();
  assert(rectangles == 2);
  dnd_performance_network(2, false, UINT32_C(0x0a000002), UINT32_C(0xff000000), 0);
  dnd_performance_format(lines);
  assert(strcmp(lines[4], "BBA: DEBUG TCP CONFIG - STATIC - LINK ?") == 0 && strcmp(lines[5], "IP: 10.0.0.2") == 0);
  dnd_performance_network(-2, true, UINT32_MAX, UINT32_MAX, UINT32_MAX);
  dnd_performance_format(lines);
  assert(strcmp(lines[4], "BBA: CONFIG FAILED - DHCP") == 0 && strcmp(lines[5], "IP: --") == 0);
  dnd_performance_network(-1, false, 0, 0, 0);
  assert(dnd_performance_format(lines) == 5 && lines[5][0] == 0 && lines[8][0] == 0);
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
  dnd_console_write_line("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
  dnd_console_write_line("abcdefghijklmnopqrstuvwxyz -+/*.:!?%()[]=_<>#");
  memset(raster,0,sizeof raster);total_glyph_quads=0;dnd_console_render();
  memcpy(reference_raster,raster,sizeof raster);unsigned old_quads=total_glyph_quads;
  dnd_performance_compact_glyphs(1);memset(raster,0,sizeof raster);total_glyph_quads=0;dnd_console_render();
  assert(!memcmp(reference_raster,raster,sizeof raster));
  assert(total_glyph_quads<old_quads);
  printf("Glyph runs preserve rasterized text pixels: %u -> %u quads.\n",old_quads,total_glyph_quads);
  dnd_performance_compact_glyphs(2);memset(raster,0,sizeof raster);total_glyph_quads=0;dnd_console_render();
  assert(!memcmp(reference_raster,raster,sizeof raster));assert(total_glyph_quads<old_quads/2);
  printf("Glyph rectangles preserve rasterized text pixels: %u -> %u quads.\n",old_quads,total_glyph_quads);
  puts("Performance timing, counters, BBA/debugger status, toggle, color and layout passed.");
  return 0;
}
