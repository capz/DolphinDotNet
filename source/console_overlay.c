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
void dnd_console_build_font_i4(unsigned char *tiles) {
  memset(tiles,0,4096);
  for(int c=0;c<128;c++) {
    const unsigned char *g=glyph((char)c);if(!g)continue;
    for(int y=0;y<7;y++)for(int x=0;x<5;x++)
      if(g[x]&(1u<<y))tiles[c*32+y*4+x/2]|=(unsigned char)(x&1?15:240);
  }
}
static void newline(const char *s) {
  head = (head + 1) % ROWS;
  size_t length = 0;
  while (length < COLS && s[length]) {
    lines[head][length] = s[length];
    length++;
  }
  lines[head][length] = 0;
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
typedef struct { unsigned char x,y,w,h; } GlyphRect;
/* Exact, non-overlapping rectangle cover of the existing 5x7 bitmap. */
static int glyph_rectangles(const unsigned char *g,GlyphRect *out) {
  unsigned char rows[7]={0};int count=0;
  for(int y=0;y<7;y++)for(int x=0;x<5;x++)if(g[x]&(1u<<y))rows[y]|=1u<<x;
  for(int y=0;y<7;y++)for(int x=0;x<5;x++)if(rows[y]&(1u<<x)) {
    int w=1,h=1;while(x+w<5&&(rows[y]&(1u<<(x+w))))w++;
    unsigned mask=((1u<<w)-1)<<x;
    while(y+h<7&&(rows[y+h]&mask)==mask)h++;
    for(int i=0;i<h;i++)rows[y+i]&=~mask;
    if(out)out[count]=(GlyphRect){x,y,w,h};
    count++;
  }
  return count;
}
static void render_text(const char *const *text, int rows, float left, float top,
                        unsigned char shade) {
  const float scale = 1.35f, cw = 6 * scale, ch = 9 * scale;
  int compact=dnd_performance_glyph_runs();
  if(compact==3&&dnd_graphics_overlay_cached_text(text,rows,left,top,shade))return;
  unsigned quads = 0;
  for(int row=0;row<rows;row++)for(int col=0;text[row][col];col++) {
    const unsigned char *g=glyph(text[row][col]);if(!g)continue;
    if(compact==3){quads++;continue;}
    if(compact==2){quads+=(unsigned)glyph_rectangles(g,NULL);continue;}
    for(int y=0;y<7;y++)for(int x=0;x<5;x++)
      if((g[x]&(1u<<y))&&(!compact||x==0||!(g[x-1]&(1u<<y))))quads++;
  }
  if (!quads) return;
  if(compact==3)dnd_graphics_overlay_atlas_begin(quads,shade);
  else dnd_graphics_overlay_glyph_begin(quads, shade);
  for (int row = 0; row < rows; row++) {
    for (int col = 0; text[row][col]; col++) {
      const unsigned char *g = glyph(text[row][col]);
      if (!g)
        continue;
      float ox = left + col * cw, oy = top + row * ch;
      if(compact==3) {
        dnd_graphics_overlay_atlas_glyph((unsigned char)text[row][col],ox,oy,scale);
        continue;
      }
      if(compact==2) {
        GlyphRect rects[35];int n=glyph_rectangles(g,rects);
        for(int i=0;i<n;i++){
          GlyphRect r=rects[i];float px=ox+r.x*scale,py=oy+r.y*scale;
          dnd_graphics_overlay_glyph_rect(px,py,(ox+(r.x+r.w-1)*scale+scale)-px,(oy+(r.y+r.h-1)*scale+scale)-py);
        }
        continue;
      }
      for(int y=0;y<7;y++)for(int x=0;x<5;) {
        if(!(g[x]&(1u<<y))){x++;continue;}
        int first=x++;if(compact)while(x<5&&(g[x]&(1u<<y)))x++;
        float px=ox+first*scale,py=oy+y*scale;
        if(compact)dnd_graphics_overlay_glyph_rect(px,py,(ox+(x-1)*scale+scale)-px,scale);
        else dnd_graphics_overlay_glyph_quad(px,py,scale);
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
    int rows = dnd_performance_format(panel);
    for (int row = 0; row < rows; row++) {
      text[row] = panel[row];
      size_t length = strlen(panel[row]);
      if (length > columns) columns = length;
    }
    const float margin = 8, padding = 6;
    float height = rows * ch + padding * 2;
    float top = (float)dnd_graphics_height() - margin - height;
    dnd_graphics_overlay_rect(margin, top, columns * cw + padding * 2,
                              height, 0, 0, 0, 1);
    render_text(text, rows, margin + padding, top + padding, 208);
  }
}
