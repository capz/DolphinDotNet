#include "dnd_storage.h"
#include "dnd_fst.h"
#include "dnd_card_transaction.h"
#ifdef DND_GAMECUBE_STORAGE
#include <dvm.h>
#include <errno.h>
#include <fat.h>
#include <gccore.h>
#include <iso9660.h>
#include <malloc.h>
#include <ogc/card.h>
#include <ogc/dvd.h>
#include <sdcard/gcsd.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

static const char *names[] = {"carda", "cardb", "sd", "dvd"};
static void *workareas[2];
static char games[2][5], companies[2][3];
static bool card_initialized;
static int disc_format;
int dnd_gc_card_recover(int device);
static int card_errno(int rc) {
  if (rc >= 0)
    return 0;
  switch (rc) {
  case CARD_ERROR_NOFILE:
    return -ENOENT;
  case CARD_ERROR_NOCARD:
  case CARD_ERROR_WRONGDEVICE:
    return -ENODEV;
  case CARD_ERROR_EXIST:
    return -EEXIST;
  case CARD_ERROR_NOENT:
  case CARD_ERROR_INSSPACE:
    return -ENOSPC;
  case CARD_ERROR_NOPERM:
    return -EACCES;
  case CARD_ERROR_BUSY:
    return -EBUSY;
  case CARD_ERROR_NAMETOOLONG:
    return -ENAMETOOLONG;
  default:
    return -EIO;
  }
}
bool dnd_gc_mount_disc(int format) {
  DVD_Init();
  if(DVD_Mount()<0)return false;
  bool result=format==1?ISO9660_Mount("dvd", &__io_gcdvd):dnd_gc_fst_mount();
  if(result)disc_format=format;
  return result;
}
bool dnd_gc_mount(int device) {
  if (device == 3) {
    return dnd_gc_mount_disc(1);
  }
  if (device < 0 || device > 2)
    return false;
  DISC_INTERFACE *iface = device == 0   ? get_io_gcsda()
                          : device == 1 ? get_io_gcsdb()
                                        : get_io_gcsd2();
  dvmRegisterFsDriver(&g_vfatFsDriver);
  dvmRegisterFsDriver(&g_exfatFsDriver);
  DvmDisc *disc = iface ? dvmDiscCreate(iface) : NULL;
  if (!disc)
    return false;
  disc = dvmDiscCacheCreate(disc, 4, 8);
  if (!disc)
    return false;
  DvmPartInfo parts[4];
  unsigned count = dvmReadPartitionTable(disc, parts, 4, DVM_IDENT_FSTYPE);
  bool mounted = false;
  if (!count)
    mounted = dvmMountVolume(names[device], disc, 0, "exfat");
  for (unsigned i = 0; i < count && !mounted; i++)
    if (parts[i].fstype)
      mounted = dvmMountPartition(names[device], disc, &parts[i]);
  if (!mounted)
    disc->vt->destroy(disc);
  return mounted;
}
void dnd_gc_unmount(int device) {
  if (device == 3) {
    if(disc_format==1)ISO9660_Unmount("dvd");else dnd_gc_fst_unmount();
    return;
  }
  if (device >= 0 && device < 3) {
    dvmUnmountVolume(names[device]);
    return;
  }
  if (device >= 4 && device < 6) {
    int slot = device - 4;
    CARD_Unmount(slot);
    free(workareas[slot]);
    workareas[slot] = NULL;
  }
}
static void identity(int slot) {
  CARD_SetGamecode(games[slot]);
  CARD_SetCompany(companies[slot]);
}
bool dnd_gc_card_mount(int device, const char *game, const char *company) {
  int slot = device - 4;
  if (slot < 0 || slot > 1)
    return false;
  if (!card_initialized) {
    CARD_Init(game, company);
    card_initialized = true;
  }
  void *area = memalign(32, CARD_WORKAREA);
  if (!area)
    return false;
  if (CARD_Mount(slot, area, NULL) < 0) {
    free(area);
    return false;
  }
  workareas[slot] = area;
  strcpy(games[slot], game);
  strcpy(companies[slot], company);
  if(dnd_gc_card_recover(device)<0){CARD_Unmount(slot);free(area);workareas[slot]=NULL;return false;}
  return true;
}
int dnd_gc_card_read(int device, const char *name, void *data, int capacity) {
  int slot = device - 4;
  identity(slot);
  card_file file;
  int rc = CARD_Open(slot, name, &file);
  if (rc < 0)
    return card_errno(rc);
  if (!data) {
    int length = file.len;
    CARD_Close(&file);
    return length;
  }
  int length = capacity < file.len ? capacity : file.len;
  void *block = memalign(32, 512);
  if (!block) {
    CARD_Close(&file);
    return -ENOMEM;
  }
  int done = 0;
  while (done < length) {
    rc = CARD_Read(&file, block, 512, (u32)done);
    if (rc < 0)
      break;
    int count = length - done < 512 ? length - done : 512;
    memcpy((char *)data + done, block, (size_t)count);
    done += count;
  }
  int close_rc = CARD_Close(&file);
  free(block);
  return rc < 0 ? card_errno(rc) : close_rc < 0 ? card_errno(close_rc) : done;
}
static int card_write_raw(int device, const char *name, const void *data,
                      int length) {
  int slot = device - 4;
  identity(slot);
  u32 sector;
  int rc = CARD_GetSectorSize(slot, &sector);
  if (rc < 0)
    return card_errno(rc);
  if (!sector || sector > 1024 * 1024)
    return -EIO;
  if ((unsigned)length > UINT32_MAX - sector)
    return -EFBIG;
  u32 physical = ((u32)length + sector - 1) / sector * sector;
  void *block = memalign(32, sector);
  if (!block)
    return -ENOMEM;
  card_file file;
  rc = CARD_Open(slot, name, &file);
  if (rc >= 0 && (u32)file.len != physical) {
    CARD_Close(&file);
    free(block);
    return -ENOSYS;
  }
  if (rc == CARD_ERROR_NOFILE)
    rc = CARD_Create(slot, name, physical, &file);
  if (rc < 0) {
    free(block);
    return card_errno(rc);
  }
  for (u32 offset = 0; offset < physical; offset += sector) {
    memset(block, 0, sector);
    u32 count = (u32)length - offset < sector ? (u32)length - offset : sector;
    if (offset < (u32)length)
      memcpy(block, (const char *)data + offset, count);
    rc = CARD_Write(&file, block, sector, offset);
    if (rc < 0)
      break;
  }
  int close_rc = CARD_Close(&file);
  free(block);
  return rc < 0 ? card_errno(rc) : card_errno(close_rc);
}
int dnd_gc_card_metadata(int device, const char *name, bool metadata,
                         bool banner, bool icon) {
  int slot = device - 4;
  identity(slot);
  card_file file;
  card_stat status;
  int rc = CARD_Open(slot, name, &file);
  if (rc < 0)
    return card_errno(rc);
  rc = CARD_GetStatus(slot, file.filenum, &status);
  if (rc >= 0) {
    status.banner_fmt = banner ? CARD_BANNER_RGB : CARD_BANNER_NONE;
    status.icon_fmt = icon ? CARD_ICON_RGB : CARD_ICON_NONE;
    status.icon_speed = icon ? CARD_SPEED_FAST : 0;
    status.comment_addr = metadata ? 32 : UINT32_MAX;
    status.icon_addr = (banner || icon) ? 96 : UINT32_MAX;
    rc = CARD_SetStatus(slot, file.filenum, &status);
  }
  int close_rc = CARD_Close(&file);
  return rc < 0 ? card_errno(rc) : card_errno(close_rc);
}
static int txn_read(void *context,const char *name,void *data,int length){return dnd_gc_card_read((int)(intptr_t)context,name,data,length);}
static int txn_write(void *context,const char *name,const void *data,int length){return card_write_raw((int)(intptr_t)context,name,data,length);}
static int txn_delete(void *context,const char *name){int slot=(int)(intptr_t)context-4;identity(slot);return card_errno(CARD_Delete(slot,name));}
static int txn_metadata(void *context,const char *name,bool metadata,bool banner,bool icon){return dnd_gc_card_metadata((int)(intptr_t)context,name,metadata,banner,icon);}
static DndCardTransaction transaction(int device){DndCardTransaction result={(void*)(intptr_t)device,txn_read,txn_write,txn_delete,txn_metadata};return result;}
int dnd_gc_card_recover(int device){DndCardTransaction t=transaction(device);return dnd_card_recover(&t);}
int dnd_gc_card_write_save(int device,const char *name,const void *data,int length,bool metadata,bool banner,bool icon){DndCardTransaction t=transaction(device);return dnd_card_replace(&t,name,data,length,metadata,banner,icon);}
int dnd_gc_card_delete(int device, const char *name) {
  int slot = device - 4;
  identity(slot);
  return card_errno(CARD_Delete(slot, name));
}
int dnd_gc_card_entries(int device, char names_out[127][33]) {
  int slot = device - 4;
  identity(slot);
  card_dir entries[127];
  s32 count = 0;
  int rc = CARD_GetDirectory(slot, entries, &count, false);
  if (rc < 0)
    return card_errno(rc);
  if (count < 0 || count > 127)
    return -EIO;
  for (int i = 0; i < count; i++) {
    memcpy(names_out[i], entries[i].filename, 32);
    names_out[i][32] = 0;
  }
  return (int)count;
}
#endif
