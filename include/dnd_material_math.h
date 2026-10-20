#ifndef DND_MATERIAL_MATH_H
#define DND_MATERIAL_MATH_H
#include <math.h>
/* Reflect the vertex-to-eye direction about a normalized view-space normal,
 * rotate back into world space, then sample a longitude/latitude environment.
 * inverse_view is rotation only; translation must not affect a direction. */
static inline void dnd_material_world_reflection(const float normal[3][4],const float inverse_view[3][4],
    float nx,float ny,float nz,float px,float py,float pz,float *u,float *v) {
    float n[3],eye[3]={-px,-py,-pz},nl=0,el=px*px+py*py+pz*pz;
    for(int i=0;i<3;i++){n[i]=normal[i][0]*nx+normal[i][1]*ny+normal[i][2]*nz;nl+=n[i]*n[i];}
    if(!(nl>0)||!(el>0)||!isfinite(nl)||!isfinite(el)){*u=*v=.5f;return;}
    nl=sqrtf(nl);el=sqrtf(el);float dot=0,r[3],w[3];
    for(int i=0;i<3;i++){n[i]/=nl;eye[i]/=el;dot+=n[i]*eye[i];}
    for(int i=0;i<3;i++)r[i]=2*dot*n[i]-eye[i];
    float length=0;
    for(int i=0;i<3;i++){w[i]=inverse_view[i][0]*r[0]+inverse_view[i][1]*r[1]+inverse_view[i][2]*r[2];length+=w[i]*w[i];}
    length=sqrtf(length);
    if(!(length>0)||!isfinite(length)){*u=*v=.5f;return;}
    *u=.5f+atan2f(w[0],w[2])*.15915494309f;
    *v=acosf(fmaxf(-1,fminf(1,w[1]/length)))*.31830988618f;
}
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
