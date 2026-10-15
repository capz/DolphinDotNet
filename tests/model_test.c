#define _POSIX_C_SOURCE 200809L
#include "dnd_model_internal.h"
#include "dnd_performance.h"
#include "dnd_storage.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static DndManagedHeap heap;
static int load(const char *path) {
  DndString *s = dnd_string_from_utf8(&heap, path);
  int m = dnd_model_begin(s, 8 * 1024 * 1024);
  assert(m);
  int status;
  while ((status = dnd_model_step(m)) == 0) {
  }
  assert(status == 1);
  return m;
}
static void near(float a, float b) { assert(fabsf(a - b) < 0.001f); }
static void put(unsigned char *p, unsigned value) {
  p[0] = (unsigned char)(value >> 24);
  p[1] = (unsigned char)(value >> 16);
  p[2] = (unsigned char)(value >> 8);
  p[3] = (unsigned char)value;
}
static void corrupt_cases(const char *root, const unsigned char *original,
                          unsigned length) {
  unsigned char *data = malloc(length);
  assert(data);
  char path[1024];
  snprintf(path, sizeof(path), "%s/sd/malformed.dndmodel", root);
  for (int test = 0; test < 6; test++) {
    memcpy(data, original, length);
    unsigned section = test == 0   ? 0
                       : test == 1 ? 1
                       : test == 2 ? 4
                       : test == 3 ? 5
                       : test == 4 ? 3
                                   : 1;
    unsigned char *dir = data + 16 + section * 16;
    unsigned off = dnd_model_u32(dir + 4), size = dnd_model_u32(dir + 8);
    if (test == 0)
      put(data + off + 4, 0); /* self-parent */
    if (test == 1)
      put(data + off + 4 + 44 + 48, 256); /* invalid vertex joint */
    if (test == 2)
      put(data + off + 8, 1024); /* invalid skin node */
    if (test == 3)
      put(data + off + 84, 3); /* unknown track property */
    if (test == 4)
      put(data + off + 4, 3); /* unsupported texture dimension */
    if (test == 5)
      put(dir + 4, 160); /* overlapping directory ranges */
    else
      put(dir + 12, dnd_model_hash(data + off, size));
    FILE *f = fopen(path, "wb");
    assert(f);
    assert(fwrite(data, 1, length, f) == length);
    assert(!fclose(f));
    DndString *name = dnd_string_from_utf8(&heap, "sd:/malformed.dndmodel");
    int token = dnd_model_begin(name, 8 * 1024 * 1024);
    assert(token);
    int result;
    while ((result = dnd_model_step(token)) == 0) {
    }
    assert(result < 0);
    assert(!dnd_model_dispose(token));
    assert(!dnd_fs_open_handles());
  }
  remove(path);
  free(data);
}
static int fzero_try(const char *path, const char *textures, int selection,
                     int budget, int options) {
  DndString *source = dnd_string_from_utf8(&heap, path);
  DndString *tpl = textures ? dnd_string_from_utf8(&heap, textures) : NULL;
  return dnd_model_begin_fzero(source, tpl, selection, budget, options);
}
static int fzero_load(const char *path, const char *textures, int selection, int options) {
  int token = fzero_try(path, textures, selection, 8 * 1024 * 1024, options);
  if (!token) fprintf(stderr, "F-Zero load failed: %s, textures=%s, selection=%d options=%d\n", path, textures ? textures : "(archive)", selection, options);
  assert(token && !dnd_exception_pending()); return token;
}
static void fzero_tests(const char *root) {
  const char *sources[] = {"sd:/fz.gma", "sd:/fz.gcmf", "sd:/fz.gma.lz", "sd:/fz-backref.gma.lz", "sd:/fz-large.gma"};
  for (unsigned i = 0; i < sizeof(sources)/sizeof(*sources); i++) {
    int token = fzero_load(sources[i],"sd:/fz.tpl.lz",-1,0);
    DndModel *m = dnd_model_find(token,false);
    assert(m->nodes==1 && m->meshes==1 && m->textures==1 && m->clips==0);
    assert(m->mesh[0].vertices==4 && m->mesh[0].indices==6);
    DndTexture *t=&m->texture[0];
    assert(t->width==8 && t->height==8 && t->levels==2 && t->size==320 && t->wrap_s==1 && t->wrap_t==1);
    const unsigned char *pixels=t->data;assert(pixels[0]==255 && pixels[1]==30 && pixels[32]==40 && pixels[33]==50 && pixels[257]==40);
    int instance=dnd_model_instance(token,4*1024*1024);assert(instance);
    float vertex[6];assert(dnd_model_vertex(instance,0,2,vertex)==0);near(vertex[0],1);near(vertex[1],1);near(vertex[5],1);
    assert(dnd_model_instance_dispose(instance)==0 && dnd_model_dispose(token)==0);
  }
  int sampler=fzero_load("sd:/fz-sampler.gcmf","sd:/fz.tpl",-1,0);DndTexture *sample=&dnd_model_find(sampler,false)->texture[0];
  assert(sample->wrap_s==2 && sample->wrap_t==0 && sample->linear==0 && sample->levels==2);assert(dnd_model_dispose(sampler)==0);
  sampler=fzero_load("sd:/fz-no-mip.gcmf","sd:/fz.tpl",-1,0);assert(dnd_model_find(sampler,false)->texture[0].levels==1);assert(dnd_model_dispose(sampler)==0);
  sampler=fzero_load("sd:/fz-lz-header-collision.gma",NULL,0,0);assert(dnd_model_dispose(sampler)==0);
  sampler=fzero_load("sd:/fz-nested-3.gma.lz",NULL,0,0);assert(dnd_model_dispose(sampler)==0);
  assert(!fzero_try("sd:/fz-nested-4.gma.lz",NULL,0,8*1024*1024,0));
  const char *archives[]={"sd:/fz.arc","sd:/fz.arc.lz","sd:/fz-inner.arc","sd:/fz.arc#assets/vehicle.gma","sd:/fz-ambiguous.arc#assets/two.gma"};
  for(unsigned i=0;i<sizeof(archives)/sizeof(*archives);i++) {
    int token=fzero_load(archives[i],NULL,-1,0);assert(dnd_model_find(token,false)->textures==1);assert(dnd_model_dispose(token)==0);
  }
  int token=fzero_load("sd:/fz.arc#assets/vehicle.gma","sd:/fz.arc#assets/vehicle.tpl",-1,0);assert(dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz-ambiguous.arc",NULL,-1,8*1024*1024,0));
  assert(!fzero_try("sd:/fz.arc#assets/missing.gma",NULL,-1,8*1024*1024,0));
  token=fzero_load("sd:/fz-multiple.gma",NULL,-1,0);
  DndModel *m=dnd_model_find(token,false);assert(m->nodes==5 && m->meshes==7);
  assert(!strcmp(m->node[1].name,"Fixed") && !strcmp(m->node[3].name,"Matrix"));
  int instance=dnd_model_instance(token,4*1024*1024);assert(instance);
  float v[6];assert(dnd_model_vertex(instance,3,0,v)==0);near(v[0],3);
  assert(dnd_model_vertex(instance,3,1,v)==0);near(v[0],5);
  for(unsigned mesh=0;mesh<3;mesh++){assert(dnd_model_vertex(instance,(int)mesh,0,v)==0);near(v[5],1);}
  assert(dnd_model_instance_dispose(instance)==0 && dnd_model_dispose(token)==0);
  token=fzero_load("sd:/fz-multiple.gma",NULL,1,0);m=dnd_model_find(token,false);assert(m->nodes==1 && !strcmp(m->node[0].name,"Fixed"));assert(dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz-multiple.gma",NULL,5,8*1024*1024,0));
  const int primitives[]={0x80,0x88,0x90,0x98,0xa0};
  for(unsigned i=0;i<5;i++) {
    char path[128];snprintf(path,sizeof(path),"sd:/fz-primitive-%d.gma",primitives[i]);token=fzero_load(path,NULL,-1,0);
    const unsigned char *indices=dnd_model_find(token,false)->mesh[0].index;
    assert(dnd_model_u32(indices)==0 && dnd_model_u32(indices+4)==1 && dnd_model_u32(indices+8)==2);
    if(primitives[i]!=0x90)assert(dnd_model_u32(indices+12)==(primitives[i]==0x98?1u:0u));
    assert(dnd_model_dispose(token)==0);
  }
  const int formats[]={0,1,2,3,4,5,6,14};
  const unsigned char expected[8][4]={{136,136,136,136},{128,128,128,128},{255,255,255,255},{255,255,255,255},{255,0,0,255},{255,0,0,255},{30,40,50,255},{255,0,0,255}};
  for(unsigned i=0;i<8;i++) {
    char path[128];snprintf(path,sizeof(path),"sd:/fz-format-%d.tpl",formats[i]);token=fzero_load("sd:/fz.gma",path,-1,0);
    const unsigned char *pixels=dnd_model_find(token,false)->texture[0].data;
    assert(pixels[1]==expected[i][0] && pixels[32]==expected[i][1] && pixels[33]==expected[i][2] && pixels[0]==expected[i][3]);
    assert(dnd_model_dispose(token)==0);
  }
  token=fzero_load("sd:/fz.gma","sd:/fz-small.tpl",-1,0);assert(dnd_model_find(token,false)->texture[0].width==4);assert(dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz.gma","sd:/fz-cmpr-bug.tpl",-1,8*1024*1024,0));
  token=fzero_load("sd:/fz.gma","sd:/fz-cmpr-bug.tpl",-1,1);assert(dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz-layered.gma","sd:/fz-two.tpl",-1,8*1024*1024,0));
  token=fzero_load("sd:/fz-layered.gma","sd:/fz-two.tpl",-1,1);assert(dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz-indexed.gma",NULL,-1,8*1024*1024,0));
  token=fzero_load("sd:/fz-indexed.gma",NULL,-1,2);instance=dnd_model_instance(token,4*1024*1024);assert(instance);
  assert(dnd_model_vertex(instance,0,2,v)==0);near(v[0],1);near(v[1],1);
  assert(dnd_model_instance_dispose(instance)==0 && dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz-with-skin.gma",NULL,-1,8*1024*1024,0));
  token=fzero_load("sd:/fz-with-skin.gma",NULL,0,0);assert(dnd_model_dispose(token)==0);
  token=fzero_load("sd:/fz-with-skin.gma",NULL,-1,2);assert(dnd_model_find(token,false)->nodes==2);assert(dnd_model_dispose(token)==0);
  token=fzero_load("sd:/fz-custom-blend.gma",NULL,-1,0);
  assert(dnd_model_find(token,false)->material[0].mode==(2|4|16|(2<<5)|(3<<8)));assert(dnd_model_dispose(token)==0);
  token=fzero_load("sd:/fz.gma","sd:/fz.tpl",-1,0);void *resident_texture=dnd_model_find(token,false)->texture[0].data;
  assert(!fzero_try("sd:/fz-partial-failure.gma","sd:/fz.tpl",-1,8*1024*1024,0));
  assert(dnd_model_find(token,false)->texture[0].data==resident_texture && dnd_model_live_handles()==1 && !dnd_fs_open_handles());
  assert(dnd_model_dispose(token)==0);
  token=fzero_load("sd:/fz-multiple.gma",NULL,0,0);assert(dnd_model_stat(token,6)==0);assert(dnd_model_dispose(token)==0);
  token=fzero_load("sd:/fz-translucent.gma",NULL,-1,0);m=dnd_model_find(token,false);
  assert((m->material[0].mode&4) && !(m->material[1].mode&4) && (m->material[3].mode&12)==12);
  instance=dnd_model_instance(token,4*1024*1024);assert(instance);DndInstance *in=dnd_instance_find(instance);unsigned used=in->bytes;
  float identity[16];dnd_model_identity(identity);dnd_model_sort_draws(in,identity,identity);
  assert(in->draw_order[0]==1 && in->draw_order[1]==2 && in->draw_order[2]==3 && in->draw_order[3]==0);
  float flipped[16];dnd_model_identity(flipped);flipped[10]=-1;dnd_model_sort_draws(in,identity,flipped);
  assert(in->draw_order[0]==1 && in->draw_order[1]==0 && in->draw_order[2]==3 && in->draw_order[3]==2 && in->bytes==used);
  DndArray *matrix=dnd_managed_array_new(&heap,16,4);memcpy(matrix->data,identity,64);
  assert(dnd_model_frame_begin(matrix,matrix)==0 && dnd_model_draw(instance,matrix)==0 && dnd_model_frame_end()==0);
  assert(dnd_model_instance_dispose(instance)==0 && dnd_model_dispose(token)==0);
  assert(!fzero_try("sd:/fz.gma",NULL,-1,8*1024*1024,0));
  assert(!fzero_try("sd:/fz.gma","sd:/fz-bad.tpl",-1,8*1024*1024,0));
  char path[2048];snprintf(path,sizeof(path),"%s/sd/fz-bad-count.bin",root);FILE *file=fopen(path,"rb");assert(file);
  unsigned char count_bytes[4];assert(fread(count_bytes,1,4,file)==4);fclose(file);
  unsigned count=(unsigned)count_bytes[0]|(unsigned)count_bytes[1]<<8|(unsigned)count_bytes[2]<<16|(unsigned)count_bytes[3]<<24;
  for(unsigned i=0;i<count;i++) {
    snprintf(path,sizeof(path),"sd:/fz-bad-%u.bin",i);
    if((token=fzero_try(path,"sd:/fz.tpl",-1,8*1024*1024,0)))fprintf(stderr,"Unexpected accepted F-Zero malformed input: %u\n",i);
    assert(!token && !dnd_exception_pending() && !dnd_fs_open_handles() && !dnd_model_live_handles());
  }
  for(int budget=1024;budget<=1024*1024;budget*=2) {
    token=fzero_try("sd:/fz.arc",NULL,-1,budget,0);if(token)assert(dnd_model_dispose(token)==0);
    assert(!dnd_fs_open_handles() && !dnd_model_live_handles());
  }
  const char *unsupported[]={"sd:/fz-unsupported.skl","sd:/fz-unsupported.mta","sd:/fz-unsupported.mal"};
  for(unsigned i=0;i<3;i++)assert(!fzero_try(unsupported[i],NULL,-1,8*1024*1024,0));
}
static int gamecube_try(const char *path, const char *textures, const char *motion,
                        int format, int approximate, int budget) {
  return dnd_model_begin_format(dnd_string_from_utf8(&heap, path),
      textures ? dnd_string_from_utf8(&heap, textures) : NULL,
      motion ? dnd_string_from_utf8(&heap, motion) : NULL, 30, format, budget, approximate);
}
static int gamecube_load(const char *path, const char *textures, const char *motion,
                         int format, int approximate) {
  int token = gamecube_try(path, textures, motion, format, approximate, 8*1024*1024);
  if (!token) fprintf(stderr, "GameCube source import failed: %s\n", path);
  assert(token); return token;
}
static void gamecube_rotation(int instance,unsigned node,float unit) {
  const float *m=dnd_instance_find(instance)->global+node*16;
  float x=unit,y=unit*2,z=unit*3;
  near(m[0],cosf(z)*cosf(y));near(m[1],sinf(z)*cosf(y));near(m[2],-sinf(y));
  near(m[4],cosf(z)*sinf(x)*sinf(y)-sinf(z)*cosf(x));
  near(m[8],cosf(z)*cosf(x)*sinf(y)+sinf(z)*sinf(x));
}
static void gamecube_tests(const char *root) {
  const char *models[] = {"sd:/sms.bmd", "sd:/sms.bdl", "sd:/sms.szs", "sd:/sms.szp",
      "sd:/sms.arc", "sd:/sms-member.arc", "sd:/sms-archive.szs", "sd:/sms-fixed.bmd",
      "sd:/sms-runs.szs", "sd:/sms-runs.szp"};
  for (unsigned i=0;i<sizeof(models)/sizeof(*models);i++) {
    int token=gamecube_load(models[i],NULL,"sd:/sms.bck",6,0);
    DndModel *m=dnd_model_find(token,false);
    assert(m->nodes==1 && m->meshes==1 && m->textures==1 && m->clips==1);
    assert(m->texture[0].levels==2 && m->texture[0].wrap_s==1 && m->texture[0].wrap_t==2);
    unsigned char *pixel=m->texture[0].data;
    assert(pixel[0]==255 && pixel[1]==255 && pixel[32]==0 && pixel[33]==0);
    int instance=dnd_model_instance(token,4*1024*1024);assert(instance);
    float vertex[6];assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],1);near(vertex[5],1);
    assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
    assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],2);
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  }
  int token=gamecube_load("sd:/sms.arc#model.bmd",NULL,"sd:/sms.arc#move.bck",6,0);
  assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/sms.bmd",NULL,"sd:/sms.bca",6,0);
  int instance=dnd_model_instance(token,4*1024*1024);assert(instance);
  assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
  float vertex[6];assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],2);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/sms.bmd",NULL,"sd:/sms-separate.bck",6,0);
  instance=dnd_model_instance(token,4*1024*1024);assert(instance);assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
  assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],2.5f);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/sms.bmd",NULL,"sd:/sms-rotation.bck",6,0);
  instance=dnd_model_instance(token,4*1024*1024);assert(instance);assert(!dnd_model_play(instance,0,0,0));
  gamecube_rotation(instance,0,4096*3.14159265358979323846f/32767);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/re4.bin","sd:/re4.tpl","sd:/re4-rotation.fcv",7,0);
  instance=dnd_model_instance(token,4*1024*1024);assert(instance);assert(!dnd_model_play(instance,0,0,0));gamecube_rotation(instance,1,.25f);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/re4.bin","sd:/re4.tpl","sd:/re4-hermite.fcv",7,0);
  instance=dnd_model_instance(token,4*1024*1024);assert(instance);assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
  assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],2);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/sms-weighted.bmd",NULL,NULL,6,0);
  DndModel *m=dnd_model_find(token,false);assert(m->nodes==2 && m->skin[0].count==2);
  instance=dnd_model_instance(token,4*1024*1024);assert(instance);
  assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],0);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  token=gamecube_load("sd:/sms-carry.bmd",NULL,NULL,6,0);assert(dnd_model_find(token,false)->mesh[0].indices==6);assert(!dnd_model_dispose(token));
  const int formats[]={0,1,2,3,4,5,6,8,9,10,14};
  for(unsigned i=0;i<sizeof(formats)/sizeof(*formats);i++) {
    char path[128];snprintf(path,sizeof(path),"sd:/sms-format-%d.bmd",formats[i]);
    token=gamecube_load(path,NULL,NULL,6,0);m=dnd_model_find(token,false);
    unsigned char *pixel=m->texture[0].data;
    if(formats[i]==0||formats[i]==1||formats[i]==2)assert(pixel[0]==255 && pixel[1]==255 && pixel[32]==255);
    else if(formats[i]==3)assert(pixel[0]==255 && pixel[1]==127 && pixel[32]==127);
    else assert(pixel[0]==255 && pixel[1]==255 && pixel[32]==0 && pixel[33]==0);
    assert(!dnd_model_dispose(token));
    snprintf(path,sizeof(path),"sd:/re4-format-%d.tpl",formats[i]);token=gamecube_load("sd:/re4.bin",path,NULL,7,0);assert(!dnd_model_dispose(token));
  }
  for(int palette=0;palette<3;palette++) {
    char path[128];snprintf(path,sizeof(path),"sd:/sms-palette-%d.bmd",palette);
    token=gamecube_load(path,NULL,NULL,6,0);m=dnd_model_find(token,false);
    unsigned char *p=m->texture[0].data;assert(p[0]==255 && p[1]==(palette==0?127:255));assert(!dnd_model_dispose(token));
  }
  for(int format=8;format<=10;format++) {
    char path[128];snprintf(path,sizeof(path),"sd:/sms-pattern-%d.bmd",format);
    token=gamecube_load(path,NULL,NULL,6,0);m=dnd_model_find(token,false);
    const unsigned char *p=m->texture[0].data;
    for(unsigned y=0;y<8;y++)for(unsigned x=0;x<8;x++) {
      unsigned at=(y/4*2+x/4)*64+(y%4*4+x%4)*2;
      assert(p[at]==255 && p[at+1]==(x+2*y)%16*17 && p[at+32]==p[at+1] && p[at+33]==p[at+1]);
    }
    assert(!dnd_model_dispose(token));
  }
  const int primitives[]={0x80,0x88,0x90,0x98,0xa0};
  for(unsigned i=0;i<sizeof(primitives)/sizeof(*primitives);i++) {
    char path[128];snprintf(path,sizeof(path),"sd:/sms-primitive-%d.bmd",primitives[i]);
    token=gamecube_load(path,NULL,NULL,6,0);assert(dnd_model_find(token,false)->mesh[0].indices==(primitives[i]==0x80||primitives[i]==0x88?6u:3u));assert(!dnd_model_dispose(token));
  }
  const char *re4[]={"sd:/re4.bin","sd:/re4-weighted.bin","sd:/re4-s8.bin","sd:/re4.dat","sd:/re4.drs"};
  for(unsigned i=0;i<sizeof(re4)/sizeof(*re4);i++) {
    int weighted=i==1||i==2;
    token=gamecube_load(re4[i],i>=3?re4[i]:"sd:/re4.tpl",i>=3?re4[i]:"sd:/re4.fcv",7,0);
    m=dnd_model_find(token,false);assert(m->nodes==(weighted?3u:2u)&&m->clips==1&&m->textures==1);
    instance=dnd_model_instance(token,4*1024*1024);assert(instance);
    assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],weighted?0:1);near(vertex[5],1);
    assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
    assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],weighted?.5f:1.5f);
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  }
  token=gamecube_load("sd:/re4.dat#0","sd:/re4.dat#1","sd:/re4.dat#2",7,0);assert(!dnd_model_dispose(token));
  for(int format=0;format<=10;format++)if(format%4<3) {
    char path[128];snprintf(path,sizeof(path),"sd:/re4-fcc-%d.fcv",format);
    token=gamecube_load("sd:/re4.bin","sd:/re4.tpl",path,7,0);instance=dnd_model_instance(token,4*1024*1024);assert(instance);
    assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
    assert(!dnd_model_vertex(instance,0,0,vertex));near(vertex[0],format/4==2?.0075f:1.5f);
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  }
  assert(!gamecube_try("sd:/re4-normal-palette.bin","sd:/re4.tpl",NULL,7,0,8*1024*1024));
  token=gamecube_load("sd:/re4-normal-palette.bin","sd:/re4.tpl",NULL,7,1);assert(!dnd_model_dispose(token));
  assert(!gamecube_try("sd:/sms-ambiguous.arc",NULL,NULL,6,0,8*1024*1024));
  assert(!gamecube_try("sd:/sms.arc#missing.bmd",NULL,NULL,6,0,8*1024*1024));
  assert(!gamecube_try("sd:/re4-ambiguous.dat","sd:/re4.tpl",NULL,7,0,8*1024*1024));
  token=gamecube_load("sd:/re4-ambiguous.dat#1","sd:/re4.tpl",NULL,7,0);assert(!dnd_model_dispose(token));
  const char *bad_j3d[]={"sd:/sms-bad-chunk.bmd","sd:/sms.arc#../model.bmd"};
  for(unsigned i=0;i<2;i++)assert(!gamecube_try(bad_j3d[i],NULL,NULL,6,0,8*1024*1024));
  assert(!gamecube_try("sd:/sms.bmd",NULL,"sd:/sms-bad-motion.bck",6,0,8*1024*1024));
  assert(!gamecube_try("sd:/re4-bad.bin","sd:/re4.tpl",NULL,7,0,8*1024*1024));
  assert(!gamecube_try("sd:/re4.bin","sd:/re4-bad.tpl",NULL,7,0,8*1024*1024));
  const char *bad_motion[]={"sd:/re4-bad.fcv","sd:/re4-ik.fcv","sd:/re4-camera.fcv"};
  for(unsigned i=0;i<3;i++)assert(!gamecube_try("sd:/re4.bin","sd:/re4.tpl",bad_motion[i],7,0,8*1024*1024));
  for(int budget=1024;budget<=128*1024;budget*=2) {
    token=gamecube_try("sd:/sms.arc",NULL,"sd:/sms.arc",6,0,budget);if(token)assert(!dnd_model_dispose(token));
    token=gamecube_try("sd:/re4.drs","sd:/re4.drs","sd:/re4.drs",7,0,budget);if(token)assert(!dnd_model_dispose(token));
  }
  /* Offset mutations exercise checked addition, palette references and cleanup. */
  char filename[1024];snprintf(filename,sizeof(filename),"%s/sd/sms.bmd",root);FILE *f=fopen(filename,"rb");assert(f);
  assert(!fseek(f,0,SEEK_END));long length=ftell(f);rewind(f);unsigned char *original=malloc((size_t)length),*copy=malloc((size_t)length);assert(original&&copy);assert(fread(original,1,(size_t)length,f)==(size_t)length);fclose(f);
  unsigned chunk=32;
  while(chunk<(unsigned)length) {
    unsigned chunk_size=dnd_model_u32(original+chunk+4);
    for(unsigned field=8;field+4<=chunk_size&&field<48;field+=4) {
      memcpy(copy,original,(size_t)length);put(copy+chunk+field,0xfffffff0u);
      snprintf(filename,sizeof(filename),"%s/sd/gc-mutated.bmd",root);f=fopen(filename,"wb");assert(f);assert(fwrite(copy,1,(size_t)length,f)==(size_t)length);fclose(f);
      token=gamecube_try("sd:/gc-mutated.bmd",NULL,NULL,6,1,8*1024*1024);if(token)assert(!dnd_model_dispose(token));
    }
    chunk+=chunk_size;
  }
  free(original);free(copy);
}
static int ep3_try(const char *path,const char *texture,const char *motion,int budget) {
  return dnd_model_begin_pso_ep3(dnd_string_from_utf8(&heap,path),texture?dnd_string_from_utf8(&heap,texture):NULL,motion?dnd_string_from_utf8(&heap,motion):NULL,30,16,budget,0);
}
static void ep3_tests(void) {
  const char *paths[]={"sd:/direct.gj","sd:/ep3-gj.prs","sd:/ep3.rel","sd:/ep3.nj","sd:/ep3.prs","sd:/ep3-copy.prs","sd:/ep3.bml","sd:/ep3-multiple.bml#npc.rel","sd:/ep3-weighted.rel","sd:/ep3-packed.rel"};
  for(unsigned i=0;i<sizeof(paths)/sizeof(*paths);i++) {
    int token=ep3_try(paths[i],i==6||i==7?NULL:"sd:/ep3-gvm.prs",i==8?"sd:/ep3-weighted-motion.rel":"sd:/ep3-motion.rel",8*1024*1024);
    if(!token)fprintf(stderr,"Episode III import failed: %s\n",paths[i]);
    assert(token);
    DndModel*m=dnd_model_find(token,false);assert(m->meshes==1&&m->textures==1&&m->clips==1);
    assert(m->mesh[0].indices==3);int instance=dnd_model_instance(token,4*1024*1024);assert(instance);
    assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));
    near(dnd_instance_find(instance)->global[12],2);
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));assert(!dnd_fs_open_handles());
  }
  int token=ep3_try("sd:/ep3.rel","sd:/direct.gvm","sd:/ep3-motion-two.rel#1",8*1024*1024);assert(token);assert(!dnd_model_dispose(token));
  token=ep3_try("sd:/ep3-sampler.rel","sd:/direct.gvm",NULL,8*1024*1024);assert(token);
  DndTexture *sampler=&dnd_model_find(token,false)->texture[0];assert(sampler->wrap_s==0&&sampler->wrap_t==2&&sampler->linear==1);assert(!dnd_model_dispose(token));
  assert(!ep3_try("sd:/ep3-mip.rel","sd:/direct.gvm",NULL,8*1024*1024));
  assert(!ep3_try("sd:/ep3-second.rel","sd:/direct.gvm",NULL,8*1024*1024));
  const char *bad[]={"sd:/ep3-fmt2.rel","sd:/ep3-bad-reloc.rel","sd:/ep3-cycle.rel","sd:/ep3-bad-index.rel","sd:/ep3-bad.prs","sd:/ep3-truncated.prs","sd:/ep3-multiple.bml"};
  for(unsigned i=0;i<sizeof(bad)/sizeof(*bad);i++){assert(!ep3_try(bad[i],"sd:/direct.gvm",NULL,8*1024*1024));assert(!dnd_fs_open_handles());assert(!dnd_exception_pending());}
  assert(!ep3_try("sd:/ep3.rel","sd:/direct.gvm","sd:/ep3-motion-two.rel",8*1024*1024));
  assert(!ep3_try("sd:/ep3.rel","sd:/direct.gvm","sd:/ep3-camera.rel",8*1024*1024));
  for(int budget=1024;budget<=512*1024;budget*=2){assert(!ep3_try("sd:/ep3.bml",NULL,NULL,budget));assert(!dnd_fs_open_handles());}
}
static void renderware_melee_tests(void) {
  const int rw_formats[]={0,1,2,3,4,5,6,8,9,14};
  for(unsigned i=0;i<sizeof(rw_formats)/sizeof(*rw_formats);i++){
    char texture[128];snprintf(texture,sizeof(texture),"sd:/rw-tex-%d.txd",rw_formats[i]);
    int t=gamecube_load("sd:/rw.dff",texture,NULL,8,0);assert(dnd_model_find(t,false)->textures==1);assert(!dnd_model_dispose(t));
  }
  int alpha=gamecube_load("sd:/rw.dff","sd:/rw-alpha.txd",NULL,8,0);assert(dnd_model_find(alpha,false)->material[0].mode&4);assert(!dnd_model_dispose(alpha));
  const char *rw[]={"sd:/rw.dff","sd:/rw-native.dff","sd:/rw-skin.dff","sd:/rw-native-skin.dff","sd:/rw-native-prefix.dff"};
  for(unsigned i=0;i<5;i++) {
    int token=gamecube_load(rw[i],"sd:/rw.txd",i>=2?"sd:/rw.anm":NULL,8,i==4?2:0);
    DndModel *m=dnd_model_find(token,false);assert(m->textures==1&&m->meshes==1&&m->nodes==(i>=2?2u:1u));
    int instance=dnd_model_instance(token,4*1024*1024);assert(instance);float v[6];
    assert(!dnd_model_vertex(instance,0,0,v));near(v[0],i>=2?0:1);near(v[5],1);
    unsigned char *p=m->texture[0].data;assert(p[0]==255&&p[1]==255&&p[32]==0&&p[33]==0);
    if(i>=2){assert(m->clips==1);assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));assert(!dnd_model_vertex(instance,0,0,v));near(v[0],1);}
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  }
  assert(!gamecube_try("sd:/rw-native-prefix.dff","sd:/rw.txd",NULL,8,0,8*1024*1024));
  const char *motion[]={"sd:/melee-motion.dat","sd:/melee-animjoint.dat","sd:/melee-aj.dat#1"};
  for(unsigned i=0;i<3;i++) {
    int token=gamecube_load("sd:/melee.dat#Fighter_joint",NULL,motion[i],9,0);DndModel*m=dnd_model_find(token,false);
    assert(m->nodes==1&&m->textures==1&&m->clips==1);int instance=dnd_model_instance(token,4*1024*1024);assert(instance);float v[6];
    assert(!dnd_model_vertex(instance,0,0,v));near(v[0],1);
    assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));assert(!dnd_model_vertex(instance,0,0,v));near(v[0],1.5f); /* helper passes 30 source fps */
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  }
  int token=gamecube_load("sd:/melee-weighted.dat",NULL,"sd:/melee-weighted-motion.dat",9,0);
  int instance=dnd_model_instance(token,4*1024*1024);assert(instance);float v[6];assert(!dnd_model_vertex(instance,0,0,v));near(v[0],0);
  assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));assert(!dnd_model_vertex(instance,0,0,v));near(v[0],.5f);
  assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  const int formats[]={0,1,2,3,4,5,6,8,9,10,14};
  for(unsigned i=0;i<sizeof(formats)/sizeof(*formats);i++) {
    char path[128];snprintf(path,sizeof(path),"sd:/melee-tex-%d.dat",formats[i]);token=gamecube_load(path,NULL,NULL,9,0);assert(dnd_model_find(token,false)->textures==1);assert(!dnd_model_dispose(token));
  }
  for(int format=0;format<5;format++) {
    char path[128];snprintf(path,sizeof(path),"sd:/melee-fobj-%d.dat",format);token=gamecube_load("sd:/melee.dat",NULL,path,9,0);
    instance=dnd_model_instance(token,4*1024*1024);assert(instance);assert(!dnd_model_play(instance,0,0,0));assert(!dnd_model_control(instance,0,.5f));assert(!dnd_model_vertex(instance,0,0,v));near(v[0],1.5f);
    assert(!dnd_model_instance_dispose(instance));assert(!dnd_model_dispose(token));
  }
  assert(!gamecube_try("sd:/rw-bad.dff","sd:/rw.txd",NULL,8,0,8*1024*1024));
  assert(!gamecube_try("sd:/melee-cycle.dat",NULL,NULL,9,0,8*1024*1024));
  assert(!gamecube_try("sd:/melee-billboard.dat",NULL,NULL,9,0,8*1024*1024));
  for(int budget=1024;budget<=256*1024;budget*=2) {
    token=gamecube_try("sd:/rw-native-skin.dff","sd:/rw.txd","sd:/rw.anm",8,0,budget);if(token)assert(!dnd_model_dispose(token));
    token=gamecube_try("sd:/melee-weighted.dat",NULL,"sd:/melee-weighted-motion.dat",9,0,budget);if(token)assert(!dnd_model_dispose(token));
  }
}
int main(int argc, char **argv) {
  assert(argc == 2);
  setenv("DND_STORAGE_ROOT", argv[1], 1);
  static unsigned char memory[128 * 1024];
  dnd_managed_heap_init(&heap, memory, sizeof(memory));
  assert(dnd_storage_mount(2));
  int m = load("sd:/animated.dndmodel"),
      a = dnd_model_instance(m, 4 * 1024 * 1024),
      b = dnd_model_instance(m, 4 * 1024 * 1024);
  assert(a && b);
  float v[6];
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0);
  assert(dnd_model_play(a, 0, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  assert(dnd_model_vertex(b, 0, 0, v) == 0);
  near(v[0], 0);
  assert(dnd_model_play(a, 1, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0);
  assert(dnd_model_control(a, 4, 1) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 2);
  assert(dnd_model_play(a, 2, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  assert(dnd_model_play(a, 3, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 1, v) == 0);
  near(v[0], 0.5f);
  near(v[1], 0.5f);
  near(v[5], 1);
  assert(dnd_model_play(a, 4, 0, 0) == 0);
  assert(dnd_model_control(a, 0, 0.5f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 1);
  assert(dnd_model_play(a, 0, 1, 0) == 0);
  assert(dnd_model_control(a, 0, 1.25f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.25f);
  assert(dnd_model_play(a, 4, 0, 0.5f) == 0);
  assert(dnd_model_control(a, 0, 0.25f) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  unsigned length = (unsigned)dnd_model_stat(m, 2);
  unsigned char *backup = malloc(length);
  assert(backup);
  DndArray *buffer = dnd_managed_array_new(&heap, 4096, 1);
  for (unsigned p = 0; p < length;) {
    unsigned n = length - p;
    if (n > 4096)
      n = 4096;
    assert(dnd_model_copy(m, (int)p, buffer, (int)n, 0) == 0);
    memcpy(backup + p, buffer->data, n);
    p += n;
  }
  int shared = load("sd:/animated.dndmodel");
  DndModel *original = dnd_model_find(m, false),
           *duplicate = dnd_model_find(shared, false);
  assert(original->texture[0].data == duplicate->texture[0].data);
  assert(dnd_model_dispose(shared) == 0);
  int bytes = dnd_model_stat(m, 1);
  assert(dnd_model_unload(m) == 0);
  assert(dnd_model_stat(m, 1) < bytes);
  assert(dnd_model_control(a, 0, 0) < 0);
  assert(dnd_model_restore_begin(m) == 0);
  buffer->data[0] = 99;
  assert(dnd_model_copy(m, 0, buffer, 1, 1) == 0);
  assert(dnd_model_restore_end(m, 1) < 0);
  assert(dnd_model_stat(m, 0) == 0);
  assert(dnd_model_restore_begin(m) == 0);
  for (unsigned p = 0; p < length;) {
    unsigned n = length - p;
    if (n > 4096)
      n = 4096;
    memcpy(buffer->data, backup + p, n);
    assert(dnd_model_copy(m, (int)p, buffer, (int)n, 1) == 0);
    p += n;
  }
  assert(dnd_model_restore_end(m, 1) == 0);
  assert(dnd_model_vertex(a, 0, 0, v) == 0);
  near(v[0], 0.5f);
  corrupt_cases(argv[1], backup, length);
  free(backup);
  DndArray *matrix = dnd_managed_array_new(&heap, 16, 4);
  float identity[16];
  dnd_model_identity(identity);
  memcpy(matrix->data, identity, 64);
  assert(dnd_model_frame_begin(matrix, matrix) == 0);
  assert(dnd_performance_stats()->triangles == 0);
  assert(dnd_model_draw(a, matrix) == 0);
  assert(dnd_performance_stats()->triangles == 1);
  assert(dnd_model_draw(b, matrix) == 0);
  assert(dnd_performance_stats()->triangles == 2);
  assert(dnd_model_draw(0, matrix) < 0);
  assert(dnd_performance_stats()->triangles == 2);
  assert(dnd_model_unload(m) < 0);
  assert(dnd_model_instance_dispose(a) < 0);
  assert(dnd_model_frame_end() == 0);
  assert(dnd_model_instance(m, 1024) == 0);
  assert(dnd_model_dispose(m) == 0);
  assert(dnd_model_play(a, 0, 0, 0) == 0);
  assert(dnd_model_instance_dispose(a) == 0);
  assert(dnd_model_instance_dispose(b) == 0);
  assert(dnd_model_dispose(m) < 0);
  assert(!dnd_model_live_handles());
  assert(!dnd_fs_open_handles());
  for (int i = 0; i < 20; i++) {
    int model = load("sd:/animated.dndmodel");
    assert(dnd_model_dispose(model) == 0);
  }
  assert(!dnd_model_live_handles());
  assert(!dnd_exception_pending());
  int fbx = dnd_model_begin_scene(dnd_string_from_utf8(&heap,"sd:/direct.fbx"),1,60,8*1024*1024,0);assert(fbx);
  int fi = dnd_model_instance(fbx, 4 * 1024 * 1024);
  assert(fi);
  DndModel *fm = dnd_model_find(fbx, false);
  int walk = -1;
  for (unsigned i = 0; i < fm->clips; i++)
    if (!strcmp(fm->clip[i].name, "Move"))
      walk = (int)i;
  assert(walk >= 0);
  assert(dnd_model_play(fi, walk, 0, 0) == 0);
  assert(dnd_model_control(fi, 0, 0.5f) == 0);
  assert(dnd_model_evaluate(dnd_instance_find(fi)) == 0);
  near(dnd_instance_find(fi)->global[fm->mesh[0].node * 16 + 12], 1.f);
  assert(dnd_model_instance_dispose(fi) == 0);
  assert(dnd_model_dispose(fbx) == 0);
  int skinned_fbx=dnd_model_begin_scene(dnd_string_from_utf8(&heap,"sd:/skinned.fbx"),1,60,8*1024*1024,0);assert(skinned_fbx);
  int ski=dnd_model_instance(skinned_fbx,4*1024*1024);assert(ski);
  assert(dnd_model_find(skinned_fbx,false)->skins==1);
  assert(dnd_model_play(ski,0,0,0)==0&&dnd_model_control(ski,0,.5f)==0);
  assert(dnd_model_vertex(ski,0,0,v)==0);near(v[0],1.f);
  assert(dnd_model_instance_dispose(ski)==0&&dnd_model_dispose(skinned_fbx)==0);
  /* The explicit legacy adapter is exercised when its CI fixture exists. */
  DndString *legacy_path=dnd_string_from_utf8(&heap,"sd:/fbx.dndmodel");
  if(dnd_fs_exists(legacy_path,false)) {
    fbx=load("sd:/fbx.dndmodel");fi=dnd_model_instance(fbx,4*1024*1024);assert(fi);
    fm=dnd_model_find(fbx,false);walk=-1;
    for(unsigned i=0;i<fm->clips;i++)if(!strcmp(fm->clip[i].name,"Walk"))walk=(int)i;
    assert(walk>=0&&dnd_model_play(fi,walk,0,0)==0&&dnd_model_control(fi,0,.5f)==0);
    assert(dnd_model_vertex(fi,0,0,v)==0);near(v[0],.5f);
    assert(dnd_model_instance_dispose(fi)==0&&dnd_model_dispose(fbx)==0);
  }
  int obj = dnd_model_begin_obj(dnd_string_from_utf8(&heap,"sd:/concave.obj"),8*1024*1024);assert(obj);
  DndModel *om = dnd_model_find(obj, false);
  assert(om->textures == 1 && om->clips == 0 && om->mesh[0].indices == 9);
  assert(dnd_model_dispose(obj) == 0);
  int textured_fbx=dnd_model_begin_scene(dnd_string_from_utf8(&heap,"sd:/textured.fbx"),1,60,8*1024*1024,0);assert(textured_fbx);
  assert(dnd_model_find(textured_fbx,false)->textures==1);
  assert(dnd_model_dispose(textured_fbx)==0);
  int binary_fbx=dnd_model_begin_scene(dnd_string_from_utf8(&heap,"sd:/embedded-binary.fbx"),1,60,8*1024*1024,0);assert(binary_fbx);
  assert(dnd_model_find(binary_fbx,false)->textures==1&&dnd_model_find(binary_fbx,false)->clips==0);
  assert(dnd_model_dispose(binary_fbx)==0);
  const char *image_models[]={"sd:/image-tga.obj","sd:/image-bmp.obj","sd:/image-jpg.obj"};
  for(unsigned i=0;i<3;i++) {
    int image_model=dnd_model_begin_obj(dnd_string_from_utf8(&heap,image_models[i]),8*1024*1024);assert(image_model);
    assert(dnd_model_find(image_model,false)->textures==1);
    assert(dnd_model_dispose(image_model)==0);
  }
  DndString *invalid_fbx=dnd_string_from_utf8(&heap,"sd:/invalid.fbx"),*invalid_obj=dnd_string_from_utf8(&heap,"sd:/invalid.obj");
  assert(!dnd_model_begin_scene(invalid_fbx,1,60,8*1024*1024,0));
  assert(!dnd_model_begin_obj(invalid_obj,8*1024*1024));
  DndString *scene_path=dnd_string_from_utf8(&heap,"sd:/skinned.fbx");
  for(int budget=1024;budget<=512*1024;budget*=2) {
    int candidate=dnd_model_begin_scene(scene_path,1,60,budget,0);
    if(candidate){assert(dnd_model_stat(candidate,1)<=budget);assert(dnd_model_dispose(candidate)==0);}
    assert(!dnd_fs_open_handles()&&!dnd_model_live_handles());
  }
  assert(!dnd_model_live_handles());
  DndString *obj_path=dnd_string_from_utf8(&heap,"sd:/live.obj");
  int live=dnd_model_begin_obj(obj_path,8*1024*1024);
  assert(live);
  assert(dnd_model_stat(live,3)==0&&dnd_model_stat(live,4)>=1);
  int live_instance=dnd_model_instance(live,4*1024*1024);
  assert(live_instance);
  assert(dnd_model_vertex(live_instance,0,1,v)==0);
  near(v[0],1.f);
  assert(dnd_model_instance_dispose(live_instance)==0);
  assert(dnd_model_dispose(live)==0);
  int pso,ps;DndModel *pm;const unsigned char *pixels;
  DndString *pso_path=dnd_string_from_utf8(&heap,"sd:/direct.gj"),*gvm_path=dnd_string_from_utf8(&heap,"sd:/direct.gvm"),*njm_path=dnd_string_from_utf8(&heap,"sd:/direct.njm");
  pso=dnd_model_begin_pso(pso_path,gvm_path,njm_path,30,16,8*1024*1024);assert(pso);
  pm=dnd_model_find(pso,false);assert(pm->textures==1&&pm->clips==1);
  ps=dnd_model_instance(pso,4*1024*1024);assert(ps);
  assert(dnd_model_play(ps,0,0,0)==0&&dnd_model_control(ps,0,.5f)==0);
  assert(dnd_model_evaluate(dnd_instance_find(ps))==0);near(dnd_instance_find(ps)->global[12],1.f);
  pixels=pm->texture[0].data;assert(pixels[0]==255&&pixels[1]==230);
  assert(dnd_model_instance_dispose(ps)==0&&dnd_model_dispose(pso)==0);
  assert(!dnd_model_begin_pso(pso_path,NULL,njm_path,30,16,8*1024*1024));
  assert(!dnd_model_begin_pso(pso_path,gvm_path,njm_path,0,16,8*1024*1024));
  assert(!dnd_model_begin_pso(pso_path,gvm_path,njm_path,30,16,1024));
  assert(!dnd_fs_open_handles()&&!dnd_model_live_handles());
  const int formats[]={0,1,2,3,4,5,6,14};
  const unsigned char expected[8][4]={{170,170,170,170},{73,73,73,73},{51,51,51,170},{99,99,99,17},{255,0,0,255},{85,119,187,109},{7,11,13,5},{255,0,0,255}};
  for(unsigned i=0;i<8;i++) {
    char texture_path[64];snprintf(texture_path,sizeof(texture_path),"sd:/format-%d.gvr",formats[i]);
    int texture_model=dnd_model_begin_pso(pso_path,dnd_string_from_utf8(&heap,texture_path),NULL,0,16,8*1024*1024);assert(texture_model);
    pixels=dnd_model_find(texture_model,false)->texture[0].data;
    assert(pixels[0]==expected[i][3]&&pixels[1]==expected[i][0]&&pixels[32]==expected[i][1]&&pixels[33]==expected[i][2]);
    if(formats[i]==14){assert(pixels[5]==159&&pixels[37]==95);assert(pixels[7]==95&&pixels[39]==159);}
    assert(dnd_model_dispose(texture_model)==0);
  }
  const char *primitive_models[]={"sd:/fan.gj","sd:/strip.gj"};
  const unsigned primitive_indices[2][6]={{0,2,1,0,3,2},{0,2,1,2,3,1}};
  for(unsigned i=0;i<2;i++) {
    int primitive_model=dnd_model_begin_pso(dnd_string_from_utf8(&heap,primitive_models[i]),gvm_path,NULL,0,16,8*1024*1024);assert(primitive_model);
    DndMesh *mesh=&dnd_model_find(primitive_model,false)->mesh[0];assert(mesh->indices==6);
    for(unsigned j=0;j<6;j++)assert(dnd_model_u32(mesh->index+j*4)==primitive_indices[i][j]);
    assert(dnd_model_dispose(primitive_model)==0);
  }
  {
    DndString *rotation_path=dnd_string_from_utf8(&heap,"sd:/rotate.njm");
    int rotated=dnd_model_begin_pso(pso_path,gvm_path,rotation_path,30,16,8*1024*1024);
    assert(rotated);int ri=dnd_model_instance(rotated,4*1024*1024);assert(ri);
    assert(dnd_model_play(ri,0,0,0)==0&&dnd_model_control(ri,0,.5f)==0);
    assert(dnd_model_evaluate(dnd_instance_find(ri))==0);
    near(dnd_instance_find(ri)->global[0],sqrtf(.5f));near(dnd_instance_find(ri)->global[1],sqrtf(.5f));
    assert(dnd_model_instance_dispose(ri)==0&&dnd_model_dispose(rotated)==0);
  }
  DndString *glb_path=dnd_string_from_utf8(&heap,"sd:/embedded.glb");
  int glb=dnd_model_begin_glb(glb_path,8*1024*1024,0);assert(glb);
  DndModel *gm=dnd_model_find(glb,false);assert(gm->clips==5&&gm->textures==1);
  int gi=dnd_model_instance(glb,4*1024*1024);assert(gi);
  assert(dnd_model_play(gi,0,0,0)==0&&dnd_model_control(gi,0,.5f)==0);
  assert(dnd_model_vertex(gi,0,0,v)==0);near(v[0],.5f);
  assert(dnd_model_instance_dispose(gi)==0&&dnd_model_dispose(glb)==0);
  assert(dnd_model_begin_glb(glb_path,1024,0)==0);
  const char *bad_glb[]={"sd:/invalid-order.glb","sd:/external.glb","sd:/sparse.glb"};
  for(unsigned j=0;j<3;j++) {
    DndString *s=dnd_string_from_utf8(&heap,bad_glb[j]);
    assert(!dnd_model_begin_glb(s,8*1024*1024,0));
    assert(!dnd_fs_open_handles()&&!dnd_model_live_handles());
  }
  live=load("sd:/geometry-gj.dndmodel");
  live_instance=dnd_model_instance(live,4*1024*1024);
  assert(live_instance);
  assert(dnd_model_vertex(live_instance,0,1,v)==0);
  near(v[0],1.f); /* vertex 1 has local position (1,0,0). */
  near(v[1],0.f);
  assert(dnd_model_instance_dispose(live_instance)==0);
  assert(dnd_model_dispose(live)==0);
  assert(!dnd_model_live_handles());
  fzero_tests(argv[1]);
  gamecube_tests(argv[1]);
  renderware_melee_tests();
  ep3_tests();
  assert(!dnd_model_live_handles()&&!dnd_fs_open_handles());
  puts("Model validation, playback, skinning and native PSO/GLB/FBX/OBJ/F-Zero GX/J3D/RE4/RenderWare/Melee/Episode III import tests passed");
  return 0;
}
