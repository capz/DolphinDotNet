#ifndef DND_RAW_HOST_H
#define DND_RAW_HOST_H
#define _POSIX_C_SOURCE 200809L
#define _FILE_OFFSET_BITS 64
#include "dnd_raw_move.h"
#include <sys/stat.h>
#include <signal.h>
#define RAW_HOST_PATH 4096

typedef struct {
    int image,backup,journal[2],build,index;
    char image_path[RAW_HOST_PATH],backup_path[RAW_HOST_PATH],journal_path[2][RAW_HOST_PATH];
    struct stat image_stat,backup_stat,journal_stat[2];
    unsigned char backup_sha256[32];
    unsigned char request_sha256[32];
    uint64_t count,events,fail_after,generation;
    volatile sig_atomic_t cancelled;
} RawHost;
int raw_host_open(RawHost *,const char *image,const char *backup,const char *a,const char *b,int create,int readonly);
void raw_host_close(RawHost *);
DndRawOps raw_host_ops(RawHost *);
int raw_host_load_request(RawHost *,DndRawMove *);
int raw_host_bind_request(RawHost *,const DndRawMove *);
int raw_host_hash(int fd,unsigned char digest[32]);
#endif
