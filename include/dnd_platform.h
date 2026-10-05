#ifndef DND_PLATFORM_H
#define DND_PLATFORM_H

#include <stdint.h>

void dnd_platform_init(void);
void dnd_platform_write(const char *text);
void dnd_platform_write_int(int32_t value);
void dnd_platform_wait_forever(void);

#endif
