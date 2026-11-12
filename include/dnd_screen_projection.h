#ifndef DND_SCREEN_PROJECTION_H
#define DND_SCREEN_PROJECTION_H
/* Column-major clip=P*V*W, applied to original object-space positions. A 3x4
 * texgen carries (s,t,q)=(.5*(clip.x+clip.w),.5*(clip.w-clip.y),clip.w).
 * GX performs perspective division after interpolation. The optional ordinary
 * six-float UV affine transform is applied homogeneously, without CPU vertices. */
static inline void dnd_screen_projection_matrix(float out[3][4],const float clip[16],const float *uv){
    for(int c=0;c<4;c++){
        float s=.5f*(clip[c*4]+clip[c*4+3]);
        float t=.5f*(clip[c*4+3]-clip[c*4+1]);
        float q=clip[c*4+3];
        out[0][c]=uv?uv[0]*s+uv[2]*t+uv[4]*q:s;
        out[1][c]=uv?uv[1]*s+uv[3]*t+uv[5]*q:t;
        out[2][c]=q;
    }
}
#endif
