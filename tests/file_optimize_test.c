#define _POSIX_C_SOURCE 200809L
#include "dnd_file_optimize.h"
#include <assert.h>
#include <errno.h>
#include <fcntl.h>
#include <stddef.h>
#include <sys/time.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

typedef struct {
    int fragmented_source;
    int fragmented_stage;
    int fail_at;
    unsigned probes;
} Fixture;
static int extents(void *context, const char *path, unsigned *count) {
    Fixture *f = context;
    f->probes++;
    if (strstr(path,".tmp"))
        *count = f->fragmented_stage ? 3u : 1u;
    else *count = f->fragmented_source ? 3u : 1u;
    return 0;
}
static int checkpoint(void *context, int point) {
    Fixture *f = context;
    return point == f->fail_at ? -EINTR : 0;
}
static void expected_file(const char *path, unsigned size) {
    int fd = open(path,O_RDONLY);
    assert(fd >= 0);
    for (unsigned i = 0; i < size; i++) {
        unsigned char b;
        assert(read(fd,&b,1) == 1);
        assert(b == (unsigned char)(i * 17u + 3u));
    }
    unsigned char end;
    assert(read(fd,&end,1) == 0);
    assert(close(fd) == 0);
}
static void make_file(const char *path, unsigned size) {
    int fd = open(path,O_WRONLY|O_CREAT|O_TRUNC,0600);
    assert(fd >= 0);
    for (unsigned i = 0; i < size; ++i) {
        unsigned char b = (unsigned char)(i * 17u + 3u);
        assert(write(fd,&b,1) == 1);
    }
    assert(close(fd) == 0);
}
static int directory_contains(const char *root, const char *suffix) {
    char name[1024];
    snprintf(name,sizeof(name),"%s/%s",root,suffix);
    struct stat st;
    return stat(name,&st) == 0;
}
int main(void) {
    char root[]="/tmp/dnd-defrag-XXXXXX", path[1024];
    assert(mkdtemp(root));
    snprintf(path,sizeof(path),"%s/application.bin",root);
    make_file(path,135169);
    /* Read-only flag and original modification date survive a replacement. */
    const time_t old_mtime=(time_t)946684800;
    struct timeval old_times[2]={{old_mtime,0},{old_mtime,0}};
    assert(utimes(path,old_times)==0);
    assert(chmod(path,0444)==0);
    Fixture fixture={0};
    DndOptimizeOps ops={&fixture,extents,checkpoint,NULL};
    assert(dnd_optimize_file(root,path,&ops) == DND_OPT_ALREADY_CONTIGUOUS);
    assert(fixture.probes == 1);
    expected_file(path,135169);
    fixture.fragmented_source = 1;
    assert(dnd_optimize_file(root,path,&ops) == DND_OPT_REPAIRED);
    expected_file(path,135169);
    struct stat preserved;
    assert(stat(path,&preserved)==0);
    assert((preserved.st_mode&0777)==0444);
    assert(preserved.st_mtime==old_mtime);
    assert(!directory_contains(root,".__dnd_opt.jrn"));
    fixture.fragmented_stage = 1;
    assert(dnd_optimize_file(root,path,&ops) == DND_OPT_NO_CONTIGUOUS_SPACE);
    expected_file(path,135169);
    fixture.fragmented_stage = 0;
    /* Recovery from an interruption before or during the staging copy. */
    const int prepare_points[]={DND_OPT_AFTER_PREPARE,DND_OPT_DURING_COPY};
    for(unsigned i=0;i<sizeof(prepare_points)/sizeof(prepare_points[0]);i++) {
        fixture.fail_at=prepare_points[i];
        assert(dnd_optimize_file(root,path,&ops)==-EINTR);
        assert(directory_contains(root,".__dnd_opt.prepare"));
        assert(!directory_contains(root,".__dnd_opt.jrn"));
        assert(dnd_optimize_recover(root)==0);
        assert(dnd_optimize_recover(root)==0); /* Idempotent on mount. */
        assert(!directory_contains(root,".__dnd_opt.prepare"));
        expected_file(path,135169);
    }
    /* A torn preparation journal must fail closed, without deleting data. */
    char prepare_path[1024];
    snprintf(prepare_path,sizeof(prepare_path),"%s/.__dnd_opt.prepare",root);
    int torn=open(prepare_path,O_WRONLY|O_CREAT|O_EXCL,0600);
    assert(torn>=0 && write(torn,"torn",4)==4 && close(torn)==0);
    assert(dnd_optimize_recover(root)<0);
    expected_file(path,135169);
    assert(unlink(prepare_path)==0);
    for (int point=1;point<=3;point++) {
        fixture.fail_at = point;
        assert(dnd_optimize_file(root,path,&ops) == -EINTR);
        assert(directory_contains(root,".__dnd_opt.jrn"));
        assert(dnd_optimize_recover(root) == 0);
        assert(!directory_contains(root,".__dnd_opt.jrn"));
        assert(!directory_contains(root,".__dnd_opt.prepare"));
        assert(dnd_optimize_recover(root)==0);
        expected_file(path,135169);
    }
    fixture.fail_at=0;
    assert(dnd_optimize_file(root,path,&ops) == DND_OPT_REPAIRED);
    expected_file(path,135169);
    /* A malformed journal is not trusted, and no file is deleted. */
    char journal[1024];
    snprintf(journal,sizeof(journal),"%s/.__dnd_opt.jrn",root);
    int fd=open(journal,O_WRONLY|O_CREAT|O_EXCL,0600);
    assert(fd>=0);
    assert(write(fd,"invalid",7)==7);
    assert(close(fd)==0);
    assert(dnd_optimize_recover(root) < 0);
    expected_file(path,135169);
    assert(unlink(journal)==0);
    assert(!directory_contains(root,".__dnd_opt.prepare"));
    assert(unlink(path)==0);
    assert(rmdir(root)==0);
    puts("selective file optimization / recovery tests passed");
    return 0;
}
