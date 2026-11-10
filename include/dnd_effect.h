#ifndef DND_EFFECT_H
#define DND_EFFECT_H
/* DNEF v1-v3: 32-byte LE header, four 36-byte name/RGBA slots, 8 bytes per stage.
 * v1 reserved=0, one texture. v2 reserved selects texture 0/1.
 * v3 p[20]=indirect warp strength 1..255: texture 1 becomes an indirect map,
 * and stage reserved=2 selects TEXCOORD1 while sampling primary TEXMAP0.
 * Every v2/v3 program resolves primary alpha in its final TEX0 stage. */
typedef struct { unsigned char a,b,c,d,op,dest,konst,reserved; } DndTevStage;
typedef struct { int count,texture,uniforms,indirect_strength; unsigned colors[4]; DndTevStage stages[16]; } DndDrawEffect;
#endif
