#pragma once
#include <sys/types.h>
#include <sys/stat.h>
struct _reent { int _errno; };
typedef struct { void *dirStruct; } DIR_ITER;
typedef struct {
 const char *name;
 int structSize;
 int (*open_r)(struct _reent *,void *,const char *,int,int);
 int (*close_r)(struct _reent *,void *);
 ssize_t (*read_r)(struct _reent *,void *,char *,size_t);
 off_t (*seek_r)(struct _reent *,void *,off_t,int);
 int (*fstat_r)(struct _reent *,void *,struct stat *);
 int (*stat_r)(struct _reent *,const char *,struct stat *);
 int dirStateSize;
 DIR_ITER *(*diropen_r)(struct _reent *,DIR_ITER *,const char *);
 int (*dirreset_r)(struct _reent *,DIR_ITER *);
 int (*dirnext_r)(struct _reent *,DIR_ITER *,char *,struct stat *);
 int (*dirclose_r)(struct _reent *,DIR_ITER *);
} devoptab_t;
int AddDevice(const devoptab_t *);
int RemoveDevice(const char *);
