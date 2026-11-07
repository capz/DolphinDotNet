#include "dnd_defrag_planner.h"
#include <assert.h>
#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "defrag_fixture.h"

static DndDefragFile files[32],fresh_files[32];
static DndDefragRun runs[128],fresh_runs[128];
static DndDefragDevice device={{17,23},1,1};
static void entry(unsigned char *e,const char *name,unsigned first,unsigned size) {
    memcpy(e,name,11);wr16(e+26,(uint16_t)first);wr32(e+28,size);
}
static unsigned sum_set(unsigned char *e,unsigned count) {
    unsigned sum=0;
    for(unsigned n=0;n<count;n++)for(unsigned i=0;i<32;i++) {
        if(!n && (i==2 || i==3))continue;
        sum=(((sum&1)?0x8000:0)+(sum>>1)+e[n*32+i])&0xffff;
    }
    return sum;
}
static int pin(void *ctx,const DndDefragFile *file) {
    (void)ctx;return file->name[0]=='A';
}
static int virtual_iso(void *ctx,uint64_t lba,void *dest) {
    unsigned *reads=ctx;(*reads)++;
    assert((uintptr_t)dest%32==0);
    unsigned char *b=dest;memset(b,0,512);
    const unsigned count=100000,fat_sectors=(count+2+127)/128;
    const uint64_t iso=1459978240ULL,needed=(iso+32767)/32768;
    if(!lba) {
        wr16(b+11,512);b[13]=64;wr16(b+14,1);b[16]=1;
        wr32(b+32,1+fat_sectors+count*64);wr32(b+36,fat_sectors);
        wr32(b+44,2);memcpy(b+82,"FAT32   ",8);b[510]=0x55;b[511]=0xaa;
    } else if(lba<=fat_sectors) {
        for(unsigned i=0;i<128;i++) {
            uint64_t c=(lba-1)*128+i;
            uint32_t value=0;
            if(c<=2 || c==2+needed)value=0x0fffffff;
            else if(c<2+needed)value=(uint32_t)c+1;
            wr32(b+i*4,value);
        }
    } else if(lba==1+fat_sectors)entry(b,"LARGE   ISO",3,(unsigned)iso);
    else if(lba<1+fat_sectors+64) { /* Remaining root sectors are empty. */ }
    else return -EIO; /* Reading ISO contents is a test failure. */
    return 0;
}
int main(void) {
    Fixture f;
    DndSectorReader reader={&f,read_sector,0};
    DndDefragSnapshot m={.files=files,.runs=runs,.file_capacity=32,.run_capacity=128};
    DndDefragSnapshot fresh={.files=fresh_files,.runs=fresh_runs,
                            .file_capacity=32,.run_capacity=128};
    DndDefragScanOps ops={0};
    fat32(&f,0);
    entry(f.sectors[2],"GAME    ISO",4,1024);
    wr32(f.sectors[1]+4*4,6);
    entry(f.sectors[2]+32,"ASSET   BIN",8,512);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==0 && m.valid);
    assert(m.file_count==3 && m.files[1].bytes==1024);
    assert(m.files[1].name[0]=='G' && m.files[1].parent==0);
    unsigned source_runs=0;uint64_t unknown=0,free_count=0,end=2;
    for(size_t i=0;i<m.run_count;i++) {
        DndDefragRun r=m.runs[i];assert(r.start==end);end+=r.count;
        if(r.owner==1)source_runs++;
        if(r.owner==DND_DEFRAG_UNKNOWN)unknown+=r.count;
        if(r.owner==DND_DEFRAG_FREE)free_count+=r.count;
    }
    assert(source_runs==2 && unknown==45 && free_count==13 && end==64);
    assert(dnd_defrag_scan(&reader,device,ops,&fresh)==0);
    assert(dnd_defrag_snapshot_matches(&m,&fresh)==0);
    device.generation++;
    assert(dnd_defrag_scan(&reader,device,ops,&fresh)==0);
    assert(dnd_defrag_snapshot_matches(&m,&fresh)==-ESTALE);
    device.generation--;
    ops.pin_file=pin;
    assert(dnd_defrag_scan(&reader,device,ops,&fresh)==0);
    assert(fresh.files[2].flags&DND_DEFRAG_PINNED);
    assert(dnd_defrag_snapshot_matches(&m,&fresh)==-ESTALE);
    ops.pin_file=NULL;
    wr32(f.sectors[2]+28,1023); /* Metadata changes without changing geometry. */
    assert(dnd_defrag_scan(&reader,device,ops,&fresh)==0);
    assert(dnd_defrag_snapshot_matches(&m,&fresh)==-ESTALE);
    /* File references a free allocation: fail closed, no partial valid map. */
    wr16(f.sectors[2]+26,10);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO && !m.valid);
    fat32(&f,0);
    entry(f.sectors[2],"ONE     BIN",4,512);
    entry(f.sectors[2]+32,"TWO     BIN",4,512);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO); /* cross-link */
    wr16(f.sectors[2]+32+26,5);
    m.run_capacity=1;
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-ENOSPC && !m.valid);
    m.run_capacity=128;m.file_capacity=1;
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-ENOSPC && !m.valid);
    m.file_capacity=32;
    wr32(f.sectors[1]+4*4,4);wr32(f.sectors[2]+28,1024);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO); /* cycle */
    /* Nested directory aliases are skipped, directory storage is pinned. */
    fat32(&f,0);
    entry(f.sectors[2],"GAMES      ",4,0);f.sectors[2][11]=16;
    entry(f.sectors[4],".          ",4,0);f.sectors[4][11]=16;
    entry(f.sectors[4]+32,"..         ",2,0);f.sectors[4][43]=16;
    entry(f.sectors[4]+64,"SAVE    GCI",5,512);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==0 && m.file_count==3);
    assert(m.files[2].parent==1 && m.files[2].flags&DND_DEFRAG_PINNED);
    /* FAT long name in UTF-16, including a surrogate pair. */
    fat32(&f,0);
    unsigned char *lfn=f.sectors[2],*shortentry=lfn+32;
    entry(shortentry,"LONGNA~1ISO",4,512);
    unsigned sum=0;for(unsigned i=0;i<11;i++)sum=(((sum&1)?128:0)+(sum>>1)+shortentry[i])&255;
    lfn[0]=0x41;lfn[11]=15;lfn[13]=(unsigned char)sum;
    wr16(lfn+1,0x65e5);wr16(lfn+3,0xd83d);wr16(lfn+5,0xdc2c);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==0);
    assert(m.files[1].name[0]==0x65e5 && m.files[1].name[2]==0xdc2c);
    lfn[13]++;
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO);
    /* exFAT NoFatChain with Unicode name and validated entry-set checksum. */
    exfat(&f,0);
    unsigned char *e=f.sectors[2]+32;
    e[0]=0x85;e[1]=2;e[32]=0xc0;e[33]=3;e[35]=2;
    wr64(e+40,1024);wr32(e+52,4);wr64(e+56,1024);
    e[64]=0xc1;wr16(e+66,0x65e5);wr16(e+68,0x672c);
    wr16(e+2,(uint16_t)sum_set(e,3));
    assert(dnd_defrag_scan(&reader,device,ops,&m)==0 && m.file_count==2);
    assert(m.files[1].flags&DND_DEFRAG_NO_FAT_CHAIN);
    assert(m.files[1].name[0]==0x65e5 && m.files[1].bytes==1024);
    e[68]++;
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO); /* corrupt set */
    e[68]--;wr32(e+52,7);wr16(e+2,(uint16_t)sum_set(e,3));
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO); /* bitmap says free */
    reader.partition_lba=4;exfat(&f,4);
    assert(dnd_defrag_scan(&reader,device,ops,&m)==0 && m.partition_lba==4);
    f.read_error=1;
    assert(dnd_defrag_scan(&reader,device,ops,&m)==-EIO && !m.valid);
    unsigned reads=0;
    DndSectorReader virtual_reader={&reads,virtual_iso,0};
    assert(dnd_defrag_scan(&virtual_reader,device,ops,&m)==0);
    assert(m.file_count==2 && m.files[1].bytes==1459978240ULL);
    assert(m.run_count==3 && reads<1500); /* Metadata only, fixed workspace. */
    puts("bounded FAT32/exFAT ownership, Unicode, cross-links, identity and stale snapshots passed");
    return 0;
}
