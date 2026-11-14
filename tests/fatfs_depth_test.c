/* Run against both the unmodified pinned FatFs and the SDK's matching overlay. */
#include "ff.h"
#include "diskio.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "fatfs_errno.inc"

enum { SECTORS = 131072, SECTOR_SIZE = 512 };
static BYTE image[SECTORS * SECTOR_SIZE];
static unsigned fatfs_allocations, disk_writes;
void *fatfs_test_malloc(size_t size) { (void)size; ++fatfs_allocations; return NULL; }
void *fatfs_test_calloc(size_t count,size_t size) { (void)count;(void)size;++fatfs_allocations;return NULL; }
void *fatfs_test_realloc(void *ptr,size_t size) { (void)ptr;(void)size;++fatfs_allocations;return NULL; }
void fatfs_test_free(void *ptr) { (void)ptr; }
DSTATUS disk_initialize(void *pdrv) { (void)pdrv; return 0; }
DSTATUS disk_status(void *pdrv) { (void)pdrv; return 0; }
DRESULT disk_read(void *pdrv,BYTE *buffer,LBA_t sector,UINT count,BYTE opt) {
    (void)pdrv;(void)opt;
    if(sector>SECTORS || count>SECTORS-sector)return RES_PARERR;
    memcpy(buffer,image+sector*SECTOR_SIZE,(size_t)count*SECTOR_SIZE);return RES_OK;
}
DRESULT disk_write(void *pdrv,const BYTE *buffer,LBA_t sector,UINT count,BYTE opt) {
    (void)pdrv;(void)opt;
    if(sector>SECTORS || count>SECTORS-sector)return RES_PARERR;
    ++disk_writes;memcpy(image+sector*SECTOR_SIZE,buffer,(size_t)count*SECTOR_SIZE);return RES_OK;
}
DRESULT disk_ioctl(void *pdrv,BYTE command,void *buffer) {
    (void)pdrv;
    switch(command) {
    case CTRL_SYNC: case CTRL_TRIM: return RES_OK;
    case GET_SECTOR_COUNT: *(LBA_t *)buffer=SECTORS;return RES_OK;
    case GET_SECTOR_SIZE: *(WORD *)buffer=SECTOR_SIZE;return RES_OK;
    case GET_BLOCK_SIZE: *(DWORD *)buffer=1;return RES_OK;
    default: return RES_PARERR;
    }
}
DWORD get_fattime(void) { return (46u<<25)|(11u<<21)|(14u<<16); }
int ff_mutex_create(FATFS *fs) { (void)fs;return 1; }
void ff_mutex_delete(FATFS *fs) { (void)fs; }
int ff_mutex_take(FATFS *fs) { (void)fs;return 1; }
void ff_mutex_give(FATFS *fs) { (void)fs; }

