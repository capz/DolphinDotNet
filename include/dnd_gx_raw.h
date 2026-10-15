/* Generated raw libogc2 bridge. NativeAddress parameters are borrowed. */
#ifndef DND_GX_RAW_H
#define DND_GX_RAW_H
#include <stdint.h>
#include "dnd_draw.h"
void dnd_Raw_GX_AbortFrame(void);
#define DND_RAW_GX_ABORTFRAME 0
void dnd_Raw_GX_AdjustForOverscan(uint64_t a0, uint64_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_ADJUSTFOROVERSCAN 1
void dnd_Raw_GX_Begin(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_BEGIN 2
void dnd_Raw_GX_BeginDispList(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_BEGINDISPLIST 3
void dnd_Raw_GX_CallDispList(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_CALLDISPLIST 4
void dnd_Raw_GX_ClearBoundingBox(void);
#define DND_RAW_GX_CLEARBOUNDINGBOX 5
void dnd_Raw_GX_ClearGPMetric(void);
#define DND_RAW_GX_CLEARGPMETRIC 6
void dnd_Raw_GX_ClearPixMetric(void);
#define DND_RAW_GX_CLEARPIXMETRIC 7
void dnd_Raw_GX_ClearVCacheMetric(void);
#define DND_RAW_GX_CLEARVCACHEMETRIC 8
void dnd_Raw_GX_ClearVtxDesc(void);
#define DND_RAW_GX_CLEARVTXDESC 9
void dnd_Raw_GX_Color1u16(uint32_t a0);
#define DND_RAW_GX_COLOR1U16 10
void dnd_Raw_GX_Color1u32(uint32_t a0);
#define DND_RAW_GX_COLOR1U32 11
void dnd_Raw_GX_Color1x16(uint32_t a0);
#define DND_RAW_GX_COLOR1X16 12
void dnd_Raw_GX_Color1x8(uint32_t a0);
#define DND_RAW_GX_COLOR1X8 13
void dnd_Raw_GX_Color3f32(float a0, float a1, float a2);
#define DND_RAW_GX_COLOR3F32 14
void dnd_Raw_GX_Color3u8(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_COLOR3U8 15
void dnd_Raw_GX_Color4u8(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_COLOR4U8 16
void dnd_Raw_GX_CopyDisp(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_COPYDISP 17
void dnd_Raw_GX_CopyTex(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_COPYTEX 18
void dnd_Raw_GX_DisableBreakPt(void);
#define DND_RAW_GX_DISABLEBREAKPT 19
void dnd_Raw_GX_DrawDone(void);
#define DND_RAW_GX_DRAWDONE 20
void dnd_Raw_GX_EnableBreakPt(uint64_t a0);
#define DND_RAW_GX_ENABLEBREAKPT 21
void dnd_Raw_GX_EnableTexOffsets(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_ENABLETEXOFFSETS 22
void dnd_Raw_GX_End(void);
#define DND_RAW_GX_END 23
uint32_t dnd_Raw_GX_EndDispList(void);
#define DND_RAW_GX_ENDDISPLIST 24
void dnd_Raw_GX_FastBegin(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_FASTBEGIN 25
void dnd_Raw_GX_FastCallDispList(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_FASTCALLDISPLIST 26
void dnd_Raw_GX_Flush(void);
#define DND_RAW_GX_FLUSH 27
void dnd_Raw_GX_GetCPUFifo(uint64_t a0);
#define DND_RAW_GX_GETCPUFIFO 28
uint32_t dnd_Raw_GX_GetCurrentGXThread(void);
#define DND_RAW_GX_GETCURRENTGXTHREAD 29
uint32_t dnd_Raw_GX_GetDrawSync(void);
#define DND_RAW_GX_GETDRAWSYNC 30
uint64_t dnd_Raw_GX_GetFifoBase(uint64_t a0);
#define DND_RAW_GX_GETFIFOBASE 31
uint32_t dnd_Raw_GX_GetFifoCount(uint64_t a0);
#define DND_RAW_GX_GETFIFOCOUNT 32
void dnd_Raw_GX_GetFifoPtrs(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GX_GETFIFOPTRS 33
uint32_t dnd_Raw_GX_GetFifoSize(uint64_t a0);
#define DND_RAW_GX_GETFIFOSIZE 34
uint32_t dnd_Raw_GX_GetFifoWrap(uint64_t a0);
#define DND_RAW_GX_GETFIFOWRAP 35
void dnd_Raw_GX_GetGPFifo(uint64_t a0);
#define DND_RAW_GX_GETGPFIFO 36
void dnd_Raw_GX_GetGPStatus(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4);
#define DND_RAW_GX_GETGPSTATUS 37
uint32_t dnd_Raw_GX_GetNumXfbLines(uint32_t a0, float a1);
#define DND_RAW_GX_GETNUMXFBLINES 38
uint32_t dnd_Raw_GX_GetOverflowCount(void);
#define DND_RAW_GX_GETOVERFLOWCOUNT 39
void dnd_Raw_GX_GetScissor(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3);
#define DND_RAW_GX_GETSCISSOR 40
uint32_t dnd_Raw_GX_GetTexBufferSize(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_GETTEXBUFFERSIZE 41
void dnd_Raw_GX_GetTexObjAll(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, uint64_t a7);
#define DND_RAW_GX_GETTEXOBJALL 42
uint32_t dnd_Raw_GX_GetTexObjBiasClamp(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJBIASCLAMP 43
uint64_t dnd_Raw_GX_GetTexObjData(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJDATA 44
uint32_t dnd_Raw_GX_GetTexObjEdgeLOD(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJEDGELOD 45
uint32_t dnd_Raw_GX_GetTexObjFmt(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJFMT 46
uint32_t dnd_Raw_GX_GetTexObjHeight(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJHEIGHT 47
void dnd_Raw_GX_GetTexObjLODAll(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, uint64_t a7, uint64_t a8);
#define DND_RAW_GX_GETTEXOBJLODALL 48
float dnd_Raw_GX_GetTexObjLODBias(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJLODBIAS 49
uint32_t dnd_Raw_GX_GetTexObjMagFilt(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJMAGFILT 50
uint32_t dnd_Raw_GX_GetTexObjMaxAniso(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJMAXANISO 51
float dnd_Raw_GX_GetTexObjMaxLOD(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJMAXLOD 52
uint32_t dnd_Raw_GX_GetTexObjMinFilt(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJMINFILT 53
float dnd_Raw_GX_GetTexObjMinLOD(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJMINLOD 54
uint32_t dnd_Raw_GX_GetTexObjMipMap(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJMIPMAP 55
uint32_t dnd_Raw_GX_GetTexObjTlut(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJTLUT 56
uint64_t dnd_Raw_GX_GetTexObjUserData(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJUSERDATA 57
uint32_t dnd_Raw_GX_GetTexObjWidth(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJWIDTH 58
uint32_t dnd_Raw_GX_GetTexObjWrapS(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJWRAPS 59
uint32_t dnd_Raw_GX_GetTexObjWrapT(uint64_t a0);
#define DND_RAW_GX_GETTEXOBJWRAPT 60
void dnd_Raw_GX_GetTlutObjAll(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3);
#define DND_RAW_GX_GETTLUTOBJALL 61
uint64_t dnd_Raw_GX_GetTlutObjData(uint64_t a0);
#define DND_RAW_GX_GETTLUTOBJDATA 62
uint32_t dnd_Raw_GX_GetTlutObjFmt(uint64_t a0);
#define DND_RAW_GX_GETTLUTOBJFMT 63
uint32_t dnd_Raw_GX_GetTlutObjNumEntries(uint64_t a0);
#define DND_RAW_GX_GETTLUTOBJNUMENTRIES 64
void dnd_Raw_GX_GetVtxDescv(uint64_t a0);
#define DND_RAW_GX_GETVTXDESCV 65
float dnd_Raw_GX_GetYScaleFactor(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_GETYSCALEFACTOR 66
uint64_t dnd_Raw_GX_Init(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_INIT 67
void dnd_Raw_GX_InitFifoBase(uint64_t a0, uint64_t a1, uint32_t a2);
#define DND_RAW_GX_INITFIFOBASE 68
void dnd_Raw_GX_InitFifoLimits(uint64_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_INITFIFOLIMITS 69
void dnd_Raw_GX_InitFifoPtrs(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GX_INITFIFOPTRS 70
void dnd_Raw_GX_InitFogAdjTable(uint64_t a0, uint32_t a1, uint64_t a2);
#define DND_RAW_GX_INITFOGADJTABLE 71
void dnd_Raw_GX_InitLightAttn(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6);
#define DND_RAW_GX_INITLIGHTATTN 72
void dnd_Raw_GX_InitLightAttnA(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GX_INITLIGHTATTNA 73
void dnd_Raw_GX_InitLightAttnK(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GX_INITLIGHTATTNK 74
void dnd_Raw_GX_InitLightColor(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_INITLIGHTCOLOR 75
void dnd_Raw_GX_InitLightDir(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GX_INITLIGHTDIR 76
void dnd_Raw_GX_InitLightDirv(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_INITLIGHTDIRV 77
void dnd_Raw_GX_InitLightDistAttn(uint64_t a0, float a1, float a2, uint32_t a3);
#define DND_RAW_GX_INITLIGHTDISTATTN 78
void dnd_Raw_GX_InitLightPos(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GX_INITLIGHTPOS 79
void dnd_Raw_GX_InitLightPosv(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_INITLIGHTPOSV 80
void dnd_Raw_GX_InitLightShininess(uint64_t a0, float a1);
#define DND_RAW_GX_INITLIGHTSHININESS 81
void dnd_Raw_GX_InitLightSpot(uint64_t a0, float a1, uint32_t a2);
#define DND_RAW_GX_INITLIGHTSPOT 82
void dnd_Raw_GX_InitSpecularDir(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GX_INITSPECULARDIR 83
void dnd_Raw_GX_InitSpecularDirHA(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6);
#define DND_RAW_GX_INITSPECULARDIRHA 84
void dnd_Raw_GX_InitSpecularDirHAv(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GX_INITSPECULARDIRHAV 85
void dnd_Raw_GX_InitSpecularDirv(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_INITSPECULARDIRV 86
void dnd_Raw_GX_InitTexCacheRegion(uint64_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5);
#define DND_RAW_GX_INITTEXCACHEREGION 87
void dnd_Raw_GX_InitTexObj(uint64_t a0, uint64_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5, uint32_t a6, uint32_t a7);
#define DND_RAW_GX_INITTEXOBJ 88
void dnd_Raw_GX_InitTexObjBiasClamp(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_INITTEXOBJBIASCLAMP 89
void dnd_Raw_GX_InitTexObjCI(uint64_t a0, uint64_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5, uint32_t a6, uint32_t a7, uint32_t a8);
#define DND_RAW_GX_INITTEXOBJCI 90
void dnd_Raw_GX_InitTexObjData(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_INITTEXOBJDATA 91
void dnd_Raw_GX_InitTexObjEdgeLOD(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_INITTEXOBJEDGELOD 92
void dnd_Raw_GX_InitTexObjFilterMode(uint64_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_INITTEXOBJFILTERMODE 93
void dnd_Raw_GX_InitTexObjLOD(uint64_t a0, uint32_t a1, uint32_t a2, float a3, float a4, float a5, uint32_t a6, uint32_t a7, uint32_t a8);
#define DND_RAW_GX_INITTEXOBJLOD 94
void dnd_Raw_GX_InitTexObjLODBias(uint64_t a0, float a1);
#define DND_RAW_GX_INITTEXOBJLODBIAS 95
void dnd_Raw_GX_InitTexObjMaxAniso(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_INITTEXOBJMAXANISO 96
void dnd_Raw_GX_InitTexObjMaxLOD(uint64_t a0, float a1);
#define DND_RAW_GX_INITTEXOBJMAXLOD 97
void dnd_Raw_GX_InitTexObjMinLOD(uint64_t a0, float a1);
#define DND_RAW_GX_INITTEXOBJMINLOD 98
void dnd_Raw_GX_InitTexObjTlut(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_INITTEXOBJTLUT 99
void dnd_Raw_GX_InitTexObjUserData(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_INITTEXOBJUSERDATA 100
void dnd_Raw_GX_InitTexObjWrapMode(uint64_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_INITTEXOBJWRAPMODE 101
void dnd_Raw_GX_InitTexPreloadRegion(uint64_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_INITTEXPRELOADREGION 102
void dnd_Raw_GX_InitTlutObj(uint64_t a0, uint64_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_INITTLUTOBJ 103
void dnd_Raw_GX_InitTlutObjData(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_INITTLUTOBJDATA 104
void dnd_Raw_GX_InitTlutRegion(uint64_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_INITTLUTREGION 105
void dnd_Raw_GX_InitXfRasMetric(void);
#define DND_RAW_GX_INITXFRASMETRIC 106
void dnd_Raw_GX_InvVtxCache(void);
#define DND_RAW_GX_INVVTXCACHE 107
void dnd_Raw_GX_InvalidateTexAll(void);
#define DND_RAW_GX_INVALIDATETEXALL 108
void dnd_Raw_GX_InvalidateTexRegion(uint64_t a0);
#define DND_RAW_GX_INVALIDATETEXREGION 109
void dnd_Raw_GX_LoadLightObj(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADLIGHTOBJ 110
void dnd_Raw_GX_LoadLightObjIdx(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_LOADLIGHTOBJIDX 111
void dnd_Raw_GX_LoadNrmMtxIdx3x3(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_LOADNRMMTXIDX3X3 112
void dnd_Raw_GX_LoadNrmMtxImm(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADNRMMTXIMM 113
void dnd_Raw_GX_LoadNrmMtxImm3x3(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADNRMMTXIMM3X3 114
void dnd_Raw_GX_LoadPosMtxIdx(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_LOADPOSMTXIDX 115
void dnd_Raw_GX_LoadPosMtxImm(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADPOSMTXIMM 116
void dnd_Raw_GX_LoadProjectionMtx(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADPROJECTIONMTX 117
void dnd_Raw_GX_LoadTexMtxIdx(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_LOADTEXMTXIDX 118
void dnd_Raw_GX_LoadTexMtxImm(uint64_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_LOADTEXMTXIMM 119
void dnd_Raw_GX_LoadTexObj(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADTEXOBJ 120
void dnd_Raw_GX_LoadTexObjPreloaded(uint64_t a0, uint64_t a1, uint32_t a2);
#define DND_RAW_GX_LOADTEXOBJPRELOADED 121
void dnd_Raw_GX_LoadTlut(uint64_t a0, uint32_t a1);
#define DND_RAW_GX_LOADTLUT 122
void dnd_Raw_GX_MatrixIndex1x8(uint32_t a0);
#define DND_RAW_GX_MATRIXINDEX1X8 123
void dnd_Raw_GX_Normal1x16(uint32_t a0);
#define DND_RAW_GX_NORMAL1X16 124
void dnd_Raw_GX_Normal1x8(uint32_t a0);
#define DND_RAW_GX_NORMAL1X8 125
void dnd_Raw_GX_Normal3f32(float a0, float a1, float a2);
#define DND_RAW_GX_NORMAL3F32 126
void dnd_Raw_GX_Normal3s16(int32_t a0, int32_t a1, int32_t a2);
#define DND_RAW_GX_NORMAL3S16 127
void dnd_Raw_GX_Normal3s8(int32_t a0, int32_t a1, int32_t a2);
#define DND_RAW_GX_NORMAL3S8 128
void dnd_Raw_GX_PeekARGB(uint32_t a0, uint32_t a1, uint64_t a2);
#define DND_RAW_GX_PEEKARGB 129
void dnd_Raw_GX_PeekZ(uint32_t a0, uint32_t a1, uint64_t a2);
#define DND_RAW_GX_PEEKZ 130
void dnd_Raw_GX_PixModeSync(void);
#define DND_RAW_GX_PIXMODESYNC 131
void dnd_Raw_GX_PokeARGB(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_POKEARGB 132
void dnd_Raw_GX_PokeAlphaMode(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_POKEALPHAMODE 133
void dnd_Raw_GX_PokeAlphaRead(uint32_t a0);
#define DND_RAW_GX_POKEALPHAREAD 134
void dnd_Raw_GX_PokeAlphaUpdate(uint32_t a0);
#define DND_RAW_GX_POKEALPHAUPDATE 135
void dnd_Raw_GX_PokeBlendMode(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_POKEBLENDMODE 136
void dnd_Raw_GX_PokeColorUpdate(uint32_t a0);
#define DND_RAW_GX_POKECOLORUPDATE 137
void dnd_Raw_GX_PokeDither(uint32_t a0);
#define DND_RAW_GX_POKEDITHER 138
void dnd_Raw_GX_PokeDstAlpha(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_POKEDSTALPHA 139
void dnd_Raw_GX_PokeZ(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_POKEZ 140
void dnd_Raw_GX_PokeZMode(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_POKEZMODE 141
void dnd_Raw_GX_Position1x16(uint32_t a0);
#define DND_RAW_GX_POSITION1X16 142
void dnd_Raw_GX_Position1x8(uint32_t a0);
#define DND_RAW_GX_POSITION1X8 143
void dnd_Raw_GX_Position2f32(float a0, float a1);
#define DND_RAW_GX_POSITION2F32 144
void dnd_Raw_GX_Position2s16(int32_t a0, int32_t a1);
#define DND_RAW_GX_POSITION2S16 145
void dnd_Raw_GX_Position2s8(int32_t a0, int32_t a1);
#define DND_RAW_GX_POSITION2S8 146
void dnd_Raw_GX_Position2u16(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_POSITION2U16 147
void dnd_Raw_GX_Position2u8(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_POSITION2U8 148
void dnd_Raw_GX_Position3f32(float a0, float a1, float a2);
#define DND_RAW_GX_POSITION3F32 149
void dnd_Raw_GX_Position3s16(int32_t a0, int32_t a1, int32_t a2);
#define DND_RAW_GX_POSITION3S16 150
void dnd_Raw_GX_Position3s8(int32_t a0, int32_t a1, int32_t a2);
#define DND_RAW_GX_POSITION3S8 151
void dnd_Raw_GX_Position3u16(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_POSITION3U16 152
void dnd_Raw_GX_Position3u8(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_POSITION3U8 153
void dnd_Raw_GX_PreloadEntireTexture(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_PRELOADENTIRETEXTURE 154
void dnd_Raw_GX_ReadBoundingBox(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3);
#define DND_RAW_GX_READBOUNDINGBOX 155
uint32_t dnd_Raw_GX_ReadClksPerVtx(void);
#define DND_RAW_GX_READCLKSPERVTX 156
void dnd_Raw_GX_ReadGPMetric(uint64_t a0, uint64_t a1);
#define DND_RAW_GX_READGPMETRIC 157
void dnd_Raw_GX_ReadPixMetric(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5);
#define DND_RAW_GX_READPIXMETRIC 158
void dnd_Raw_GX_ReadVCacheMetric(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GX_READVCACHEMETRIC 159
void dnd_Raw_GX_ReadXfRasMetric(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3);
#define DND_RAW_GX_READXFRASMETRIC 160
uint64_t dnd_Raw_GX_RedirectWriteGatherPipe(uint64_t a0);
#define DND_RAW_GX_REDIRECTWRITEGATHERPIPE 161
uint32_t dnd_Raw_GX_ResetOverflowCount(void);
#define DND_RAW_GX_RESETOVERFLOWCOUNT 162
void dnd_Raw_GX_RestoreWriteGatherPipe(void);
#define DND_RAW_GX_RESTOREWRITEGATHERPIPE 163
void dnd_Raw_GX_SetAlphaCompare(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_SETALPHACOMPARE 164
void dnd_Raw_GX_SetAlphaUpdate(uint32_t a0);
#define DND_RAW_GX_SETALPHAUPDATE 165
void dnd_Raw_GX_SetArray(uint32_t a0, uint64_t a1, uint32_t a2);
#define DND_RAW_GX_SETARRAY 166
void dnd_Raw_GX_SetBlendMode(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETBLENDMODE 167
uint64_t dnd_Raw_GX_SetBreakPtCallback(uint64_t a0);
#define DND_RAW_GX_SETBREAKPTCALLBACK 168
void dnd_Raw_GX_SetCPUFifo(uint64_t a0);
#define DND_RAW_GX_SETCPUFIFO 169
void dnd_Raw_GX_SetChanAmbColor(int32_t a0, uint32_t a1);
#define DND_RAW_GX_SETCHANAMBCOLOR 170
void dnd_Raw_GX_SetChanCtrl(int32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5, uint32_t a6);
#define DND_RAW_GX_SETCHANCTRL 171
void dnd_Raw_GX_SetChanMatColor(int32_t a0, uint32_t a1);
#define DND_RAW_GX_SETCHANMATCOLOR 172
void dnd_Raw_GX_SetClipMode(uint32_t a0);
#define DND_RAW_GX_SETCLIPMODE 173
void dnd_Raw_GX_SetCoPlanar(uint32_t a0);
#define DND_RAW_GX_SETCOPLANAR 174
void dnd_Raw_GX_SetColorUpdate(uint32_t a0);
#define DND_RAW_GX_SETCOLORUPDATE 175
void dnd_Raw_GX_SetCopyClamp(uint32_t a0);
#define DND_RAW_GX_SETCOPYCLAMP 176
void dnd_Raw_GX_SetCopyClear(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETCOPYCLEAR 177
void dnd_Raw_GX_SetCopyFilter(uint32_t a0, uint64_t a1, uint32_t a2, uint64_t a3);
#define DND_RAW_GX_SETCOPYFILTER 178
void dnd_Raw_GX_SetCullMode(uint32_t a0);
#define DND_RAW_GX_SETCULLMODE 179
uint32_t dnd_Raw_GX_SetCurrentGXThread(void);
#define DND_RAW_GX_SETCURRENTGXTHREAD 180
void dnd_Raw_GX_SetCurrentMtx(uint32_t a0);
#define DND_RAW_GX_SETCURRENTMTX 181
void dnd_Raw_GX_SetDispCopyDst(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETDISPCOPYDST 182
void dnd_Raw_GX_SetDispCopyFrame2Field(uint32_t a0);
#define DND_RAW_GX_SETDISPCOPYFRAME2FIELD 183
void dnd_Raw_GX_SetDispCopyGamma(uint32_t a0);
#define DND_RAW_GX_SETDISPCOPYGAMMA 184
void dnd_Raw_GX_SetDispCopySrc(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETDISPCOPYSRC 185
uint32_t dnd_Raw_GX_SetDispCopyYScale(float a0);
#define DND_RAW_GX_SETDISPCOPYYSCALE 186
void dnd_Raw_GX_SetDither(uint32_t a0);
#define DND_RAW_GX_SETDITHER 187
void dnd_Raw_GX_SetDrawDone(void);
#define DND_RAW_GX_SETDRAWDONE 188
uint64_t dnd_Raw_GX_SetDrawDoneCallback(uint64_t a0);
#define DND_RAW_GX_SETDRAWDONECALLBACK 189
void dnd_Raw_GX_SetDrawSync(uint32_t a0);
#define DND_RAW_GX_SETDRAWSYNC 190
uint64_t dnd_Raw_GX_SetDrawSyncCallback(uint64_t a0);
#define DND_RAW_GX_SETDRAWSYNCCALLBACK 191
void dnd_Raw_GX_SetDstAlpha(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETDSTALPHA 192
void dnd_Raw_GX_SetFieldMask(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETFIELDMASK 193
void dnd_Raw_GX_SetFieldMode(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETFIELDMODE 194
void dnd_Raw_GX_SetFog(uint32_t a0, float a1, float a2, float a3, float a4, uint32_t a5);
#define DND_RAW_GX_SETFOG 195
void dnd_Raw_GX_SetFogColor(uint32_t a0);
#define DND_RAW_GX_SETFOGCOLOR 196
void dnd_Raw_GX_SetFogRangeAdj(uint32_t a0, uint32_t a1, uint64_t a2);
#define DND_RAW_GX_SETFOGRANGEADJ 197
void dnd_Raw_GX_SetGPFifo(uint64_t a0);
#define DND_RAW_GX_SETGPFIFO 198
void dnd_Raw_GX_SetGPMetric(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETGPMETRIC 199
void dnd_Raw_GX_SetIndTexCoordScale(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETINDTEXCOORDSCALE 200
void dnd_Raw_GX_SetIndTexMatrix(uint32_t a0, uint64_t a1, int32_t a2);
#define DND_RAW_GX_SETINDTEXMATRIX 201
void dnd_Raw_GX_SetIndTexOrder(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETINDTEXORDER 202
void dnd_Raw_GX_SetLineWidth(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETLINEWIDTH 203
void dnd_Raw_GX_SetMisc(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETMISC 204
void dnd_Raw_GX_SetNumChans(uint32_t a0);
#define DND_RAW_GX_SETNUMCHANS 205
void dnd_Raw_GX_SetNumIndStages(uint32_t a0);
#define DND_RAW_GX_SETNUMINDSTAGES 206
void dnd_Raw_GX_SetNumTevStages(uint32_t a0);
#define DND_RAW_GX_SETNUMTEVSTAGES 207
void dnd_Raw_GX_SetNumTexGens(uint32_t a0);
#define DND_RAW_GX_SETNUMTEXGENS 208
void dnd_Raw_GX_SetPixelFmt(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETPIXELFMT 209
void dnd_Raw_GX_SetPointSize(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETPOINTSIZE 210
void dnd_Raw_GX_SetScissor(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETSCISSOR 211
void dnd_Raw_GX_SetScissorBoxOffset(int32_t a0, int32_t a1);
#define DND_RAW_GX_SETSCISSORBOXOFFSET 212
void dnd_Raw_GX_SetTevAlphaIn(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_SETTEVALPHAIN 213
void dnd_Raw_GX_SetTevAlphaOp(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5);
#define DND_RAW_GX_SETTEVALPHAOP 214
void dnd_Raw_GX_SetTevColor(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETTEVCOLOR 215
void dnd_Raw_GX_SetTevColorIn(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_SETTEVCOLORIN 216
void dnd_Raw_GX_SetTevColorOp(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5);
#define DND_RAW_GX_SETTEVCOLOROP 217
void dnd_Raw_GX_SetTevColorS10(uint32_t a0, uint64_t a1);
#define DND_RAW_GX_SETTEVCOLORS10 218
void dnd_Raw_GX_SetTevDirect(uint32_t a0);
#define DND_RAW_GX_SETTEVDIRECT 219
void dnd_Raw_GX_SetTevIndBumpST(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETTEVINDBUMPST 220
void dnd_Raw_GX_SetTevIndBumpXYZ(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETTEVINDBUMPXYZ 221
void dnd_Raw_GX_SetTevIndRepeat(uint32_t a0);
#define DND_RAW_GX_SETTEVINDREPEAT 222
void dnd_Raw_GX_SetTevIndTile(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5, uint32_t a6, uint32_t a7, uint32_t a8, uint32_t a9);
#define DND_RAW_GX_SETTEVINDTILE 223
void dnd_Raw_GX_SetTevIndWarp(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_SETTEVINDWARP 224
void dnd_Raw_GX_SetTevIndirect(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5, uint32_t a6, uint32_t a7, uint32_t a8, uint32_t a9);
#define DND_RAW_GX_SETTEVINDIRECT 225
void dnd_Raw_GX_SetTevKAlphaSel(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETTEVKALPHASEL 226
void dnd_Raw_GX_SetTevKColor(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETTEVKCOLOR 227
void dnd_Raw_GX_SetTevKColorS10(uint32_t a0, uint64_t a1);
#define DND_RAW_GX_SETTEVKCOLORS10 228
void dnd_Raw_GX_SetTevKColorSel(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETTEVKCOLORSEL 229
void dnd_Raw_GX_SetTevOp(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETTEVOP 230
void dnd_Raw_GX_SetTevOrder(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETTEVORDER 231
void dnd_Raw_GX_SetTevSwapMode(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETTEVSWAPMODE 232
void dnd_Raw_GX_SetTevSwapModeTable(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_SETTEVSWAPMODETABLE 233
void dnd_Raw_GX_SetTexCoordBias(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETTEXCOORDBIAS 234
void dnd_Raw_GX_SetTexCoordCylWrap(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETTEXCOORDCYLWRAP 235
void dnd_Raw_GX_SetTexCoordGen(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETTEXCOORDGEN 236
void dnd_Raw_GX_SetTexCoordGen2(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4, uint32_t a5);
#define DND_RAW_GX_SETTEXCOORDGEN2 237
void dnd_Raw_GX_SetTexCoordScaleManually(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETTEXCOORDSCALEMANUALLY 238
void dnd_Raw_GX_SetTexCopyDst(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETTEXCOPYDST 239
void dnd_Raw_GX_SetTexCopySrc(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_GX_SETTEXCOPYSRC 240
uint64_t dnd_Raw_GX_SetTexRegionCallback(uint64_t a0);
#define DND_RAW_GX_SETTEXREGIONCALLBACK 241
uint64_t dnd_Raw_GX_SetTlutRegionCallback(uint64_t a0);
#define DND_RAW_GX_SETTLUTREGIONCALLBACK 242
void dnd_Raw_GX_SetVCacheMetric(uint32_t a0);
#define DND_RAW_GX_SETVCACHEMETRIC 243
void dnd_Raw_GX_SetViewport(float a0, float a1, float a2, float a3, float a4, float a5);
#define DND_RAW_GX_SETVIEWPORT 244
void dnd_Raw_GX_SetViewportJitter(float a0, float a1, float a2, float a3, float a4, float a5, uint32_t a6);
#define DND_RAW_GX_SETVIEWPORTJITTER 245
void dnd_Raw_GX_SetVtxAttrFmt(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3, uint32_t a4);
#define DND_RAW_GX_SETVTXATTRFMT 246
void dnd_Raw_GX_SetVtxAttrFmtv(uint32_t a0, uint64_t a1);
#define DND_RAW_GX_SETVTXATTRFMTV 247
void dnd_Raw_GX_SetVtxDesc(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_SETVTXDESC 248
void dnd_Raw_GX_SetVtxDescv(uint64_t a0);
#define DND_RAW_GX_SETVTXDESCV 249
void dnd_Raw_GX_SetZCompLoc(uint32_t a0);
#define DND_RAW_GX_SETZCOMPLOC 250
void dnd_Raw_GX_SetZMode(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETZMODE 251
void dnd_Raw_GX_SetZTexture(uint32_t a0, uint32_t a1, uint32_t a2);
#define DND_RAW_GX_SETZTEXTURE 252
void dnd_Raw_GX_TexCoord1f32(float a0);
#define DND_RAW_GX_TEXCOORD1F32 253
void dnd_Raw_GX_TexCoord1s16(int32_t a0);
#define DND_RAW_GX_TEXCOORD1S16 254
void dnd_Raw_GX_TexCoord1s8(int32_t a0);
#define DND_RAW_GX_TEXCOORD1S8 255
void dnd_Raw_GX_TexCoord1u16(uint32_t a0);
#define DND_RAW_GX_TEXCOORD1U16 256
void dnd_Raw_GX_TexCoord1u8(uint32_t a0);
#define DND_RAW_GX_TEXCOORD1U8 257
void dnd_Raw_GX_TexCoord1x16(uint32_t a0);
#define DND_RAW_GX_TEXCOORD1X16 258
void dnd_Raw_GX_TexCoord1x8(uint32_t a0);
#define DND_RAW_GX_TEXCOORD1X8 259
void dnd_Raw_GX_TexCoord2f32(float a0, float a1);
#define DND_RAW_GX_TEXCOORD2F32 260
void dnd_Raw_GX_TexCoord2s16(int32_t a0, int32_t a1);
#define DND_RAW_GX_TEXCOORD2S16 261
void dnd_Raw_GX_TexCoord2s8(int32_t a0, int32_t a1);
#define DND_RAW_GX_TEXCOORD2S8 262
void dnd_Raw_GX_TexCoord2u16(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_TEXCOORD2U16 263
void dnd_Raw_GX_TexCoord2u8(uint32_t a0, uint32_t a1);
#define DND_RAW_GX_TEXCOORD2U8 264
void dnd_Raw_GX_TexModeSync(void);
#define DND_RAW_GX_TEXMODESYNC 265
void dnd_Raw_GX_WaitDrawDone(void);
#define DND_RAW_GX_WAITDRAWDONE 266
void dnd_Raw_VIDEO_ClearFrameBuffer(uint64_t a0, uint64_t a1, uint32_t a2);
#define DND_RAW_VIDEO_CLEARFRAMEBUFFER 267
void dnd_Raw_VIDEO_Configure(uint64_t a0);
#define DND_RAW_VIDEO_CONFIGURE 268
void dnd_Raw_VIDEO_ConfigurePan(uint32_t a0, uint32_t a1, uint32_t a2, uint32_t a3);
#define DND_RAW_VIDEO_CONFIGUREPAN 269
uint64_t dnd_Raw_VIDEO_DisablePositionInterrupt(void);
#define DND_RAW_VIDEO_DISABLEPOSITIONINTERRUPT 270
void dnd_Raw_VIDEO_EnablePositionInterrupt(int32_t a0, int32_t a1, uint64_t a2);
#define DND_RAW_VIDEO_ENABLEPOSITIONINTERRUPT 271
void dnd_Raw_VIDEO_Flush(void);
#define DND_RAW_VIDEO_FLUSH 272
void dnd_Raw_VIDEO_GetAdjustingValues(uint64_t a0, uint64_t a1);
#define DND_RAW_VIDEO_GETADJUSTINGVALUES 273
float dnd_Raw_VIDEO_GetAspectRatio(void);
#define DND_RAW_VIDEO_GETASPECTRATIO 274
uint64_t dnd_Raw_VIDEO_GetCurrentFramebuffer(void);
#define DND_RAW_VIDEO_GETCURRENTFRAMEBUFFER 275
uint32_t dnd_Raw_VIDEO_GetCurrentLine(void);
#define DND_RAW_VIDEO_GETCURRENTLINE 276
uint32_t dnd_Raw_VIDEO_GetCurrentTvMode(void);
#define DND_RAW_VIDEO_GETCURRENTTVMODE 277
void dnd_Raw_VIDEO_GetFrameBufferPan(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4);
#define DND_RAW_VIDEO_GETFRAMEBUFFERPAN 278
uint32_t dnd_Raw_VIDEO_GetFrameBufferSize(uint64_t a0);
#define DND_RAW_VIDEO_GETFRAMEBUFFERSIZE 279
uint32_t dnd_Raw_VIDEO_GetNextField(void);
#define DND_RAW_VIDEO_GETNEXTFIELD 280
uint64_t dnd_Raw_VIDEO_GetNextFramebuffer(void);
#define DND_RAW_VIDEO_GETNEXTFRAMEBUFFER 281
uint64_t dnd_Raw_VIDEO_GetPreferredMode(uint64_t a0);
#define DND_RAW_VIDEO_GETPREFERREDMODE 282
uint32_t dnd_Raw_VIDEO_GetRetraceCount(void);
#define DND_RAW_VIDEO_GETRETRACECOUNT 283
float dnd_Raw_VIDEO_GetRetraceRate(void);
#define DND_RAW_VIDEO_GETRETRACERATE 284
uint32_t dnd_Raw_VIDEO_GetScanMode(void);
#define DND_RAW_VIDEO_GETSCANMODE 285
uint32_t dnd_Raw_VIDEO_HaveComponentCable(void);
#define DND_RAW_VIDEO_HAVECOMPONENTCABLE 286
void dnd_Raw_VIDEO_Init(void);
#define DND_RAW_VIDEO_INIT 287
void dnd_Raw_VIDEO_Set3D(int32_t a0);
#define DND_RAW_VIDEO_SET3D 288
void dnd_Raw_VIDEO_SetAdjustingValues(int32_t a0, int32_t a1);
#define DND_RAW_VIDEO_SETADJUSTINGVALUES 289
void dnd_Raw_VIDEO_SetBlack(int32_t a0);
#define DND_RAW_VIDEO_SETBLACK 290
void dnd_Raw_VIDEO_SetNextFramebuffer(uint64_t a0);
#define DND_RAW_VIDEO_SETNEXTFRAMEBUFFER 291
void dnd_Raw_VIDEO_SetNextRightFramebuffer(uint64_t a0);
#define DND_RAW_VIDEO_SETNEXTRIGHTFRAMEBUFFER 292
uint64_t dnd_Raw_VIDEO_SetPostRetraceCallback(uint64_t a0);
#define DND_RAW_VIDEO_SETPOSTRETRACECALLBACK 293
uint64_t dnd_Raw_VIDEO_SetPreRetraceCallback(uint64_t a0);
#define DND_RAW_VIDEO_SETPRERETRACECALLBACK 294
void dnd_Raw_VIDEO_WaitForFlush(void);
#define DND_RAW_VIDEO_WAITFORFLUSH 295
uint32_t dnd_Raw_VIDEO_WaitForRetrace(uint32_t a0);
#define DND_RAW_VIDEO_WAITFORRETRACE 296
void dnd_Raw_VIDEO_WaitVSync(void);
#define DND_RAW_VIDEO_WAITVSYNC 297
void dnd_Raw_guFrustum(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6);
#define DND_RAW_GUFRUSTUM 298
void dnd_Raw_guLightFrustum(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6, float a7, float a8, float a9);
#define DND_RAW_GULIGHTFRUSTUM 299
void dnd_Raw_guLightOrtho(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6, float a7, float a8);
#define DND_RAW_GULIGHTORTHO 300
void dnd_Raw_guLightPerspective(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6);
#define DND_RAW_GULIGHTPERSPECTIVE 301
void dnd_Raw_guLookAt(uint64_t a0, uint64_t a1, uint64_t a2, uint64_t a3);
#define DND_RAW_GULOOKAT 302
void dnd_Raw_guMtx44Concat(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUMTX44CONCAT 303
void dnd_Raw_guMtx44Copy(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTX44COPY 304
void dnd_Raw_guMtx44Identity(uint64_t a0);
#define DND_RAW_GUMTX44IDENTITY 305
uint32_t dnd_Raw_guMtx44Inverse(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTX44INVERSE 306
void dnd_Raw_guMtx44RotAxisRad(uint64_t a0, uint64_t a1, float a2);
#define DND_RAW_GUMTX44ROTAXISRAD 307
void dnd_Raw_guMtx44RotRad(uint64_t a0, int32_t a1, float a2);
#define DND_RAW_GUMTX44ROTRAD 308
void dnd_Raw_guMtx44RotTrig(uint64_t a0, int32_t a1, float a2, float a3);
#define DND_RAW_GUMTX44ROTTRIG 309
void dnd_Raw_guMtx44Scale(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GUMTX44SCALE 310
void dnd_Raw_guMtx44ScaleApply(uint64_t a0, uint64_t a1, float a2, float a3, float a4);
#define DND_RAW_GUMTX44SCALEAPPLY 311
void dnd_Raw_guMtx44Trans(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GUMTX44TRANS 312
void dnd_Raw_guMtx44TransApply(uint64_t a0, uint64_t a1, float a2, float a3, float a4);
#define DND_RAW_GUMTX44TRANSAPPLY 313
void dnd_Raw_guMtx44Transpose(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTX44TRANSPOSE 314
void dnd_Raw_guMtxApplyScale(uint64_t a0, uint64_t a1, float a2, float a3, float a4);
#define DND_RAW_GUMTXAPPLYSCALE 315
void dnd_Raw_guMtxApplyTrans(uint64_t a0, uint64_t a1, float a2, float a3, float a4);
#define DND_RAW_GUMTXAPPLYTRANS 316
void dnd_Raw_guMtxConcat(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUMTXCONCAT 317
void dnd_Raw_guMtxCopy(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTXCOPY 318
void dnd_Raw_guMtxIdentity(uint64_t a0);
#define DND_RAW_GUMTXIDENTITY 319
uint32_t dnd_Raw_guMtxInvXpose(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTXINVXPOSE 320
uint32_t dnd_Raw_guMtxInverse(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTXINVERSE 321
void dnd_Raw_guMtxQuat(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTXQUAT 322
void dnd_Raw_guMtxReflect(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUMTXREFLECT 323
void dnd_Raw_guMtxRotAxisRad(uint64_t a0, uint64_t a1, float a2);
#define DND_RAW_GUMTXROTAXISRAD 324
void dnd_Raw_guMtxRotRad(uint64_t a0, int32_t a1, float a2);
#define DND_RAW_GUMTXROTRAD 325
void dnd_Raw_guMtxRotTrig(uint64_t a0, int32_t a1, float a2, float a3);
#define DND_RAW_GUMTXROTTRIG 326
void dnd_Raw_guMtxScale(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GUMTXSCALE 327
void dnd_Raw_guMtxScaleApply(uint64_t a0, uint64_t a1, float a2, float a3, float a4);
#define DND_RAW_GUMTXSCALEAPPLY 328
void dnd_Raw_guMtxTrans(uint64_t a0, float a1, float a2, float a3);
#define DND_RAW_GUMTXTRANS 329
void dnd_Raw_guMtxTransApply(uint64_t a0, uint64_t a1, float a2, float a3, float a4);
#define DND_RAW_GUMTXTRANSAPPLY 330
void dnd_Raw_guMtxTranspose(uint64_t a0, uint64_t a1);
#define DND_RAW_GUMTXTRANSPOSE 331
void dnd_Raw_guOrtho(uint64_t a0, float a1, float a2, float a3, float a4, float a5, float a6);
#define DND_RAW_GUORTHO 332
void dnd_Raw_guPerspective(uint64_t a0, float a1, float a2, float a3, float a4);
#define DND_RAW_GUPERSPECTIVE 333
void dnd_Raw_guQuatAdd(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUQUATADD 334
float dnd_Raw_guQuatDotProduct(uint64_t a0, uint64_t a1);
#define DND_RAW_GUQUATDOTPRODUCT 335
void dnd_Raw_guQuatInverse(uint64_t a0, uint64_t a1);
#define DND_RAW_GUQUATINVERSE 336
void dnd_Raw_guQuatMtx(uint64_t a0, uint64_t a1);
#define DND_RAW_GUQUATMTX 337
void dnd_Raw_guQuatMultiply(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUQUATMULTIPLY 338
void dnd_Raw_guQuatNormalize(uint64_t a0, uint64_t a1);
#define DND_RAW_GUQUATNORMALIZE 339
void dnd_Raw_guQuatScale(uint64_t a0, uint64_t a1, float a2);
#define DND_RAW_GUQUATSCALE 340
void dnd_Raw_guQuatSub(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUQUATSUB 341
void dnd_Raw_guVecAdd(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUVECADD 342
void dnd_Raw_guVecCross(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUVECCROSS 343
float dnd_Raw_guVecDotProduct(uint64_t a0, uint64_t a1);
#define DND_RAW_GUVECDOTPRODUCT 344
void dnd_Raw_guVecHalfAngle(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUVECHALFANGLE 345
void dnd_Raw_guVecMultiply(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUVECMULTIPLY 346
void dnd_Raw_guVecMultiplySR(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUVECMULTIPLYSR 347
void dnd_Raw_guVecNormalize(uint64_t a0, uint64_t a1);
#define DND_RAW_GUVECNORMALIZE 348
void dnd_Raw_guVecScale(uint64_t a0, uint64_t a1, float a2);
#define DND_RAW_GUVECSCALE 349
void dnd_Raw_guVecSub(uint64_t a0, uint64_t a1, uint64_t a2);
#define DND_RAW_GUVECSUB 350
#endif
