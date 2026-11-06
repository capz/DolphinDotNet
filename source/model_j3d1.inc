/* Original J3D1 MAT1 profile. Layout was qualified against the nine models in
 * NTSC 1.0/1.2 and PAL 1.2. It is deliberately separate from MAT3: MAT1 has
 * 152-byte records, a 100-byte header and precomputed 52-byte texture matrices.
 * Unknown multi-stage/generator profiles fail instead of becoming diffuse. */
static void j3d1_tev(PsoReader r,unsigned at,DndAuthoredTev *out) {
  for(unsigned k=0;k<4;k++)out->input[k]=(uint8_t)pso_byte(r,at+k);
  out->operation=(uint8_t)pso_byte(r,at+4);out->bias=(uint8_t)pso_byte(r,at+5);
  out->scale=(uint8_t)pso_byte(r,at+6);out->clamp=(uint8_t)pso_byte(r,at+7);
  out->output=(uint8_t)pso_byte(r,at+8);
}
static int j3d1_materials(J3dImport *c) {
  PsoReader r=c->mat;GcImport *g=&c->gc;
  unsigned count=pso_u16(r,8),table=pso_u32(r,12),remap=pso_u32(r,16);
  if(r.size<100||!g->authored||g->authored_count!=count||!pso_range(r,remap,count*2))return 0;
  for(unsigned i=0;i<count;i++) {
    unsigned index=pso_u16(r,remap+i*2);
    if(index>=count||table>r.size||index>(r.size-table)/152||!pso_range(r,table+index*152,152))return 0;
    unsigned at=table+index*152,texture=UINT_MAX;
    DndAuthoredMaterial *m=&g->authored[i];
    if((pso_byte(r,at)!=1&&pso_byte(r,at)!=4)||
       pso_byte(r,j3d_lookup(r,32,pso_byte(r,at+2),1,1))!=1||
       pso_byte(r,j3d_lookup(r,64,pso_byte(r,at+4),1,1))!=1)return 0;
    unsigned cull=pso_u32(r,j3d_lookup(r,24,pso_byte(r,at+1),4,4));
    if(cull>3)return 0;
    m->cull=(uint8_t)cull;
    m->texgens=(uint8_t)pso_byte(r,j3d_lookup(r,40,pso_byte(r,at+3),1,1));
    m->depth_location=(uint8_t)pso_byte(r,j3d_lookup(r,88,pso_byte(r,at+5),1,1));
    m->dither=(uint8_t)pso_byte(r,j3d_lookup(r,92,pso_byte(r,at+7),1,1));
    unsigned depth=j3d_lookup(r,84,pso_byte(r,at+6),4,4);
    for(unsigned k=0;k<3;k++)m->depth[k]=(uint8_t)pso_byte(r,depth+k);
    unsigned alpha=j3d_lookup(r,76,pso_u16(r,at+146),8,8);
    unsigned blend=j3d_lookup(r,80,pso_u16(r,at+148),4,4);
    for(unsigned k=0;k<5;k++)m->alpha[k]=(uint8_t)pso_byte(r,alpha+k);
    for(unsigned k=0;k<4;k++)m->blend[k]=(uint8_t)pso_byte(r,blend+k);
    if(pso_u16(r,at+144)!=65535||pso_u16(r,at+150)!=65535||
       pso_u16(r,at+110)!=65535)return 0; /* no authored fog/NBT or PREV initializer */
    float color[4]={1,1,1,1};unsigned ci=pso_u16(r,at+8);
    if(ci!=65535) {
      unsigned entry=j3d_lookup(r,28,ci,4,4);
      for(unsigned k=0;k<4;k++)color[k]=pso_byte(r,entry+k)/255.f;
    }
    for(unsigned ch=0;ch<2;ch++) {
      unsigned idx=pso_u16(r,at+12+ch*2);
      if(idx==65535)return 0;
      unsigned entry=j3d_lookup(r,36,idx,8,8);
      for(unsigned k=0;k<5;k++)m->channel[ch][k]=(uint8_t)pso_byte(r,entry+k);
    }
    c->vertex_color[i]=(unsigned char)(m->channel[0][1]||m->channel[1][1]);
    for(unsigned k=0;k<8;k++) {
      unsigned idx=pso_u16(r,at+56+k*2);
      if(idx==65535)continue;
      if(k)return 0;
      texture=pso_u16(r,j3d_lookup(r,52,idx,2,2));
      if(texture>=g->textures)return 0;
    }
    if((texture!=UINT_MAX)!=(m->texgens==1))return 0;
    if(m->texgens) {
      unsigned gen=pso_u16(r,at+20),matrix=pso_u16(r,at+36);
      if(gen==65535)return 0;
      unsigned entry=j3d_lookup(r,44,gen,4,4);
      if(pso_byte(r,entry)!=1)return 0;
      m->texsource=(uint8_t)pso_byte(r,entry+1);
      unsigned id=pso_byte(r,entry+2);
      if(id!=30&&id!=60)return 0;
      if(id==30) {
        if(matrix==65535)return 0;
        unsigned mt=j3d_lookup(r,48,matrix,52,52);
        if(pso_byte(r,mt+48)!=1)return 0;
        for(unsigned k=0;k<12;k++)m->texmatrix[k]=pso_float(r,mt+k*4);
        m->texmatrix_active=1;
      }
      for(unsigned k=1;k<8;k++)if(pso_u16(r,at+20+k*2)!=65535)return 0;
    }
    unsigned order=j3d_lookup(r,56,pso_u16(r,at+72),4,4);
    for(unsigned k=0;k<3;k++)m->order[k]=(uint8_t)pso_byte(r,order+k);
    for(unsigned reg=0;reg<3;reg++) {
      unsigned idx=pso_u16(r,at+104+reg*2);
      if(idx==65535)continue;
      unsigned entry=j3d_lookup(r,60,idx,8,8);
      for(unsigned k=0;k<4;k++)m->colors[reg][k]=(int16_t)pso_u16(r,entry+k*2);
    }
    unsigned stage=j3d_lookup(r,68,pso_u16(r,at+112),20,20);
    j3d1_tev(r,stage+1,&m->rgb);j3d1_tev(r,stage+10,&m->opacity);
    for(unsigned k=1;k<16;k++)
      if(pso_u16(r,at+72+k*2)!=65535||pso_u16(r,at+112+k*2)!=65535)return 0;
    if(!dnd_authored_material_valid(m))return 0;
    if(texture==UINT_MAX)for(unsigned k=0;k<4;k++)
      if(m->rgb.input[k]==8||m->rgb.input[k]==9||m->opacity.input[k]==4)return 0;
    unsigned mode=2;
    if(m->blend[0])mode|=4|16|(unsigned)m->blend[1]<<5|(unsigned)m->blend[2]<<8;
    floats(&g->section[2],color,4);number(&g->section[2],texture);number(&g->section[2],mode);
    scalar(&g->section[2],0);number(&g->section[2],m->cull==0);g->materials++;
  }
  return !g->memory.failed;
}
