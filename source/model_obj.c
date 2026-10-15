#define _POSIX_C_SOURCE 200809L
#include "dnd_model_internal.h"
#include "dnd_storage.h"
#include <ctype.h>
#include <errno.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Restricted live GameCube OBJ parser: static triangular/polygon geometry,
 * v/vt/vn faces, negative OBJ indices and fan triangulation. Deliberately no
 * MTL/shader import or animation; prefer offline conversion for full assets.
 * All transient allocations and the output package have strict upper bounds.
 */
enum { MAX_ITEMS=32768, MAX_TRIANGLES=16384, MAX_OBJ_BYTES=2*1024*1024 };
typedef struct {float v[3];} Vec3;
typedef struct {float v[2];} Vec2;
typedef struct {int v, t, n;} Ref;
typedef struct {Ref a,b,c;} Face;
static void be32(unsigned char *p,unsigned v) {
  p[0]=(unsigned char)(v>>24);p[1]=(unsigned char)(v>>16);
  p[2]=(unsigned char)(v>>8);p[3]=(unsigned char)v;
}
static void bf(unsigned char *p,float v) {
  unsigned word;memcpy(&word,&v,sizeof(word));be32(p,word);
}
static int obj_index(long index,int count) {
  long actual=index>0?index-1:index<0?(long)count+index:-1;
  return actual>=0&&actual<count?(int)actual:-1;
}
static int component(const char **p,int count,int empty) {
  if(**p=='/'||**p==0)return empty?-1:-2;
  char *end=NULL;errno=0;long value=strtol(*p,&end,10);
  if(errno||end==*p||(*end!='/'&&*end!=0))return -2;
  *p=end;
  return obj_index(value,count);
}
static int face_ref(const char *s,int vertices,int uvs,int norms,Ref *r) {
  const char *p=s;
  r->v=component(&p,vertices,0);r->t=-1;r->n=-1;
  if(r->v<0)return 0;
  if(*p=='/') {p++;r->t=component(&p,uvs,1);if(r->t==-2)return 0;}
  if(*p=='/') {p++;r->n=component(&p,norms,1);if(r->n==-2)return 0;}
  return *p==0;
}
static void triangle_normal(float out[3],const Vec3 *a,const Vec3 *b,const Vec3 *c) {
  float x=b->v[0]-a->v[0],y=b->v[1]-a->v[1],z=b->v[2]-a->v[2];
  float p=c->v[0]-a->v[0],q=c->v[1]-a->v[1],r=c->v[2]-a->v[2];
  out[0]=y*r-z*q;out[1]=z*p-x*r;out[2]=x*q-y*p;
  float mag=sqrtf(out[0]*out[0]+out[1]*out[1]+out[2]*out[2]);
  if(mag<1e-8f){out[0]=0;out[1]=1;out[2]=0;}
  else for(int i=0;i<3;i++)out[i]/=mag;
}
static int parse(char *input,Vec3 *positions,Vec2 *uvs,Vec3 *normals,
                 Face *triangles,int *count) {
  int nv=0,nt=0,nn=0,nf=0;
  char *save=NULL;
  for(char *line=strtok_r(input,"\n",&save);line;line=strtok_r(NULL,"\n",&save)) {
    while(isspace((unsigned char)*line))line++;
    if(line[0]=='v'&&isspace((unsigned char)line[1])) {
      if(nv>=MAX_ITEMS||sscanf(line+1,"%f %f %f",
                  &positions[nv].v[0],&positions[nv].v[1],&positions[nv].v[2])!=3)return 0;
      for(int j=0;j<3;j++)if(!isfinite(positions[nv].v[j]))return 0;
      nv++;
    } else if(line[0]=='v'&&line[1]=='t'&&isspace((unsigned char)line[2])) {
      if(nt>=MAX_ITEMS||sscanf(line+2,"%f %f",&uvs[nt].v[0],&uvs[nt].v[1])!=2)return 0;
      if(!isfinite(uvs[nt].v[0])||!isfinite(uvs[nt].v[1]))return 0;nt++;
    } else if(line[0]=='v'&&line[1]=='n'&&isspace((unsigned char)line[2])) {
      if(nn>=MAX_ITEMS||sscanf(line+2,"%f %f %f",
             &normals[nn].v[0],&normals[nn].v[1],&normals[nn].v[2])!=3)return 0;
      for(int j=0;j<3;j++)if(!isfinite(normals[nn].v[j]))return 0;
      nn++;
    } else if(line[0]=='f'&&isspace((unsigned char)line[1])) {
      Ref refs[64];int n=0;char *toksave=NULL;
      for(char *token=strtok_r(line+1," \t\r",&toksave);
          token;token=strtok_r(NULL," \t\r",&toksave)) {
        if(*token=='#')break;
        if(n>=64||!face_ref(token,nv,nt,nn,&refs[n]))return 0;
        n++;
      }
      if(n<3||nf+n-2>MAX_TRIANGLES)return 0;
      for(int i=1;i<n-1;i++)
        triangles[nf++]=(Face){refs[0],refs[i],refs[i+1]};
    }
  }
  *count=nf;return nf>0;
}
static unsigned align32(unsigned x){return (x+31u)&~31u;}
int dnd_model_begin_obj(DndString *path,int budget) {
  if(budget<1024||budget>(int)DND_MODEL_LIMIT||!path)return 0;
  int handle=dnd_fs_open(path,3,1,1);
  if(!handle)return 0;
  int64_t len=dnd_fs_length_native(handle);
  if(len<4||len>MAX_OBJ_BYTES) {dnd_fs_close(handle);return 0;}
  char *input=malloc((size_t)len+1);
  if(!input){dnd_fs_close(handle);return 0;}
  int n=0;while(n<len) {
    int got=dnd_fs_read_native(handle,input+n,(int)(len-n));
    if(got<=0)break;n+=got;
  }
  dnd_fs_close(handle);
  if(n!=len){free(input);return 0;}
  input[n]=0;
  if(memchr(input,0,(size_t)n)) {free(input);return 0;}
  Vec3 *positions=calloc(MAX_ITEMS,sizeof(Vec3));
  Vec2 *uvs=calloc(MAX_ITEMS,sizeof(Vec2));
  Vec3 *normals=calloc(MAX_ITEMS,sizeof(Vec3));
  Face *faces=calloc(MAX_TRIANGLES,sizeof(Face));
  int count=0,token=0;
  if(!positions||!uvs||!normals||!faces||!parse(input,positions,uvs,normals,faces,&count))
    goto done;
  unsigned vert=(unsigned)count*3;
  unsigned section[6]={112,48+vert*84,36,4,4,4};
  unsigned offsets[6],total=160;
  for(int i=0;i<6;i++){offsets[i]=total;total=align32(total+section[i]);}
  if(total>DND_MODEL_LIMIT||total>(unsigned)budget)goto done;
  unsigned char *pack=calloc(total,1);if(!pack)goto done;
  be32(pack,0x444e444d);be32(pack+4,1);
  be32(pack+8,total);be32(pack+12,6);
  unsigned char *node=pack+offsets[0],*mesh=pack+offsets[1],*mat=pack+offsets[2];
  be32(node,1);be32(node+4,0xffffffffu);memcpy(node+8,"LiveOBJ",7);
  bf(node+8+64+3*4+3*4,1.f); /* node quaternion W */
  for(int i=0;i<3;i++)bf(node+8+64+7*4+i*4,1.f); /* scale */
  be32(mesh,1);be32(mesh+4,0);be32(mesh+8,0xffffffffu);
  be32(mesh+12,0);be32(mesh+16,vert);be32(mesh+20,vert);
  float lo[3]={INFINITY,INFINITY,INFINITY},hi[3]={-INFINITY,-INFINITY,-INFINITY};
  for(int i=0;i<count;i++){
    const Ref refs[3]={faces[i].a,faces[i].b,faces[i].c};
    float fallback[3];triangle_normal(fallback,&positions[refs[0].v],
                   &positions[refs[1].v],&positions[refs[2].v]);
    for(int j=0;j<3;j++) {
      unsigned ix=(unsigned)i*3+(unsigned)j;
      unsigned char *dst=mesh+48+ix*80;
      const Ref *ref=&refs[j];
      for(int a=0;a<3;a++) {
        float v=positions[ref->v].v[a];
        bf(dst+a*4,v);if(v<lo[a])lo[a]=v;if(v>hi[a])hi[a]=v;
        bf(dst+(a+3)*4,ref->n<0?fallback[a]:normals[ref->n].v[a]);
      }
      bf(dst+24,ref->t<0?0:uvs[ref->t].v[0]);
      bf(dst+28,ref->t<0?0:1.f-uvs[ref->t].v[1]);
      for(int a=0;a<4;a++)bf(dst+32+a*4,1.f);
      be32(mesh+48+vert*80+ix*4,ix);
    }
  }
  for(int a=0;a<3;a++){bf(mesh+24+a*4,lo[a]);bf(mesh+36+a*4,hi[a]);}
  be32(mat,1);for(int a=0;a<4;a++)bf(mat+4+a*4,1.f);
  be32(mat+20,0xffffffffu);bf(mat+28,.5f);
  for(int i=3;i<6;i++)be32(pack+offsets[i],0);
  for(int i=0;i<6;i++){
    unsigned char *dir=pack+16+i*16;
    be32(dir,(unsigned)i+1);be32(dir+4,offsets[i]);
    be32(dir+8,section[i]);
    be32(dir+12,dnd_model_hash(pack+offsets[i],section[i]));
  }
  token=dnd_model_begin_data(pack,total,budget);
  free(pack);
done:
  free(input);free(positions);free(uvs);free(normals);free(faces);
  return token;
}
