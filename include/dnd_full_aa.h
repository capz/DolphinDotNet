#ifndef DND_FULL_AA_H
#define DND_FULL_AA_H
/* 248 rows fit the 264-row RGB565/Z16 multisample EFB. The 16-row
 * overlap also aligns both 1:1 and half-size captures to GX 4-row blocks. */
#define DND_AA_HEIGHT 248
#define DND_AA_ORIGIN(part) ((part)?232:0)
static inline void dnd_full_aa_projection(float out[4][4],const float *in,
                                        int part,const int viewport[4]) {
    for(int r=0;r<4;r++)for(int c=0;c<4;c++)out[r][c]=in[c*4+r];
    float sx=viewport[2]/640.f, bx=(2*viewport[0]+viewport[2])/640.f-1;
    float sy=viewport[3]/(float)DND_AA_HEIGHT;
    float by=1-sy-2*(viewport[1]-DND_AA_ORIGIN(part))/(float)DND_AA_HEIGHT;
    for(int c=0;c<4;c++){
        out[0][c]=sx*in[c*4]+bx*in[c*4+3];
        out[1][c]=sy*in[c*4+1]+by*in[c*4+3];
    }
}
#endif
