/* Unfiltered IPL-style save management. Filenames remain raw native bytes.
 * No managed pointer survives a call. One bounded 8 KiB transfer is active. */
#include "dnd_card_manager.h"
#include "dnd_storage.h"
#include <limits.h>
#include <stdlib.h>
#include <string.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <malloc.h>
#include <ogc/card.h>
#define STALE -1001
#define NOT_READY -1002
#define COPY_PROTECTED -1003
#define MOVE_PROTECTED -1004
#define VERIFY_FAILED -1005
#define OCCUPIED -1006
#define PARTIAL_RETAINED -1007
#define COMMITTED_REFRESH_FAILED -1008
#define BLOCK 8192u
#define MAX_FILES 127
static struct Session {
    int token,revision,count,status; bool valid; volatile bool removed;
    void *work; u16 blocks; card_dir entries[MAX_FILES]; u32 times[MAX_FILES];
} sessions[2];
static int serial=1;
static struct Transfer {
    int token,source,destination,revision,index,phase; bool move,created,source_open,dest_open;
    u32 position,length,write_crc,read_crc; unsigned char *buffer;
    card_file input,output; card_dir created_entry; card_stat source_stat,created_stat;
    u8 attributes;
} transfer;
static int next_token(void){return serial==INT_MAX?OCCUPIED:serial++;}
static struct Session *find(int token){for(int i=0;i<2;i++)if(token>0&&sessions[i].token==token&&!sessions[i].removed)return &sessions[i];return NULL;}
static int channel(struct Session *s){return (int)(s-sessions);}
static void detached(s32 slot,s32 result){(void)result;if(slot>=0&&slot<2)sessions[slot].removed=true;}
bool dnd_cmgr_active(int slot){return slot>=0&&slot<2&&sessions[slot].token!=0;}
static bool same(const card_dir *entry,const card_stat *stat,u32 time){return entry->filelen==stat->len&&time==stat->time&&!memcmp(entry->filename,stat->filename,32)&&!memcmp(entry->gamecode,stat->gamecode,4)&&!memcmp(entry->company,stat->company,2);}
static int checked(int token,int revision,int index,struct Session **session,card_stat *stat){
    struct Session *s=find(token);if(!s||revision!=s->revision)return STALE;
    if(s->status<0||!s->valid)return NOT_READY;
    if(index<0||index>=s->count)return STALE;
    int rc=CARD_GetStatus(channel(s),(s32)s->entries[index].fileno,stat);if(rc<0)return rc;
    if(!find(token)||!same(&s->entries[index],stat,s->times[index]))return STALE;
    u8 attributes;rc=CARD_GetAttributes(channel(s),(s32)s->entries[index].fileno,&attributes);if(rc<0)return rc;
    if(attributes!=s->entries[index].permissions)return STALE;
    *session=s;return 0;
}
int dnd_cmgr_refresh(int token){
    struct Session *s=find(token);if(!s)return STALE;if(transfer.token)return OCCUPIED;
    if(s->status<0)return NOT_READY;
    if(s->revision==INT_MAX)return STALE;
    s->revision++;s->valid=false;
    int count=0,rc=CARD_GetDirectory(channel(s),s->entries,&count,true);if(rc==CARD_ERROR_NOFILE&&count==0)rc=0;if(rc<0)return rc;
    if(count<0||count>MAX_FILES)return VERIFY_FAILED;
    rc=CARD_GetBlockCount(channel(s),&s->blocks);if(rc<0)return rc;
    u32 sector;rc=CARD_GetSectorSize(channel(s),&sector);if(rc<0)return rc;
    if(sector!=BLOCK||s->blocks<5)return VERIFY_FAILED;
    for(int i=0;i<count;i++){card_stat stat;rc=CARD_GetStatus(channel(s),(s32)s->entries[i].fileno,&stat);if(rc<0)return rc;s->times[i]=stat.time;}
    if(!find(token))return STALE;
    s->count=count;s->valid=true;return 0;
}
int dnd_cmgr_open(int slot){
    if(slot<0||slot>1||sessions[slot].token||dnd_storage_is_mounted(slot+4))return OCCUPIED;
    CARD_Init(NULL,NULL);void *work=memalign(32,CARD_WORKAREA);if(!work)return -2001;
    int token=next_token();if(token<0){free(work);return token;}
    struct Session *s=&sessions[slot];memset(s,0,sizeof(*s));s->work=work;s->token=token;
    s->status=CARD_Mount(slot,work,detached);
    if(s->status<0&&s->status!=CARD_ERROR_BROKEN&&s->status!=CARD_ERROR_ENCODING){int error=s->status;CARD_Unmount(slot);free(work);memset(s,0,sizeof(*s));return error;}
    if(s->status>=0){int rc=dnd_cmgr_refresh(token);if(rc<0){dnd_cmgr_close(token);return rc;}}
    return token;
}
int dnd_cmgr_stat(int token,int field){
    struct Session *s=find(token);if(!s)return STALE;
    if(field==0)return s->status;
    if(field==4)return s->revision;
    if(s->status<0||!s->valid)return NOT_READY;
    if(field==1)return s->count;
    if(field==3)return s->blocks-5;
    if(field==5)return MAX_FILES-s->count;
    if(field==2){u32 used=0;for(int i=0;i<s->count;i++){u32 len=s->entries[i].filelen;if(len%BLOCK||len/BLOCK>s->blocks)return VERIFY_FAILED;used+=len/BLOCK;}return used>(u32)(s->blocks-5)?VERIFY_FAILED:(int)(s->blocks-5-used);}
    return STALE;
}
static void put32(unsigned char *p,u32 v){p[0]=(u8)(v>>24);p[1]=(u8)(v>>16);p[2]=(u8)(v>>8);p[3]=(u8)v;}
/* Read full sectors: libogc mutates card_file.len, and older backends do not
 * honor an offset within the first sector. Cache one native sector for an
 * icon and its palette; no managed memory or pointer survives these calls. */
