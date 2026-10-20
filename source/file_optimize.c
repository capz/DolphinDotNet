#define _POSIX_C_SOURCE 200809L
#include "dnd_file_optimize.h"
#include <errno.h>
#include <fcntl.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>
#include <sys/time.h>

#define DND_OPT_PATH 1024
#define DND_OPT_BUFFER 16384
#define DND_OPT_MAGIC "DNDFRG01"
#define DND_OPT_PREPARE ".__dnd_opt.prepare"
#define DND_OPT_COMMIT ".__dnd_opt.jrn"
typedef struct {
    char magic[8];
    uint64_t bytes;
    uint64_t digest;
    char original[DND_OPT_PATH];
    char stage[DND_OPT_PATH];
    char backup[DND_OPT_PATH];
    uint32_t integrity;
} DndOptimizeJournal;

static uint64_t digest_update(uint64_t hash, const unsigned char *data, size_t len) {
    for (size_t i = 0; i < len; ++i)
        hash = (hash ^ data[i]) * UINT64_C(1099511628211);
    return hash;
}
static uint32_t integrity(const void *data, size_t len) {
    const unsigned char *p = data;
    uint32_t hash = 2166136261u;
    for (size_t i = 0; i < len; ++i)
        hash = (hash ^ p[i]) * 16777619u;
    return hash;
}
static int compose(char *dst, const char *root, const char *suffix) {
    size_t r = strlen(root), s = strlen(suffix);
    if (!r || r + s + (root[r-1] == '/' ? 0u : 1u) >= DND_OPT_PATH)
        return -ENAMETOOLONG;
    memcpy(dst, root, r);
    size_t n = r;
    if (root[r-1] != '/') dst[n++] = '/';
    memcpy(dst+n, suffix, s+1);
    return 0;
}
static int path_is_below(const char *root, const char *path) {
    size_t n = strlen(root);
    if (!n || strncmp(root,path,n)) return 0;
    if (root[n-1] != '/') {
        if (path[n] != '/') return 0;
        n++;
    }
    if (!path[n] || strlen(path) >= DND_OPT_PATH) return 0;
    const char *p = path+n;
    while (*p) {
        const char *component = p;
        while (*p && *p != '/') {
            if (*p == '\\') return 0;
            p++;
        }
        size_t size = (size_t)(p-component);
        if (!size || (size==1 && component[0]=='.') ||
            (size==2 && component[0]=='.' && component[1]=='.')) return 0;
        if (*p && !*++p) return 0;
    }
    return 1;
}
static int exists(const char *path) {
    struct stat st;
    return stat(path, &st) == 0 ? 1 : errno == ENOENT ? 0 : -errno;
}
static int sync_close(int fd) {
    int rc = fsync(fd) < 0 ? -errno : 0;
    if (close(fd) < 0 && !rc) rc = -errno;
    return rc;
}
static int close_file(int fd) { return close(fd) < 0 ? -errno : 0; }
static int exact_write(int fd, const void *buffer, size_t size) {
    const unsigned char *p = buffer;
    while (size) {
        ssize_t n = write(fd, p, size);
        if (n < 0 && errno == EINTR) continue;
        if (n <= 0) return n < 0 ? -errno : -EIO;
        p += n;
        size -= (size_t)n;
    }
    return 0;
}
static int full_digest(const char *path, uint64_t *bytes, uint64_t *digest) {
    int fd = open(path, O_RDONLY);
    if (fd < 0) return -errno;
    _Alignas(32) unsigned char buffer[DND_OPT_BUFFER];
    uint64_t sum = UINT64_C(14695981039346656037), count = 0;
    int rc = 0;
    for (;;) {
        ssize_t n = read(fd, buffer, sizeof(buffer));
        if (n < 0 && errno == EINTR) continue;
        if (n < 0) { rc = -errno; break; }
        if (n == 0) break;
        sum = digest_update(sum, buffer, (size_t)n);
        count += (uint64_t)n;
    }
    int closed = close_file(fd);
    if (!rc) rc = closed;
    if (!rc) { *bytes = count; *digest = sum; }
    return rc;
}
static int matches(const char *path, const DndOptimizeJournal *j) {
    uint64_t bytes, digest;
    int rc = full_digest(path, &bytes, &digest);
    return rc < 0 ? rc : (bytes == j->bytes && digest == j->digest);
}
static int journal_path(char *out, const char *root, const char *name) {
    return compose(out, root, name);
}
static int journal_load(const char *root, const char *name, DndOptimizeJournal *j) {
    char file[DND_OPT_PATH];
    int rc = journal_path(file, root, name);
    if (rc) return rc;
    int fd = open(file, O_RDONLY);
    if (fd < 0) return errno == ENOENT ? 0 : -errno;
    unsigned char *p = (unsigned char *)j;
    size_t done = 0;
    while (done < sizeof(*j)) {
        ssize_t n = read(fd, p+done, sizeof(*j)-done);
        if (n < 0 && errno == EINTR) continue;
        if (n <= 0) { rc = n < 0 ? -errno : -EIO; break; }
        done += (size_t)n;
    }
    if (!rc) {
        unsigned char extra;
        ssize_t n = read(fd, &extra, 1);
        if (n != 0) rc = n < 0 ? -errno : -EINVAL;
    }
    int closed = close_file(fd);
    if (!rc) rc = closed;
    if (rc) return rc;
    if (memcmp(j->magic, DND_OPT_MAGIC, 8) ||
        j->integrity != integrity(j, offsetof(DndOptimizeJournal, integrity)) ||
        !memchr(j->original,0,sizeof(j->original)) ||
        !memchr(j->stage,0,sizeof(j->stage)) ||
        !memchr(j->backup,0,sizeof(j->backup)) ||
        !path_is_below(root,j->original) ||
        !path_is_below(root,j->stage) ||
        !path_is_below(root,j->backup) ||
        !strcmp(j->original,j->stage) ||
        !strcmp(j->original,j->backup) ||
        !strcmp(j->stage,j->backup))
        return -EINVAL;
    /* Even a forged journal must not be able to delete an arbitrary SD file.
     * Only the exact deterministic staging paths are eligible for cleanup. */
    uint32_t id = integrity(j->original,strlen(j->original));
    char leaf[40], expected[DND_OPT_PATH];
    snprintf(leaf,sizeof(leaf),".__dndopt_%08x.tmp",id);
    if (compose(expected,root,leaf) || strcmp(expected,j->stage)) return -EINVAL;
    snprintf(leaf,sizeof(leaf),".__dndopt_%08x.bak",id);
    if (compose(expected,root,leaf) || strcmp(expected,j->backup)) return -EINVAL;
    return 1;
}
static int journal_save(const char *root, const char *name, DndOptimizeJournal *j) {
    char path[DND_OPT_PATH];
    int rc = journal_path(path, root, name);
    if (rc) return rc;
    memcpy(j->magic,DND_OPT_MAGIC,8);
    j->integrity = integrity(j,offsetof(DndOptimizeJournal,integrity));
    int fd = open(path,O_WRONLY|O_CREAT|O_EXCL,0600);
    if (fd < 0) return -errno;
    rc = exact_write(fd,j,sizeof(*j));
    if (!rc) rc = sync_close(fd);
    else close_file(fd);
    if (rc) unlink(path);
    return rc;
}
static int remove_journal(const char *root,const char *name) {
    char path[DND_OPT_PATH];
    int rc = journal_path(path,root,name);
    if (rc) return rc;
    return unlink(path) == 0 ? 0 : -errno;
}
/* Do not follow a symlink or operate on non-regular transaction files.
 * On FAT/exFAT lstat is equivalent to stat; the host tests also cover links. */
