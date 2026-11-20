#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include "dnd_iso_file.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

/* Exercise the production GameCube storage registry with a bounded backend.
 * Volumes are real host directories named sd:, cardb:, etc. ISO parsing and
 * reads use the real portable FST implementation. The backend materializes
 * that fixture's four-byte asset for host stdio, rather than emulating an EXI
 * device or a system devoptab. Physical adapter behavior is not claimed. */
static const char *roots[]={"carda:","cardb:","sd:","dvd:","mca:","mcb:","gcloader:"};
static DndIsoFile image;
static int image_source=-1;

static bool exists(const char *path) { struct stat info;return stat(path,&info)==0; }
int FAT_getAttr(const char *path) { (void)path;return 0; }
int FAT_setAttr(const char *path,unsigned attributes) { (void)path;(void)attributes;return 0; }
int dnd_gc_contiguous_extents(int device,const char *path,unsigned *extents)
{ (void)device;(void)path;(void)extents;return -ENOTSUP; }
bool dnd_gc_mount_volume(const DndMountConfiguration *request,DndMountConfiguration *effective) {
    assert(request->device>=0&&request->device<7&&request->device!=3&&request->device!=4&&request->device!=5);
    if(!exists(roots[request->device]))return false;
    *effective=*request;effective->partition_lba=0;effective->filesystem=1;return true;
}
bool dnd_gc_mount_disc(int format) {
    assert(format==1||format==2);
    return exists("physical-disc-present");
}
bool dnd_gc_mount(int device) { assert(device==3);return dnd_gc_mount_disc(1); }
bool dnd_gc_mount_disc_image(const char *path) {
    assert(!image.file&&image_source==-1);
    if(!dnd_iso_file_open(&image,path))return false;
    int entry=dnd_iso_file_entry(&image,"/payload.bin");unsigned char payload[4];
    if(entry<0||dnd_iso_file_read(&image,(unsigned)entry,0,payload,sizeof(payload))!=4){dnd_iso_file_close(&image);return false;}
    FILE *out=fopen("dvd:/payload.bin","wb");assert(out);
    assert(fwrite(payload,1,sizeof(payload),out)==sizeof(payload));assert(!fclose(out));
    for(int i=0;i<7;i++)if(i!=3&&i!=4&&i!=5&&strncmp(path,roots[i],strlen(roots[i]))==0)image_source=i;
    assert(image_source>=0);return true;
}
void dnd_gc_unmount(int device) {
    if(device==3){dnd_iso_file_close(&image);image_source=-1;}
    else assert(device!=image_source); /* Mounted image must release first. */
}
bool dnd_gc_storage_unmount(int device) {
    const char *fault=device==6?"fail-unmount-gcloader":device==3?"fail-unmount-dvd":NULL;
    if(fault&&exists(fault)){assert(!unlink(fault));errno=EIO;return false;}
    dnd_gc_unmount(device);return true;
}
int dnd_gc_storage_presence(int device) { return exists(roots[device])?1:0; }
void dnd_gc_storage_invalidate(int device) { (void)device; }
void launch_content_image_finish(void) { assert(!image.file&&image_source==-1); }
