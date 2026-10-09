#include "dnd_fst.h"
#ifdef DND_GAMECUBE_STORAGE
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <fcntl.h>
#include <gccore.h>
#include <ogc/dvd.h>
#include <string.h>
#include <sys/iosupport.h>
#include <sys/stat.h>
static DndFst fst;
typedef struct {uint32_t entry,position;} FstFile;
typedef struct {uint32_t parent,next;} FstDirectory;
static bool disc_read(void *context,uint64_t offset,void *data,size_t length){
 (void)context;uint8_t block[2048] __attribute__((aligned(32)));dvdcmdblk command;
 while(length){uint64_t base=offset&~(uint64_t)2047;size_t skip=(size_t)(offset-base),n=2048-skip;if(n>length)n=length;memset(&command,0,sizeof(command));if(DVD_ReadAbs(&command,block,sizeof(block),(s64)base)!=(s32)sizeof(block))return false;memcpy(data,block+skip,n);data=(uint8_t*)data+n;offset+=n;length-=n;}return true;
}
static int error(struct _reent *r,int e){r->_errno=e;return -1;}
static void info(uint32_t i,struct stat *st){memset(st,0,sizeof(*st));st->st_mode=dnd_fst_directory(&fst,i)?S_IFDIR|0555:S_IFREG|0444;st->st_size=dnd_fst_directory(&fst,i)?0:dnd_fst_length(&fst,i);st->st_nlink=1;}
static int fs_open(struct _reent *r,void *state,const char *path,int flags,int mode){(void)mode;if((flags&O_ACCMODE)!=O_RDONLY||(flags&(O_CREAT|O_TRUNC|O_APPEND)))return error(r,EROFS);int i=dnd_fst_find(&fst,path);if(i<0)return error(r,ENOENT);if(dnd_fst_directory(&fst,(uint32_t)i))return error(r,EISDIR);FstFile *f=state;f->entry=(uint32_t)i;f->position=0;return (int)(intptr_t)state;}
static int fs_close(struct _reent *r,void *state){(void)r;(void)state;return 0;}
static ssize_t fs_read(struct _reent *r,void *state,char *data,size_t length){FstFile *f=state;int n=dnd_fst_read(&fst,f->entry,f->position,data,length);if(n<0)return error(r,EIO);f->position+=(uint32_t)n;return n;}
static off_t fs_seek(struct _reent *r,void *state,off_t offset,int origin){FstFile *f=state;int64_t basis=origin==SEEK_SET?0:origin==SEEK_CUR?(int64_t)f->position:origin==SEEK_END?(int64_t)dnd_fst_length(&fst,f->entry):-1;if(basis<0||offset< -basis||offset>INT32_MAX-basis)return error(r,EINVAL);f->position=(uint32_t)(basis+offset);return (off_t)f->position;}
static int fs_fstat(struct _reent *r,void *state,struct stat *st){(void)r;info(((FstFile*)state)->entry,st);return 0;}
static int fs_stat(struct _reent *r,const char *path,struct stat *st){int i=dnd_fst_find(&fst,path);if(i<0)return error(r,ENOENT);info((uint32_t)i,st);return 0;}
static DIR_ITER *fs_diropen(struct _reent *r,DIR_ITER *iterator,const char *path){int i=dnd_fst_find(&fst,path);if(i<0){error(r,ENOENT);return NULL;}if(!dnd_fst_directory(&fst,(uint32_t)i)){error(r,ENOTDIR);return NULL;}FstDirectory *d=iterator->dirStruct;d->parent=(uint32_t)i;d->next=(uint32_t)i+1;return iterator;}
static int fs_dirreset(struct _reent *r,DIR_ITER *iterator){(void)r;FstDirectory *d=iterator->dirStruct;d->next=d->parent+1;return 0;}
static int fs_dirnext(struct _reent *r,DIR_ITER *iterator,char *name,struct stat *st){FstDirectory *d=iterator->dirStruct;if(d->next>=dnd_fst_next(&fst,d->parent))return error(r,ENOENT);strcpy(name,dnd_fst_name(&fst,d->next));info(d->next,st);d->next=dnd_fst_next(&fst,d->next);return 0;}
static int fs_dirclose(struct _reent *r,DIR_ITER *iterator){(void)r;(void)iterator;return 0;}
static const devoptab_t device={.name="dvd",.structSize=sizeof(FstFile),.open_r=fs_open,.close_r=fs_close,.read_r=fs_read,.seek_r=fs_seek,.fstat_r=fs_fstat,.stat_r=fs_stat,.dirStateSize=sizeof(FstDirectory),.diropen_r=fs_diropen,.dirreset_r=fs_dirreset,.dirnext_r=fs_dirnext,.dirclose_r=fs_dirclose};
bool dnd_gc_fst_mount(void){if(!dnd_fst_mount(&fst,disc_read,NULL,1459978240ull))return false;if(AddDevice(&device)<0){dnd_fst_unmount(&fst);return false;}return true;}
void dnd_gc_fst_unmount(void){RemoveDevice("dvd:");dnd_fst_unmount(&fst);}
#endif
