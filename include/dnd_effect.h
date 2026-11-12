#ifndef DND_EFFECT_H
#define DND_EFFECT_H
/* DNEF v1-v3: 32-byte LE header, four 36-byte name/RGBA slots, 8 bytes per stage.
 * v1 reserved=0, one texture. v2 reserved selects texture 0/1.
 * v3 p[20]=indirect warp strength 1..255: texture 1 becomes an indirect map,
 * and stage reserved=2 selects TEXCOORD1 while sampling primary TEXMAP0.
 * Every v2/v3 program resolves primary alpha in its final TEX0 stage. */
typedef struct { unsigned char a,b,c,d,op,dest,konst,reserved; } DndTevStage;
/* v5 keeps the v4 envelope. RGB dest bit7 opts into the same clamp/scale/bias/
 * destination packing as alpha control; otherwise the old 0..3 dest is used.
 * v5 adds GX alpha-replicated RGB selectors and optional subtract arithmetic.
 * Header byte22 enables custom blend; bytes29/30 store GX source/destination
 * blend factors. Byte31 stays zero. These bytes stay reserved/zero in v4. */
/* v4 interleaves each RGB stage with an 8-byte alpha record. control: explicit
 * bit 7, clamp bit 6, scale bits 4..5, bias bits 2..3, destination bits 0..1.
 * All-zero alpha records retain legacy raster * bound texture opacity.
 * Header bytes 20/21: warp gain / compare enabled. Bytes 24..28: cmp/ref/op/cmp/ref. */
typedef struct { unsigned char a,b,c,d,op,control,konst,reserved; } DndTevAlphaStage;
typedef struct { DndTevAlphaStage stages[16]; unsigned char compare_enabled,compare0,reference0,operation,compare1,reference1,blend_enabled,blend_source,blend_destination; } DndEffectAlpha;
typedef struct { int count,texture,uniforms,indirect_strength; unsigned colors[4]; DndTevStage stages[16]; const DndEffectAlpha *alpha; } DndDrawEffect;
#endif
