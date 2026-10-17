#pragma once
#include <gccore.h>
typedef struct { int unused; } dvdcmdblk;
extern int __io_gcdvd;
void DVD_Init(void);
int DVD_Mount(void);
s32 DVD_ReadAbs(dvdcmdblk *,void *,s32,s64);
