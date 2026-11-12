/* Compile the production GameCube cache-flush branch against a recording host
 * function. Only model_animation.c uses this header in the focused fixture. */
#ifndef DND_SKIN_TEST_GCCORE_H
#define DND_SKIN_TEST_GCCORE_H
void DCFlushRange(void *address, unsigned length);
#endif
