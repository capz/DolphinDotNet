#ifndef MODEL_EFFECT_TEST_GCCORE_H
#define MODEL_EFFECT_TEST_GCCORE_H
#include <stdbool.h>
#include <stdint.h>
typedef uint8_t u8;typedef uint16_t u16;typedef uint32_t u32;typedef float f32;
typedef float Mtx[3][4];typedef float Mtx44[4][4];
typedef struct {u8 r,g,b,a;} GXColor;
typedef struct {void *data;int width,height,format,wrap_s,wrap_t,mips,min_filter,mag_filter;float max_lod;} GXTexObj;
#define GX_AF_NONE 2
#define GX_COLOR0 0
#define GX_ALPHA0 2
#define GX_ENABLE 1
#define GX_LIGHT0 1
#define GX_DF_CLAMP 2
void GX_SetNumIndStages(int count);
#define GX_ALWAYS 7
#define GX_ANISO_1 0
#define GX_AOP_AND 0
#define GX_BL_INVSRCALPHA 5
#define GX_BL_ONE 1
#define GX_BL_SRCALPHA 4
#define GX_BL_ZERO 0
#define GX_BM_BLEND 1
#define GX_BM_NONE 0
#define GX_CA_RASA 5
#define GX_CA_TEXA 4
#define GX_CA_ZERO 7
#define GX_CC_RASC 10
#define GX_CC_TEXC 8
#define GX_CC_ZERO 15
#define GX_CH_ALPHA 3
#define GX_CH_BLUE 2
#define GX_CH_GREEN 1
#define GX_CH_RED 0
#define GX_CLR_RGBA 1
#define GX_COLOR0A0 4
#define GX_CS_SCALE_1 0
#define GX_CULL_BACK 2
#define GX_CULL_FRONT 1
#define GX_CULL_NONE 0
#define GX_DF_NONE 0
#define GX_DIRECT 1
#define GX_DISABLE 0
#define GX_F32 4
#define GX_FALSE 0
#define GX_GEQUAL 6
#define GX_IDENTITY 60
#define GX_LEQUAL 3
#define GX_LIGHTNULL 0
#define GX_LINEAR 1
#define GX_LIN_MIP_LIN 5
#define GX_LO_CLEAR 0
#define GX_NEAR 0
#define GX_NEAR_MIP_NEAR 2
#define GX_PERSPECTIVE 0
#define GX_PNMTX0 0
#define GX_POS_XYZ 1
#define GX_RGBA8 5
#define GX_SRC_REG 0
#define GX_SRC_VTX 1
#define GX_TB_ZERO 0
#define GX_TEVPREV 0
#define GX_TEV_ADD 0
#define GX_TEV_KASEL_1 0
#define GX_TEV_KCSEL_1 0
#define GX_TEV_SWAP0 0
#define GX_TEXCOORD0 0
#define GX_TEXCOORD1 1
#define GX_TEXMAP1 1
#define GX_TEXCOORDNULL 255
#define GX_TEXMAP0 0
#define GX_TEXMAP_NULL 255
#define GX_TEX_ST 1
#define GX_TF_RGBA8 6
#define GX_TG_TEX0 4
#define GX_TG_MTX2x4 1
#define GX_TRIANGLES 144
#define GX_TRUE 1
#define GX_VA_CLR0 11
#define GX_VA_POS 9
#define GX_VA_TEX0 13
#define GX_VTXFMT0 0
void GX_LoadProjectionMtx(Mtx44 p,int kind);
void GX_LoadPosMtxImm(Mtx m,int slot);
void GX_SetCurrentMtx(int slot);
void GX_SetCullMode(int mode);
void GX_SetZMode(int enable,int compare,int write);
void GX_SetBlendMode(int mode,int source,int destination,int op);
void GX_SetAlphaCompare(int compare,int cutoff,int alpha_op,int other,int reference);
void GX_ClearVtxDesc(void);
void GX_SetVtxDesc(int attribute,int mode);
void GX_SetVtxAttrFmt(int format,int attribute,int components,int type,int frac);
void GX_SetNumChans(int count);
void GX_SetChanCtrl(int channel,int enable,int ambient,int material,int lights,int diffuse,int attenuation);
void GX_SetNumTexGens(int count);
void GX_InitTexObj(GXTexObj *t,void *data,u16 width,u16 height,int format,int wrap_s,int wrap_t,int mipmap);
void GX_InitTexObjLOD(GXTexObj *t,int min_filter,int mag_filter,float min_lod,float max_lod,float bias,int clamp,int edge,int aniso);
void GX_LoadTexObj(GXTexObj *t,int slot);
void GX_SetTexCoordGen(int coordinate,int kind,int source,int matrix);
void GX_Begin(int primitive,int format,u16 count);
void GX_End(void);
void GX_Position3f32(float x,float y,float z);
void GX_Color4u8(u8 r,u8 g,u8 b,u8 a);
void GX_TexCoord2f32(float u,float v);
void GX_SetTevSwapModeTable(int table,int r,int g,int b,int a);
void GX_SetTevColor(int slot,GXColor color);
void GX_SetTevKColor(int slot,GXColor color);
void GX_SetNumTevStages(int count);
void GX_SetTevDirect(int stage);
void GX_SetTevSwapMode(int stage,int raster,int texture);
void GX_SetTevOrder(int stage,int coordinate,int texture,int raster);
void GX_SetTevKColorSel(int stage,int select);
void GX_SetTevKAlphaSel(int stage,int select);
void GX_SetTevColorIn(int stage,int a,int b,int c,int d);
void GX_SetTevColorOp(int stage,int op,int bias,int scale,int clamp,int destination);
void GX_SetTevAlphaIn(int stage,int a,int b,int c,int d);
void GX_SetTevAlphaOp(int stage,int op,int bias,int scale,int clamp,int destination);
#endif
