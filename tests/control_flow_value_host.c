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
extern intptr_t dnd_value_Program_RethrowIdentityCase(void);
extern intptr_t dnd_value_Program_CatchThrowsCase(void);
extern intptr_t dnd_value_Program_FinallyReturnCase(void);
extern intptr_t dnd_value_Program_ObjectPrimitiveCase(void);
extern intptr_t dnd_value_Program_StringPrimitiveCase(void);
extern intptr_t dnd_value_Program_ArrayPrimitiveCase(void);
extern intptr_t dnd_value_Program_ArrayCase(void);
extern intptr_t dnd_value_Program_StaticCase(void);
extern intptr_t dnd_value_Program_BoxCase(void);
extern intptr_t dnd_value_Program_VirtualCase(void);
extern intptr_t dnd_value_Program_TypeCase(void);
extern intptr_t dnd_value_Program_InheritedFieldCase(void);
extern intptr_t dnd_value_Program_ByRefCase(void);
extern intptr_t dnd_value_Program_InterfaceIdentityCase(void);
extern intptr_t dnd_value_Program_InterfaceCallCase(void);
extern intptr_t dnd_value_Program_DelegateCase(void);

static intptr_t capture0(intptr_t (*fn)(void), int *exception)
{
    dnd_exception_clear();
    intptr_t value=fn();
    *exception=(int)dnd_exception_kind();
    dnd_exception_clear();
    return value;
}

int main(void)
{
    intptr_t loop=dnd_value_Program_LoopSum();
    intptr_t branches=dnd_value_Program_BranchCases(7);
    intptr_t nested=dnd_value_Program_Nested(3,8);
    intptr_t short_circuit=dnd_value_Program_ShortCircuit(4,9);
    intptr_t mutated=dnd_value_Program_MutateArgument(2);
    intptr_t switched=dnd_value_Program_SwitchCase(4);

    unsigned char storage[4096];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,storage,sizeof(storage));

    dnd_exception_clear();
    intptr_t result=dnd_value_aot_entry(&heap);
    int entry_exception=(int)dnd_exception_kind();
    dnd_exception_clear();

    int ex_array,ex_static,ex_box,ex_virtual,ex_type,ex_inherited,ex_byref,ex_iface_id,ex_iface_call,ex_delegate;
    intptr_t v_array=capture0(dnd_value_Program_ArrayCase,&ex_array);
    intptr_t v_static=capture0(dnd_value_Program_StaticCase,&ex_static);
    intptr_t v_box=capture0(dnd_value_Program_BoxCase,&ex_box);
    intptr_t v_virtual=capture0(dnd_value_Program_VirtualCase,&ex_virtual);
    intptr_t v_type=capture0(dnd_value_Program_TypeCase,&ex_type);
    intptr_t v_inherited=capture0(dnd_value_Program_InheritedFieldCase,&ex_inherited);
    intptr_t v_byref=capture0(dnd_value_Program_ByRefCase,&ex_byref);
    intptr_t v_iface_id=capture0(dnd_value_Program_InterfaceIdentityCase,&ex_iface_id);
    intptr_t v_iface_call=capture0(dnd_value_Program_InterfaceCallCase,&ex_iface_call);
    intptr_t v_delegate=capture0(dnd_value_Program_DelegateCase,&ex_delegate);

    int ex_object,ex_string,ex_array_core;
    intptr_t object_core=capture0(dnd_value_Program_ObjectPrimitiveCase,&ex_object);
    intptr_t string_core=capture0(dnd_value_Program_StringPrimitiveCase,&ex_string);
    intptr_t array_core=capture0(dnd_value_Program_ArrayPrimitiveCase,&ex_array_core);

    printf("precore: array=%ld/%d static=%ld/%d box=%ld/%d virtual=%ld/%d type=%ld/%d inherited=%ld/%d byref=%ld/%d ifaceid=%ld/%d ifacecall=%ld/%d delegate=%ld/%d\n",
        (long)v_array,ex_array,(long)v_static,ex_static,(long)v_box,ex_box,(long)v_virtual,ex_virtual,(long)v_type,ex_type,
        (long)v_inherited,ex_inherited,(long)v_byref,ex_byref,(long)v_iface_id,ex_iface_id,(long)v_iface_call,ex_iface_call,
        (long)v_delegate,ex_delegate);
    printf("core primitives: object=%ld/%d string=%ld/%d array=%ld/%d\n",
        (long)object_core,ex_object,(long)string_core,ex_string,(long)array_core,ex_array_core);
    printf("value-ir components: loop=%ld branches=%ld nested=%ld short=%ld mutated=%ld switched=%ld total=%ld exception=%d\n",
        (long)loop,(long)branches,(long)nested,(long)short_circuit,(long)mutated,(long)switched,(long)result,entry_exception);

    if(capture0(dnd_value_Program_ReturningDelegateCase,&ex_delegate)!=11)return 1;
    if(capture0(dnd_value_Program_ExceptionCase,&ex_delegate)!=7)return 1;
    if(capture0(dnd_value_Program_FinallyCase,&ex_delegate)!=5)return 1;
    if(capture0(dnd_value_Program_NestedFinallyCase,&ex_delegate)!=7)return 1;
    if(capture0(dnd_value_Program_RethrowIdentityCase,&ex_delegate)!=8)return 1;
    if(capture0(dnd_value_Program_CatchThrowsCase,&ex_delegate)!=9)return 1;
    if(capture0(dnd_value_Program_FinallyReturnCase,&ex_delegate)!=7)return 1;
    if(loop!=10||branches!=13||nested!=5||short_circuit!=24||mutated!=5||switched!=40||result!=254)return 1;
    return 0;
}