static _Alignas(32) unsigned char read_sector[BLOCK];
static u32 read_sector_address=UINT32_MAX;
static void read_begin(void){read_sector_address=UINT32_MAX;}
static int read_range(card_file *file,u32 file_length,u32 offset,unsigned char *out,u32 length){
    if(file_length%BLOCK||offset>file_length||length>file_length-offset)return VERIFY_FAILED;
    while(length){u32 block=offset&~(BLOCK-1),within=offset&(BLOCK-1),count=BLOCK-within;if(count>length)count=length;
        if(block!=read_sector_address){int rc=CARD_Read(file,read_sector,BLOCK,block);if(rc<0)return rc;read_sector_address=block;}
        memcpy(out,read_sector+within,count);offset+=count;out+=count;length-=count;
    }
    return 0;
}
int dnd_cmgr_entry(int token,int revision,int index,DndArray *metadata){
    if(!metadata||metadata->element_size!=1||metadata->length<128)return STALE;
    struct Session *s;card_stat stat;int rc=checked(token,revision,index,&s,&stat);if(rc<0)return rc;
    unsigned char *out=metadata->data;memset(out,0,128);put32(out,stat.len);put32(out+4,stat.time);out[8]=s->entries[index].permissions;out[9]=stat.banner_fmt;
    out[10]=(u8)(stat.icon_speed>>8);out[11]=(u8)stat.icon_speed;put32(out+12,s->entries[index].fileno);memcpy(out+16,stat.gamecode,4);memcpy(out+20,stat.company,2);out[22]=(u8)(stat.icon_fmt>>8);out[23]=(u8)stat.icon_fmt;memcpy(out+24,stat.filename,32);
    if(stat.comment_addr!=UINT32_MAX&&stat.comment_addr<=stat.len&&64<=stat.len-stat.comment_addr){card_file f;rc=CARD_OpenEntry(channel(s),&s->entries[index],&f);if(rc<0)return rc;read_begin();rc=read_range(&f,stat.len,stat.comment_addr,out+56,64);int close=CARD_Close(&f);if(rc>=0)rc=close;}
    return !find(token)?STALE:rc;
}
static void rgb5a3(u16 pixel,unsigned char *rgba){if(pixel&0x8000){rgba[0]=(u8)((pixel>>10&31)*255/31);rgba[1]=(u8)((pixel>>5&31)*255/31);rgba[2]=(u8)((pixel&31)*255/31);rgba[3]=255;}else{rgba[0]=(u8)((pixel>>8&15)*17);rgba[1]=(u8)((pixel>>4&15)*17);rgba[2]=(u8)((pixel&15)*17);rgba[3]=(u8)((pixel>>12&7)*255/7);}}
int dnd_cmgr_icon(int token,int revision,int index,int frame,DndArray *rgba){
    if(!rgba||rgba->element_size!=1||rgba->length!=4096||frame<0||frame>7)return STALE;
    struct Session *s;card_stat stat;int rc=checked(token,revision,index,&s,&stat);if(rc<0)return rc;
    memset(rgba->data,0,4096);int format=(stat.icon_fmt>>(frame*2))&3;if(format!=1&&format!=2)return 0;
    unsigned char *image=malloc(2048),palette[512];if(!image)return -2001;
    card_file file;rc=CARD_OpenEntry(channel(s),&s->entries[index],&file);if(rc<0){free(image);return rc;}
    read_begin();rc=read_range(&file,stat.len,stat.offset_icon[frame],image,format==2?2048:1024);
    if(rc>=0&&format==1)rc=read_range(&file,stat.len,stat.offset_icon_tlut[frame],palette,512);
    int close=CARD_Close(&file);if(rc>=0)rc=close;
    if(rc>=0){int width=format==2?4:8;for(int y=0;y<32;y++)for(int x=0;x<32;x++){int tiled=((y/4)*(32/width)+x/width)*width*4+(y%4)*width+x%width;const unsigned char *p=format==2?image+tiled*2:palette+image[tiled]*2;rgb5a3((u16)(p[0]<<8|p[1]),rgba->data+(y*32+x)*4);}}
    free(image);return !find(token)?STALE:rc;
}
int dnd_cmgr_delete(int token,int revision,int index){
    if(transfer.token)return OCCUPIED;
    struct Session *s;card_stat stat;int rc=checked(token,revision,index,&s,&stat);if(rc<0)return rc;
    rc=CARD_DeleteEntry(channel(s),&s->entries[index]);if(rc<0)return rc;rc=dnd_cmgr_refresh(token);return rc<0?COMMITTED_REFRESH_FAILED:0;
}
int dnd_cmgr_format(int token){
    if(transfer.token)return OCCUPIED;
    struct Session *s=find(token);if(!s)return STALE;
    if(s->status<0&&s->status!=CARD_ERROR_BROKEN&&s->status!=CARD_ERROR_ENCODING)return NOT_READY;
    int rc=CARD_Format(channel(s));if(rc<0)return rc;s->status=0;rc=dnd_cmgr_refresh(token);return rc<0?COMMITTED_REFRESH_FAILED:0;
}
static u32 crc(u32 state,const unsigned char *data,u32 count){for(u32 i=0;i<count;i++){state^=data[i];for(int bit=0;bit<8;bit++)state=(state>>1)^((state&1)?0xedb88320u:0);}return state;}
static int cleanup(bool remove_partial){
    int result=0;if(transfer.source_open)CARD_Close(&transfer.input);if(transfer.dest_open)CARD_Close(&transfer.output);
    if(remove_partial&&transfer.created){struct Session *d=find(transfer.destination);card_stat now;
        if(!d||CARD_GetStatus(channel(d),(s32)transfer.created_entry.fileno,&now)<0||!same(&transfer.created_entry,&now,transfer.created_stat.time)||CARD_DeleteEntry(channel(d),&transfer.created_entry)<0)result=PARTIAL_RETAINED;
    }
    free(transfer.buffer);memset(&transfer,0,sizeof(transfer));return result;
}
int dnd_cmgr_copy_begin(int source,int revision,int index,int destination,int move){
    if(transfer.token)return OCCUPIED;
    struct Session *s;card_stat stat;int rc=checked(source,revision,index,&s,&stat);if(rc<0)return rc;
    struct Session *d=find(destination);if(!d)return STALE;if(d==s||d->status<0||!d->valid)return NOT_READY;
    u8 attr=s->entries[index].permissions;if(move?(attr&0x10):(attr&0x08))return move?MOVE_PROTECTED:COPY_PROTECTED;
    for(int i=0;i<d->count;i++)if(!memcmp(d->entries[i].filename,stat.filename,32)&&!memcmp(d->entries[i].gamecode,stat.gamecode,4)&&!memcmp(d->entries[i].company,stat.company,2))return CARD_ERROR_EXIST;
    int free_blocks=dnd_cmgr_stat(destination,2);if(free_blocks<0)return free_blocks;if(stat.len/BLOCK>(u32)free_blocks||d->count==MAX_FILES)return CARD_ERROR_INSSPACE;
    if(!stat.len||stat.len%BLOCK)return VERIFY_FAILED;
    unsigned char *buffer=memalign(32,BLOCK);if(!buffer)return -2001;
    int op=next_token();if(op<0){free(buffer);return op;}
    transfer.token=op;transfer.source=source;transfer.destination=destination;transfer.revision=revision;transfer.index=index;transfer.move=move!=0;transfer.length=stat.len;transfer.buffer=buffer;transfer.source_stat=stat;transfer.attributes=attr;transfer.write_crc=transfer.read_crc=UINT32_MAX;
    rc=CARD_OpenEntry(channel(s),&s->entries[index],&transfer.input);if(rc<0){cleanup(false);return rc;}transfer.source_open=true;
    char game[5],company[3],name[33];memcpy(game,stat.gamecode,4);game[4]=0;memcpy(company,stat.company,2);company[2]=0;memcpy(name,stat.filename,32);name[32]=0;
    CARD_SetGamecode(game);CARD_SetCompany(company);rc=CARD_Create(channel(d),name,stat.len,&transfer.output);
    if(rc<0){cleanup(false);return rc;}transfer.dest_open=true;
    transfer.created_entry=s->entries[index];transfer.created_entry.chn=channel(d);transfer.created_entry.fileno=(u32)transfer.output.filenum;transfer.created_entry.showall=true;
    rc=CARD_GetStatus(channel(d),transfer.output.filenum,&transfer.created_stat);
    if(rc<0){/* Identity could not be established: preserve source and unknown destination. */cleanup(false);return PARTIAL_RETAINED;}
    transfer.created=true;return op;
}
int dnd_cmgr_copy_step(int token){
    if(!token||token!=transfer.token)return STALE;
    struct Session *s,*d=find(transfer.destination);card_stat source,now;
    int rc=checked(transfer.source,transfer.revision,transfer.index,&s,&source);
    if(rc>=0&&(!d||CARD_GetStatus(channel(d),(s32)transfer.created_entry.fileno,&now)<0||!same(&transfer.created_entry,&now,transfer.created_stat.time)))rc=STALE;
    if(rc<0){int clean=cleanup(true);return clean<0?clean:rc;}
    if(transfer.phase<2){u32 count=transfer.length-transfer.position;if(count>BLOCK)count=BLOCK;
        if(transfer.phase==0){rc=CARD_Read(&transfer.input,transfer.buffer,count,transfer.position);if(rc>=0){transfer.write_crc=crc(transfer.write_crc,transfer.buffer,count);rc=CARD_Write(&transfer.output,transfer.buffer,count,transfer.position);}}
        else {rc=CARD_Read(&transfer.output,transfer.buffer,count,transfer.position);if(rc>=0)transfer.read_crc=crc(transfer.read_crc,transfer.buffer,count);}
        if(rc>=0&&transfer.phase==0){
            /* CARD_Write mutates card_file.len and the directory timestamp.
             * Refresh only the timestamp of this still-owned destination. */
            card_stat updated;rc=CARD_GetStatus(channel(d),transfer.output.filenum,&updated);
            if(rc>=0&&(!find(transfer.destination)||!same(&transfer.created_entry,&updated,updated.time)))rc=STALE;
            if(rc>=0)transfer.created_stat=updated;
        }
        if(rc<0){int clean=cleanup(true);return clean<0?clean:rc;}
        transfer.position+=count;if(transfer.position==transfer.length){transfer.position=0;transfer.phase++;}
        return (int)(((uint64_t)transfer.phase*transfer.length+transfer.position)*95u/(2u*transfer.length));
    }
    if(transfer.write_crc!=transfer.read_crc){int clean=cleanup(true);return clean<0?clean:VERIFY_FAILED;}
    rc=CARD_SetStatus(channel(d),transfer.output.filenum,&transfer.source_stat);
    if(rc>=0){/* SetStatus changes modification time; recapture before any guarded cleanup. */rc=CARD_GetStatus(channel(d),transfer.output.filenum,&transfer.created_stat);if(rc<0){cleanup(false);return PARTIAL_RETAINED;}}
    if(rc>=0)rc=CARD_SetAttributes(channel(d),transfer.output.filenum,transfer.attributes);
    if(rc<0){int clean=cleanup(true);return clean<0?clean:rc;}
    card_stat verified;u8 verified_attributes;
    rc=CARD_GetStatus(channel(d),transfer.output.filenum,&verified);
    if(rc>=0)rc=CARD_GetAttributes(channel(d),transfer.output.filenum,&verified_attributes);
    if(rc>=0&&(verified_attributes!=transfer.attributes||verified.banner_fmt!=transfer.source_stat.banner_fmt||verified.icon_addr!=transfer.source_stat.icon_addr||verified.icon_fmt!=transfer.source_stat.icon_fmt||verified.icon_speed!=transfer.source_stat.icon_speed||verified.comment_addr!=transfer.source_stat.comment_addr))rc=VERIFY_FAILED;
    if(rc<0){int clean=cleanup(true);return clean<0?clean:rc;}
    /* Only a complete, verified destination permits deleting the source. */
    if(transfer.move){rc=checked(transfer.source,transfer.revision,transfer.index,&s,&source);if(rc>=0){CARD_Close(&transfer.input);transfer.source_open=false;rc=CARD_DeleteEntry(channel(s),&s->entries[transfer.index]);}}
    int source_token=transfer.source,dest_token=transfer.destination;cleanup(false);
    if(rc<0)return rc;
    int source_refresh=dnd_cmgr_refresh(source_token),destination_refresh=dnd_cmgr_refresh(dest_token);return source_refresh<0||destination_refresh<0?COMMITTED_REFRESH_FAILED:100;
}
int dnd_cmgr_copy_abort(int token){if(token!=transfer.token||!token)return STALE;return cleanup(true);}
int dnd_cmgr_close(int token){
    for(int i=0;i<2;i++)if(token>0&&sessions[i].token==token){if(transfer.token&&(transfer.source==token||transfer.destination==token))cleanup(true);CARD_Unmount(i);free(sessions[i].work);memset(&sessions[i],0,sizeof(sessions[i]));return 0;}
    return STALE;
}
void dnd_cmgr_shutdown(void){if(transfer.token)cleanup(true);for(int i=0;i<2;i++)if(sessions[i].token)dnd_cmgr_close(sessions[i].token);}
#else
bool dnd_cmgr_active(int slot){(void)slot;return false;}
int dnd_cmgr_open(int slot){(void)slot;return -2002;}
int dnd_cmgr_refresh(int t){(void)t;return -2002;}
int dnd_cmgr_stat(int t,int f){(void)t;(void)f;return -2002;}
int dnd_cmgr_entry(int t,int r,int i,DndArray *a){(void)t;(void)r;(void)i;(void)a;return -2002;}
int dnd_cmgr_icon(int t,int r,int i,int f,DndArray *a){(void)t;(void)r;(void)i;(void)f;(void)a;return -2002;}
int dnd_cmgr_delete(int t,int r,int i){(void)t;(void)r;(void)i;return -2002;}
int dnd_cmgr_format(int t){(void)t;return -2002;}
int dnd_cmgr_close(int t){(void)t;return -2002;}
int dnd_cmgr_copy_begin(int s,int r,int i,int d,int m){(void)s;(void)r;(void)i;(void)d;(void)m;return -2002;}
int dnd_cmgr_copy_step(int t){(void)t;return -2002;}
int dnd_cmgr_copy_abort(int t){(void)t;return -2002;}
void dnd_cmgr_shutdown(void){}
#endif
