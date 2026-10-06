#include "dnd_managed.h"
#include "dnd_bcl.h"
#include <assert.h>
#include <stdint.h>
#include <stdio.h>

typedef struct { DndObject object; DndObject *child; int32_t value; } TestNode;
static const uint32_t node_refs[] = { (uint32_t)offsetof(TestNode, child) };
static const DndType NODE_TYPE = { "TestNode", &DND_TYPE_OBJECT, sizeof(TestNode), 0, NULL, 1, node_refs, 0, 0, NULL, 0, NULL };

static intptr_t virtual_base(intptr_t *args) { (void)args; return 10; }
static intptr_t virtual_derived(intptr_t *args) { (void)args; return 20; }
static intptr_t interface_method(intptr_t *args) { (void)args; return 30; }
static const DndManagedMethod base_vtable[] = { virtual_base };
static const DndManagedMethod derived_vtable[] = { virtual_derived };
static const DndType INTERFACE_TYPE = { "ITest", &DND_TYPE_OBJECT, sizeof(DndObject), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL };
static const DndType CHILD_INTERFACE_TYPE = { "IChildTest", &DND_TYPE_OBJECT, sizeof(DndObject), 1, (const DndType *const[]){ &INTERFACE_TYPE }, 0, NULL, 0, 0, NULL, 0, NULL };
static const DndType TRANSITIVE_TYPE = { "Transitive", &DND_TYPE_OBJECT, sizeof(DndObject), 1, (const DndType *const[]){ &CHILD_INTERFACE_TYPE }, 0, NULL, 0, 0, NULL, 0, NULL };
static const DndManagedMethod iface_methods[] = { interface_method };
static const DndInterfaceEntry iface_map[] = { { &INTERFACE_TYPE, 1, iface_methods } };
static const DndType BASE_TYPE = { "Base", &DND_TYPE_OBJECT, sizeof(DndObject), 0, NULL, 0, NULL, 0, 1, base_vtable, 0, NULL };
static const DndType DERIVED_TYPE = { "Derived", &BASE_TYPE, sizeof(DndObject), 1, (const DndType *const[]){ &INTERFACE_TYPE }, 0, NULL, 0, 1, derived_vtable, 1, iface_map };

static intptr_t managed_add(intptr_t *args) { return args[0]+1; }
static intptr_t managed_double(intptr_t *args) { return args[0]*2; }
static intptr_t managed_instance(intptr_t *args) { return ((TestNode *)args[0])->value + args[1]; }
static int invoked;
static void callback(void *target, void *arg) {
    (void)target; invoked = *(int *)arg;
}

static int delegate_total;
static void add_delegate(void *target, void *argument) { delegate_total += *(int *)target + *(int *)argument; }

