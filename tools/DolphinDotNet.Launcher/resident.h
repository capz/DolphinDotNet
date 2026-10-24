#ifndef DND_LAUNCHER_RESIDENT_H
#define DND_LAUNCHER_RESIDENT_H

/* Fixed MEM1 layout shared by the packer, bootstrap and validator. Keep numeric
 * literals assembler-compatible. The pristine receiver survives every launch. */
#define DND_RESIDENT_BASE 0x81600000
#define DND_RESIDENT_END 0x81800000
#define DND_RESIDENT_PLAN 32
#define DND_RESIDENT_STUB 320
#define DND_RESIDENT_IMAGE 1024
#define DND_RETURN_STUB 0x80001800

#endif
