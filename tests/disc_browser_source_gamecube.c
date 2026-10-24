/* Test-only GC Loader volume. Browser, stdio and ISO mounts remain real;
 * only firmware-backed card discovery and source sectors are synthetic. */
#include <gccore.h>
#include <sys/iosupport.h>
#include <sys/stat.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <string.h>

static unsigned char images[2][8192];
static const char *names[]={"bad.iso","first.iso","second.gcm"};
typedef struct { unsigned position,index; } ImageFile;
static int entry(const char *path) {
    const char *prefix="gcloader:/games/";
    if(strncmp(path,prefix,strlen(prefix)))return -1;
    for(int i=0;i<3;i++)if(!strcmp(path+strlen(prefix),names[i]))return i;
    return -1;
}
static int directory(const char *path) {
    return !strcmp(path,"gcloader:/")?1:!strcmp(path,"gcloader:/games")||!strcmp(path,"gcloader:/games/")?2:0;
}
static void info(struct stat *st,bool dir) {
    memset(st,0,sizeof(*st));st->st_mode=dir?S_IFDIR|0555:S_IFREG|0444;st->st_size=dir?0:8192;
}
static int source_stat(struct _reent *r,const char *path,struct stat *st) {
    int dir=directory(path);if(!dir&&entry(path)<0){r->_errno=ENOENT;return -1;}info(st,dir!=0);return 0;
}
static int source_open(struct _reent *r,void *state,const char *path,int flags,int mode) {
    (void)mode;int index=entry(path);
    if(index<0||(flags&O_ACCMODE)!=O_RDONLY){r->_errno=ENOENT;return -1;}
    *(ImageFile*)state=(ImageFile){0,(unsigned)index};return 0;
}
static int source_close(struct _reent *r,void *state){(void)r;(void)state;return 0;}
static ssize_t source_read(struct _reent *r,void *state,char *data,size_t count) {
    (void)r;ImageFile *file=state;size_t available=file->position<8192?8192-file->position:0;
    if(count>available)count=available;
    if(file->index==0)memset(data,0,count);
    else memcpy(data,images[file->index-1]+file->position,count);
    file->position+=count;return (ssize_t)count;
}
static off_t source_seek(struct _reent *r,void *state,off_t offset,int origin) {
    ImageFile *file=state;long long base=origin==SEEK_SET?0:origin==SEEK_CUR?(long long)file->position:origin==SEEK_END?8192:-1;
    long long position=base+offset;if(base<0||position<0||position>8192){r->_errno=EINVAL;return -1;}
    file->position=(unsigned)position;return (off_t)position;
}
static int source_fstat(struct _reent *r,void *state,struct stat *st){(void)r;(void)state;info(st,false);return 0;}
typedef struct { unsigned index,kind; } SourceDirectory;
static DIR_ITER *source_diropen(struct _reent *r,DIR_ITER *iterator,const char *path) {
    int kind=directory(path);if(!kind){r->_errno=ENOENT;return NULL;}
    *(SourceDirectory*)iterator->dirStruct=(SourceDirectory){0,(unsigned)kind};return iterator;
}
static int source_dirnext(struct _reent *r,DIR_ITER *iterator,char *name,struct stat *st) {
    SourceDirectory *dir=iterator->dirStruct;
    if(dir->index>=(dir->kind==1?1u:3u)){r->_errno=ENOENT;return -1;}
    strcpy(name,dir->kind==1?"games":names[dir->index]);info(st,dir->kind==1);dir->index++;return 0;
}
static int source_dirreset(struct _reent *r,DIR_ITER *iterator){(void)r;((SourceDirectory*)iterator->dirStruct)->index=0;return 0;}
static int source_dirclose(struct _reent *r,DIR_ITER *iterator){(void)r;(void)iterator;return 0;}
static const devoptab_t source={.name="gcloader",.structSize=sizeof(ImageFile),
    .open_r=source_open,.close_r=source_close,.read_r=source_read,.seek_r=source_seek,
    .fstat_r=source_fstat,.stat_r=source_stat,.dirStateSize=sizeof(SourceDirectory),
    .diropen_r=source_diropen,.dirreset_r=source_dirreset,.dirnext_r=source_dirnext,.dirclose_r=source_dirclose};
static void put(unsigned char *bytes,unsigned at,unsigned value){for(int i=0;i<4;i++)bytes[at+i]=(unsigned char)(value>>(24-i*8));}
static void prepare(void) {
    for(int i=0;i<2;i++){
        unsigned char *data=images[i];memset(data,0,8192);memcpy(data,"TEST01",6);
        strcpy((char*)data+0x20,i?"Second image":"First image");put(data,0x1c,0xc2339f3d);
        put(data,0x424,0x500);put(data,0x428,30);put(data,0x500,0x01000000);put(data,0x508,2);
        put(data,0x50c,1);put(data,0x510,0x1800);put(data,0x514,5);memcpy(data+0x518,"\0data\0",6);
        memcpy(data+0x1800,"hello",5);
    }
}
bool __real_dnd_gc_mount(int device);
void __real_dnd_gc_unmount(int device);
bool __wrap_dnd_gc_mount(int device) {
    if(device!=6)return __real_dnd_gc_mount(device);
    prepare();return AddDevice(&source)>=0;
}
void __wrap_dnd_gc_unmount(int device) {
    if(device==6)RemoveDevice("gcloader:");else __real_dnd_gc_unmount(device);
}
