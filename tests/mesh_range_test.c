#include "dnd_draw.h"
#include <assert.h>
#include <stdlib.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
static bool active;
bool dnd_device_active(void){return active;}
int dnd_raw_depth(void){return 0;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_flush(void *p,int n,int invalidate){(void)p;(void)n;(void)invalidate;}
void dnd_exception_throw(DndExceptionKind k,const char *m){(void)k;(void)m;abort();}
static DndArray *array(unsigned count,unsigned size){
 DndArray *a=calloc(1,sizeof(DndArray)+count*size);assert(a);
 a->length=count;a->element_size=size;return a;
}
int main(void){
 DndArray *v=array(6,36),*i=array(6,2);
 uint16_t *index=(uint16_t*)i->data;index[0]=0;index[1]=1;index[2]=2;
 index[3]=index[4]=index[5]=65535;
 float invalid=NAN;memcpy(v->data+3*36,&invalid,4);
 int token=dnd_mesh_create_range(v,3,i,3,2,0);assert(token);
 assert(dnd_draw_bytes(token)==3*36+3*2);
 assert(dnd_draw_release(token)==0);
 assert(dnd_mesh_create(v,i,2,0)==0); /* unused invalid tail is checked by full-array API */
 assert(dnd_mesh_create_range(v,7,i,3,2,0)==0);
 assert(dnd_mesh_create_range(v,-1,i,3,2,0)==0);
 assert(dnd_mesh_create_range(v,3,i,4,2,0)==0);
 assert(dnd_mesh_create_range(v,3,i,9,2,0)==0);
 assert(dnd_mesh_create_range(v,3,NULL,3,2,0)==0);
 active=true;assert(dnd_mesh_create_range(v,3,i,3,2,0)==0);active=false;
 index[2]=3;assert(dnd_mesh_create_range(v,3,i,3,2,0)==0);
 index[2]=2;token=dnd_mesh_create_range(v,3,NULL,0,2,0);assert(token);
 assert(dnd_draw_bytes(token)==3*36);assert(dnd_draw_release(token)==0);
 assert(dnd_draw_total_bytes()==0);free(v);free(i);
 puts("Mesh used ranges: prefix copy, exact accounting, invalid tail, bounds and active-frame rejection passed.");
}
