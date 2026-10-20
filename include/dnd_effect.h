#ifndef DND_EFFECT_H
#define DND_EFFECT_H
/* DNEF v1/v2: 32-byte LE header, four 36-byte name/RGBA slots, 8 bytes per stage.
 * v1 reserved=0, one texture. v2 reserved selects texture slot 0/1; texture is
 * a two-bit requirement mask. Final stage uses slot 0 to resolve base alpha. */
typedef struct { unsigned char a,b,c,d,op,dest,konst,reserved; } DndTevStage;
typedef struct { int count,texture,uniforms; unsigned colors[4]; DndTevStage stages[16]; } DndDrawEffect;
#endif
