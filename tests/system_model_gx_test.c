/* Reuse the recording GX transport. The original fixture retains every strict
 * assertion unless authored_test is explicitly enabled by this separate test. */
#define DND_MODEL_ALLOC_COMPILE_TEST
#define main legacy_model_effect_main
#include "model_effect_test.c"
#undef main

static unsigned fixture_chunk(DndArray *source,const char *tag) {
  unsigned at=32;while(at+8<=source->length) {
    unsigned size=dnd_model_u32(source->data+at+4);
    if(!memcmp(source->data+at,tag,4))return at;
    assert(size>=8&&size<=source->length-at);at+=size;
  }assert(0);return 0;
}
static void rejects_byte(DndArray *source,unsigned at,unsigned char value) {
  unsigned char saved=source->data[at];source->data[at]=value;
  assert(!dnd_model_j3d_memory(source,1024*1024));source->data[at]=saved;
  assert(!dnd_model_live_handles());
}
int main(int argc,char **argv) {
  assert(argc==2);FILE *f=fopen(argv[1],"rb");assert(f);
  assert(!fseek(f,0,SEEK_END));long length=ftell(f);assert(length>32);rewind(f);
  DndArray *source=calloc(1,sizeof(*source)+(size_t)length);assert(source);
  source->element_size=1;source->length=(uint32_t)length;
  assert(fread(source->data,1,(size_t)length,f)==(size_t)length);assert(!fclose(f));
  assert(!dnd_model_j3d_memory(source,1024));assert(!dnd_model_live_handles());
  unsigned mat=fixture_chunk(source,"MAT1"),shp=fixture_chunk(source,"SHP1");
  rejects_byte(source,mat+12,255); /* out-of-range material table */
  rejects_byte(source,mat+104,255); /* invalid stage-count index */
  rejects_byte(source,mat+100+144,0); /* unsupported fog instead of approximation */
  rejects_byte(source,shp+128,0xff); /* never submit unknown display-list opcode */
  unsigned saved=source->length;source->length--;assert(!dnd_model_j3d_memory(source,1024*1024));source->length=saved;
  int token=dnd_model_j3d_memory(source,1024*1024);assert(token);
  DndModel *model=dnd_model_find(token,false);
  assert(model&&model->authored_count==1&&model->nodes==1&&model->meshes==1);
  assert(!strcmp(model->node[0].name,"boot_demo_base_cube"));
  memset(source->data,0,source->length);free(source);
  int instance=dnd_model_instance(token,1024*1024);assert(instance);
  DndArray *world=identity(),*camera=identity();authored_test=true;
  assert(dnd_model_frame_begin(camera,camera)==0);
  assert(dnd_model_draw(instance,world)==0&&draws==1);
  Draw captured=record[0];
  assert(captured.position[0]==-1&&captured.position[1]==-1&&captured.position[3]==1&&captured.position[7]==1);
  assert(captured.cull==GX_CULL_FRONT&&captured.zwrite==0&&captured.blend==GX_BM_BLEND);
  assert(captured.src==GX_BL_SRCALPHA&&captured.dst==GX_BL_INVSRCALPHA);
  assert(captured.count==1&&captured.texgens==1&&scene_normals==3);
  assert(authored_tex_source==1&&authored_tex_id==30);
  assert(authored_tex_matrix[0][0]==.5f&&authored_tex_matrix[0][3]==.5f);
  assert(authored_colors[1].r==-12&&authored_colors[1].g==20&&authored_colors[1].b==40);
  assert(authored_rgb_op[2]==1&&authored_rgb_op[3]==0&&authored_alpha_op[2]==0&&authored_alpha_op[3]==1);
  assert(authored_depth_location==1&&authored_dither==1&&indexed_tint.a==128);
  assert(captured.stages[0].a==15&&captured.stages[0].b==2&&captured.stages[0].c==8&&captured.stages[0].d==15);
  assert(captured.stages[0].alpha[0]==7&&captured.stages[0].alpha[1]==5&&captured.stages[0].alpha[2]==4&&captured.stages[0].alpha[3]==7);
  assert(dnd_model_node_offset(instance,0,NULL)<0&&dnd_model_color(instance,0,0,NULL)<0);
  assert(dnd_model_frame_end()==0);
  DndArray *pose=identity();pose->length=10;float *p=(float*)pose->data;
  memset(p,0,40);p[0]=2;p[6]=1;p[7]=p[8]=p[9]=1;
  assert(!dnd_model_node_offset(instance,0,pose));p[7]=0;assert(dnd_model_node_offset(instance,0,pose)<0);
  DndArray *color=identity();color->length=4;float *c=(float*)color->data;c[0]=1;c[1]=2;c[2]=3;c[3]=200;
  assert(!dnd_model_color(instance,0,0,color));c[0]=-50;assert(!dnd_model_color(instance,0,1,color));
  c[0]=-2000;assert(dnd_model_color(instance,0,1,color)<0);
  int other=dnd_model_instance(token,1024*1024);assert(other);
  assert(dnd_model_frame_begin(camera,camera)==0);assert(!dnd_model_draw(instance,world));
  assert(indexed_tint.a==200&&authored_colors[1].r==-50&&record[1].position[0]==1);
  assert(!dnd_model_draw(other,world));assert(indexed_tint.a==128&&authored_colors[1].r==-12&&record[2].position[0]==-1);
  assert(!dnd_model_frame_end());assert(!dnd_model_node_offset(instance,0,NULL));
  for(int slot=0;slot<4;slot++)assert(!dnd_model_color(instance,0,slot,NULL));
  assert(!dnd_model_instance_dispose(other));free(pose);free(color);
  assert(dnd_model_instance_dispose(instance)==0&&dnd_model_dispose(token)==0);
  free(world);free(camera);assert(!dnd_model_live_handles()&&!dnd_draw_live());
  puts("Generated J3D1 geometry, signed TEV, alpha, projection and lifecycle recording passed");return 0;
}
