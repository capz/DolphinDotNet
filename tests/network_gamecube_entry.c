/* Link-only AOT ABI harness. Runtime fixtures require a separate host and target setup. */
#include "dnd_managed.h"
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void){static unsigned char storage[256*1024];DndManagedHeap heap;dnd_managed_heap_init(&heap,storage,sizeof(storage));return (int)dnd_aot_entry(&heap);}
