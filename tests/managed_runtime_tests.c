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
static const DndManagedMethod iface_methods[] = { interface_method };
static const DndInterfaceEntry iface_map[] = { { &INTERFACE_TYPE, 1, iface_methods } };
static const DndType BASE_TYPE = { "Base", &DND_TYPE_OBJECT, sizeof(DndObject), 0, NULL, 0, NULL, 0, 1, base_vtable, 0, NULL };
static const DndType DERIVED_TYPE = { "Derived", &BASE_TYPE, sizeof(DndObject), 1, (const DndType *const[]){ &INTERFACE_TYPE }, 0, NULL, 0, 1, derived_vtable, 1, iface_map };

static intptr_t managed_add(intptr_t *args) { return args[0]+1; }
static int invoked;
static void callback(void *target, void *arg) {
    (void)target; invoked = *(int *)arg;
}

int main(void) {
    uint8_t memory[16384];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap, memory, sizeof(memory));

    DndString *unicode = dnd_string_from_utf8(&heap, "A\xC3\xA9\xF0\x9F\x98\x80");
    assert(unicode && unicode->length == 4);
    assert(unicode->chars[0] == 'A' && unicode->chars[1] == 0x00e9);
    assert(unicode->chars[2] == 0xd83d && unicode->chars[3] == 0xde00);
    DndString *invalid_utf8 = dnd_string_from_utf8(&heap, "\xFF");
    assert(invalid_utf8 && invalid_utf8->length == 1 && invalid_utf8->chars[0] == 0xfffd);
    /* Phase 6 integration: ordinal UTF-16 search must operate on code units. */
    DndString *emoji_needle = dnd_string_from_utf8(&heap, "\xF0\x9F\x98\x80");
    assert(emoji_needle && dnd_string_index_of(unicode, emoji_needle) == 2);
    assert(dnd_string_char_at(unicode, 2) == 0xd83d);
    assert(dnd_string_char_at(unicode, 3) == 0xde00);
    DndString *unicode_copy = dnd_string_substring(&heap, unicode, 1, 3);
    assert(unicode_copy && unicode_copy->length == 3);
    assert(dnd_string_index_of(unicode_copy, emoji_needle) == 1);
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

    /* SZ arrays are zero-initialized and preserve scalar widths. */
    DndArray *bytes = dnd_managed_array_new(&heap, 2, 1);
    DndArray *wide = dnd_managed_array_new(&heap, 1, 8);
    assert(bytes && wide && dnd_array_load_scalar(bytes, 0, 1, false) == 0);
    assert(dnd_array_store_scalar(bytes, 0, 0xff, 1));
    assert(dnd_array_load_scalar(bytes, 0, 1, false) == 0xff);
    assert(dnd_array_store_scalar(wide, 0, UINT64_C(0xf000000000000002), 8));
    assert(dnd_array_load_scalar(wide, 0, 8, false) == UINT64_C(0xf000000000000002));

    /* Reference array stores enforce their declared element type. */
    DndArray *typed_refs = dnd_managed_array_new_typed(&heap, 1, sizeof(DndObject *), &BASE_TYPE, true);
    DndObject *derived_for_array = dnd_object_new(&heap, &DERIVED_TYPE);
    DndObject *wrong_for_array = dnd_object_new(&heap, &NODE_TYPE);
    assert(dnd_array_store_ref(typed_refs, 0, derived_for_array));
    dnd_exception_clear();
    assert(!dnd_array_store_ref(typed_refs, 0, wrong_for_array));
    assert(dnd_exception_kind() == DND_EXCEPTION_INVALID_CAST);
    dnd_exception_clear();

    DndArray *array_copy = dnd_managed_array_new(&heap, 4, sizeof(int32_t));
    assert(array_copy && dnd_array_long_length(array) == 4 && dnd_array_get_length(array, 0) == 4);
    assert(dnd_array_get_lower_bound(array, 0) == 0 && dnd_array_get_upper_bound(array, 0) == 3);
    assert(dnd_array_copy(array, 0, array_copy, 0, 4));
    assert(dnd_array_load_i32(array_copy, 2) == 42);
    assert(dnd_array_index_of(array_copy, 42, 4, false, 0, 4) == 2);
    assert(dnd_array_clear(array_copy, 1, 2));
    assert(dnd_array_load_i32(array_copy, 2) == 0);

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

    assert(dnd_isinst(dispatch, &BASE_TYPE) == dispatch);
    assert(dnd_cast(dispatch, &BASE_TYPE) == dispatch);
    dnd_exception_clear(); assert(!dnd_require_object(NULL)); assert(dnd_exception_kind() == DND_EXCEPTION_NULL_REFERENCE); dnd_exception_clear();

    DndDelegate *managed_delegate=dnd_managed_delegate_new(&heap,NULL,managed_add,false); intptr_t managed_args[1]={10}; assert(dnd_managed_delegate_invoke(managed_delegate,managed_args,1)==11);

    /* EH frames cost nothing in methods that do not install one. A throw jumps
       directly to the nearest protected frame and restores the precise-GC chain. */
    DndEhFrame eh;
    dnd_eh_push(&eh);
    if (setjmp(eh.environment) == 0) {
        dnd_exception_throw(DND_EXCEPTION_INVALID_OPERATION, "boom");
        assert(!"throw must transfer control");
    } else {
        assert(dnd_exception_pending());
        assert(dnd_exception_kind() == DND_EXCEPTION_INVALID_OPERATION);
        assert(dnd_exception_message()[0] == 'b');
        dnd_exception_clear();
    }
    dnd_eh_pop(&eh);

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

    /* Phase 7: pending exception objects must survive a collection even when
       no managed stack frame currently contains a reference to them. */
    dnd_exception_clear();
    DndException *pending = dnd_exception_new(&heap, &DND_TYPE_EXCEPTION, NULL);
    assert(pending);
    dnd_exception_throw_object((DndObject *)pending);
    assert(dnd_exception_pending() && dnd_exception_object() == (DndObject *)pending);
    dnd_gc_collect(&heap, NULL);
    assert(dnd_exception_object() == (DndObject *)pending);
    assert(dnd_exception_object()->type == &DND_TYPE_EXCEPTION);
    dnd_exception_clear();

    /* Oversized requests must fail safely instead of wrapping the allocation. */
    assert(dnd_managed_array_new(&heap, UINT32_MAX, UINT32_MAX) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_OUT_OF_MEMORY);
    dnd_exception_clear();

    /* Malformed metadata must not corrupt precise reference-array tracing. */
    assert(dnd_object_new(&heap, NULL) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_ARGUMENT);
    dnd_exception_clear();
    assert(dnd_managed_array_new_typed(&heap, 2, 1, &NODE_TYPE, true) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_ARGUMENT);
    dnd_exception_clear();
    assert(dnd_managed_array_new(&heap, 1, 0) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_ARGUMENT);
    dnd_exception_clear();

    /* Length arithmetic must reject impossible UTF-16 concatenations before
       reading either source buffer or attempting allocation. */
    struct { DndObject object; uint32_t length; uint16_t chars[1]; } huge_string = {0};
    huge_string.object.type = &DND_TYPE_STRING;
    huge_string.length = UINT32_MAX;
    assert(dnd_string_concat(&heap, (DndString *)&huge_string, hello) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_OUT_OF_MEMORY);
    dnd_exception_clear();

    /* Input references to allocating runtime helpers must be rooted even when
       the caller has not installed a separate shadow-stack frame. */
    dnd_gc_set_stress(false);
    DndString *gc_left = dnd_string_from_utf8(&heap, "Left");
    DndString *gc_right = dnd_string_from_utf8(&heap, "Right");
    assert(gc_left && gc_right);
    dnd_gc_set_stress(true);
    DndString *gc_joined = dnd_string_concat(&heap, gc_left, gc_right);
    assert(gc_joined && gc_joined->length == 9);
    assert(gc_joined->chars[0] == 'L' && gc_joined->chars[8] == 't');
    dnd_gc_set_stress(false);
    DndString *gc_message = dnd_string_from_utf8(&heap, "Survives");
    assert(gc_message);
    dnd_gc_set_stress(true);
    DndException *gc_exception = dnd_exception_new(&heap, &DND_TYPE_EXCEPTION, gc_message);
    assert(gc_exception && gc_exception->message == gc_message);
    assert(gc_exception->message->length == 8);
    dnd_gc_set_stress(false);

    /* A reference store into scalar storage must fail, even if the stride
       happens to match a native pointer width. */
    DndArray *scalar_pointer_width = dnd_managed_array_new(&heap, 1, sizeof(void *));
    assert(scalar_pointer_width);
    assert(!dnd_array_store_ref(scalar_pointer_width, 0, NULL));
    assert(dnd_exception_kind() == DND_EXCEPTION_INVALID_CAST);
    dnd_exception_clear();

    /* Scalar and reference accessors must never reinterpret each other's
       storage: doing so can hide pointers from precise tracing. */
    DndArray *access_scalars = dnd_managed_array_new(&heap, 1, sizeof(void *));
    DndArray *access_refs = dnd_managed_array_new_typed(&heap, 1, sizeof(DndObject *), &DND_TYPE_OBJECT, true);
    assert(access_scalars && access_refs);
    assert(dnd_array_load_ref(access_scalars, 0) == NULL);
    assert(dnd_exception_kind() == DND_EXCEPTION_INVALID_CAST);
    dnd_exception_clear();
    assert(dnd_array_load_scalar(access_refs, 0, 4, false) == 0);
    assert(dnd_exception_kind() == DND_EXCEPTION_INVALID_CAST);
    dnd_exception_clear();
    assert(!dnd_array_store_scalar(access_refs, 0, 42, 4));
    assert(dnd_exception_kind() == DND_EXCEPTION_INVALID_CAST);
    dnd_exception_clear();

    /* Substring must preserve its source across an allocation-triggered GC. */
    dnd_gc_set_stress(false);
    DndString *substring_source = dnd_string_from_utf8(&heap, "abcdef");
    assert(substring_source);
    dnd_gc_set_stress(true);
    DndString *substring_stress = dnd_string_substring(&heap, substring_source, 2, 3);
    assert(substring_stress && substring_stress->length == 3);
    assert(substring_stress->chars[0] == 'c' && substring_stress->chars[2] == 'e');
    dnd_gc_set_stress(false);

    puts("managed runtime + core BCL tests passed");
    return 0;
}