static int safe_file(const char *path) {
    struct stat st;
    if(lstat(path,&st)<0)return errno==ENOENT?0:-errno;
    return S_ISREG(st.st_mode)?1:-EINVAL;
}
static int safe_unlink(const char *path) {
    int rc=safe_file(path);
    if(rc<=0)return rc;
    return unlink(path)<0?-errno:0;
}
int dnd_optimize_recover(const char *root) {
    DndOptimizeJournal prepare={0},committed={0};
    int p=journal_load(root,DND_OPT_PREPARE,&prepare);
    if(p<0)return p;
    int q=journal_load(root,DND_OPT_COMMIT,&committed);
    if(q<0)return q;
    if(!p && !q)return 0;
    if(!p || (q && (strcmp(prepare.original,committed.original) ||
        strcmp(prepare.stage,committed.stage) ||
        strcmp(prepare.backup,committed.backup) ||
        prepare.bytes!=committed.bytes)))return -EINVAL;
    const DndOptimizeJournal *j=q?&committed:&prepare;
    int source=safe_file(j->original), backup=safe_file(j->backup);
    if(source<0)return source;
    if(backup<0)return backup;
    if(!q) {
        /* The preparation journal is durable BEFORE the temporary file can
         * exist. No source rename is allowed before COMMIT is durable. */
        if(!source || backup)return -EIO;
        int rc=safe_unlink(j->stage);
        if(rc<0)return rc;
        return remove_journal(root,DND_OPT_PREPARE);
    }
    if(backup) {
        if(!source) {
            if(rename(j->backup,j->original))return -errno;
        } else {
            int good=matches(j->original,j);
            if(good<0)return good;
            if(!good)return -EIO;
            int rc=safe_unlink(j->backup);
            if(rc<0)return rc;
        }
    } else {
        if(!source)return -EIO;
        int good=matches(j->original,j);
        if(good<0)return good;
        /* Before the first rename, the original need not match a copy that
         * was prepared but never published. The stage still exists then. */
        int stage=safe_file(j->stage);
        if(stage<0)return stage;
        if(!stage && !good)return -EIO;
    }
    int rc=safe_unlink(j->stage);
    if(rc<0)return rc;
    rc=remove_journal(root,DND_OPT_COMMIT);
    return rc<0?rc:remove_journal(root,DND_OPT_PREPARE);
}
static int copy_and_hash(const char *from, const char *to, uint64_t size,
                         uint64_t *hash,const DndOptimizeOps *ops) {
    int input = open(from,O_RDONLY);
    if (input < 0) return -errno;
    int output = open(to,O_WRONLY|O_CREAT|O_EXCL,0600);
    if (output < 0) { int rc=-errno; close_file(input); return rc; }
    int rc = 0;
    /* On libdvm this attempts f_expand before ordinary allocation. We still
     * verify extents; libdvm currently ignores the f_expand result. */
    if (ftruncate(output,(off_t)size) < 0) rc = -errno;
    if (!rc && lseek(output,0,SEEK_SET) < 0) rc = -errno;
    _Alignas(32) unsigned char buffer[DND_OPT_BUFFER];
    uint64_t written=0, digest=UINT64_C(14695981039346656037);
    while (!rc && written < size) {
        size_t want = size-written < sizeof(buffer) ? (size_t)(size-written) : sizeof(buffer);
        ssize_t got = read(input,buffer,want);
        if (got < 0 && errno == EINTR) continue;
        if (got <= 0) { rc = got < 0 ? -errno : -EIO; break; }
        rc = exact_write(output,buffer,(size_t)got);
        if (rc) break;
        digest = digest_update(digest,buffer,(size_t)got);
        written += (uint64_t)got;
        if(written==(uint64_t)got && ops->checkpoint) {
            rc=ops->checkpoint(ops->context,DND_OPT_DURING_COPY);
        }
    }
    if (!rc) rc = sync_close(output); else close_file(output);
    int closed = close_file(input);
    if (!rc) rc = closed;
    if (!rc) *hash = digest;
    return rc;
}
/* Preserve user-visible timestamps and POSIX permission bits on host.
 * GameCube FAT attributes require the dedicated libdvm metadata callback. */
