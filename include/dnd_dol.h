#ifndef DND_DOL_H
#define DND_DOL_H
#include "dnd_managed.h"

#define DND_DOL_SECTIONS 18
#define DND_DOL_MAX_SIZE (24u * 1024u * 1024u)
typedef struct { uint32_t offset, address, size; } DndDolSection;
typedef struct {
    uint32_t entry, bss_address, bss_size, count;
    DndDolSection sections[DND_DOL_SECTIONS];
} DndDolPlan;
/* Parse only the 256-byte header. All lengths/ranges use subtraction to avoid overflow. */
bool dnd_dol_validate(const uint8_t header[256], size_t length, DndDolPlan *plan);
bool dnd_dol_stage_safe(const DndDolPlan *plan, uintptr_t start, size_t size);
int dnd_dol_prepare(DndArray *image);
int dnd_dol_begin(DndString *path);
int dnd_dol_step(int token);
int dnd_dol_info(int token, int kind);
int dnd_dol_dispose(int token);
int dnd_dol_request(int token);
bool dnd_dol_pending(void);
void dnd_dol_finish(bool success);
/* Platform boundary; the final trampoline never returns or uses the old stack. */
bool dnd_dol_platform_supported(void);
size_t dnd_dol_platform_stub_size(void);
void dnd_dol_platform_stub_copy(void *destination);
void dnd_dol_platform_launch(DndDolPlan *plan, void *image, void *stub);
#endif
