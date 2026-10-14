/* Execute the actual AOT sample with a recording graphics backend. */
#include "dnd_graphics.h"
#include "dnd_input.h"
#include "dnd_managed.h"
#include "dnd_performance.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>

extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
static unsigned stage, polls;
static void next(unsigned expected) { assert(stage++ == expected); }
static void close_to(float actual, float expected) { assert(fabsf(actual - expected) < 0.0001f); }
void dnd_console_write_line(const char *text) {
  assert(strcmp(text, "csharp with dolphin dotnet") == 0);
}
void dnd_input_backend_init(void) { }
void dnd_input_backend_shutdown(void) { }
void dnd_input_backend_sample(DndInputSample pads[4]) {
  pads[0].connected = true;
  pads[0].held = ++polls == 1 ? 0 : DND_PAD_BUTTON_START;
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
  next(3); close_to(angle, 1); close_to(x, 0.4f); close_to(y, 1); close_to(z, 0.2f);
}
void dnd_graphics_begin_triangles(void) { next(4); }
void dnd_graphics_color(float r, float g, float b) {
  static const float colors[][3] = {{1, 0.25f, 0.15f}, {0.15f, 0.8f, 0.35f}, {0.2f, 0.45f, 1}};
  assert(stage == 5 || stage == 7 || stage == 9);
  unsigned i = (stage - 5) / 2;
  close_to(r, colors[i][0]); close_to(g, colors[i][1]); close_to(b, colors[i][2]);
  stage++;
}
void dnd_graphics_vertex(float x, float y, float z) {
  static const float vertices[][3] = {{0, 1, 0}, {-1, -1, 0}, {1, -1, 0}};
  assert(stage == 6 || stage == 8 || stage == 10);
  unsigned i = (stage - 6) / 2;
  close_to(x, vertices[i][0]); close_to(y, vertices[i][1]); close_to(z, vertices[i][2]);
  stage++;
}
void dnd_graphics_end_triangles(void) { next(11); }
void dnd_graphics_present(void) { next(12); }
int main(void) {
  unsigned char memory[256 * 1024];
  DndManagedHeap heap;
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  assert(dnd_aot_entry(&heap) == 0);
  assert(stage == 13 && polls == 2);
  assert(dnd_performance_visible());
  puts("Managed sample owns and submits the complete demo frame.");
  return 0;
}