int main(void) {
    uint8_t memory[16384];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, memory, sizeof(memory));

    DndString *hello = dnd_string_from_utf8(&heap, "Hello");
    DndString *space = dnd_string_from_utf8(&heap, " ");
    DndString *world = dnd_string_from_utf8(&heap, "World");
    DndString *hello2 = dnd_string_from_utf8(&heap, "Hello");
    assert(hello && hello->length == 5);
    assert(dnd_string_equals(hello, hello2));
    DndString *left = dnd_string_concat(&heap, hello, space);
    DndString *sentence = dnd_string_concat(&heap, left, world);
    assert(sentence && sentence->length == 11);

    DndArray *array = dnd_managed_array_new(&heap, 4, sizeof(int32_t));
    *(int32_t *)dnd_managed_array_at(array, 2) = 42;
    assert(*(int32_t *)dnd_managed_array_at(array, 2) == 42);
    assert(dnd_managed_array_at(array, 9) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_INDEX_OUT_OF_RANGE);
    dnd_exception_clear();

    DndList list;
    assert(dnd_list_init(&list, &heap, sizeof(int32_t), 2));
    for (int32_t i = 0; i < 10; i++) assert(dnd_list_add(&list, &i));
    assert(dnd_list_count(&list) == 10);
    assert(*(int32_t *)dnd_list_get(&list, 7) == 7);

    DndRandom random;
    dnd_random_init(&random, 1234);
    int32_t value = dnd_random_next_max(&random, 10);
    assert(value >= 0 && value < 10);
    assert(dnd_math_abs_i32(-12) == 12);
    assert(dnd_math_min_i32(2, 9) == 2);
    assert(dnd_math_max_i32(2, 9) == 9);

    DndDelegate *delegate = dnd_delegate_new(&heap, NULL, callback);
    int n = 73; dnd_delegate_invoke(delegate, &n);
    assert(invoked == 73);

    DndObject *root = (DndObject *)hello;
    DndObject **slots[1];
    DndRootSet roots;
    dnd_roots_init(&roots, slots, 1);
    assert(dnd_root_add(&roots, &root));
    dnd_gc_collect(&heap, &roots);
    assert(root == (DndObject *)hello);
    assert(hello->length == 5);
    root = NULL;
    dnd_gc_collect(&heap, &roots);
    assert(heap.used == 0);

    DndObject *dispatch = dnd_object_new(&heap, &DERIVED_TYPE); assert(dispatch); intptr_t dispatch_args[1] = { (intptr_t)dispatch };
    assert(dnd_virtual_resolve(dispatch, 0)(dispatch_args) == 20);
    assert(dnd_interface_resolve(dispatch, &INTERFACE_TYPE, 0)(dispatch_args) == 30);
    assert(dnd_type_is_assignable_from(&BASE_TYPE, dispatch->type));
    assert(dnd_type_is_assignable_from(&INTERFACE_TYPE, dispatch->type));
    DndObject *transitive = dnd_object_new(&heap, &TRANSITIVE_TYPE);
    assert(transitive && dnd_type_is_assignable_from(&INTERFACE_TYPE, transitive->type));

    assert(dnd_isinst(dispatch, &BASE_TYPE) == dispatch);
    assert(dnd_cast(dispatch, &BASE_TYPE) == dispatch);
    dnd_exception_clear(); assert(!dnd_require_object(NULL)); assert(dnd_exception_kind() == DND_EXCEPTION_NULL_REFERENCE); dnd_exception_clear();

    int d1v=2,d2v=3,arg=4; DndDelegate *d1=dnd_delegate_new(&heap,&d1v,add_delegate); DndDelegate *d2=dnd_delegate_new(&heap,&d2v,add_delegate);
    DndDelegate *multi=dnd_delegate_combine(&heap,d1,d2); delegate_total=0; dnd_delegate_invoke(multi,&arg); assert(delegate_total==13);
    multi=dnd_delegate_remove(multi,d1); delegate_total=0; dnd_delegate_invoke(multi,&arg); assert(delegate_total==7);

    DndExceptionObject managed_exception={{&DND_TYPE_EXCEPTION,0},DND_EXCEPTION_ARGUMENT,NULL};
    dnd_exception_throw_object(&managed_exception); assert(dnd_exception_object()==&managed_exception); assert(dnd_exception_kind()==DND_EXCEPTION_ARGUMENT); dnd_exception_clear();

    DndDelegate *managed_delegate=dnd_managed_delegate_new(&heap,NULL,managed_add,false); intptr_t managed_args[1]={10}; assert(dnd_managed_delegate_invoke(managed_delegate,managed_args,1)==11);

    assert(dnd_managed_delegate_invoke(managed_delegate, NULL, 1) == 0);
    assert(dnd_exception_kind() == DND_EXCEPTION_ARGUMENT);
    dnd_exception_clear();
    assert(dnd_managed_delegate_invoke(managed_delegate, managed_args, 257) == 0);
    assert(dnd_exception_kind() == DND_EXCEPTION_ARGUMENT);
    dnd_exception_clear();
    DndDelegate *managed_second = dnd_managed_delegate_new(&heap, NULL, managed_double, false);
    DndDelegate *managed_multi = dnd_delegate_combine(&heap, managed_delegate, managed_second);
    assert(managed_multi && dnd_managed_delegate_invoke(managed_multi, managed_args, 1) == 20);
    managed_multi = dnd_delegate_remove(managed_multi, managed_second);
    assert(dnd_managed_delegate_invoke(managed_multi, managed_args, 1) == 11);

    /* Collection during delegate allocation must retain an instance target. */
    uint8_t delegate_memory[1024];
    DndManagedHeap delegate_heap;
    dnd_managed_heap_init(&delegate_heap, delegate_memory, sizeof(delegate_memory));
    TestNode *delegate_target = (TestNode *)dnd_object_new(&delegate_heap, &NODE_TYPE);
    delegate_target->value = 32;
    dnd_gc_set_stress(true);
    DndDelegate *instance_delegate = dnd_managed_delegate_new(&delegate_heap, (DndObject *)delegate_target, managed_instance, true);
    dnd_gc_set_stress(false);
    assert(instance_delegate && dnd_managed_delegate_invoke(instance_delegate, managed_args, 1) == 42);

    /* Pending managed exceptions are implicit GC roots across collection. */
    DndExceptionObject *heap_exception = (DndExceptionObject *)dnd_object_new(&heap, &DND_TYPE_EXCEPTION);
    assert(heap_exception); heap_exception->kind = DND_EXCEPTION_ARGUMENT;
    dnd_exception_throw_object(heap_exception); dnd_gc_collect(&heap, NULL);
    assert(dnd_exception_object() == heap_exception && heap_exception->object.type == &DND_TYPE_EXCEPTION);
    dnd_exception_clear();

    DndObject *boxed = dnd_box_i32(&heap, 123);
    assert(boxed && dnd_unbox_i32(boxed) == 123);

    /* Reference arrays participate in precise tracing. */
    DndArray *references = dnd_managed_array_new_typed(&heap, 1, sizeof(DndObject *), &NODE_TYPE, true);
    TestNode *array_child = (TestNode *)dnd_object_new(&heap, &NODE_TYPE);
    assert(references && array_child);
    assert(dnd_array_store_ref(references, 0, (DndObject *)array_child));
    DndObject *array_root = (DndObject *)references; DndObject **array_slots[1]; DndRootSet array_roots;
    dnd_roots_init(&array_roots, array_slots, 1); assert(dnd_root_add(&array_roots, &array_root));
    dnd_gc_collect(&heap, &array_roots);
    assert(dnd_array_load_ref(references, 0) == (DndObject *)array_child);

    /* Stress collection exercises precise roots on every allocation. */
    dnd_gc_set_stress(true);
    DndObject *stress_root = dnd_object_new(&heap, &NODE_TYPE); DndObject **stress_slots[1]; DndRootSet stress_roots;
    dnd_roots_init(&stress_roots, stress_slots, 1); assert(dnd_root_add(&stress_roots, &stress_root));
    for (int i=0;i<8;i++) { DndObject *temporary=dnd_object_new(&heap,&NODE_TYPE); assert(temporary); dnd_gc_collect(&heap,&stress_roots); assert(stress_root); }
    dnd_gc_set_stress(false);

    /* Precise tracing keeps an object reachable through a managed field. */
    TestNode *parent = (TestNode *)dnd_object_new(&heap, &NODE_TYPE);
    TestNode *child = (TestNode *)dnd_object_new(&heap, &NODE_TYPE);
    assert(parent && child); parent->child = (DndObject *)child; child->value = 99;
    DndObject *parent_root = (DndObject *)parent; DndObject **trace_slots[1]; DndRootSet trace_roots;
    dnd_roots_init(&trace_roots, trace_slots, 1); assert(dnd_root_add(&trace_roots, &parent_root));
    dnd_gc_collect(&heap, &trace_roots); assert(parent->child == (DndObject *)child); assert(child->value == 99);

    /* Dead middle blocks are reusable without requiring the whole heap to die. */
    parent->child = NULL; size_t before_collect = heap.used; dnd_gc_collect(&heap, &trace_roots);
    TestNode *replacement = (TestNode *)dnd_object_new(&heap, &NODE_TYPE);
    assert(replacement); assert(heap.used <= before_collect);

    puts("managed runtime + core BCL tests passed");
    return 0;
}
