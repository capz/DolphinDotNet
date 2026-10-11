#include "dnd_console.h"
#include "dnd_graphics.h"
#include "dnd_performance.h"
#include <stdio.h>
#include <string.h>
#define ROWS 10
#define COLS 78
static char lines[ROWS][COLS + 1];
static int head, count, visible = 1;
static const unsigned char font[43][5] = {
    {0x7e, 0x11, 0x11, 0x11, 0x7e}, {0x7f, 0x49, 0x49, 0x49, 0x36},
    {0x3e, 0x41, 0x41, 0x41, 0x22}, {0x7f, 0x41, 0x41, 0x22, 0x1c},
    {0x7f, 0x49, 0x49, 0x49, 0x41}, {0x7f, 0x09, 0x09, 0x09, 0x01},
    {0x3e, 0x41, 0x49, 0x49, 0x7a}, {0x7f, 0x08, 0x08, 0x08, 0x7f},
    {0x00, 0x41, 0x7f, 0x41, 0x00}, {0x20, 0x40, 0x41, 0x3f, 0x01},
    {0x7f, 0x08, 0x14, 0x22, 0x41}, {0x7f, 0x40, 0x40, 0x40, 0x40},
    {0x7f, 0x02, 0x0c, 0x02, 0x7f}, {0x7f, 0x04, 0x08, 0x10, 0x7f},
    {0x3e, 0x41, 0x41, 0x41, 0x3e}, {0x7f, 0x09, 0x09, 0x09, 0x06},
    {0x3e, 0x41, 0x51, 0x21, 0x5e}, {0x7f, 0x09, 0x19, 0x29, 0x46},
    {0x46, 0x49, 0x49, 0x49, 0x31}, {0x01, 0x01, 0x7f, 0x01, 0x01},
    {0x3f, 0x40, 0x40, 0x40, 0x3f}, {0x1f, 0x20, 0x40, 0x20, 0x1f},
    {0x3f, 0x40, 0x38, 0x40, 0x3f}, {0x63, 0x14, 0x08, 0x14, 0x63},
    {0x07, 0x08, 0x70, 0x08, 0x07}, {0x61, 0x51, 0x49, 0x45, 0x43},
    {0x3e, 0x51, 0x49, 0x45, 0x3e}, {0x00, 0x42, 0x7f, 0x40, 0x00},
    {0x42, 0x61, 0x51, 0x49, 0x46}, {0x21, 0x41, 0x45, 0x4b, 0x31},
    {0x18, 0x14, 0x12, 0x7f, 0x10}, {0x27, 0x45, 0x45, 0x45, 0x39},
    {0x3c, 0x4a, 0x49, 0x49, 0x30}, {0x01, 0x71, 0x09, 0x05, 0x03},
    {0x36, 0x49, 0x49, 0x49, 0x36}, {0x06, 0x49, 0x49, 0x29, 0x1e},
    {0x00, 0x36, 0x36, 0x00, 0x00}, {0x00, 0x60, 0x60, 0x00, 0x00},
    {0x08, 0x08, 0x08, 0x08, 0x08}, {0x14, 0x14, 0x14, 0x14, 0x14},
    {0x00, 0x00, 0x5f, 0x00, 0x00}, {0x02, 0x01, 0x51, 0x09, 0x06},
    {0x20, 0x10, 0x08, 0x04, 0x02}};
static const unsigned char *glyph(char c) {
  if (c >= 'a' && c <= 'z')
    c -= 32;
  if (c >= 'A' && c <= 'Z')
    return font[c - 'A'];
  if (c >= '0' && c <= '9')
    return font[26 + c - '0'];
  if (c == ':')
    return font[36];
  if (c == '.')
    return font[37];
  if (c == '-')
    return font[38];
  if (c == '=')
    return font[39];
  if (c == '!')
    return font[40];
  if (c == '?')
    return font[41];
  if (c == '/')
    return font[42];
  return 0;
}
static void newline(const char *s) {
  head = (head + 1) % ROWS;
  strncpy(lines[head], s, COLS);
  lines[head][COLS] = 0;
  if (count < ROWS)
    count++;
}
void dnd_console_init(void) {
  memset(lines, 0, sizeof(lines));
  head = ROWS - 1;
  count = 0;
}
void dnd_console_write_line(const char *s) { newline(s ? s : ""); }
void dnd_console_write(const char *s) {
  if (!s)
    return;
  char buf[COLS + 1];
  size_t n = 0;
  while (*s) {
    if (*s == '\n') {
      buf[n] = 0;
      newline(buf);
      n = 0;
    } else if (n < COLS)
      buf[n++] = *s;
    s++;
  }
  if (n) {
    buf[n] = 0;
    newline(buf);
  }
}
void dnd_console_set_visible(int v) { visible = !!v; }
int dnd_console_visible(void) { return visible; }
static void render_text(const char *const *text, int rows, float left, float top,
                        unsigned char shade) {
  const float scale = 1.35f, cw = 6 * scale, ch = 9 * scale;
  unsigned quads = 0;
  for (int row = 0; row < rows; row++) {
    for (int col = 0; text[row][col]; col++) {
      const unsigned char *g = glyph(text[row][col]);
      if (g)
        for (int x = 0; x < 5; x++)
          for (int y = 0; y < 7; y++)
            if (g[x] & (1u << y))
              quads++;
    }
  }
  if (!quads) return;
  dnd_graphics_overlay_glyph_begin(quads, shade);
  for (int row = 0; row < rows; row++) {
    for (int col = 0; text[row][col]; col++) {
      const unsigned char *g = glyph(text[row][col]);
      if (!g)
        continue;
      float ox = left + col * cw, oy = top + row * ch;
      for (int x = 0; x < 5; x++)
        for (int y = 0; y < 7; y++)
          if (g[x] & (1u << y)) {
            float px = ox + x * scale, py = oy + y * scale;
            dnd_graphics_overlay_glyph_quad(px, py, scale);
          }
    }
  }
  dnd_graphics_overlay_glyph_end();
}

void dnd_console_render(void) {
  const float scale = 1.35f, cw = 6 * scale, ch = 9 * scale;
  if (visible && count) {
    const char *text[ROWS];
    for (int row = 0; row < count; row++)
      text[row] = lines[(head - count + 1 + row + ROWS) % ROWS];
    dnd_graphics_overlay_rect(0, 0, (float)dnd_graphics_width(), count * ch + 8,
                              0, 0, 0, .62f);
    render_text(text, count, 4, 4, 255);
  }
  if (dnd_performance_visible()) {
    char panel[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS];
    const char *text[DND_PERFORMANCE_ROWS];
    size_t columns = 0;
    dnd_performance_format(panel);
    for (int row = 0; row < DND_PERFORMANCE_ROWS; row++) {
      text[row] = panel[row];
      size_t length = strlen(panel[row]);
      if (length > columns) columns = length;
    }
    const float margin = 8, padding = 6;
    float height = DND_PERFORMANCE_ROWS * ch + padding * 2;
    float top = (float)dnd_graphics_height() - margin - height;
    dnd_graphics_overlay_rect(margin, top, columns * cw + padding * 2,
                              height, 0, 0, 0, 1);
    render_text(text, DND_PERFORMANCE_ROWS, margin + padding, top + padding, 208);
  }
}
