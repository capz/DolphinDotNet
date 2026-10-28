/* Shared scene/sampler state owned by the current managed pass. */
static bool scene_options_active,style_options_active;
static float scene_options[136],style_options[20];
void dnd_draw_backend_options(const float *p,int scene){
    bool *active=scene?&scene_options_active:&style_options_active;
    float *values=scene?scene_options:style_options;size_t bytes=(scene?136u:20u)*sizeof(float);
    if(*active!=(p!=NULL)||(p&&memcmp(values,p,bytes))){if(p)memcpy(values,p,bytes);*active=p!=NULL;dnd_draw_backend_invalidate_cache();}
    if(scene&&style_options_active){style_options_active=false;dnd_draw_backend_invalidate_cache();}
}
bool dnd_draw_backend_scene_active(void){return scene_options_active&&dnd_device_pass()==1;}
static void scene_vector(const float *view,const float *p,float out[3],bool point){
    for(int i=0;i<3;i++)out[i]=view[i]*p[0]+view[i+4]*p[1]+view[i+8]*p[2]+(point?view[i+12]:0);
}
void dnd_draw_backend_scene(int lit,const float *view){
    if(!dnd_draw_backend_scene_active()){return;}
    const float *p=scene_options;
    if(lit){
        unsigned mask=0;
        for(int i=0;i<(int)p[13];i++){
            const float *l=p+16+i*15;GXLightObj light={0};GX_InitLightAttn(&light,1,0,0,1,0,0);float position[3],direction[3];
            scene_vector(view,l+7,direction,false);
            float n=sqrtf(direction[0]*direction[0]+direction[1]*direction[1]+direction[2]*direction[2]);
            if(l[0]==0){for(int j=0;j<3;j++)position[j]=-direction[j]*1000000.f/n;}
            else scene_vector(view,l+4,position,true);
            GX_InitLightPos(&light,position[0],position[1],position[2]);
            GX_InitLightColor(&light,(GXColor){(u8)l[1],(u8)l[2],(u8)l[3],255});
            if(l[0]!=0)GX_InitLightDistAttn(&light,l[10],.5f,GX_DA_MEDIUM);
            if(l[0]==2){GX_InitLightDir(&light,direction[0]/n,direction[1]/n,direction[2]/n);GX_InitLightSpot(&light,l[11],GX_SP_COS);}
            GX_LoadLightObj(&light,GX_LIGHT0<<i);mask|=GX_LIGHT0<<i;
        }
        GX_SetChanAmbColor(GX_COLOR0A0,(GXColor){(u8)p[1],(u8)p[2],(u8)p[3],255});
        GX_SetChanCtrl(GX_COLOR0,GX_ENABLE,GX_SRC_REG,GX_SRC_VTX,mask,GX_DF_CLAMP,GX_AF_SPOT);
        GX_SetChanCtrl(GX_ALPHA0,GX_DISABLE,GX_SRC_REG,GX_SRC_VTX,GX_LIGHTNULL,GX_DF_NONE,GX_AF_NONE);
    }
    int fog=(int)p[4];
    u8 kind=fog==1?GX_FOG_PERSP_LIN:fog==2?GX_FOG_PERSP_EXP:fog==3?GX_FOG_PERSP_EXP2:GX_FOG_NONE;
    if(fog&&p[12])kind|=8; /* GX orthographic fog bit. */
    GX_SetFog(kind,fog?p[5]:0,fog?p[6]:1,p[10],p[11],(GXColor){(u8)p[7],(u8)p[8],(u8)p[9],255});
}
static bool managed_style_active(void){return style_options_active&&(dnd_device_pass()==1||dnd_device_pass()==2);}
static int managed_wrap(int fallback,int slot,int axis){int mode=(int)style_options[slot*10+axis+1];return !managed_style_active()||mode==0?fallback:mode-1;}
static void managed_sampler(GXTexObj *tex,int slot,int levels,bool linear,int wrap_s,int wrap_t){
    if(!managed_style_active())return;
    int filter=(int)style_options[slot*10];
    GX_InitTexObjWrapMode(tex,managed_wrap(wrap_s,slot,0),managed_wrap(wrap_t,slot,1));
    int kind=(filter?filter==2:linear)?GX_LINEAR:GX_NEAR;
    GX_InitTexObjLOD(tex,levels>1?(kind==GX_LINEAR?GX_LIN_MIP_LIN:GX_NEAR_MIP_NEAR):kind,kind,0,levels>1?levels-1:0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);
}
static void managed_uv(int slot){
    if(!managed_style_active())return;
    const float *p=style_options+slot*10+4;
    Mtx matrix={{p[0],p[2],0,p[4]},{p[1],p[3],0,p[5]},{0,0,1,0}};
    u32 id=slot==0?GX_TEXMTX0:GX_TEXMTX1;
    GX_LoadTexMtxImm(matrix,id,GX_MTX2x4);
    GX_SetTexCoordGen(slot==0?GX_TEXCOORD0:GX_TEXCOORD1,GX_TG_MTX2x4,slot==0?GX_TG_TEX0:GX_TG_TEX1,id);
}
