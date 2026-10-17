/* Exercise actual libogc/newlib devoptab mounting from a synthetic ISO source.
 * Only the backing drive is synthetic; fopen/fread/fseek/opendir and dvd:/ are real. */
#include "dnd_fst.h"
#include <gccore.h>
#include <sys/iosupport.h>
#include <sys/stat.h>
#include <dirent.h>
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <unistd.h>

bool dnd_gc_mount_disc_image(const char *);
void dnd_gc_unmount_disc(void);
static unsigned char image[16384];
static unsigned opened,closed;
volatile uint32_t dnd_disc_test_result;
typedef struct { uint32_t position; } ImageFile;
static int source_open(struct _reent *r,void *state,const char *path,int flags,int mode) {
    (void)mode;
    if(strcmp(path,"fixture:/game.iso") || (flags&O_ACCMODE)!=O_RDONLY) {
        r->_errno=ENOENT;return -1;
    }
    ((ImageFile *)state)->position=0;opened++;return 0;
}
static int source_close(struct _reent *r,void *state) { (void)r;(void)state;closed++;return 0; }
static ssize_t source_read(struct _reent *r,void *state,char *data,size_t count) {
    (void)r;ImageFile *file=state;
    size_t available=file->position<sizeof(image)?sizeof(image)-file->position:0;
    if(count>available)count=available;
    memcpy(data,image+file->position,count);file->position+=count;return (ssize_t)count;
}
static off_t source_seek(struct _reent *r,void *state,off_t offset,int origin) {
    ImageFile *file=state;
    int64_t basis=origin==SEEK_SET?0:origin==SEEK_CUR?(int64_t)file->position:
        origin==SEEK_END?(int64_t)sizeof(image):-1;
    int64_t position=basis+offset;
    if(basis<0 || position<0 || position>(int64_t)sizeof(image)) { r->_errno=EINVAL;return -1; }
    file->position=(uint32_t)position;return (off_t)position;
}
static int source_stat(struct _reent *r,void *state,struct stat *st) {
    (void)r;(void)state;memset(st,0,sizeof(*st));st->st_mode=S_IFREG|0444;st->st_size=sizeof(image);return 0;
}
static const devoptab_t source={.name="fixture",.structSize=sizeof(ImageFile),
    .open_r=source_open,.close_r=source_close,.read_r=source_read,.seek_r=source_seek,.fstat_r=source_stat};
static void put(unsigned at,uint32_t value) {
    for(int i=0;i<4;i++)image[at+i]=(unsigned char)(value>>(24-i*8));
}
static void prepare(void) {
    memcpy(image,"TEST01",6);memcpy(image+0x20,"DolphinDotNet fixture",21);
    put(0x1c,0xc2339f3d);put(0x424,0x500);put(0x428,72);
    put(0x500,0x01000000);put(0x508,4);
    put(0x50c,0x01000001);put(0x510,0);put(0x514,3);
    put(0x518,5);put(0x51c,0x1800);put(0x520,5);
    put(0x524,10);put(0x528,0x2800);put(0x52c,3);
    memcpy(image+0x530,"\0dir\0data\0top\0",14);
    memcpy(image+0x1800,"hello",5);memcpy(image+0x2800,"abc",3);
}
#define REQUIRE(x,n) do { if(!(x))return (n); } while(0)
static int verify(void) {
    prepare();REQUIRE(AddDevice(&source)>=0,1);
    REQUIRE(!dnd_gc_mount_disc_image("fixture:/missing.iso"),2);
    for(int cycle=0;cycle<3;cycle++) {
        REQUIRE(dnd_gc_mount_disc_image("fixture:/game.iso"),3);
        uint8_t header[0x440];
        REQUIRE(dnd_gc_fst_copy_header(header) && !memcmp(header,image,sizeof(header)),4);
        REQUIRE(!dnd_gc_mount_disc_image("fixture:/game.iso"),5);
        FILE *file=fopen("dvd:/dir/data","rb");REQUIRE(file,6);
        char text[16]={0};
        REQUIRE(fread(text,1,sizeof(text),file)==5 && !memcmp(text,"hello",5),7);
        REQUIRE(fseek(file,-4,SEEK_END)==0 && ftell(file)==1,8);
        REQUIRE(fread(text,1,4,file)==4 && !memcmp(text,"ello",4),9);
        REQUIRE(fread(text,1,1,file)==0 && feof(file),10);
        REQUIRE(fclose(file)==0,11);
        file=fopen("dvd:/top","rb");REQUIRE(file,12);
        REQUIRE(fread(text,1,sizeof(text),file)==3 && !memcmp(text,"abc",3),13);
        REQUIRE(fclose(file)==0,14);
        errno=0;REQUIRE(!fopen("dvd:/top","wb") && errno==EROFS,15);
        errno=0;REQUIRE(!fopen("dvd:/missing","rb") && errno==ENOENT,16);
        struct stat st;REQUIRE(stat("dvd:/dir",&st)==0 && S_ISDIR(st.st_mode),17);
        DIR *dir=opendir("dvd:/");REQUIRE(dir,18);
        struct dirent *entry=readdir(dir);REQUIRE(entry && !strcmp(entry->d_name,"dir"),19);
        entry=readdir(dir);REQUIRE(entry && !strcmp(entry->d_name,"top"),20);
        REQUIRE(!readdir(dir),21);rewinddir(dir);
        entry=readdir(dir);REQUIRE(entry && !strcmp(entry->d_name,"dir"),22);
        REQUIRE(closedir(dir)==0,23);
        dir=opendir("dvd:/dir");REQUIRE(dir,24);
        entry=readdir(dir);REQUIRE(entry && !strcmp(entry->d_name,"data"),25);
        REQUIRE(!readdir(dir) && closedir(dir)==0,26);
        dnd_gc_unmount_disc();REQUIRE(opened==closed,27);
        REQUIRE(!dnd_gc_fst_copy_header(header),28);
    }
    /* Corruption fails cleanly, then the same path can be mounted again. */
    image[0x1c]=0;REQUIRE(!dnd_gc_mount_disc_image("fixture:/game.iso") && opened==closed,29);
    image[0x1c]=0xc2;REQUIRE(dnd_gc_mount_disc_image("fixture:/game.iso"),30);
    dnd_gc_unmount_disc();REQUIRE(opened==closed,31);
    REQUIRE(RemoveDevice("fixture:")>=0,32);return 0;
}
__attribute__((noinline,noreturn)) void dnd_disc_test_done(void) {
    for(;;)usleep(1000);
}
int main(void) {
    int result=verify();
    dnd_disc_test_result=result?0xdead0000u|(uint32_t)result:0x49534f31;
    DCFlushRange((void *)&dnd_disc_test_result,sizeof(dnd_disc_test_result));
    dnd_disc_test_done();
}
