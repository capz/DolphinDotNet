/* Minimal exception-stack fake for isolated native audio tests. */
#include "dnd_managed.h"
static DndEhFrame *audio_test_eh;
static bool audio_test_exception;
void dnd_eh_push(DndEhFrame *frame){frame->previous=audio_test_eh;audio_test_eh=frame;}
void dnd_eh_pop(DndEhFrame *frame){if(audio_test_eh==frame)audio_test_eh=frame->previous;}
bool dnd_exception_pending(void){return audio_test_exception;}
void dnd_exception_clear(void){audio_test_exception=false;}
