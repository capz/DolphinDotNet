/* Exercise production palette allocation, skin math, budgeting and unload-style
 * cleanup without a desktop asset converter or GX backend. */
#include "dnd_model_internal.h"
#include "dnd_draw.h"
#include "dnd_storage.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

bool dnd_device_active(void){return false;}
void dnd_fs_close(int token){(void)token;assert(!"Fixture does not own a storage handle.");}

static void put(unsigned char *p, unsigned value) {
  for (unsigned i = 0; i < 4; i++)p[i]=(unsigned char)(value >> (24-i*8));
}
static void put_float(unsigned char *p, float value) {
  unsigned bits;memcpy(&bits,&value,sizeof(bits));put(p,bits);
}
static void near(float actual,float expected){assert(fabsf(actual-expected)<.0001f);}
static unsigned charged(unsigned payload){return (payload+63u)&~31u;}
static unsigned allocation_bytes(const void *p){return ((const unsigned *)p)[-8];}

static void check_workspace(unsigned first_joints,unsigned second_joints){
  unsigned largest=second_joints>first_joints?second_joints:first_joints;
  unsigned mesh_count=second_joints?2:1;
  DndNode *nodes=calloc(largest,sizeof(*nodes));assert(nodes);
  DndSkin skins[2]={{0}};DndMesh meshes[2]={{0}};
  unsigned char *joint_data[2]={NULL,NULL};unsigned char vertices[2][80]={{0}};
  for(unsigned i=0;i<largest;i++){
    nodes[i].parent=-1;nodes[i].rest.q[3]=1;nodes[i].rest.t[0]=(float)(i+1);
    nodes[i].rest.s[0]=2;nodes[i].rest.s[1]=3;nodes[i].rest.s[2]=4;
  }
  for(unsigned i=0;i<mesh_count;i++){
    unsigned joints=i?second_joints:first_joints;
    joint_data[i]=calloc(joints,68);assert(joint_data[i]);
    skins[i]=(DndSkin){.count=(int)joints,.data=joint_data[i]};
    for(unsigned j=0;j<joints;j++){
      put(joint_data[i]+j*68,j);
      for(unsigned k=0;k<16;k++)put_float(joint_data[i]+j*68+4+k*4,k%5==0?1:0);
    }
    for(unsigned j=0;j<3;j++){
      put_float(vertices[i]+j*4,(float)(j+1));
      put_float(vertices[i]+12+j*4,1/sqrtf(3));
    }
    for(unsigned j=0;j<4;j++)put(vertices[i]+48+j*4,joints-1);
    put_float(vertices[i]+64,1);
    meshes[i]=(DndMesh){.node=0,.skin=(int)i,.material=0,.vertices=1,.data=vertices[i]};
  }
  DndMaterial material={0};
  DndModel model={.token=700,.refs=1,.ready=true,.nodes=largest,.meshes=mesh_count,.materials=1,.skins=mesh_count,
    .node=nodes,.mesh=meshes,.material=&material,.skin=skins};
  assert(!dnd_models[0]);dnd_models[0]=&model;
  int token=dnd_model_instance(model.token,4*1024*1024);assert(token);
  DndInstance *in=dnd_instance_find(token);assert(in&&in->workspace&&in->palette_joints==largest);
  unsigned workspace_bytes=charged(largest*32*sizeof(float));
  assert(allocation_bytes(in->workspace)==workspace_bytes);
  unsigned before=in->bytes;float *workspace=in->workspace;
  for(unsigned i=0;i<mesh_count;i++){
    unsigned joints=i?second_joints:first_joints;float value[6];
    assert(dnd_model_vertex(token,(int)i,0,value)==0);
    near(value[0],2+(float)joints);near(value[1],6);near(value[2],12);
    float length=sqrtf(.5f*.5f+1.f/9+.25f*.25f);
    near(value[3],.5f/length);near(value[4],(1.f/3)/length);near(value[5],.25f/length);
  }
  assert(dnd_model_evaluate(in)==0&&in->workspace==workspace&&in->bytes==before);
  dnd_model_drop_vertices(in);
  assert(!in->workspace&&!in->palette_joints&&in->bytes==before-workspace_bytes-mesh_count*charged(6*sizeof(float)));
  for(unsigned i=0;i<mesh_count;i++)assert(!in->vertices[i]);
  assert(dnd_model_evaluate(in)==0&&in->bytes==before&&allocation_bytes(in->workspace)==workspace_bytes);
  if(largest==256){
    /* A newly validated package can have a different largest palette. Fresh
     * workspace ownership must recalculate its bound after dropping the old. */
    unsigned changed=mesh_count-1;dnd_model_drop_vertices(in);skins[changed].count=2;
    for(unsigned j=0;j<4;j++)put(vertices[changed]+48+j*4,1);
    assert(dnd_model_evaluate(in)==0&&in->palette_joints==2);
    assert(allocation_bytes(in->workspace)==charged(2*32*sizeof(float)));
    assert(in->bytes==before-workspace_bytes+charged(2*32*sizeof(float)));
    float value[6];assert(dnd_model_vertex(token,(int)changed,0,value)==0);near(value[0],4);
    dnd_model_drop_vertices(in);skins[changed].count=256;
    for(unsigned j=0;j<4;j++)put(vertices[changed]+48+j*4,255);
    assert(dnd_model_evaluate(in)==0&&in->palette_joints==256&&in->bytes==before);
  }
  assert(dnd_model_instance_dispose(token)==0&&model.refs==1);
  if(before>1024){
    token=dnd_model_instance(model.token,(int)before);assert(token);
    assert(dnd_model_instance_bytes(token)==(int)before);
    assert(dnd_model_instance_dispose(token)==0);
    assert(!dnd_model_instance(model.token,(int)before-1)&&model.refs==1);
    for(unsigned i=0;i<DND_INSTANCE_SLOTS;i++)assert(!dnd_instances[i]);
  }
  dnd_models[0]=NULL;
  printf("Skin workspace %u/%u joints: %u bytes including allocation header; saves %u bytes.\n",
    first_joints,second_joints,workspace_bytes,charged(256*32*sizeof(float))-workspace_bytes);
  for(unsigned i=0;i<mesh_count;i++)free(joint_data[i]);
  free(nodes);
}

int main(void){
  check_workspace(1,0);check_workspace(2,0);check_workspace(256,0);check_workspace(1,256);
  puts("Model skin workspace sizing, normal matrices, reuse, budget and cleanup passed.");
  return 0;
}
