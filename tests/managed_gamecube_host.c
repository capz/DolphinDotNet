/* Execute the actual AOT sample with a recording graphics backend. */
#include "dnd_graphics.h"
#include "dnd_input.h"
#include "dnd_managed.h"
#include "dnd_performance.h"
#include "dnd_timing.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static unsigned stage, polls, frames, ticks;
int64_t dnd_timing_now(void) {
  static const int64_t times[] = {1000000, 1050000, 1250000};
  assert(ticks < 3); return times[ticks++];
}
static void next(unsigned expected) { assert(stage++ == frames * 13 + expected); }
static void close_to(float actual, float expected) { assert(fabsf(actual - expected) < 0.0001f); }
void dnd_console_write_line(const char *text) {
  assert(strcmp(text, "csharp with dolphin dotnet") == 0);
}
void dnd_input_backend_init(void) { }
void dnd_input_backend_shutdown(void) { }
void dnd_input_backend_sample(DndInputSample pads[4]) {
  pads[0].connected = true;
  pads[0].held = ++polls < 4 ? 0 : DND_PAD_BUTTON_START;
}
bool dnd_input_backend_rumble(int port, uint32_t duration) { (void)port; (void)duration; return false; }
void dnd_input_backend_stop_rumble(int port) { (void)port; }
void dnd_graphics_begin_frame(float r, float g, float b, float a) {
  next(0); close_to(r, 0.025f); close_to(g, 0.035f); close_to(b, 0.06f); close_to(a, 1);
}
void dnd_graphics_frustum(float l, float r, float b, float t, float n, float f) {
  next(1); close_to(l, -0.8f); close_to(r, 0.8f); close_to(b, -0.6f);
  close_to(t, 0.6f); close_to(n, 1); close_to(f, 100);
}
void dnd_graphics_translate(float x, float y, float z) {
  next(2); close_to(x, 0); close_to(y, 0); close_to(z, -4);
}
void dnd_graphics_rotate(float angle, float x, float y, float z) {
  static const float angles[] = {0, 3, 9};
  next(3); close_to(angle, angles[frames]); close_to(x, 0.4f); close_to(y, 1); close_to(z, 0.2f);
}
void dnd_graphics_begin_triangles(void) { next(4); }
void dnd_graphics_color(float r, float g, float b) {
  static const float colors[][3] = {{1, 0.25f, 0.15f}, {0.15f, 0.8f, 0.35f}, {0.2f, 0.45f, 1}};
  unsigned local = stage % 13;
  assert(local == 5 || local == 7 || local == 9);
  unsigned i = (local - 5) / 2;
  close_to(r, colors[i][0]); close_to(g, colors[i][1]); close_to(b, colors[i][2]);
  stage++;
}
void dnd_graphics_vertex(float x, float y, float z) {
  static const float vertices[][3] = {{0, 1, 0}, {-1, -1, 0}, {1, -1, 0}};
  unsigned local = stage % 13;
  assert(local == 6 || local == 8 || local == 10);
  unsigned i = (local - 6) / 2;
  close_to(x, vertices[i][0]); close_to(y, vertices[i][1]); close_to(z, vertices[i][2]);
  stage++;
}
void dnd_graphics_end_triangles(void) { next(11); }
void dnd_graphics_present(void) { next(12); frames++; }
int main(void) {
  unsigned char memory[256 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  assert(dnd_aot_entry(&heap) == 0);
  assert(stage == 39 && polls == 4 && frames == 3 && ticks == 3);
  assert(dnd_performance_visible());
  puts("Managed sample submits frames with elapsed-time rotation and stall clamping.");
  return 0;
}

void dnd_graphics_init(void){}
int dnd_graphics_width(void){return 640;}
int dnd_graphics_height(void){return 480;}
