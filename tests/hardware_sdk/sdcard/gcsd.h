#include <gccore.h>
typedef struct DISC_INTERFACE {
    bool (*startup)(struct DISC_INTERFACE*);
    bool (*isInserted)(struct DISC_INTERFACE*);
} DISC_INTERFACE;
DISC_INTERFACE *get_io_gcsda(void);
DISC_INTERFACE *get_io_gcsdb(void);
DISC_INTERFACE *get_io_gcsd2(void);
