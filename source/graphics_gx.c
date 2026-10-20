#include "dnd_draw.h"
#include "dnd_graphics.h"
#include "dnd_console.h"
#include "dnd_performance.h"
#include "dnd_debug_heartbeat.h"
#include "dnd_network_platform.h"
#include "dnd_aram.h"
#include <math.h>
#include <string.h>
#include <stdlib.h>
#include "dnd_material_math.h"
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <malloc.h>
#include <ogc/timesupp.h>
static GXRModeObj *mode;
static void *xfb[2],*fifo;
static unsigned fb;
static bool first_frame=true;
static float view_matrix[16], projection_matrix[16];
static float immediate_world[16];
static DndDrawVertex immediate_vertices[1536];
static int immediate_count;
static bool immediate_batch;
static uint32_t immediate_color=0xffffffffu;
static unsigned glyph_quads;
static int glyph_vertices;
static bool projection_ortho, external_gpu;
bool dnd_draw_backend_immediate_ready(void){return !immediate_batch;}
static bool immediate_pass(void){if(dnd_device_pass()!=4){dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Begin an immediate frame first.");return false;}return true;}
static void identity(float *m) { memset(m,0,64);m[0]=m[5]=m[10]=m[15]=1; }
static void multiply(float *out,const float *a,const float *b) {
    float m[16];for(int c=0;c<4;c++)for(int r=0;r<4;r++) { m[c*4+r]=0;for(int k=0;k<4;k++)m[c*4+r]+=a[k*4+r]*b[c*4+k]; }memcpy(out,m,64);
}
static GXColor color(uint32_t rgba) { return (GXColor){rgba>>24,rgba>>16,rgba>>8,rgba}; }
static uint32_t tint(uint32_t a,uint32_t b) {
    uint32_t value=0;for(int s=0;s<=24;s+=8)value|=((((a>>s)&255)*((b>>s)&255)/255)<<s);return value;
}
static uint32_t rgba(float r,float g,float b,float a) {
    return (uint32_t)(r*255)<<24|(uint32_t)(g*255)<<16|(uint32_t)(b*255)<<8|(uint32_t)(a*255);
}
void dnd_graphics_init(void) {
    if(mode)return;
    VIDEO_Init();mode=VIDEO_GetPreferredMode(NULL);
    if(!mode){dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No video mode available.");return;}
    void *first=SYS_AllocateFramebuffer(mode),*second=SYS_AllocateFramebuffer(mode);
    fifo=memalign(32,256*1024);
    /* A null allocation must be checked before the K0-to-K1 address alias. */
    if(!first||!second||!fifo){free(first);free(second);free(fifo);fifo=NULL;mode=NULL;dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Graphics initialization failed.");return;}
    xfb[0]=MEM_K0_TO_K1(first);xfb[1]=MEM_K0_TO_K1(second);
    memset(fifo,0,256*1024);GX_Init(fifo,256*1024);
    VIDEO_Configure(mode);VIDEO_SetNextFramebuffer(xfb[0]);VIDEO_SetBlack(TRUE);VIDEO_Flush();VIDEO_WaitVSync();
    if(mode->viTVMode&VI_NON_INTERLACE)VIDEO_WaitVSync();
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight,mode->xfbHeight));
    GX_SetDispCopySrc(0,0,mode->fbWidth,mode->efbHeight);GX_SetDispCopyDst(mode->fbWidth,mode->xfbHeight);
    GX_SetCopyFilter(mode->aa,mode->sample_pattern,GX_TRUE,mode->vfilter);
    GX_SetFieldMode(mode->field_rendering,mode->viHeight==2*mode->xfbHeight?GX_ENABLE:GX_DISABLE);
    projection_ortho=false;identity(view_matrix);identity(projection_matrix);identity(immediate_world);
}
int dnd_graphics_width(void){return mode?mode->fbWidth:640;}
int dnd_graphics_height(void){return mode?mode->efbHeight:480;}
void dnd_draw_backend_external(void){external_gpu=true;}
void dnd_draw_backend_wait(void){if(mode||external_gpu)GX_DrawDone();}
void dnd_draw_backend_abort(void){if(mode||external_gpu)GX_AbortFrame();immediate_batch=false;immediate_count=0;glyph_vertices=0;}
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){if(invalidate==1)DCInvalidateRange(data,(u32)bytes);else {DCFlushRange(data,(u32)bytes);if(invalidate==2&&mode)GX_InvalidateTexAll();}}
void dnd_draw_backend_viewport(int x,int y,int w,int h){GX_SetViewport((float)x,(float)y,(float)w,(float)h,0,1);}
void dnd_draw_backend_scissor(int x,int y,int w,int h){GX_SetScissorBoxOffset(0,0);GX_SetScissor((u32)x,(u32)y,(u32)w,(u32)h);}
void dnd_draw_backend_camera(const float *view,const float *projection){memcpy(view_matrix,view,64);memcpy(projection_matrix,projection,64);projection_ortho=false;}
void dnd_draw_backend_2d(int w,int h) {
    projection_ortho=true;Mtx44 p;guOrtho(p,0,(float)h,0,(float)w,-1,1);identity(view_matrix);
    for(int r=0;r<4;r++)for(int c=0;c<4;c++)projection_matrix[c*4+r]=p[r][c];
    dnd_draw_backend_viewport(0,0,dnd_graphics_width(),dnd_graphics_height());
    dnd_draw_backend_scissor(0,0,dnd_graphics_width(),dnd_graphics_height());
}
void dnd_draw_backend_restore(void) {
    GX_SetPixelFmt(mode&&mode->aa?GX_PF_RGB565_Z16:GX_PF_RGB8_Z24,GX_ZC_LINEAR);
    GX_SetDither(GX_FALSE);GX_SetDstAlpha(GX_FALSE,0);GX_SetColorUpdate(GX_TRUE);GX_SetAlphaUpdate(GX_TRUE);GX_SetZCompLoc(GX_FALSE);
    GX_SetNumIndStages(0);GX_SetTevDirect(GX_TEVSTAGE0);GX_SetTevSwapMode(GX_TEVSTAGE0,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevSwapModeTable(GX_TEV_SWAP0,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_ALPHA);
    GX_SetTexCoordScaleManually(GX_TEXCOORD0,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD0,GX_FALSE,GX_FALSE);
    GX_SetClipMode(GX_CLIP_ENABLE);GX_SetCoPlanar(GX_FALSE);GX_SetFog(GX_FOG_NONE,0,1,0,1,(GXColor){0,0,0,0});
}
#include "dnd_tev_backend.inc"
#include "dnd_material_channels.inc"
void dnd_draw_backend_vertices(const DndDrawVertex *v,int count,int primitive,const DndDrawTexture *texture,const DndDrawMaterial *material,const float *world) {
    dnd_draw_backend_restore();
    float mv[16];multiply(mv,view_matrix,world);Mtx m;Mtx44 p;
    for(int r=0;r<3;r++)for(int c=0;c<4;c++)m[r][c]=mv[c*4+r];
    for(int r=0;r<4;r++)for(int c=0;c<4;c++)p[r][c]=projection_matrix[c*4+r];
    GX_LoadProjectionMtx(p,projection_ortho?GX_ORTHOGRAPHIC:GX_PERSPECTIVE);GX_LoadPosMtxImm(m,GX_PNMTX0);GX_SetCurrentMtx(GX_PNMTX0);
    bool lit=(material->flags&32)!=0,environment=(material->flags&192)!=0,world_reflection=(material->flags&128)!=0;
    const DndDrawTexture *secondary=material->secondary;
    Mtx inverse_view;
    if(world_reflection){Mtx view;for(int r=0;r<3;r++)for(int c=0;c<4;c++)view[r][c]=view_matrix[c*4+r];if(!guMtxInverse(view,inverse_view))return;}
    Mtx normal;
    if(lit||environment) {
        Mtx inverse;
        if(!guMtxInverse(m,inverse))return;
        guMtxTranspose(inverse,normal);GX_LoadNrmMtxImm(normal,GX_PNMTX0);
    }
    if(lit) {
        GXLightObj light={0};GX_InitLightPos(&light,0,0,1000000);GX_InitLightColor(&light,(GXColor){192,192,192,255});GX_LoadLightObj(&light,GX_LIGHT0);
        GX_SetChanAmbColor(GX_COLOR0A0,(GXColor){64,64,64,255});
    }
    GX_SetTevSwapMode(GX_TEVSTAGE0,GX_TEV_SWAP0,GX_TEV_SWAP0);GX_SetTevSwapModeTable(GX_TEV_SWAP0,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_ALPHA);
    GX_SetTexCoordScaleManually(GX_TEXCOORD0,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD0,GX_FALSE,GX_FALSE);
    GX_SetLineWidth(6,GX_TO_ZERO);GX_SetPointSize(6,GX_TO_ZERO);
    GX_SetNumIndStages(0);GX_SetNumChans(1);GX_SetNumTexGens(secondary?2:texture?1:0);
    dnd_material_channels(lit);
    GX_SetZMode((material->flags&16)?GX_FALSE:GX_TRUE,GX_LEQUAL,(material->flags&8)?GX_FALSE:GX_TRUE);
    GX_SetZCompLoc(GX_FALSE);GX_SetColorUpdate(GX_TRUE);GX_SetAlphaUpdate(GX_TRUE);
    float determinant=world[0]*(world[5]*world[10]-world[9]*world[6])-world[4]*(world[1]*world[10]-world[9]*world[2])+world[8]*(world[1]*world[6]-world[5]*world[2]);
    GX_SetCullMode((material->flags&4)?(determinant<0?GX_CULL_FRONT:GX_CULL_BACK):GX_CULL_NONE);
    GX_SetAlphaCompare(material->cutoff>0?GX_GEQUAL:GX_ALWAYS,(u8)(material->cutoff*255),GX_AOP_AND,GX_ALWAYS,0);
    int blend=material->flags&3;
    GX_SetBlendMode(blend?GX_BM_BLEND:GX_BM_NONE,blend==2?GX_BL_ONE:GX_BL_SRCALPHA,blend==2?GX_BL_ONE:blend==3?GX_BL_INVSRCALPHA:GX_BL_INVSRCALPHA,GX_LO_CLEAR);
    if(blend==3)GX_SetBlendMode(GX_BM_BLEND,GX_BL_ONE,GX_BL_INVSRCALPHA,GX_LO_CLEAR);
    GX_SetClipMode(GX_CLIP_ENABLE);GX_SetCoPlanar(GX_FALSE);GX_SetFog(GX_FOG_NONE,0,1,0,1,(GXColor){0,0,0,0});
    if(texture) {
        GXTexObj tex;GX_InitTexObj(&tex,(void*)texture->data,(u16)texture->width,(u16)texture->height,(u8)texture->format,world_reflection&&!secondary?GX_REPEAT:GX_CLAMP,GX_CLAMP,GX_FALSE);
        GX_InitTexObjLOD(&tex,texture->linear?GX_LINEAR:GX_NEAR,texture->linear?GX_LINEAR:GX_NEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);
        GX_LoadTexObj(&tex,GX_TEXMAP0);
    }
    if(texture||secondary)GX_SetTexCoordGen(GX_TEXCOORD0,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
    if(secondary){
        GXTexObj tex;GX_InitTexObj(&tex,(void*)secondary->data,(u16)secondary->width,(u16)secondary->height,(u8)secondary->format,world_reflection?GX_REPEAT:GX_CLAMP,GX_CLAMP,GX_FALSE);
        GX_InitTexObjLOD(&tex,secondary->linear?GX_LINEAR:GX_NEAR,secondary->linear?GX_LINEAR:GX_NEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);
        GX_LoadTexObj(&tex,GX_TEXMAP1);GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX1,GX_IDENTITY);
        GX_SetTexCoordScaleManually(GX_TEXCOORD1,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD1,GX_FALSE,GX_FALSE);
    }
    dnd_draw_backend_tev(material->effect,texture!=NULL);
    GX_ClearVtxDesc();GX_SetVtxDesc(GX_VA_POS,GX_DIRECT);GX_SetVtxDesc(GX_VA_CLR0,GX_DIRECT);
    GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_POS,GX_POS_XYZ,GX_F32,0);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_CLR0,GX_CLR_RGBA,GX_RGBA8,0);
    if(lit){GX_SetVtxDesc(GX_VA_NRM,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_NRM,GX_NRM_XYZ,GX_F32,0);}
    if(texture||secondary){GX_SetVtxDesc(GX_VA_TEX0,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_TEX0,GX_TEX_ST,GX_F32,0);}
    if(secondary){GX_SetVtxDesc(GX_VA_TEX1,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_TEX1,GX_TEX_ST,GX_F32,0);}
    GX_Begin((u8)primitive,GX_VTXFMT0,(u16)count);
    for(int i=0;i<count;i++) {
        GX_Position3f32(v[i].x,v[i].y,v[i].z);
        if(lit)GX_Normal3f32(v[i].nx,v[i].ny,v[i].nz);
        GXColor c=color(tint(v[i].color,material->color));GX_Color4u8(c.r,c.g,c.b,c.a);
        float u=v[i].u,t=v[i].v;
        if(environment){
            if(world_reflection){
                float px=m[0][0]*v[i].x+m[0][1]*v[i].y+m[0][2]*v[i].z+m[0][3];
                float py=m[1][0]*v[i].x+m[1][1]*v[i].y+m[1][2]*v[i].z+m[1][3];
                float pz=m[2][0]*v[i].x+m[2][1]*v[i].y+m[2][2]*v[i].z+m[2][3];
                dnd_material_world_reflection(normal,inverse_view,v[i].nx,v[i].ny,v[i].nz,px,py,pz,&u,&t);
            }else dnd_material_view_normal(normal,v[i].nx,v[i].ny,v[i].nz,&u,&t);
        }
        if(texture||secondary)GX_TexCoord2f32(secondary?v[i].u:u,secondary?v[i].v:t);
        if(secondary)GX_TexCoord2f32(u,t);
    }
    GX_End();
}
void dnd_draw_backend_copy(const DndDrawTexture *texture,int x,int y,int w,int h) {
    GX_SetTexCopySrc((u16)x,(u16)y,(u16)w,(u16)h);GX_SetTexCopyDst((u16)w,(u16)h,GX_TF_RGBA8,GX_FALSE);
    GX_CopyTex((void*)texture->data,GX_FALSE);GX_PixModeSync();GX_DrawDone();GX_InvalidateTexAll();
}
void dnd_graphics_begin_frame(float r,float g,float b,float a) {
    dnd_graphics_init();if(!mode)return;
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight,mode->xfbHeight));
    GX_SetDispCopySrc(0,0,mode->fbWidth,mode->efbHeight);GX_SetDispCopyDst(mode->fbWidth,mode->xfbHeight);
    GX_SetCopyFilter(mode->aa,mode->sample_pattern,GX_TRUE,mode->vfilter);
    GX_SetPixelFmt(mode->aa?GX_PF_RGB565_Z16:GX_PF_RGB8_Z24,GX_ZC_LINEAR);
    GX_SetCopyClear(color(rgba(r,g,b,a)),0x00ffffff);GX_SetColorUpdate(GX_TRUE);GX_SetAlphaUpdate(GX_TRUE);
    GX_SetZMode(GX_TRUE,GX_LEQUAL,GX_TRUE);GX_CopyDisp(xfb[fb],GX_TRUE);GX_DrawDone();
    dnd_draw_backend_viewport(0,0,mode->fbWidth,mode->efbHeight);dnd_draw_backend_scissor(0,0,mode->fbWidth,mode->efbHeight);
    projection_ortho=false;identity(view_matrix);identity(projection_matrix);identity(immediate_world);
}
void dnd_graphics_frustum(float l,float r,float b,float t,float n,float f) {
    if(!immediate_pass())return;
    if(immediate_batch||!isfinite(l)||!isfinite(r)||!isfinite(b)||!isfinite(t)||!isfinite(n)||!isfinite(f)||r==l||t==b||n<=0||f<=n){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Invalid frustum or active triangle batch.");return;}
    projection_ortho=false;Mtx44 p;guFrustum(p,t,b,l,r,n,f);for(int y=0;y<4;y++)for(int x=0;x<4;x++)projection_matrix[x*4+y]=p[y][x];
}
void dnd_graphics_translate(float x,float y,float z){if(!immediate_pass())return;if(immediate_batch||!isfinite(x)||!isfinite(y)||!isfinite(z)){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Invalid translation or active triangle batch.");return;}float m[16];identity(m);m[12]=x;m[13]=y;m[14]=z;multiply(immediate_world,immediate_world,m);}
void dnd_graphics_rotate(float angle,float x,float y,float z) {
    if(!immediate_pass())return;if(immediate_batch||!isfinite(angle)||!isfinite(x)||!isfinite(y)||!isfinite(z)){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Invalid rotation or active triangle batch.");return;}
    float length=sqrtf(x*x+y*y+z*z);if(length==0)return;x/=length;y/=length;z/=length;
    float a=angle*0.01745329252f,c=cosf(a),s=sinf(a),q=1-c;
    float m[16]={c+x*x*q,y*x*q+z*s,z*x*q-y*s,0,x*y*q-z*s,c+y*y*q,z*y*q+x*s,0,x*z*q+y*s,y*z*q-x*s,c+z*z*q,0,0,0,0,1};multiply(immediate_world,immediate_world,m);
}
static void immediate_flush(void){if(!immediate_count)return;DndDrawMaterial mat={0xffffffffu,4,0,NULL,NULL};dnd_draw_backend_vertices(immediate_vertices,immediate_count,GX_TRIANGLES,NULL,&mat,immediate_world);dnd_performance_add_triangles((uint32_t)immediate_count/3);immediate_count=0;}
void dnd_graphics_begin_triangles(void){if(!immediate_pass())return;if(immediate_batch){dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Triangle batch already active.");return;}immediate_batch=true;immediate_count=0;}
void dnd_graphics_color(float r,float g,float b){if(!immediate_pass())return;if(!isfinite(r)||!isfinite(g)||!isfinite(b)||r<0||r>1||g<0||g>1||b<0||b>1){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Color must be finite and normalized.");return;}immediate_color=rgba(r,g,b,1);}
void dnd_graphics_vertex(float x,float y,float z){if(!immediate_pass())return;if(!isfinite(x)||!isfinite(y)||!isfinite(z)){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Vertex must be finite.");return;}if(!immediate_batch){dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"No triangle batch.");return;}if(immediate_count==1536)immediate_flush();immediate_vertices[immediate_count++]=(DndDrawVertex){x,y,z,0,0,1,0,0,immediate_color};}
void dnd_graphics_end_triangles(void){if(!immediate_pass())return;if(!immediate_batch||immediate_count%3){dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION,"Incomplete triangle batch.");return;}immediate_flush();immediate_batch=false;}
void dnd_graphics_begin_overlay(void) {
    if(dnd_performance_visible()) {
        uint64_t now=ticks_to_microsecs(gettime());
        if(dnd_performance_connectivity_due(now)) {
            uint32_t ip=0,mask=0,gateway=0;int dhcp=0;
            int network=dnd_np_snapshot(&ip,&mask,&gateway,&dhcp);
            if(network!=-1)dnd_performance_network(network,dhcp!=0,ip,mask,gateway);
            bool configured=network==1 || dnd_performance_stats()->network_state==2;
            dnd_performance_connectivity_sampled(now,configured?dnd_np_link():-1);
            dnd_debug_heartbeat_tick(now,network,dnd_performance_stats()->network_link);
        }
        if(dnd_performance_memory_due(now)) {
            struct mallinfo info=mallinfo();uint32_t total=SYS_GetPhysicalMem1Size(),aram_used=0,aram_total=0;
            uint64_t free_bytes=(uint64_t)SYS_GetArena1Size()+info.fordblks;
            bool aram_available=dnd_aram_memory_usage(&aram_used,&aram_total);
            dnd_performance_memory(now,free_bytes<total?total-(uint32_t)free_bytes:0,total,aram_available,aram_used,aram_total);
        }
    }
    dnd_draw_backend_2d(dnd_graphics_width(),dnd_graphics_height());
}
void dnd_graphics_overlay_rect(float x,float y,float w,float h,float r,float g,float b,float a) {
    DndDrawVertex v[6]={{x,y,0,0,0,1,0,0,0},{x+w,y,0,0,0,1,0,0,0},{x+w,y+h,0,0,0,1,0,0,0},{x,y,0,0,0,1,0,0,0},{x+w,y+h,0,0,0,1,0,0,0},{x,y+h,0,0,0,1,0,0,0}};
    DndDrawMaterial mat={rgba(r,g,b,a),1|8|16,0,NULL,NULL};for(int i=0;i<6;i++)v[i].color=0xffffffffu;
    float world[16];identity(world);dnd_draw_backend_vertices(v,6,GX_TRIANGLES,NULL,&mat,world);
}
static void glyph_flush(void) {
    if(!glyph_vertices)return;
    DndDrawMaterial mat={0xffffffffu,1|8|16,0,NULL,NULL};float world[16];identity(world);
    dnd_draw_backend_vertices(immediate_vertices,glyph_vertices,GX_TRIANGLES,NULL,&mat,world);glyph_vertices=0;
}
void dnd_graphics_overlay_glyph_begin(unsigned quads,unsigned char shade){glyph_quads=quads;glyph_vertices=0;immediate_color=(uint32_t)shade<<24|(uint32_t)shade<<16|(uint32_t)shade<<8|255;}
void dnd_graphics_overlay_glyph_quad(float x,float y,float size) {
    if(glyph_vertices+6>1536)glyph_flush();
    DndDrawVertex *v=immediate_vertices+glyph_vertices;
    v[0]=(DndDrawVertex){x,y,0,0,0,1,0,0,immediate_color};v[1]=v[0];v[1].x+=size;v[2]=v[1];v[2].y+=size;
    v[3]=v[0];v[4]=v[2];v[5]=v[0];v[5].y+=size;glyph_vertices+=6;if(glyph_quads)glyph_quads--;
}
void dnd_graphics_overlay_glyph_end(void){glyph_flush();}
void dnd_graphics_end_frame(void) {
    GX_SetDispCopyYScale(GX_GetYScaleFactor(mode->efbHeight,mode->xfbHeight));GX_SetDispCopySrc(0,0,mode->fbWidth,mode->efbHeight);GX_SetDispCopyDst(mode->fbWidth,mode->xfbHeight);
    GX_SetCopyFilter(mode->aa,mode->sample_pattern,GX_TRUE,mode->vfilter);
    GX_SetZMode(GX_TRUE,GX_LEQUAL,GX_TRUE);GX_SetColorUpdate(GX_TRUE);GX_CopyDisp(xfb[fb],GX_TRUE);GX_DrawDone();
    VIDEO_SetNextFramebuffer(xfb[fb]);if(first_frame){first_frame=false;VIDEO_SetBlack(FALSE);}VIDEO_Flush();VIDEO_WaitVSync();fb^=1;
    if(dnd_performance_visible())dnd_performance_presented(ticks_to_microsecs(gettime()));
}
void dnd_graphics_present(void){dnd_graphics_begin_overlay();dnd_console_render();dnd_graphics_end_frame();}
void dnd_graphics_model_begin(void){dnd_graphics_begin_frame(0.025f,0.035f,0.06f,1);}
void dnd_graphics_model_end(void){dnd_graphics_present();}
#else
#ifndef DND_GRAPHICS_TEST
bool dnd_draw_backend_immediate_ready(void){return true;}
void dnd_draw_backend_external(void){}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_abort(void){}
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){(void)data;(void)bytes;(void)invalidate;}
void dnd_draw_backend_viewport(int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_scissor(int x,int y,int w,int h){(void)x;(void)y;(void)w;(void)h;}
void dnd_draw_backend_camera(const float *view,const float *projection){(void)view;(void)projection;}
void dnd_draw_backend_2d(int w,int h){(void)w;(void)h;}
void dnd_draw_backend_tev(const DndDrawEffect *effect,bool texture_bound){(void)effect;(void)texture_bound;}
void dnd_draw_backend_vertices(const DndDrawVertex *v,int n,int primitive,const DndDrawTexture *t,const DndDrawMaterial *m,const float *world){(void)v;(void)n;(void)primitive;(void)t;(void)m;(void)world;}
void dnd_draw_backend_copy(const DndDrawTexture *t,int x,int y,int w,int h){(void)t;(void)x;(void)y;(void)w;(void)h;}
#endif
#endif
void dnd_draw_set_camera(const float *view,const float *projection){dnd_draw_backend_camera(view,projection);}
