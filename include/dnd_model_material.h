#ifndef DND_MODEL_MATERIAL_H
#define DND_MODEL_MATERIAL_H
#include <stdbool.h>
#include <stdint.h>
#include <math.h>

/* Normalized authored GX state. These are validated fields, never a display
 * list or borrowed source pointer. The original MAT1 profile has one TEV stage,
 * one color/alpha channel and at most one texture generator per material. */
typedef struct {
  uint8_t input[4], operation, bias, scale, clamp, output;
} DndAuthoredTev;
typedef struct {
  uint8_t channel[2][5], alpha[5], blend[4], depth[3];
  uint8_t cull, depth_location, dither, texgens, texsource, texmatrix_active;
  uint8_t order[3];
  int16_t colors[3][4];
  float texmatrix[12];
  DndAuthoredTev rgb, opacity;
} DndAuthoredMaterial;

static inline bool dnd_authored_material_valid(const DndAuthoredMaterial *m) {
  if(m->cull>3||m->depth_location>1||m->dither>1||m->texgens>1||
     m->texmatrix_active>1||m->depth[0]>1||m->depth[1]>7||m->depth[2]>1||
     m->blend[0]>3||m->blend[1]>7||m->blend[2]>7||m->blend[3]>15||
     m->alpha[0]>7||m->alpha[2]>3||m->alpha[3]>7)return false;
  if(m->texgens&&m->texsource!=0&&m->texsource!=1&&m->texsource!=4)return false;
  if((m->order[0]!=0&&m->order[0]!=255)||(m->order[1]!=0&&m->order[1]!=255)||
     (m->order[2]!=4&&m->order[2]!=255))return false;
  for(unsigned c=0;c<2;c++)
    if(m->channel[c][0]>1||m->channel[c][1]>1||m->channel[c][2]>1||
       m->channel[c][3]>2||m->channel[c][4]>2)return false;
  for(unsigned c=0;c<3;c++)for(unsigned k=0;k<4;k++)
    if(m->colors[c][k]<-1024||m->colors[c][k]>1023)return false;
  for(unsigned k=0;k<12;k++)if(!isfinite(m->texmatrix[k]))return false;
  for(unsigned c=0;c<2;c++) {
    const DndAuthoredTev *t=c?&m->opacity:&m->rgb;
    for(unsigned k=0;k<4;k++)if(t->input[k]>(c?7:15))return false;
    if(t->operation>1||t->bias>2||t->scale>3||t->clamp>1||t->output>3)return false;
  }
  return true;
}
#endif
