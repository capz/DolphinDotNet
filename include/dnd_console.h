#ifndef DND_CONSOLE_H
#define DND_CONSOLE_H
#include <stddef.h>
void dnd_console_init(void);
void dnd_console_write(const char *text);
void dnd_console_write_line(const char *text);
void dnd_console_render(void);
void dnd_console_set_visible(int visible);
int dnd_console_visible(void);
#endif
