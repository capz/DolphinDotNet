#ifndef DND_INPUT_TEST_BACKEND_H
#define DND_INPUT_TEST_BACKEND_H
#include "dnd_input.h"
void test_input_set(int port, DndInputSample sample);
void test_input_advance(uint32_t microseconds);
unsigned test_input_polls(void);
unsigned test_input_motors(void);
#endif
