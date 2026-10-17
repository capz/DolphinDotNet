#include "dnd_fst.h"
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <iso9660.h>
#include <ogc/dvd.h>
#include <ogc/dvdlow.h>

static int disc_format;
bool dnd_gc_mount_disc_image(const char *path) {
  if (!path || !dnd_gc_fst_mount_image(path))
    return false;
  disc_format = 2;
  return true;
}
bool dnd_gc_mount_disc(int format) {
  DVD_Init();
  /* DVD_Mount waits for a cover-close event when no disc is inserted. A mount
   * attempt must report unavailable instead of freezing a DOL-only launch. */
  if (DVD_LowGetCoverStatus() != DVD_COVER_CLOSED)
    return false;
  if (DVD_Mount() < 0)
    return false;
  bool result =
      format == 1 ? ISO9660_Mount("dvd", &__io_gcdvd) : dnd_gc_fst_mount();
  if (result)
    disc_format = format;
  return result;
}
void dnd_gc_unmount_disc(void) {
  if (disc_format == 1)
    ISO9660_Unmount("dvd");
  else
    dnd_gc_fst_unmount();
  disc_format = 0;
}
#endif
