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
extern intptr_t dnd_value_Program_ReturningDelegateCase(void);
extern intptr_t dnd_value_Program_ExceptionCase(void);
extern intptr_t dnd_value_Program_FinallyCase(void);
extern intptr_t dnd_value_Program_NestedFinallyCase(void);
extern intptr_t dnd_value_Program_ObjectPrimitiveCase(void);
extern intptr_t dnd_value_Program_StringPrimitiveCase(void);
extern intptr_t dnd_value_Program_ArrayPrimitiveCase(void);
extern intptr_t dnd_value_Program_ArrayCase(void);
extern intptr_t dnd_value_Program_BoxCase(void);
extern intptr_t dnd_value_Program_TypeCase(void);\nextern intptr_t dnd_value_Program_ArrayCase(void);
extern intptr_t dnd_value_Program_StaticCase(void);
extern intptr_t dnd_value_Program_BoxCase(void);
extern intptr_t dnd_value_Program_VirtualCase(void);
extern intptr_t dnd_value_Program_TypeCase(void);
extern intptr_t dnd_value_Program_InheritedFieldCase(void);
extern intptr_t dnd_value_Program_ByRefCase(void);
extern intptr_t dnd_value_Program_InterfaceIdentityCase(void);
extern intptr_t dnd_value_Program_InterfaceCallCase(void);
extern intptr_t dnd_value_Program_DelegateCase(void);

int main(void)
{
    intptr_t loop=dnd_value_Program_LoopSum();
    intptr_t branches=dnd_value_Program_BranchCases(7);
    intptr_t nested=dnd_value_Program_Nested(3,8);
    intptr_t short_circuit=dnd_value_Program_ShortCircuit(4,9);
    intptr_t mutated=dnd_value_Program_MutateArgument(2);
    intptr_t switched=dnd_value_Program_SwitchCase(4);
    unsigned char storage[4096]; DndManagedHeap heap; dnd_managed_heap_init(&heap,storage,sizeof(storage));
    intptr_t result=dnd_value_aot_entry(&heap); int entry_exception=(int)dnd_exception_kind(); dnd_exception_clear();
    intptr_t object_core=dnd_value_Program_ObjectPrimitiveCase(); int object_exception=(int)dnd_exception_kind(); dnd_exception_clear();
    intptr_t string_core=dnd_value_Program_StringPrimitiveCase(); int string_exception=(int)dnd_exception_kind(); dnd_exception_clear();
    intptr_t array_core=dnd_value_Program_ArrayPrimitiveCase(); int array_exception=(int)dnd_exception_kind(); dnd_exception_clear();
    printf("core primitives: object=%ld/%d string=%ld/%d array=%ld/%d\n",(long)object_core,object_exception,(long)string_core,string_exception,(long)array_core,array_exception);
    printf("value-ir components: loop=%ld branches=%ld nested=%ld short=%ld mutated=%ld switched=%ld total=%ld exception=%d\n",
        (long)loop,(long)branches,(long)nested,(long)short_circuit,(long)mutated,(long)switched,(long)result,entry_exception);
    if(dnd_value_Program_ReturningDelegateCase()!=11)return 1;
    dnd_exception_clear();
    if(dnd_value_Program_ExceptionCase()!=7)return 1;
    dnd_exception_clear();
    if(dnd_value_Program_FinallyCase()!=5)return 1;
    dnd_exception_clear();
    if(dnd_value_Program_NestedFinallyCase()!=7)return 1;
    dnd_exception_clear();
    if(loop!=10||branches!=13||nested!=5||short_circuit!=24||mutated!=5||switched!=40||result!=230)return 1;
    return 0;
}