static void append_directory(char *path,unsigned depth) {
    char part[16];snprintf(part,sizeof(part),"/level%02u",depth);strcat(path,part);
}
static FRESULT inspect_directory(FATFS *fs,const char *path) {
    unsigned writes=disk_writes,allocations=fatfs_allocations;
    FILINFO info;FRESULT stat_result=f_stat(fs,path,&info);
    FFDIR dir;FRESULT open_result=f_opendir(&dir,fs,path);
    assert(stat_result==open_result);
    if(open_result==FR_OK) {
        /* Reading a directory and revalidating its attributes is the native
           path used by Directory.EnumerateFileSystemEntries. */
        assert(f_readdir(&dir,&info)==FR_OK);
        assert(f_closedir(&dir)==FR_OK);
    }
    assert(disk_writes==writes && fatfs_allocations==allocations);
    return stat_result;
}
static void format(FATFS *fs,BYTE type) {
    BYTE work[4096];MKFS_PARM options={type|FM_SFD,1,0,0,type==FM_EXFAT?32768:512};
    memset(image,0,sizeof(image));
    assert(f_mkfs(image,0,&options,work,sizeof(work))==FR_OK);
    assert(f_mount(fs,image,0)==FR_OK);
}
static void baseline_exfat(const char *filename) {
    FATFS fs={0};format(&fs,FM_EXFAT);assert(fs.fs_type==4 && FF_PATH_DEPTH==10);
    char path[1024]="";
    for(unsigned depth=1;depth<=11;++depth) {
        append_directory(path,depth);assert(f_mkdir(&fs,path)==FR_OK);
        FRESULT result=inspect_directory(&fs,path);
        printf("Unpatched exFAT directory depth %u: result %u\n",depth,result);
        if(depth<=10)assert(result==FR_OK);
        else {
            int error=0;assert(result==FR_NOT_ENOUGH_CORE);
            assert(!_FAT_set_errno(result,&error) && error==ENOMEM);
        }
    }
    assert(f_umount(&fs)==FR_OK);
    FILE *file=fopen(filename,"wb");assert(file);
    assert(fwrite(image,1,sizeof(image),file)==sizeof(image));assert(!fclose(file));
    puts("Unpatched exFAT depth 11 reproduces false ENOMEM without a FatFs heap allocation.");
}
static void patched_exfat(const char *filename) {
    FILE *file=fopen(filename,"rb");assert(file);
    assert(fread(image,1,sizeof(image),file)==sizeof(image));assert(fgetc(file)==EOF);assert(!fclose(file));
    FATFS fs={0};assert(f_mount(&fs,image,0)==FR_OK);
    assert(fs.fs_type==4 && FF_PATH_DEPTH==64);
    char path[1024]="";
    for(unsigned depth=1;depth<=11;++depth) {
        append_directory(path,depth);assert(inspect_directory(&fs,path)==FR_OK);
    }
    assert(disk_writes==0);
    puts("Patched exFAT reads the identical depth-11 image successfully with zero writes.");
    for(unsigned depth=12;depth<=65;++depth) {
        append_directory(path,depth);assert(f_mkdir(&fs,path)==FR_OK);
        FRESULT result=inspect_directory(&fs,path);
        if(depth<=64)assert(result==FR_OK);
        else {
            int error=0;assert((unsigned)result==20);
            assert(!_FAT_set_errno(result,&error) && error==ENAMETOOLONG && error!=ENOMEM);
        }
        if(depth==31 || depth==64 || depth==65)
            printf("Patched exFAT directory depth %u: result %u\n",depth,result);
        if(depth==64) {
            char file_path[1100];snprintf(file_path,sizeof(file_path),"%s/For\xc3\xaat \xf0\x9f\xa6\x8a.bin",path);
            static const char contents[]="verified leaf at exFAT depth64";
            FFFIL leaf;UINT count;
            assert(f_open(&leaf,&fs,file_path,FA_CREATE_NEW|FA_WRITE)==FR_OK);
            assert(f_write(&leaf,contents,sizeof(contents),&count)==FR_OK && count==sizeof(contents));
            assert(f_close(&leaf)==FR_OK);
            unsigned writes=disk_writes;char actual[sizeof(contents)];
            assert(f_open(&leaf,&fs,file_path,FA_READ)==FR_OK);
            assert(f_read(&leaf,actual,sizeof(actual),&count)==FR_OK && count==sizeof(actual));
            assert(!memcmp(actual,contents,sizeof(actual)) && f_close(&leaf)==FR_OK && writes==disk_writes);
        }
    }
    assert(f_umount(&fs)==FR_OK);
}
static void unchanged_fat32(void) {
    FATFS fs={0};format(&fs,FM_FAT32);assert(fs.fs_type==3);
    char path[1024]="";
    for(unsigned depth=1;depth<=65;++depth) {
        append_directory(path,depth);assert(f_mkdir(&fs,path)==FR_OK);
        assert(inspect_directory(&fs,path)==FR_OK);
    }
    assert(f_umount(&fs)==FR_OK);
    puts("FAT32 depth 65 remains readable; metadata traversal allocates no heap and writes no sectors.");
}
int main(int argc,char **argv) {
    assert(argc==3);
    if(!strcmp(argv[1],"--baseline"))baseline_exfat(argv[2]);
    else {assert(!strcmp(argv[1],"--patched"));patched_exfat(argv[2]);}
    unchanged_fat32();assert(fatfs_allocations==0);
    puts("FatFs depth/errno, Unicode leaf contents, read-only metadata and allocation guards passed.");
    return 0;
}
