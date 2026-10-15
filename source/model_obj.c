#include "dnd_model.h"

/* Compatibility native entry point; OBJ now shares the bounded source reader
 * with FBX, including MTL, image decoding and non-convex triangulation. */
int dnd_model_begin_obj(DndString *path, int budget) {
  return dnd_model_begin_scene(path, 0, 60.f, budget, 0);
}
