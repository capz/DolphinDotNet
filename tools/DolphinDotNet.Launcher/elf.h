#ifndef DND_LAUNCHER_ELF_H
#define DND_LAUNCHER_ELF_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define DND_ELF_SEGMENTS 16
#define DND_LAUNCHER_MAX_IMAGE (16u * 1024u * 1024u - 4096u)
typedef struct { uint32_t offset, address, file_size, memory_size; } DndElfSegment;
typedef struct { uint32_t entry, count; DndElfSegment segments[DND_ELF_SEGMENTS]; } DndElfPlan;

/* Only fixed-address, big-endian PPC32 ET_EXEC in cached GameCube MEM1.
 * The entire staging allocation (plan, stub and image) must survive the copy. */
bool dnd_elf_validate(const uint8_t *image, size_t size, uintptr_t stage, size_t stage_size, DndElfPlan *plan);
uint32_t dnd_launcher_crc32(const uint8_t *data, size_t size);
#endif
