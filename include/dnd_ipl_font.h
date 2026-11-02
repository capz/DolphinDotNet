#ifndef DND_IPL_FONT_H
#define DND_IPL_FONT_H
#include <stddef.h>
#include <stdint.h>
#define DND_IPL_FIRST 32
#define DND_IPL_GLYPHS 95
#define DND_IPL_METADATA (8 + DND_IPL_GLYPHS * 5)
#define DND_IPL_WIDTH 512
#define DND_IPL_HEIGHT 256
/* White-intensity IA8 atlas, built from the console ROM through libogc. */
int dnd_ipl_font_build(uint8_t *atlas,size_t bytes,int32_t *metadata,size_t count);
int dnd_ipl_font_cache_bytes(void);
void dnd_ipl_font_shutdown(void);
#endif
