#ifndef DND_MATERIAL_MATH_H
#define DND_MATERIAL_MATH_H
#include <math.h>
/* Normalized inverse-transpose normal projection (matcap), not a cubemap or
 * position-dependent ray reflection. CPU normalization handles nonuniform scale
 * before GX's ordinary TEX0 identity generator interpolates the coordinates. */
static inline void dnd_material_view_normal(const float normal[3][4],float x,float y,float z,float *u,float *v) {
    float nx=normal[0][0]*x+normal[0][1]*y+normal[0][2]*z;
    float ny=normal[1][0]*x+normal[1][1]*y+normal[1][2]*z;
    float nz=normal[2][0]*x+normal[2][1]*y+normal[2][2]*z;
    float length=sqrtf(nx*nx+ny*ny+nz*nz);
    *u=length>0&&isfinite(length)?0.5f+0.5f*nx/length:0.5f;
    *v=length>0&&isfinite(length)?0.5f-0.5f*ny/length:0.5f;
}
#endif
