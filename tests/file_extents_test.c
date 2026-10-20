#include "dnd_file_extents.h"
#include <assert.h>
#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct { unsigned char sectors[128][512]; } Fixture;
static void put16(unsigned char *p,uint16_t x) {p[0]=(unsigned char)x;p[1]=(unsigned char)(x>>8);}
static void put32(unsigned char *p,uint32_t x) {put16(p,(uint16_t)x);put16(p+2,(uint16_t)(x>>16));}
static void put64(unsigned char *p,uint64_t x) {put32(p,(uint32_t)x);put32(p+4,(uint32_t)(x>>32));}
static int sector(void *ctx,uint64_t lba,void *buffer) {
  Fixture *f=ctx;
  if(lba>=128)return -EIO;
  memcpy(buffer,f->sectors[lba],512);
  return 0;
}
static void fat32(Fixture *f) {
  memset(f,0,sizeof(*f));
  unsigned char *boot=f->sectors[0],*fat=f->sectors[1],*root=f->sectors[2];
  put16(boot+11,512);boot[13]=1;put16(boot+14,1);boot[16]=1;
  put32(boot+32,64);put32(boot+36,1);put32(boot+44,2);
  memcpy(boot+82,"FAT32   ",8);boot[510]=0x55;boot[511]=0xaa;
  put32(fat+8,0x0fffffff); /* root cluster 2 */
  put32(fat+12,4);put32(fat+16,6);put32(fat+24,0x0fffffff);
  memcpy(root,"APP     DOL",11);root[11]=0x20;
  put16(root+26,3);put32(root+28,1536);
}
static void exfat(Fixture *f) {
  memset(f,0,sizeof(*f));
  unsigned char *boot=f->sectors[0],*fat=f->sectors[1],*root=f->sectors[2];
  memcpy(boot+3,"EXFAT   ",8);
  put32(boot+80,1);put32(boot+84,1);
  put32(boot+88,2);put32(boot+92,60);put32(boot+96,2);
  boot[108]=9;boot[109]=0;boot[110]=1;
  boot[510]=0x55;boot[511]=0xaa;
  put32(fat+8,0xffffffff);
  put32(fat+12,4);put32(fat+16,6);put32(fat+24,0xffffffff);
  root[0]=0x85;root[1]=2;put16(root+4,0x20);
  unsigned char *stream=root+32,*name=root+64;
  stream[0]=0xc0;stream[3]=7;put32(stream+20,3);put64(stream+24,1536);
  name[0]=0xc1;
  const char *text="app.dol";
  for(unsigned i=0;i<7;i++)put16(name+2+i*2,(unsigned char)text[i]);
}
int main(void) {
  Fixture fixture;
  DndSectorReader reader={&fixture,sector,0};
  unsigned runs=0;
  fat32(&fixture);
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==2);
  put32(fixture.sectors[1]+16,0x0fffffff);
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==-EIO);
  put32(fixture.sectors[1]+16,5);
  put32(fixture.sectors[1]+20,0x0fffffff);
  assert(dnd_fat_count_extents(&reader,"/app.dol",&runs)==0 && runs==1);
  /* A two-entry FAT32 LFN sequence (20 ASCII chars) resolves to the
   * same 8.3 directory entry after validating the checksum. */
  fat32(&fixture);
  unsigned char *root=fixture.sectors[2];
  memmove(root+64,root,32);
  memset(root,0xff,64);
  root[0]=0x42;root[11]=0x0f;root[12]=0;
  root[32]=0x01;root[32+11]=0x0f;root[32+12]=0;
  unsigned checksum=0;
  for(unsigned i=0;i<11;i++)
    checksum=((checksum&1u)?0x80u:0u)+(checksum>>1)+root[64+i];
  root[13]=root[32+13]=(unsigned char)checksum;
  static const unsigned lfn_offsets[13]=
    {1,3,5,7,9,14,16,18,20,22,24,28,30};
  const char *longname="verylongfilename.dol";
  size_t name_length=strlen(longname);
  for(unsigned i=0;i<26;i++) {
    unsigned char *entry=i<13?root+32:root;
    unsigned idx=i%13;
    uint16_t ch=i<name_length?(unsigned char)longname[i]:
                i==name_length?0:0xffff;
    put16(entry+lfn_offsets[idx],ch);
  }
  assert(dnd_fat_count_extents(&reader,longname,&runs)==0 && runs==2);
  assert(dnd_fat_count_extents(&reader,"r\xc3\xa9sume.dol",&runs)==-ENOENT);
  /* FAT32 long filename: Japanese BMP code points are UTF-16 entries. */
  fat32(&fixture);
  root=fixture.sectors[2];
  memmove(root+32,root,32);
  memset(root,0xff,32);
  root[0]=0x41;root[11]=0x0f;root[12]=0;
  checksum=0;
  for(unsigned i=0;i<11;i++)
    checksum=((checksum&1u)?0x80u:0u)+(checksum>>1)+root[32+i];
  root[13]=(unsigned char)checksum;
  const uint16_t japanese[]={0x65e5,0x672c,0x8a9e,'.','d','o','l',0};
  for(unsigned i=0;i<13;i++)
    put16(root+lfn_offsets[i],i<8?japanese[i]:0xffff);
  assert(dnd_fat_count_extents(&reader,"\xe6\x97\xa5\xe6\x9c\xac\xe8\xaa\x9e.dol",&runs)==0 && runs==2);
  /* Surrogate pair in a FAT32 LFN: U+1F600. */
  const uint16_t emoji[]={0xd83d,0xde00,'.','d','o','l',0};
  for(unsigned i=0;i<13;i++)
    put16(root+lfn_offsets[i],i<7?emoji[i]:0xffff);
  assert(dnd_fat_count_extents(&reader,"\xf0\x9f\x98\x80.dol",&runs)==0 && runs==2);
  assert(dnd_fat_count_extents(&reader,"\xf0\x28\x8c\x28.dol",&runs)==-ENOENT);

  /* Non-default FAT32 cluster size: file fits in just one four-sector run. */
  fat32(&fixture);
  fixture.sectors[0][13]=4;
  put32(fixture.sectors[1]+12,0x0fffffff);
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==1);
  exfat(&fixture);
  /* An exFAT name can contain Japanese characters and surrogate pairs. */
  unsigned char *stream=fixture.sectors[2]+32,*name=fixture.sectors[2]+64;
  stream[3]=7;
  for(unsigned i=0;i<7;i++)put16(name+2+2*i,japanese[i]);
  assert(dnd_fat_count_extents(&reader,"\xe6\x97\xa5\xe6\x9c\xac\xe8\xaa\x9e.dol",&runs)==0 && runs==2);
  stream[3]=6;
  for(unsigned i=0;i<6;i++)put16(name+2+2*i,emoji[i]);
  assert(dnd_fat_count_extents(&reader,"\xf0\x9f\x98\x80.dol",&runs)==0 && runs==2);
  exfat(&fixture);
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==2);
  fixture.sectors[2][33]=2; /* Stream.GeneralSecondaryFlags.NoFatChain */
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==1);
  /* exFAT with 1024-byte clusters: 1536 bytes occupy two contiguous runs. */
  exfat(&fixture);
  fixture.sectors[0][109]=1;
  put32(fixture.sectors[1]+12,4);
  put32(fixture.sectors[1]+16,0xffffffff); /* second cluster ends chain */
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==1);
  put32(fixture.sectors[1]+12,6);
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==2);
  /* A near-full, fragmented FAT can still be inspected read-only. */
  fat32(&fixture);
  for(unsigned i=7;i<64;i++)put32(fixture.sectors[1]+i*4,0x0fffffff);
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==0 && runs==2);
  exfat(&fixture);
    fixture.sectors[0][108]=12;
  assert(dnd_fat_count_extents(&reader,"app.dol",&runs)==-ENOTSUP);
  puts("FAT32/exFAT cluster extent inspection passed");
  return 0;
}