static int preserve_metadata(const char *original,const char *stage,
                             const struct stat *metadata) {
    (void)original;
    struct timeval times[2]={{0,0},{0,0}};
    times[0].tv_sec=metadata->st_atime;
    times[1].tv_sec=metadata->st_mtime;
    if(utimes(stage,times)<0)return -errno;
    if(chmod(stage,metadata->st_mode&07777)<0)return -errno;
    return 0;
}
int dnd_optimize_file(const char *root, const char *path,
                      const DndOptimizeOps *ops) {
    if (!root || !path || !ops || !ops->count_extents ||
        !path_is_below(root,path))
        return -EINVAL;
    DndOptimizeJournal j = {0};
    int rc = dnd_optimize_recover(root);
    if (rc < 0) return rc;
    unsigned count = 0;
    rc = ops->count_extents(ops->context,path,&count);
    if (rc == -ENOTSUP) return DND_OPT_UNSUPPORTED;
    if (rc < 0) return rc;
    if (count <= 1) return DND_OPT_ALREADY_CONTIGUOUS;
    struct stat st;
    if (stat(path,&st)) return -errno;
    if (!S_ISREG(st.st_mode) || st.st_size <= 0) return -EINVAL;
    /* This runtime uses native 32-bit off_t on GameCube; fail rather than
     * truncating offsets or allocating only part of a large file. */
    if ((uint64_t)(off_t)st.st_size != (uint64_t)st.st_size) return -EFBIG;
    uint32_t id = integrity(path,strlen(path));
    char leaf[40];
    snprintf(leaf,sizeof(leaf),".__dndopt_%08x.tmp",id);
    rc = compose(j.stage,root,leaf);
    if (rc) return rc;
    snprintf(leaf,sizeof(leaf),".__dndopt_%08x.bak",id);
    rc = compose(j.backup,root,leaf);
    if (rc) return rc;
    if (strlen(path) >= sizeof(j.original)) return -ENAMETOOLONG;
    strcpy(j.original,path);
    j.bytes = (uint64_t)st.st_size;
    int occupied = exists(j.stage), backup = exists(j.backup);
    if (occupied < 0) return occupied;
    if (backup < 0) return backup;
    if (occupied || backup) return -EEXIST; /* Never delete unknown files. */
    /* Record ownership before touching staging, including empty/partial
     * copies. Recovery can safely remove an orphan after a power cut. */
    rc=journal_save(root,DND_OPT_PREPARE,&j);
    if(rc<0)return rc;
    if(ops->checkpoint) {
        rc=ops->checkpoint(ops->context,DND_OPT_AFTER_PREPARE);
        if(rc<0)return rc;
    }
    rc = copy_and_hash(path,j.stage,j.bytes,&j.digest,ops);
    if(rc<0) {
        if(rc==-EINTR && ops->checkpoint)return rc;
        int cleanup=dnd_optimize_recover(root);
        return cleanup<0?cleanup:rc;
    }
    uint64_t length=0, check=0;
    rc = full_digest(j.stage,&length,&check);
    if (rc < 0 || length != j.bytes || check != j.digest) {
        int cleanup=dnd_optimize_recover(root);
        if(cleanup<0)return cleanup;
        return rc < 0 ? rc : -EIO;
    }
    unsigned stage_extents=0;
    rc = ops->count_extents(ops->context,j.stage,&stage_extents);
    if (rc < 0 || stage_extents != 1) {
        int cleanup=dnd_optimize_recover(root);
        if(cleanup<0)return cleanup;
        return rc == -ENOTSUP ? DND_OPT_UNSUPPORTED
             : rc < 0 ? rc : DND_OPT_NO_CONTIGUOUS_SPACE;
    }
    /* Refuse to publish a stale staged copy if the source changed. */
    rc=ops->preserve_metadata?
       ops->preserve_metadata(ops->context,path,j.stage,&st):
       preserve_metadata(path,j.stage,&st);
    if(rc<0) {
        int cleanup=dnd_optimize_recover(root);
        return cleanup<0?cleanup:(rc==-ENOSYS || rc==-ENOTSUP?
                                   DND_OPT_UNSUPPORTED:rc);
    }
    rc=matches(path,&j);
    if(rc<0 || !rc) {
        int cleanup=dnd_optimize_recover(root);
        return cleanup<0?cleanup:(rc<0?rc:-EBUSY);
    }
    /* A second durable journal activates the rename transaction only after
     * the staged copy, metadata and hash have all been verified. */
    rc=journal_save(root,DND_OPT_COMMIT,&j);
    if(rc<0) {
        int cleanup=dnd_optimize_recover(root);
        return cleanup<0?cleanup:rc;
    }
    if (ops->checkpoint) {
        rc = ops->checkpoint(ops->context,DND_OPT_BEFORE_BACKUP);
        if (rc < 0) return rc; /* Recovery handles journal and stage. */
    }
    if (rename(j.original,j.backup)) return -errno;
    if (ops->checkpoint) {
        rc = ops->checkpoint(ops->context,DND_OPT_AFTER_BACKUP);
        if (rc < 0) return rc;
    }
    if (rename(j.stage,j.original)) return -errno;
    if (ops->checkpoint) {
        rc = ops->checkpoint(ops->context,DND_OPT_AFTER_PUBLISH);
        if (rc < 0) return rc;
    }
    rc = dnd_optimize_recover(root);
    return rc < 0 ? rc : DND_OPT_REPAIRED;
}
