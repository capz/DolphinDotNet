#include "input_test_backend.h"
#include <string.h>
static DndInputSample inputs[4];
static uint64_t now, stops[4];
static unsigned polls, motors;
void test_input_set(int port, DndInputSample sample) { inputs[port]=sample; }
void test_input_advance(uint32_t us) {
    now+=us;
    for (unsigned i=0;i<4;i++) if ((motors&(1u<<i)) && now>=stops[i]) motors&=~(1u<<i);
}
unsigned test_input_polls(void) { return polls; }
unsigned test_input_motors(void) { return motors; }
void dnd_input_backend_init(void) { }
void dnd_input_backend_shutdown(void) { memset(stops,0,sizeof(stops)); }
void dnd_input_backend_sample(DndInputSample samples[4]) { polls++; memcpy(samples,inputs,sizeof(inputs)); }
bool dnd_input_backend_rumble(int port,uint32_t us) { motors|=1u<<port; stops[port]=now+us; return true; }
void dnd_input_backend_stop_rumble(int port) { motors&=~(1u<<port); }
