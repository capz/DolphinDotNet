#ifndef DND_EFFECT_H
#define DND_EFFECT_H
/* DNEF v1: 32-byte LE header, four 36-byte name/RGBA slots, 8 bytes per stage. */
typedef struct { unsigned char a,b,c,d,op,dest,konst,reserved; } DndTevStage;
typedef struct { int count,texture,uniforms; unsigned colors[4]; DndTevStage stages[16]; } DndDrawEffect;
#endif
