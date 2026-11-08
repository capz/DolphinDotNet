#define _POSIX_C_SOURCE 200809L
#include "host.h"
#include <errno.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static RawHost host;
static void interrupted(int signal_number) {(void)signal_number;host.cancelled=1;}
static int number(const char *s,uint64_t *out) {
    if(!s || *s<'0' || *s>'9')return -EINVAL;
    errno=0;char *end=NULL;unsigned long long x=strtoull(s,&end,10);
    if(errno || !end || *end)return -EINVAL;
    *out=(uint64_t)x;return 0;
}
static int component(const char *s,size_t bytes,uint16_t out[256]) {
    size_t at=0,n=0;memset(out,0,512);
    while(at<bytes) {
        uint32_t cp;unsigned size;unsigned char ch=(unsigned char)s[at];
        if(ch<128) {cp=ch;size=1;}
        else if(ch>=0xc2 && ch<=0xdf) {cp=ch&31;size=2;}
        else if(ch>=0xe0 && ch<=0xef) {cp=ch&15;size=3;}
        else if(ch>=0xf0 && ch<=0xf4) {cp=ch&7;size=4;}
        else return -EINVAL;
        if(size>bytes-at)return -EINVAL;
        for(unsigned i=1;i<size;i++) {
            unsigned char b=(unsigned char)s[at+i];if((b&0xc0)!=0x80)return -EINVAL;cp=(cp<<6)|(b&63);
        }
        if((size==2 && cp<128) || (size==3 && cp<2048) || (size==4 && cp<65536) ||
           cp>0x10ffff || (cp>=0xd800 && cp<=0xdfff) || cp<32 || cp==':' || cp=='\\')return -EINVAL;
        if(cp>65535) {
            if(n>252)return -ENAMETOOLONG;
            cp-=65536;
            out[n++]=(uint16_t)(0xd800+(cp>>10));out[n++]=(uint16_t)(0xdc00+(cp&1023));
        } else {if(n>=255)return -ENAMETOOLONG;out[n++]=(uint16_t)cp;}
        at+=size;
    }
    return !n || (n==1 && out[0]=='.') || (n==2 && out[0]=='.' && out[1]=='.')?-EINVAL:0;
}
static int name_equal(const uint16_t a[256],const uint16_t b[256]) {
    for(unsigned i=0;i<256;i++) {
        uint16_t x=a[i],y=b[i];if(x>='A' && x<='Z')x=(uint16_t)(x+32);
        if(y>='A' && y<='Z')y=(uint16_t)(y+32);
        if(x!=y)return 0;
        if(!x)return 1;
    }return 0;
}
static int lookup(DndDefragSnapshot *s,const char *path,uint32_t *out) {
    if(*path=='/')path++;
    uint32_t parent=0;
    while(*path) {
        const char *end=strchr(path,'/');size_t bytes=end?(size_t)(end-path):strlen(path);
        uint16_t name[256];int rc=component(path,bytes,name);if(rc)return rc;
        uint32_t match=UINT32_MAX;
        for(size_t i=1;i<s->file_count;i++)if(s->files[i].parent==parent && name_equal(s->files[i].name,name)) {
            if(match!=UINT32_MAX)return -EILSEQ;
            match=(uint32_t)i;
        }
        if(match==UINT32_MAX)return -ENOENT;
        if(!end) {*out=match;return 0;}
        if(!(s->files[match].flags&DND_DEFRAG_DIRECTORY) || !end[1])return -ENOTDIR;
        parent=match;path=end+1;
    }return -EINVAL;
}
static void print_hex(const unsigned char hash[32]) {for(unsigned i=0;i<32;i++)printf("%02x",hash[i]);}
static int finish(int rc,DndRawResult *result) {
    printf("{\"error\":%d,\"state\":%u,\"rolledBack\":%d,\"afterExtents\":%u,"
           "\"dataRead\":%" PRIu64 ",\"dataWritten\":%" PRIu64 ",\"metadataSectorsWritten\":%" PRIu64 ",\"events\":%" PRIu64 "}\n",
        rc,result->state,result->rolled_back,result->after_extents,result->data_bytes_read,
        result->data_bytes_written,result->metadata_sectors_written,host.events);
    if(rc)fprintf(stderr,"P6 image operation stopped: %s (%d). Keep the image offline and retain both journals and backup.\n",strerror(-rc),rc);
    raw_host_close(&host);return rc?1:0;
}
int main(int argc,char **argv) {
    if(argc<2)goto usage;
    int planning=!strcmp(argv[1],"plan"),executing=!strcmp(argv[1],"execute");
    int recovering=!strcmp(argv[1],"recover"),inspecting=!strcmp(argv[1],"inspect");
    if((planning && argc!=8) || (executing && argc!=11 && argc!=13) ||
       ((recovering||inspecting) && argc!=6) || (!planning&&!executing&&!recovering&&!inspecting))goto usage;
    DndRawResult result={0};int rc;
    if(planning)rc=raw_host_open(&host,argv[2],NULL,NULL,NULL,0,1);
    else rc=raw_host_open(&host,argv[2],argv[3],argv[4],argv[5],executing,inspecting);
    if(rc)return finish(rc,&result);
    struct sigaction action={0};action.sa_handler=interrupted;sigemptyset(&action.sa_mask);
    sigaction(SIGINT,&action,NULL);sigaction(SIGTERM,&action,NULL);
    DndRawOps ops=raw_host_ops(&host);
    DndDefragFile *files=calloc(256,sizeof(*files));DndDefragRun *runs=calloc(8192,sizeof(*runs));
    if(!files || !runs) {free(files);free(runs);return finish(-ENOMEM,&result);}
    DndDefragSnapshot workspace={.files=files,.runs=runs,.file_capacity=256,.run_capacity=8192};
    _Alignas(32) unsigned char transfer[DND_COPY_BUFFER];DndRawMove move={0};
    if(planning || executing) {
        unsigned arg=planning?3:6;uint64_t partition=0,first=0,count=0,destination=0;
        if(number(argv[arg],&partition) || number(argv[arg+2],&first) ||
           number(argv[arg+3],&count) || number(argv[arg+4],&destination) || destination>UINT32_MAX)rc=-EINVAL;
        else {
            DndSectorReader reader={ops.context,ops.read_sector,partition};
            DndDefragDevice device={{(uint64_t)host.image_stat.st_dev,(uint64_t)host.image_stat.st_ino},host.generation,1};
            DndDefragScanOps policy={0};rc=dnd_defrag_scan(&reader,device,policy,&workspace);
            uint32_t file=0;if(!rc)rc=lookup(&workspace,argv[arg+1],&file);
            if(!rc)rc=dnd_raw_move_plan(ops,&reader,device,(uint64_t)host.image_stat.st_size/512,
                &workspace,file,first,count,(uint32_t)destination,&move,transfer,sizeof(transfer));
        }
        if(!rc && planning) {
            printf("{\"fileBytes\":%" PRIu64 ",\"logicalFirst\":%" PRIu64 ",\"clusters\":%" PRIu64
                   ",\"destination\":%u,\"fileSha256\":\"",move.file_bytes,move.logical_first,move.cluster_count,move.destination);
            print_hex(move.file_sha256);printf("\",\"physicalDeviceExecution\":false}\n");
            free(files);free(runs);raw_host_close(&host);return 0;
        }
        if(!rc) {
            move.operation[0]=(uint64_t)host.image_stat.st_ino;
            move.operation[1]=(uint64_t)host.image_stat.st_ctime^(uint64_t)host.image_stat.st_size^0x503657494e444f57ULL;
            memcpy(move.backup_sha256,host.backup_sha256,32);
            rc=raw_host_bind_request(&host,&move);
            if(argc==13) {
                if(strcmp(argv[11],"--fail-after") || number(argv[12],&host.fail_after))rc=-EINVAL;
            }
            if(!rc)rc=dnd_raw_move_execute(ops,&move,&workspace,transfer,sizeof(transfer),&result);
        }
    } else {
        rc=raw_host_load_request(&host,&move);
        if(!rc && inspecting) {
            DndRawCheckpoint checkpoint;rc=dnd_raw_move_inspect(ops,&move,&checkpoint);
            if(rc>=0) {
                printf("{\"status\":%d,\"state\":%u,\"sequence\":%" PRIu64 ",\"mountAllowed\":%d}\n",
                       rc,checkpoint.state,checkpoint.sequence,dnd_raw_move_mount_allowed(ops,&move));
                free(files);free(runs);raw_host_close(&host);return 0;
            }
        } else if(!rc)rc=dnd_raw_move_recover(ops,&move,&workspace,transfer,sizeof(transfer),&result);
    }
    free(files);free(runs);return finish(rc,&result);
usage:
    fprintf(stderr,"P6 IMAGE ONLY; never pass a physical device.\n"
        "plan IMAGE PARTITION FILE FIRST COUNT DESTINATION\n"
        "execute WORKING_IMAGE VERIFIED_BACKUP NEW_JOURNAL_A NEW_JOURNAL_B PARTITION FILE FIRST COUNT DESTINATION [--fail-after N]\n"
        "recover|inspect WORKING_IMAGE VERIFIED_BACKUP JOURNAL_A JOURNAL_B\n");
    return 2;
}
