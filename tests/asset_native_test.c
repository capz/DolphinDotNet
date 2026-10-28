#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include "dnd_draw.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
int dnd_raw_depth(void){return 0;}
bool dnd_device_active(void){return false;}
void dnd_draw_backend_wait(void){}
static int destination,dispose_during_read,short_limit,interrupts,io_error;
static size_t largest_read;
/* Production storage is compiled with read redirected only in this fixture.
 * pread keeps the fault backend independent of that macro. */
ssize_t dnd_asset_test_read(int fd,void *buffer,size_t count) {
 if(count>largest_read)largest_read=count;
 if(interrupts){interrupts--;errno=EINTR;return -1;}
 if(io_error){errno=EIO;return -1;}
 if(short_limit&&count>(size_t)short_limit)count=(size_t)short_limit;
 off_t position=lseek(fd,0,SEEK_CUR);ssize_t n=pread(fd,buffer,count,position);
 if(n>0)assert(lseek(fd,n,SEEK_CUR)>=0);
 return n;
}
int dnd_async_native_begin(void) {
 assert(dnd_buffer_address(destination,0)==0); /* exclusive lease */
 assert(dnd_exception_pending());dnd_exception_clear();
 if(dispose_during_read)assert(dnd_draw_release(destination)==0);
 return -1;
}
void dnd_async_native_end(int context){assert(context==-1);}
void dnd_async_wait_external(void){assert(!"Unexpected contention in single-thread fixture.");}
void dnd_console_write_line(const char *s){(void)s;}
static void bytes(int start,int offset,int count) {
 unsigned char *p=(void*)(uintptr_t)dnd_buffer_address(destination,0);assert(p);
 for(int i=0;i<count;i++)assert(p[offset+i]==((start+i)&255));
}
int main(void) {
 char root[]="/tmp/dnd-asset-XXXXXX",path[256];assert(mkdtemp(root));setenv("DND_STORAGE_ROOT",root,1);
 snprintf(path,sizeof(path),"%s/sd",root);assert(!mkdir(path,0700));
 snprintf(path,sizeof(path),"%s/sd/data",root);FILE *f=fopen(path,"wb");assert(f);
 for(int i=0;i<131079;i++)fputc(i&255,f);
 fclose(f);assert(dnd_storage_mount(2));
 int file=dnd_fs_open_utf8_native("sd:/data",3,1,1);assert(file>0);
 destination=dnd_buffer_create(131136,32);assert(destination);
 int quanta[]={0,16384,32768,65536};
 for(int q=0;q<4;q++) {
  dnd_asset_read_quantum(quanta[q]);dnd_asset_reset_stats();largest_read=0;
  assert(dnd_asset_read(file,0,destination,0,131072)==131072);bytes(0,0,131072);
  assert(dnd_asset_stat(0)==(q?131072/quanta[q]:32));
  assert(dnd_asset_stat(1)==131072&&dnd_asset_stat(4)==1);
  assert(dnd_asset_stat(2)==(q?0:131072));assert(dnd_asset_stat(3)==(q?131072:0));
  assert(largest_read==(size_t)(q?quanta[q]:4096));
  // Unaligned file offset and destination, aligned bulk, and tiny tail.
  assert(dnd_asset_read(file,7,destination,3,131090)==131072);bytes(7,3,131072);
  assert(dnd_asset_read(file,131079,destination,0,32)==0);
 }
 dnd_asset_read_quantum(32768);short_limit=19;interrupts=1;
 assert(dnd_asset_read(file,17,destination,1,2051)==2051);bytes(17,1,2051);
 short_limit=0;io_error=1;
 assert(dnd_asset_read(file,0,destination,0,65536)==-1);assert(dnd_exception_pending());dnd_exception_clear();
 assert(dnd_buffer_address(destination,0)); // Error retired the exclusive lease.
 io_error=0;
 dnd_asset_read_quantum(4096);assert(dnd_exception_pending());dnd_exception_clear();
 assert(dnd_asset_read(file,-1,destination,0,1)==-1);assert(dnd_exception_pending());dnd_exception_clear();
 assert(dnd_asset_read(file,0,destination,131136,1)==-1);assert(dnd_exception_pending());dnd_exception_clear();
 DndArray *snapshot=calloc(1,sizeof(DndArray)+7*8);assert(snapshot);
 snapshot->length=7;snapshot->element_size=8;dnd_asset_snapshot(snapshot);
 for(int i=0;i<7;i++){int64_t value;memcpy(&value,snapshot->data+i*8,8);assert(value==dnd_asset_stat(i));}
 snapshot->length=1;dnd_asset_snapshot(snapshot);assert(dnd_exception_pending());dnd_exception_clear();free(snapshot);
 dispose_during_read=1;assert(dnd_asset_read(file,0,destination,0,65536)==65536);
 assert(dnd_draw_live()==0);dnd_fs_close(file);assert(dnd_fs_open_handles()==0);
 unlink(path);snprintf(path,sizeof(path),"%s/sd",root);rmdir(path);rmdir(root);
 puts("Native reads: bounded 16/32/64 KiB, bounce fallback, bytes, short/EINTR/EOF/error, exclusive lease and dispose: PASS");
}
