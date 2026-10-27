#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/stat.h>
#include <unistd.h>
int dnd_raw_depth(void){return 0;}
bool dnd_device_active(void){return false;}
void dnd_draw_backend_wait(void){}
static int destination,dispose_during_read;
int dnd_async_native_begin(void) {
    assert(dnd_buffer_address(destination,0)==0); /* exclusive lease */
    assert(dnd_exception_pending());dnd_exception_clear();
    if(dispose_during_read)assert(dnd_draw_release(destination)==0);
    return -1;
}
void dnd_async_native_end(int context){assert(context==-1);}
void dnd_async_wait_external(void){assert(!"Unexpected contention in single-thread storage fixture.");}
void dnd_console_write_line(const char *s){(void)s;}
int main(void) {
 char root[]="/tmp/dnd-asset-XXXXXX",path[256];assert(mkdtemp(root));setenv("DND_STORAGE_ROOT",root,1);
 snprintf(path,sizeof(path),"%s/sd",root);assert(!mkdir(path,0700));
 snprintf(path,sizeof(path),"%s/sd/data",root);FILE *f=fopen(path,"wb");assert(f);
 for(int i=0;i<10000;i++)fputc(i&255,f);
 fclose(f);assert(dnd_storage_mount(2));
 int file=dnd_fs_open_utf8_native("sd:/data",3,1,1);assert(file>0);
 destination=dnd_buffer_create(16384,32);assert(destination);
 assert(dnd_asset_read(file,7,destination,3,12000)==9993);assert(!dnd_exception_pending());
 unsigned char *p=(void*)(uintptr_t)dnd_buffer_address(destination,0);
 for(int i=0;i<9993;i++)assert(p[i+3]==((i+7)&255));
 assert(dnd_asset_read(file,10000,destination,0,10)==0);
 dispose_during_read=1;assert(dnd_asset_read(file,0,destination,0,100)==100);
 assert(dnd_draw_live()==0);dnd_fs_close(file);assert(dnd_fs_open_handles()==0);
 unlink(path);snprintf(path,sizeof(path),"%s/sd",root);rmdir(path);rmdir(root);
 puts("Native asset leases, offset/range, EOF, exact bytes and dispose during I/O: PASS");
}
