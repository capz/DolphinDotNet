#include "dnd_card_manager.h"
#include <ogc/card.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define FILES 8
#define BLOCK 8192
static struct FakeFile {bool used;card_stat stat;u8 attr;unsigned char *data;} files[2][FILES];
static cardcallback callbacks[2];static int mount_status[2],formats,reads,writes,deletes;static u16 blocks[2]={64,64};
static bool corrupt_read,detach_read,fail_write,fail_destination_refresh;static char game[4],company[2];
bool dnd_storage_is_mounted(int d){(void)d;return false;}
s32 CARD_Init(const char*g,const char*c){(void)g;(void)c;return 0;}
s32 CARD_Mount(s32 c,void*w,cardcallback cb){assert(w);callbacks[c]=cb;return mount_status[c];}
s32 CARD_Unmount(s32 c){(void)c;return 0;}
s32 CARD_GetDirectory(s32 c,card_dir*out,s32*count,bool all){assert(all);if(c==1&&fail_destination_refresh&&deletes>0)return -5;*count=0;for(int i=0;i<FILES;i++)if(files[c][i].used){struct FakeFile*f=&files[c][i];card_dir *d=&out[(*count)++];memset(d,0,sizeof(*d));d->chn=c;d->fileno=(u32)i;d->filelen=f->stat.len;d->permissions=f->attr;memcpy(d->filename,f->stat.filename,32);memcpy(d->gamecode,f->stat.gamecode,4);memcpy(d->company,f->stat.company,2);d->showall=true;}return *count==0?-4:0;}
s32 CARD_GetBlockCount(s32 c,u16*b){*b=blocks[c];return 0;}
s32 CARD_GetSectorSize(s32 c,u32*b){(void)c;*b=BLOCK;return 0;}
s32 CARD_GetStatus(s32 c,s32 n,card_stat*s){if(n<0||n>=FILES||!files[c][n].used)return -4;*s=files[c][n].stat;return 0;}
s32 CARD_SetStatus(s32 c,s32 n,card_stat*s){card_stat*stat=&files[c][n].stat;stat->banner_fmt=s->banner_fmt;stat->icon_addr=s->icon_addr;stat->icon_fmt=s->icon_fmt;stat->icon_speed=s->icon_speed;stat->comment_addr=s->comment_addr;memcpy(stat->offset_icon,s->offset_icon,sizeof(stat->offset_icon));memcpy(stat->offset_icon_tlut,s->offset_icon_tlut,sizeof(stat->offset_icon_tlut));stat->time++;return 0;}
s32 CARD_GetAttributes(s32 c,s32 n,u8*a){*a=files[c][n].attr;return 0;}
s32 CARD_SetAttributes(s32 c,s32 n,u8 a){files[c][n].attr=a;return 0;}
s32 CARD_OpenEntry(s32 c,card_dir*d,card_file*f){if(!files[c][d->fileno].used)return -4;*f=(card_file){c,(s32)d->fileno,0,d->filelen,0};return 0;}
s32 CARD_Close(card_file*f){(void)f;return 0;}
s32 CARD_Read(card_file*f,void*out,u32 n,u32 offset){assert(n%BLOCK==0&&offset%BLOCK==0&&((uintptr_t)out&31)==0&&offset+n<=files[f->chn][f->filenum].stat.len);reads++;if(detach_read&&f->chn==1){detach_read=false;callbacks[1](1,-3);return -3;}memcpy(out,files[f->chn][f->filenum].data+offset,n);if(corrupt_read&&f->chn==1)((u8*)out)[0]^=1;f->len=0;return 0;}
s32 CARD_Write(card_file*f,void*in,u32 n,u32 offset){assert(n%BLOCK==0&&offset%BLOCK==0&&((uintptr_t)in&31)==0&&offset+n<=files[f->chn][f->filenum].stat.len);writes++;if(fail_write)return -5;memcpy(files[f->chn][f->filenum].data+offset,in,n);files[f->chn][f->filenum].stat.time++;f->len=0;return 0;}
s32 CARD_DeleteEntry(s32 c,card_dir*d){assert(files[c][d->fileno].used);deletes++;free(files[c][d->fileno].data);memset(&files[c][d->fileno],0,sizeof(files[c][d->fileno]));return 0;}
s32 CARD_Format(s32 c){formats++;for(int i=0;i<FILES;i++)if(files[c][i].used){free(files[c][i].data);memset(&files[c][i],0,sizeof(files[c][i]));}mount_status[c]=0;return 0;}
s32 CARD_SetGamecode(const char*g){memcpy(game,g,4);return 0;}
s32 CARD_SetCompany(const char*c){memcpy(company,c,2);return 0;}
s32 CARD_Create(s32 c,const char*name,u32 len,card_file*out){for(int i=0;i<FILES;i++)if(files[c][i].used&&!strncmp(files[c][i].stat.filename,name,32)&&!memcmp(files[c][i].stat.gamecode,game,4)&&!memcmp(files[c][i].stat.company,company,2))return -7;for(int i=0;i<FILES;i++)if(!files[c][i].used){struct FakeFile*f=&files[c][i];f->used=true;f->stat.len=len;memset(f->stat.filename,0,32);memcpy(f->stat.filename,name,strlen(name)>32?32:strlen(name));memcpy(f->stat.gamecode,game,4);memcpy(f->stat.company,company,2);f->attr=4;f->stat.time=123;f->stat.icon_addr=f->stat.comment_addr=UINT32_MAX;f->data=calloc(1,len);*out=(card_file){c,i,0,len,0};return 0;}return -8;}
static DndArray* array(size_t n){DndArray*a=calloc(1,sizeof(DndArray)+n);a->length=(u32)n;a->element_size=1;return a;}
static void fixture(void){dnd_cmgr_shutdown();for(int c=0;c<2;c++){for(int i=0;i<FILES;i++){free(files[c][i].data);memset(&files[c][i],0,sizeof(files[c][i]));}blocks[c]=64;mount_status[c]=0;}formats=reads=writes=deletes=0;corrupt_read=detach_read=fail_write=fail_destination_refresh=false;memcpy(game,"ABCD",4);memcpy(company,"01",2);card_file f;assert(CARD_Create(0,"SAVE",BLOCK*2,&f)==0);struct FakeFile*save=&files[0][0];for(u32 i=0;i<save->stat.len;i++)save->data[i]=(u8)(i*37);save->stat.comment_addr=700;memcpy(save->data+700,"Original title\0Original comment\0",32);save->stat.icon_addr=0;save->stat.icon_fmt=2;save->stat.icon_speed=1;save->stat.offset_icon[0]=2048;for(int i=0;i<1024;i++){save->data[2048+i*2]=0xfc;save->data[2048+i*2+1]=0;}}
static int drain(int op){int rc;int calls=0;do{rc=dnd_cmgr_copy_step(op);assert(++calls<20);}while(rc>=0&&rc<100);return rc;}
int main(void){
 fixture();int a=dnd_cmgr_open(0),b=dnd_cmgr_open(1);assert(a>0&&b>0&&dnd_cmgr_stat(a,1)==1&&dnd_cmgr_stat(a,2)==57&&dnd_cmgr_stat(a,3)==59);assert(formats==0);int rev=dnd_cmgr_stat(a,4);DndArray*m=array(128),*icon=array(4096);assert(dnd_cmgr_entry(a,rev,0,m)==0&&!memcmp(m->data+56,"Original title",14));assert(dnd_cmgr_icon(a,rev,0,0,icon)==0&&icon->data[0]==255&&icon->data[1]==0&&icon->data[3]==255);
 int op=dnd_cmgr_copy_begin(a,rev,0,b,0);assert(op>0);assert(dnd_cmgr_refresh(a)==-1006);assert(drain(op)==100&&files[0][0].used&&files[1][0].used&&!memcmp(files[0][0].data,files[1][0].data,BLOCK*2)&&files[1][0].stat.icon_fmt==2&&files[1][0].attr==4);rev=dnd_cmgr_stat(a,4);assert(dnd_cmgr_copy_begin(a,rev,0,b,0)==-7);assert(dnd_cmgr_entry(a,rev-1,0,m)==-1001);
 fixture();a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);files[0][0].attr=12;assert(dnd_cmgr_copy_begin(a,rev,0,b,0)==-1001);assert(dnd_cmgr_refresh(a)==0);rev=dnd_cmgr_stat(a,4);assert(dnd_cmgr_copy_begin(a,rev,0,b,0)==-1003);op=dnd_cmgr_copy_begin(a,rev,0,b,1);assert(op>0&&drain(op)==100&&!files[0][0].used&&files[1][0].attr==12);
 fixture();files[0][0].attr=20;a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);assert(dnd_cmgr_copy_begin(a,rev,0,b,1)==-1004);assert(drain(dnd_cmgr_copy_begin(a,rev,0,b,0))==100);
 fixture();a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);op=dnd_cmgr_copy_begin(a,rev,0,b,1);corrupt_read=true;assert(drain(op)==-1005&&files[0][0].used&&!files[1][0].used);
 fixture();a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);op=dnd_cmgr_copy_begin(a,rev,0,b,1);assert(dnd_cmgr_copy_step(op)>=0);assert(dnd_cmgr_copy_abort(op)==0&&files[0][0].used&&!files[1][0].used);
 fixture();a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);op=dnd_cmgr_copy_begin(a,rev,0,b,1);fail_write=true;assert(drain(op)==-5&&files[0][0].used&&!files[1][0].used);
 fixture();a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);op=dnd_cmgr_copy_begin(a,rev,0,b,1);assert(dnd_cmgr_copy_step(op)>=0&&dnd_cmgr_copy_step(op)>=0);detach_read=true;int before=deletes;assert(drain(op)==-1007&&files[0][0].used&&deletes==before);assert(dnd_cmgr_format(b)==-1001&&formats==0);
 fixture();blocks[1]=6;a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);assert(dnd_cmgr_copy_begin(a,rev,0,b,0)==-9);
 fixture();mount_status[0]=-6;a=dnd_cmgr_open(0);assert(a>0&&dnd_cmgr_stat(a,0)==-6&&formats==0);assert(dnd_cmgr_format(a)==0&&formats==1&&dnd_cmgr_stat(a,1)==0);
 fixture();memset(files[0][0].stat.filename,0x82,32);a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);rev=dnd_cmgr_stat(a,4);assert(drain(dnd_cmgr_copy_begin(a,rev,0,b,0))==100&&!memcmp(files[0][0].stat.filename,files[1][0].stat.filename,32));
 fixture();files[0][0].stat.icon_fmt=1;files[0][0].stat.offset_icon[0]=2048;files[0][0].stat.offset_icon_tlut[0]=3072;memset(files[0][0].data+2048,3,1024);files[0][0].data[3078]=0x80;files[0][0].data[3079]=0x1f;a=dnd_cmgr_open(0);int before_icon=reads;assert(dnd_cmgr_icon(a,dnd_cmgr_stat(a,4),0,0,icon)==0&&icon->data[0]==0&&icon->data[2]==255&&reads==before_icon+1);
 fixture();a=dnd_cmgr_open(0);b=dnd_cmgr_open(1);op=dnd_cmgr_copy_begin(a,dnd_cmgr_stat(a,4),0,b,1);fail_destination_refresh=true;assert(drain(op)==-1008&&!files[0][0].used&&files[1][0].used&&dnd_cmgr_stat(b,1)==-1002);
 dnd_cmgr_shutdown();free(m);free(icon);for(int c=0;c<2;c++)for(int i=0;i<FILES;i++)free(files[c][i].data);puts("Unfiltered card manager: paging, icons, protections, CRC copy/move, cancellation, stale snapshots, removal safety, low space and explicit format passed.");return 0;
}
