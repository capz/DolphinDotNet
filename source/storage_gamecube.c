#include "dnd_card_transaction.h"
#include "dnd_fst.h"
#include "dnd_storage.h"
#include "dnd_file_extents.h"
#include "dnd_gcloader.h"
#ifdef DND_GAMECUBE_STORAGE
#include <dvm.h>
#include <errno.h>
#include <fat.h>
#include <fcntl.h>
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
#include <unistd.h>

static const char *names[] = {"carda", "cardb", "sd", "dvd", "mca", "mcb", "gcloader"};
static void *workareas[2];
static char games[2][5], companies[2][3];
static bool card_initialized;
/* Retain the mounted cache-layer device and partition offset, so extent
 * inspection sees the same filesystem state as the devoptab writer. */
static DvmDisc *mounted_discs[7];
static sec_t mounted_partition_lba[7];
#include "storage_disc_gate.inc"
static MountDiscGate *mount_gates[7];
static DISC_INTERFACE *mount_interfaces[7];
void dnd_gc_storage_invalidate(int device) {
  if(device>=0&&device<7&&mount_gates[device])mount_gates[device]->removed=true;
}
int dnd_gc_disc_presence(void);
int dnd_gc_storage_presence(int device) {
  if(device==3)return dnd_gc_disc_presence();
  DISC_INTERFACE *iface=device>=0&&device<7?mount_interfaces[device]:NULL;
  return iface&&iface->isInserted?(iface->isInserted(iface)?1:0):3;
}
static int extent_sector_read(void *context,uint64_t lba,void *buffer) {
  DvmDisc *disc=context;
  if((uint64_t)(sec_t)lba!=lba)return -EIO;
  return dvmDiscReadSectors(disc,buffer,(sec_t)lba,1)?0:-EIO;
}
int dnd_gc_contiguous_extents(int device,const char *native_path,unsigned *extents) {
  if(device<0 || device>=7 || !mounted_discs[device])return -ENODEV;
  if(mounted_discs[device]->sector_sz!=512)return -ENOTSUP;
  const char *colon=strchr(native_path,':');
  if(!colon || colon[1]!='/')return -EINVAL;
  DndSectorReader sector={mounted_discs[device],extent_sector_read,
                          (uint64_t)mounted_partition_lba[device]};
  return dnd_fat_count_extents(&sector,colon+1,extents);
}
static int gcloader_command(void *context,const uint32_t words[3],uint32_t *response) {
  (void)context;
  dvdcmdblk block={0};
  dvdcmdbuf command={words[0],words[1],words[2]};
  /* libogc2 serializes with the raw-volume driver and bounds each command's
   * wait with its DVD timeout handler. No direct DI register polling. */
  return DVD_ReadImmPrio(&block,command,response,sizeof(*response),2)==sizeof(*response)?0:-EIO;
}
static int gcloader_read_mapped(void *context,uint32_t offset,void *buffer,uint32_t length) {
  (void)context;
  dvdcmdblk block={0};
  return DVD_ReadAbsPrio(&block,buffer,length,offset,2)==(s32)length?0:-EIO;
}
int dnd_gc_select_gcloader_disc_image(const char *native_path) {
  DvmDisc *disc=mounted_discs[6];
  if(!disc)return -ENODEV;
  if(disc->sector_sz!=512)return -ENOTSUP;
  const char *colon=strchr(native_path,':');
  if(!colon || colon[1]!='/')return -EINVAL;
  /* Complete previously closed writers' cache flush before inspecting FAT.
   * No filesystem allocation or file contents are changed by selection. */
  if(!dvmDiscFlush(disc))return -EIO;
  DndSectorReader sector={disc,extent_sector_read,(uint64_t)mounted_partition_lba[6]};
  uint64_t first_sector,length;
  int rc=dnd_fat_contiguous_span(&sector,colon+1,&first_sector,&length);
  if(rc<0)return rc;
  _Alignas(32) unsigned char expected_header[DND_GCLOADER_HEADER_SIZE];
  int fd=open(native_path,O_RDONLY);
  if(fd<0)return -errno;
  struct stat st;
  rc=fstat(fd,&st)?-errno:!S_ISREG(st.st_mode)||(uint64_t)st.st_size!=length?-EIO:0;
  size_t done=0;
  while(!rc && done<sizeof(expected_header)) {
    ssize_t count=read(fd,expected_header+done,sizeof(expected_header)-done);
    if(count<0 && errno==EINTR)continue;
    if(count<=0){rc=count<0?-errno:-EIO;break;}
    done+=(size_t)count;
  }
  if(close(fd) && !rc)rc=-errno;
  if(rc<0)return rc;
  DndGcLoaderIo io={NULL,gcloader_command,gcloader_read_mapped};
  return dnd_gcloader_select_contiguous(&io,first_sector,length,expected_header);
}
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
bool dnd_gc_mount_disc(int format);
void dnd_gc_unmount_disc(void);
bool dnd_gc_mount_volume(const DndMountConfiguration *request,DndMountConfiguration *effective) {
  int device=request->device;
  if (device < 0 || (device > 2 && device != 6))return false;
  DISC_INTERFACE *iface = device == 0   ? get_io_gcsda()
                          : device == 1 ? get_io_gcsdb()
                          : device == 2 ? get_io_gcsd2() : &__io_gcode;
  dvmRegisterFsDriver(&g_vfatFsDriver);dvmRegisterFsDriver(&g_exfatFsDriver);
  DvmDisc *raw=iface?dvmDiscCreate(iface):NULL;
  if(!raw)return false;
  MountDiscGate *gate=calloc(1,sizeof(*gate));
  if(!gate){raw->vt->destroy(raw);return false;}
  gate->base=*raw;gate->base.vt=&gate_interface;gate->inner=raw;
  DvmDisc *disc=dvmDiscCacheCreate(&gate->base,(unsigned)request->cache_pages,(unsigned)request->sectors_per_page);
  /* libdvm can return its input when cache allocation fails. Do not report
   * the requested cache as effective when the volume would be uncached. */
  if(!disc||disc==&gate->base){gate_destroy(&gate->base);return false;}
  bool success=false;sec_t selected=0;int filesystem=0;
  if(request->partition_lba>=0) {
    selected=(sec_t)request->partition_lba;
    if((uint64_t)selected==(uint64_t)request->partition_lba) {
      if(request->filesystem!=2&&dvmMountVolume(names[device],disc,selected,g_vfatFsDriver.fstype)){success=true;filesystem=1;}
      else if(request->filesystem!=1&&dvmMountVolume(names[device],disc,selected,g_exfatFsDriver.fstype)){success=true;filesystem=2;}
    }
  }else {
    DvmPartInfo parts[4];unsigned count=dvmReadPartitionTable(disc,parts,4,DVM_IDENT_FSTYPE);
    if(!count&&request->filesystem!=1&&dvmMountVolume(names[device],disc,0,g_exfatFsDriver.fstype)){success=true;filesystem=2;}
    for(unsigned i=0;i<count&&!success;i++)if(parts[i].fstype) {
      int fs=!strcmp(parts[i].fstype,g_vfatFsDriver.fstype)?1:!strcmp(parts[i].fstype,g_exfatFsDriver.fstype)?2:0;
      if(fs&&(!request->filesystem||request->filesystem==fs)&&dvmMountPartition(names[device],disc,&parts[i])) {
        success=true;selected=parts[i].start_sector;filesystem=fs;
      }
    }
  }
  if(!success){disc->vt->destroy(disc);return false;}
  *effective=*request;effective->partition_lba=selected;effective->filesystem=filesystem;
  mounted_discs[device]=disc;mounted_partition_lba[device]=selected;
  mount_gates[device]=gate;mount_interfaces[device]=iface;
  return true;
}
bool dnd_gc_mount(int device) {
  if(device==3)return dnd_gc_mount_disc(1);
  DndMountConfiguration request={.device=device,.kind=1,.partition_lba=-1,.cache_pages=4,.sectors_per_page=8,.source_device=-1},effective;
  return dnd_gc_mount_volume(&request,&effective);
}
bool dnd_gc_storage_unmount(int device) {
  if (device == 3) {
    dnd_gc_unmount_disc();
    return true;
  }
  if ((device >= 0 && device < 3) || device == 6) {
    if(!dvmUnmountVolume(names[device]))return false;
    mounted_discs[device] = NULL;
    mounted_partition_lba[device] = 0;
    mount_gates[device]=NULL;mount_interfaces[device]=NULL;
    return true;
  }
  if (device >= 4 && device < 6) {
    int slot = device - 4;
    CARD_Unmount(slot);
    free(workareas[slot]);
    workareas[slot] = NULL;
  }
  return true;
}
void dnd_gc_unmount(int device) { (void)dnd_gc_storage_unmount(device); }
static void identity(int slot) {
  CARD_SetGamecode(games[slot]);
  CARD_SetCompany(companies[slot]);
}
bool dnd_gc_card_mount(int device, const char *game, const char *company) {
  int slot = device - 4;
  if (slot < 0 || slot > 1 || dnd_cmgr_active(slot))
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
  if (dnd_gc_card_recover(device) < 0) {
    CARD_Unmount(slot);
    free(area);
    workareas[slot] = NULL;
    return false;
  }
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
  u32 sector;
  rc = CARD_GetSectorSize(slot, &sector);
  if (rc < 0 || !sector || sector > 1024 * 1024) { CARD_Close(&file); return rc < 0 ? card_errno(rc) : -EIO; }
  void *block = memalign(32, sector);
  if (!block) {
    CARD_Close(&file);
    return -ENOMEM;
  }
  int done = 0;
  while (done < length) {
    rc = CARD_Read(&file, block, sector, (u32)done);
    if (rc < 0)
      break;
    int count = (u32)(length - done) < sector ? length - done : (int)sector;
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
static int txn_read(void *context, const char *name, void *data, int length) {
  return dnd_gc_card_read((int)(intptr_t)context, name, data, length);
}
static int txn_write(void *context, const char *name, const void *data,
                     int length) {
  return card_write_raw((int)(intptr_t)context, name, data, length);
}
static int txn_delete(void *context, const char *name) {
  int slot = (int)(intptr_t)context - 4;
  identity(slot);
  return card_errno(CARD_Delete(slot, name));
}
static int txn_metadata(void *context, const char *name, bool metadata,
                        bool banner, bool icon) {
  return dnd_gc_card_metadata((int)(intptr_t)context, name, metadata, banner,
                              icon);
}
static int txn_ready(void *context, bool mark) {
  int slot = (int)(intptr_t)context - 4;
  identity(slot);
  card_file file;
  card_stat status;
  int rc = CARD_Open(slot, DND_CARD_JOURNAL, &file);
  if (rc < 0)
    return card_errno(rc);
  rc = CARD_GetStatus(slot, file.filenum, &status);
  int ready = 0;
  if (rc >= 0) {
    ready = status.comment_addr == 0;
    if (mark) {
      /* Fresh/deleted directory entries have comment_addr == UINT32_MAX.
         CARD_SetStatus commits through libogc's redundant directory update. */
      status.comment_addr = 0;
      rc = CARD_SetStatus(slot, file.filenum, &status);
    }
  }
  int close_rc = CARD_Close(&file);
  if (rc < 0)
    return card_errno(rc);
  if (close_rc < 0)
    return card_errno(close_rc);
  return mark ? 0 : ready;
}
static DndCardTransaction transaction(int device) {
  DndCardTransaction result = {(void *)(intptr_t)device,
                               txn_read,
                               txn_write,
                               txn_delete,
                               txn_ready,
                               txn_metadata};
  return result;
}
int dnd_gc_card_recover(int device) {
  DndCardTransaction t = transaction(device);
  return dnd_card_recover(&t);
}
int dnd_gc_card_write_save(int device, const char *name, const void *data,
                           int length, bool metadata, bool banner, bool icon) {
  DndCardTransaction t = transaction(device);
  return dnd_card_replace(&t, name, data, length, metadata, banner, icon);
}
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
  if (rc == CARD_ERROR_NOFILE && count == 0) rc = 0;
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
