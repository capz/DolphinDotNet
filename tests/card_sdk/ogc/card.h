#ifndef CMGR_TEST_CARD_H
#define CMGR_TEST_CARD_H
#include <gccore.h>
#define CARD_WORKAREA (5*8192)
#define CARD_ERROR_NOFILE -4
#define CARD_ERROR_BROKEN -6
#define CARD_ERROR_ENCODING -13
#define CARD_ERROR_EXIST -7
#define CARD_ERROR_INSSPACE -9
typedef void (*cardcallback)(s32,s32);
typedef struct {s32 chn; s32 filenum; u32 offset;s32 len;u32 iblock;} card_file;
typedef struct {s32 chn;u32 fileno,filelen;u8 permissions;char filename[32];u8 gamecode[4],company[2];bool showall;} card_dir;
typedef struct {char filename[32];u32 len,time;u8 gamecode[4],company[2],banner_fmt;u32 icon_addr;u16 icon_fmt,iconfmt[8],icon_speed,iconspeed[8];u32 comment_addr,offset_banner,offset_banner_tlut,offset_icon[8],offset_icon_tlut[8],offset_data;} card_stat;
s32 CARD_Init(const char*,const char*);s32 CARD_Mount(s32,void*,cardcallback);s32 CARD_Unmount(s32);
s32 CARD_GetDirectory(s32,card_dir*,s32*,bool);s32 CARD_GetBlockCount(s32,u16*);s32 CARD_GetSectorSize(s32,u32*);
s32 CARD_GetStatus(s32,s32,card_stat*);s32 CARD_SetStatus(s32,s32,card_stat*);s32 CARD_GetAttributes(s32,s32,u8*);s32 CARD_SetAttributes(s32,s32,u8);
s32 CARD_OpenEntry(s32,card_dir*,card_file*);s32 CARD_Close(card_file*);s32 CARD_Read(card_file*,void*,u32,u32);s32 CARD_Write(card_file*,void*,u32,u32);
s32 CARD_DeleteEntry(s32,card_dir*);s32 CARD_Format(s32);s32 CARD_SetGamecode(const char*);s32 CARD_SetCompany(const char*);s32 CARD_Create(s32,const char*,u32,card_file*);
#endif
