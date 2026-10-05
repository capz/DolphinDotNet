#include <stdio.h>
#include <stdint.h>
#include "dnd_managed.h"

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
extern intptr_t dnd_value_Program_LoopSum(void);
extern intptr_t dnd_value_Program_MutateArgument(intptr_t);
extern intptr_t dnd_value_Program_SwitchCase(intptr_t);
extern intptr_t dnd_value_Program_BranchCases(intptr_t);
extern intptr_t dnd_value_Program_Nested(intptr_t,intptr_t);
extern intptr_t dnd_value_Program_ShortCircuit(intptr_t,intptr_t);

int main(void)
{
    intptr_t loop=dnd_value_Program_LoopSum();
    intptr_t branches=dnd_value_Program_BranchCases(7);
    intptr_t nested=dnd_value_Program_Nested(3,8);
    intptr_t short_circuit=dnd_value_Program_ShortCircuit(4,9);
    intptr_t mutated=dnd_value_Program_MutateArgument(2);
    intptr_t switched=dnd_value_Program_SwitchCase(4);
    unsigned char storage[4096]; DndManagedHeap heap; dnd_managed_heap_init(&heap,storage,sizeof(storage));
    intptr_t result=dnd_value_aot_entry(&heap);
    printf("value-ir components: loop=%ld branches=%ld nested=%ld short=%ld mutated=%ld switched=%ld total=%ld\n",
        (long)loop,(long)branches,(long)nested,(long)short_circuit,(long)mutated,(long)switched,(long)result);
    if(loop!=10||branches!=13||nested!=5||short_circuit!=24||mutated!=5||switched!=40||result!=121)return 1;
    return 0;
}
