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
typedef struct {int cancel;unsigned reports;uint64_t durable;} Progress;
static int cancel_copy(void *ctx) {return ((Progress *)ctx)->cancel;}
static void copy_progress(void *ctx,const DndCopyResult *p) {
    Progress *progress=ctx;progress->reports++;
    assert(p->durable_bytes>=progress->durable);
    progress->durable=p->durable_bytes;
}
static void cancel_after_sync(void *ctx,const DndCopyResult *p) {
    copy_progress(ctx,p);((Progress *)ctx)->cancel=1;
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
    /* A checksum-correct but forged preparation journal must not remove
     * any file other than the deterministic internal staging path. */
    typedef struct {
        char magic[8];
        uint64_t bytes,digest;
        char original[1024],stage[1024],backup[1024];
        unsigned char sha256[32];
        uint32_t integrity;
    } TestJournal;
    fixture.fail_at=DND_OPT_AFTER_PREPARE;
    assert(dnd_optimize_file(root,path,&ops)==-EINTR);
    TestJournal forged={0};
    int journal_fd=open(prepare_path,O_RDONLY);
    assert(journal_fd>=0 && read(journal_fd,&forged,sizeof(forged))==
           (ssize_t)sizeof(forged) && close(journal_fd)==0);
    char keep[1024];
    snprintf(keep,sizeof(keep),"%s/keep.bin",root);
    int keep_fd=open(keep,O_WRONLY|O_CREAT|O_EXCL,0600);
    assert(keep_fd>=0 && write(keep_fd,"keep",4)==4 && close(keep_fd)==0);
    strcpy(forged.stage,keep);
    const unsigned char *encoded=(const unsigned char *)&forged;
    uint32_t hash=2166136261u;
    for(size_t i=0;i<offsetof(TestJournal,integrity);i++)
        hash=(hash^encoded[i])*16777619u;
    forged.integrity=hash;
    journal_fd=open(prepare_path,O_WRONLY|O_TRUNC);
    assert(journal_fd>=0 && write(journal_fd,&forged,sizeof(forged))==
           (ssize_t)sizeof(forged) && close(journal_fd)==0);
    assert(dnd_optimize_recover(root)==-EINVAL);
    assert(directory_contains(root,"keep.bin"));
    expected_file(path,135169);
    assert(unlink(prepare_path)==0);
    assert(unlink(keep)==0);
    fixture.fail_at=0;
    Progress progress={0};DndCopyResult metrics;
    DndOptimizeOptions options={&progress,cancel_copy,copy_progress};
    assert(dnd_optimize_file_ex(root,path,&ops,options,&metrics)==DND_OPT_REPAIRED);
    /* Source stream + stage readback + source revalidation + published
     * recovery verification: exact actual I/O, no hidden redundant stage pass. */
    assert(metrics.bytes_written==135169 && metrics.bytes_read==4*135169);
    assert(metrics.durable_bytes==135169 && metrics.verified_bytes==135169);
    assert(progress.reports>0);
    options.progress=cancel_after_sync;progress=(Progress){0};
    assert(dnd_optimize_file_ex(root,path,&ops,options,&metrics)==-ECANCELED);
    expected_file(path,135169);
    assert(dnd_optimize_recover(root)==0 && !directory_contains(root,".__dnd_opt.prepare"));
    /* Even on a 64-bit host, reject inputs the PowerPC off_t cannot represent. */
    char huge[1024];snprintf(huge,sizeof(huge),"%s/huge.iso",root);
    int huge_fd=open(huge,O_WRONLY|O_CREAT|O_EXCL,0600);
    assert(huge_fd>=0 && ftruncate(huge_fd,(off_t)2147483648ULL)==0 && close(huge_fd)==0);
    assert(dnd_optimize_file(root,huge,&ops)==-EFBIG);
    assert(!directory_contains(root,".__dnd_opt.prepare") && unlink(huge)==0);
    /* An SDK upgrade can still recover an intact version-one transaction. */
    fixture.fail_at=DND_OPT_BEFORE_BACKUP;
    assert(dnd_optimize_file(root,path,&ops)==-EINTR);
    typedef struct {
        char magic[8];uint64_t bytes,digest;
        char original[1024],stage[1024],backup[1024];uint32_t integrity;
    } LegacyJournal;
    const char *legacy_names[]={".__dnd_opt.prepare",".__dnd_opt.jrn"};
    for(unsigned i=0;i<2;i++) {
        char record_path[1024];snprintf(record_path,sizeof(record_path),"%s/%s",root,legacy_names[i]);
        TestJournal current;int fd=open(record_path,O_RDONLY);
        assert(fd>=0 && read(fd,&current,sizeof(current))==(ssize_t)sizeof(current));
        assert(close(fd)==0);
        LegacyJournal legacy={0};memcpy(&legacy,&current,offsetof(LegacyJournal,integrity));
        memcpy(legacy.magic,"DNDFRG01",8);
        uint32_t checksum=2166136261u;const unsigned char *bytes=(const unsigned char *)&legacy;
        for(size_t j=0;j<offsetof(LegacyJournal,integrity);j++)checksum=(checksum^bytes[j])*16777619u;
        legacy.integrity=checksum;fd=open(record_path,O_WRONLY|O_TRUNC);
        assert(fd>=0 && write(fd,&legacy,sizeof(legacy))==(ssize_t)sizeof(legacy) && close(fd)==0);
    }
    assert(dnd_optimize_recover(root)==0 && dnd_optimize_recover(root)==0);
    expected_file(path,135169);fixture.fail_at=0;
    /* A torn COMMIT journal fails closed. Restoring the intact record
     * makes repeated recovery possible without data loss. */
    fixture.fail_at=DND_OPT_BEFORE_BACKUP;
    assert(dnd_optimize_file(root,path,&ops)==-EINTR);
    char commit_path[1024];
    snprintf(commit_path,sizeof(commit_path),"%s/.__dnd_opt.jrn",root);
    TestJournal complete={0};
    journal_fd=open(commit_path,O_RDONLY);
    assert(journal_fd>=0 && read(journal_fd,&complete,sizeof(complete))==
           (ssize_t)sizeof(complete) && close(journal_fd)==0);
    journal_fd=open(commit_path,O_WRONLY|O_TRUNC);
    assert(journal_fd>=0 && write(journal_fd,"torn",4)==4 &&
           close(journal_fd)==0);
    assert(dnd_optimize_recover(root)<0);
    expected_file(path,135169);
    journal_fd=open(commit_path,O_WRONLY|O_TRUNC);
    assert(journal_fd>=0 && write(journal_fd,&complete,sizeof(complete))==
           (ssize_t)sizeof(complete) && close(journal_fd)==0);
    assert(dnd_optimize_recover(root)==0);
    assert(dnd_optimize_recover(root)==0);
    fixture.fail_at=0;
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
