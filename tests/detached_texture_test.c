#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static bool frame,application=true;
static int waits,publishes,flushes,completed;
bool dnd_device_active(void){return frame;}
int dnd_device_pass(void){return 0;}
int dnd_raw_depth(void){return 0;}
int dnd_device_fence_completed(int fence){return fence>0&&fence<=completed;}
void dnd_async_require_application_thread(void){assert(application);}
void dnd_draw_backend_wait(void){waits++;}
void dnd_draw_backend_publish_texture(void){assert(application);publishes++;}
void dnd_draw_backend_flush(void *p,int n,int mode){assert(p&&n>0);if(!application)assert(mode==0);flushes++;}
void dnd_draw_backend_2d(int w,int h){(void)w;(void)h;}
void dnd_exception_throw(DndExceptionKind kind,const char *message){(void)kind;(void)message;abort();}
static DndArray *bytes(int n){DndArray *a=calloc(1,sizeof(*a)+(size_t)n);assert(a);a->length=n;a->element_size=1;for(int i=0;i<n;i++)a->data[i]=(unsigned char)(i*37+11);return a;}
int main(void){
    const int chunk=32768,total=1024*1024;
    int source=dnd_buffer_create(chunk,32),target=dnd_texture_allocate_detached(512,512,6,1);assert(source&&target);
    assert(dnd_draw_total_bytes()==chunk+total);DndDrawTexture texture;
    assert(dnd_texture_resolve(target,&texture)<0);assert(dnd_texture_publish_staged(target)<0);
    DndArray *data=bytes(chunk);assert(dnd_buffer_write(source,0,data,0,chunk)==0);
    assert(dnd_texture_stage_buffer_range(target,source,0,0,chunk)<0);
    int before=waits;
    for(int offset=0;offset<total;offset+=chunk){
        assert(dnd_buffer_hold(source)==0&&dnd_texture_upload_hold(target)==0);
        assert(dnd_buffer_write(source,0,data,0,32)<0);assert(dnd_texture_upload_hold(target)<0);
        assert(dnd_texture_publish_staged(target)<0);
        application=false;frame=true;
        assert(dnd_texture_stage_buffer_range(target,source,1,offset,chunk)<0);
        assert(dnd_texture_stage_buffer_range(target,source,0,offset,31)<0);
        assert(dnd_texture_stage_buffer_range(target,source,0,offset+32,chunk)<0);
        assert(dnd_texture_stage_buffer_range(target,source,0,offset,chunk)==0);
        assert(dnd_texture_stage_buffer_range(target,source,0,offset,chunk)<0);
        frame=false;application=true;
        assert(dnd_texture_upload_unhold(target)==0&&dnd_buffer_unhold(source)==0);
        if(offset+chunk<total)assert(dnd_texture_publish_staged(target)<0);
    }
    assert(waits==before&&publishes==0);assert(dnd_texture_publish_staged(target)==0&&publishes==1);
    assert(dnd_texture_resolve(target,&texture)==0&&texture.bytes==total&&texture.levels==1);
    for(int i=0;i<total;i++)assert(((const unsigned char*)texture.data)[i]==data->data[i%chunk]);
    assert(dnd_texture_release_completed(target,0)<0&&dnd_texture_release_completed(target,9)<0);
    completed=9;assert(dnd_texture_release_completed(target,9)==0&&waits==before);
    assert(dnd_draw_total_bytes()==chunk);assert(dnd_texture_resolve(target,&texture)<0);
    int aborted=dnd_texture_allocate_detached(4,4,6,1);assert(aborted);
    assert(dnd_texture_upload_hold(aborted)==0&&dnd_buffer_hold(source)==0);
    assert(dnd_draw_release(aborted)==0); /* held, never freed under the worker */
    application=false;assert(dnd_texture_stage_buffer_range(aborted,source,0,0,64)==0);application=true;
    assert(dnd_texture_publish_staged(aborted)<0);assert(dnd_texture_upload_unhold(aborted)==0&&dnd_buffer_unhold(source)==0);
    assert(dnd_draw_total_bytes()==chunk&&waits==before);
    aborted=dnd_texture_allocate_detached(8,8,14,1);frame=true;assert(dnd_draw_release(aborted)==0);frame=false;
    assert(dnd_draw_total_bytes()==chunk&&waits==before);
    int held_source=dnd_buffer_create(64,32);aborted=dnd_texture_allocate_detached(4,4,6,1);
    assert(dnd_buffer_hold(held_source)==0&&dnd_texture_upload_hold(aborted)==0&&dnd_draw_release(held_source)==0);
    application=false;assert(dnd_texture_stage_buffer_range(aborted,held_source,0,0,64)==0);application=true;
    assert(dnd_texture_upload_unhold(aborted)==0&&dnd_buffer_unhold(held_source)==0);
    assert(dnd_draw_release(aborted)==0&&dnd_draw_release(source)==0);free(data);
    assert(dnd_draw_total_bytes()==0&&dnd_draw_live()==0&&flushes>0);
    puts("Detached texture ranges: holds, 1-MiB chunking, no GX staging/waits, publication, abort and fence retirement passed");return 0;
}
