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
extern intptr_t dnd_value_Program_RethrowCase(void);
extern intptr_t dnd_value_Program_CatchThrowsCase(void);
extern intptr_t dnd_value_Program_ReturnFinallyCase(void);
extern intptr_t dnd_value_Program_RethrowIdentityCase(void);
extern intptr_t dnd_value_Program_TypedCatchCase(void);
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

    int ex_returning,ex_exception,ex_finally,ex_nested_finally,ex_rethrow,ex_rethrow_simple,ex_catch_throws,ex_finally_return,ex_typed;
    intptr_t returning_value=capture0(dnd_value_Program_ReturningDelegateCase,&ex_returning);
    intptr_t exception_value=capture0(dnd_value_Program_ExceptionCase,&ex_exception);
    intptr_t finally_value=capture0(dnd_value_Program_FinallyCase,&ex_finally);
    intptr_t nested_finally_value=capture0(dnd_value_Program_NestedFinallyCase,&ex_nested_finally);
    intptr_t rethrow_value=capture0(dnd_value_Program_RethrowIdentityCase,&ex_rethrow);
    intptr_t rethrow_simple_value=capture0(dnd_value_Program_RethrowCase,&ex_rethrow_simple);
    intptr_t catch_throws_value=capture0(dnd_value_Program_CatchThrowsCase,&ex_catch_throws);
    intptr_t finally_return_value=capture0(dnd_value_Program_ReturnFinallyCase,&ex_finally_return);
    intptr_t typed_value=capture0(dnd_value_Program_TypedCatchCase,&ex_typed);
    printf("eh cases: delegate=%ld/%d catch=%ld/%d finally=%ld/%d nested=%ld/%d rethrow=%ld/%d rethrowsimple=%ld/%d catchthrow=%ld/%d returnfinally=%ld/%d typed=%ld/%d\n",
        (long)returning_value,ex_returning,(long)exception_value,ex_exception,(long)finally_value,ex_finally,
        (long)nested_finally_value,ex_nested_finally,(long)rethrow_value,ex_rethrow,(long)rethrow_simple_value,ex_rethrow_simple,(long)catch_throws_value,ex_catch_throws,
        (long)finally_return_value,ex_finally_return,(long)typed_value,ex_typed);
    if(returning_value!=11||exception_value!=7||finally_value!=5||nested_finally_value!=7||rethrow_value!=19||rethrow_simple_value!=11||catch_throws_value!=13||finally_return_value!=22||typed_value!=23)return 1;
    if(ex_returning||ex_exception||ex_finally||ex_nested_finally||ex_rethrow||ex_rethrow_simple||ex_catch_throws||ex_finally_return||ex_typed)return 1;
    if(object_core!=31||string_core!=128||array_core!=22)return 1;
    if(ex_object||ex_string||ex_array_core||entry_exception)return 1;
    if(loop!=10||branches!=13||nested!=5||short_circuit!=24||mutated!=5||switched!=40||result!=484)return 1;
    return 0;
}
