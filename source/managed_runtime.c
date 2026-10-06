#include "dnd_managed.h"
#include <string.h>

const DndType DND_TYPE_OBJECT = {"System.Object", NULL, sizeof(DndObject), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_STRING = {"System.String", &DND_TYPE_OBJECT, sizeof(DndString), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_ARRAY = {"System.Array", &DND_TYPE_OBJECT, sizeof(DndArray), 0, NULL, 0, NULL, DND_TYPE_FLAG_ARRAY, 0, NULL, 0, NULL};
static const uint32_t delegate_refs[] = {(uint32_t)offsetof(DndDelegate, target), (uint32_t)offsetof(DndDelegate, next)};
const DndType DND_TYPE_DELEGATE = {"System.Delegate", &DND_TYPE_OBJECT, sizeof(DndDelegate), 0, NULL, 2, delegate_refs, 0, 0, NULL, 0, NULL};
typedef struct { DndObject object; int32_t value; } DndBoxedInt32;
static const uint32_t exception_refs[] = {(uint32_t)offsetof(DndExceptionObject, message)};
const DndType DND_TYPE_EXCEPTION = {"System.Exception", &DND_TYPE_OBJECT, sizeof(DndExceptionObject), 0, NULL, 1, exception_refs, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_BOXED_INT32 = {"System.Int32", &DND_TYPE_OBJECT, sizeof(DndBoxedInt32), 0, NULL, 0, NULL, DND_TYPE_FLAG_VALUE_TYPE, 0, NULL, 0, NULL};

typedef struct DndHeapBlock {
    uint32_t size;
    uint8_t marked;
    uint8_t free;
    uint16_t reserved;
    struct DndHeapBlock *next;
} DndHeapBlock;

static DndGcFrame *gc_frames;
static bool gc_stress;
static DndExceptionKind exception_kind;
static const char *exception_text;
static DndExceptionObject *exception_object;

static size_t align8(size_t n) { return (n + 7u) & ~(size_t)7u; }
static size_t block_header_size(void) { return align8(sizeof(DndHeapBlock)); }
static DndHeapBlock *first_block(DndManagedHeap *heap) { return (DndHeapBlock *)heap->blocks; }
static DndObject *block_object(DndHeapBlock *block) { return (DndObject *)((uint8_t *)block + block_header_size()); }
static DndHeapBlock *find_block_containing(const DndManagedHeap *heap, const void *pointer) {
    if (!heap || !pointer) return NULL;
    const uint8_t *p = (const uint8_t *)pointer;
    for (DndHeapBlock *block = first_block((DndManagedHeap *)heap); block; block = block->next) {
        const uint8_t *start = (const uint8_t *)block_object(block);
        const uint8_t *end = start + block->size;
        if (!block->free && p >= start && p < end) return block;
    }
    return NULL;
}

static void rebuild_free_list(DndManagedHeap *heap) {
    heap->free_list = NULL;
    DndHeapBlock *block = first_block(heap);
    while (block) {
        if (block->free) {
            while (block->next && block->next->free &&
                   (uint8_t *)block + block_header_size() + block->size == (uint8_t *)block->next) {
                DndHeapBlock *next = block->next;
                block->size += (uint32_t)(block_header_size() + next->size);
                block->next = next->next;
            }
            if (!heap->free_list) heap->free_list = block;
        }
        block = block->next;
    }
}

static DndObject *prepare_block(DndManagedHeap *heap, DndHeapBlock *block, const DndType *type, size_t bytes) {
    size_t original = block->size;
    size_t minimum_tail = block_header_size() + align8(sizeof(DndObject) + 8);
    if (original >= bytes + minimum_tail) {
        DndHeapBlock *tail = (DndHeapBlock *)((uint8_t *)block + block_header_size() + bytes);
        memset(tail, 0, block_header_size());
        tail->size = (uint32_t)(original - bytes - block_header_size());
        tail->free = 1;
        tail->next = block->next;
        block->next = tail;
        block->size = (uint32_t)bytes;
    }
    block->free = 0;
    block->marked = 0;
    DndObject *object = block_object(block);
    memset(object, 0, block->size);
    object->type = type;
    rebuild_free_list(heap);
    return object;
}

static DndObject *allocate_from_free(DndManagedHeap *heap, const DndType *type, size_t bytes) {
    for (DndHeapBlock *block = first_block(heap); block; block = block->next)
        if (block->free && block->size >= bytes) return prepare_block(heap, block, type, bytes);
    return NULL;
}

static DndObject *allocate(DndManagedHeap *heap, const DndType *type, size_t bytes) {
    if (!heap || !type) return NULL;
    bytes = align8(bytes < sizeof(DndObject) ? sizeof(DndObject) : bytes);
    if (gc_stress && heap->blocks) dnd_gc_collect(heap, NULL);
    DndObject *reused = allocate_from_free(heap, type, bytes);
    if (reused) return reused;

    size_t total = block_header_size() + bytes;
    if (total > heap->capacity - heap->used) {
        dnd_gc_collect(heap, NULL);
        reused = allocate_from_free(heap, type, bytes);
        if (reused) return reused;
        if (total > heap->capacity - heap->used) {
            dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY, "Managed heap exhausted.");
            return NULL;
        }
    }

    DndHeapBlock *block = (DndHeapBlock *)(heap->start + heap->used);
    memset(block, 0, block_header_size());
    block->size = (uint32_t)bytes;
    if (!heap->blocks) heap->blocks = block;
    else {
        DndHeapBlock *tail = first_block(heap);
        while (tail->next) tail = tail->next;
        tail->next = block;
    }
    heap->used += total;
    return prepare_block(heap, block, type, bytes);
}

void dnd_managed_heap_init(DndManagedHeap *heap, void *memory, size_t size) {
    heap->start = memory;
    heap->capacity = size;
    heap->used = 0;
    heap->blocks = NULL;
    heap->free_list = NULL;
    heap->collections = 0;
}

DndObject *dnd_object_new(DndManagedHeap *heap, const DndType *type) {
    size_t size = type->instance_size < sizeof(DndObject) ? sizeof(DndObject) : type->instance_size;
    return allocate(heap, type, size);
}

static size_t utf8_ascii_length(const char *text) {
    size_t length = 0;
    while (text && *text++) length++;
    return length;
}

DndString *dnd_string_from_utf8(DndManagedHeap *heap, const char *text) {
    if (!text) return NULL;
    size_t length = utf8_ascii_length(text);
    DndString *string = (DndString *)allocate(heap, &DND_TYPE_STRING,
        sizeof(DndString) + (length + 1) * sizeof(uint16_t));
    if (!string) return NULL;
    string->length = (uint32_t)length;
    for (size_t i = 0; i < length; i++) string->chars[i] = (uint8_t)text[i];
    string->chars[length] = 0;
    return string;
}

DndString *dnd_string_concat(DndManagedHeap *heap, const DndString *a, const DndString *b) {
    uint32_t a_length = a ? a->length : 0;
    uint32_t b_length = b ? b->length : 0;
    DndString *string = (DndString *)allocate(heap, &DND_TYPE_STRING,
        sizeof(DndString) + ((size_t)a_length + b_length + 1) * sizeof(uint16_t));
    if (!string) return NULL;
    string->length = a_length + b_length;
    if (a) memcpy(string->chars, a->chars, (size_t)a_length * sizeof(uint16_t));
    if (b) memcpy(string->chars + a_length, b->chars, (size_t)b_length * sizeof(uint16_t));
    string->chars[string->length] = 0;
    return string;
}

bool dnd_string_equals(const DndString *a, const DndString *b) {
    if (a == b) return true;
    if (!a || !b || a->length != b->length) return false;
    return memcmp(a->chars, b->chars, (size_t)a->length * sizeof(uint16_t)) == 0;
}

DndArray *dnd_managed_array_new_typed(DndManagedHeap *heap, uint32_t length,
    uint32_t element_size, const DndType *element_type, bool references) {
    if (element_size && length > SIZE_MAX / element_size) {
        dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY, "Array size overflow.");
        return NULL;
    }
    DndArray *array = (DndArray *)allocate(heap, &DND_TYPE_ARRAY,
        sizeof(DndArray) + (size_t)length * element_size);
    if (!array) return NULL;
    array->length = length;
    array->element_size = element_size;
    array->element_type = element_type;
    array->elements_are_references = references ? 1 : 0;
    return array;
}

DndArray *dnd_managed_array_new(DndManagedHeap *heap, uint32_t length, uint32_t element_size) {
    return dnd_managed_array_new_typed(heap, length, element_size, NULL, false);
}

void *dnd_managed_array_at(DndArray *array, uint32_t index) {
    if (!array) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Array is null.");
        return NULL;
    }
    if (index >= array->length) {
        dnd_exception_throw(DND_EXCEPTION_INDEX_OUT_OF_RANGE, "Array index out of range.");
        return NULL;
    }
    return array->data + (size_t)index * array->element_size;
}

void *dnd_array_element_address(DndArray *array, uint32_t index) { return dnd_managed_array_at(array, index); }

uint32_t dnd_array_length(DndArray *array) {
    if (!array) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Array is null.");
        return 0;
    }
    return array->length;
}

int32_t dnd_array_load_i32(DndArray *array, uint32_t index) {
    void *address = dnd_managed_array_at(array, index);
    return address ? *(int32_t *)address : 0;
}

DndObject *dnd_array_load_ref(DndArray *array, uint32_t index) {
    void *address = dnd_managed_array_at(array, index);
    return address ? *(DndObject **)address : NULL;
}

bool dnd_array_store_i32(DndArray *array, uint32_t index, int32_t value) {
    void *address = dnd_managed_array_at(array, index);
    if (!address) return false;
    *(int32_t *)address = value;
    return true;
}

bool dnd_array_store_ref(DndArray *array, uint32_t index, DndObject *value) {
    void *address = dnd_managed_array_at(array, index);
    if (!address) return false;
    *(DndObject **)address = value;
    return true;
}

static bool type_reaches(const DndType *actual, const DndType *target, unsigned depth) {
    if (!actual || !target || depth > 64) return false;
    if (actual == target) return true;
    for (uint16_t i = 0; i < actual->interface_count; i++)
        if (type_reaches(actual->interfaces[i], target, depth + 1)) return true;
    return actual->base_type ? type_reaches(actual->base_type, target, depth + 1) : false;
}

bool dnd_type_is_assignable_from(const DndType *target, const DndType *actual) {
    return type_reaches(actual, target, 0);
}

DndObject *dnd_isinst(DndObject *object, const DndType *target) {
    return object && dnd_type_is_assignable_from(target, object->type) ? object : NULL;
}

bool dnd_require_object(const DndObject *object) {
    if (object) return true;
    dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Object reference is null.");
    return false;
}

DndObject *dnd_cast(DndObject *object, const DndType *target) {
    if (!object) return NULL;
    if (dnd_type_is_assignable_from(target, object->type)) return object;
    dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Invalid managed cast.");
    return NULL;
}

DndObject *dnd_box_i32(DndManagedHeap *heap, int32_t value) {
    DndBoxedInt32 *boxed = (DndBoxedInt32 *)dnd_object_new(heap, &DND_TYPE_BOXED_INT32);
    if (boxed) boxed->value = value;
    return (DndObject *)boxed;
}

int32_t dnd_unbox_i32(DndObject *object) {
    if (!object) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Cannot unbox null.");
        return 0;
    }
    if (object->type != &DND_TYPE_BOXED_INT32) {
        dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Boxed value is not System.Int32.");
        return 0;
    }
    return ((DndBoxedInt32 *)object)->value;
}

DndObject *dnd_box_value(DndManagedHeap *heap, const DndType *type, const void *value, uint32_t size) {
    if (!type || !value) return NULL;
    DndObject *boxed = allocate(heap, type, sizeof(DndObject) + size);
    if (boxed) memcpy((uint8_t *)boxed + sizeof(DndObject), value, size);
    return boxed;
}

bool dnd_unbox_value(DndObject *object, const DndType *type, void *value, uint32_t size) {
    if (!object || !value) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Cannot unbox null."); return false; }
    if (object->type != type) { dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Boxed value has the wrong type."); return false; }
    memcpy(value, (uint8_t *)object + sizeof(DndObject), size); return true;
}

void dnd_value_init(void *value, uint32_t size) { if (value) memset(value, 0, size); }
void dnd_value_copy(void *destination, const void *source, uint32_t size) { if (destination && source) memcpy(destination, source, size); }

DndManagedMethod dnd_virtual_resolve(const DndObject *object, uint16_t slot) {
    if (!object) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Virtual call target is null.");
        return NULL;
    }
    if (!object->type || slot >= object->type->vtable_count || !object->type->vtable) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Invalid virtual method slot.");
        return NULL;
    }
    return object->type->vtable[slot];
}

DndManagedMethod dnd_interface_resolve(const DndObject *object, const DndType *interface_type, uint16_t slot) {
    if (!object) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Interface call target is null.");
        return NULL;
    }
    for (const DndType *type = object->type; type; type = type->base_type) {
        for (uint16_t i = 0; i < type->interface_map_count; i++) {
            const DndInterfaceEntry *entry = &type->interface_map[i];
            if (entry->interface_type == interface_type && slot < entry->method_count)
                return entry->methods[slot];
        }
    }
    dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Interface method is not implemented.");
    return NULL;
}

DndDelegate *dnd_delegate_new(DndManagedHeap *heap, void *target, DndDelegateFn method) {
    if (!method) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Delegate method is null.");
        return NULL;
    }
    DndDelegate *delegate = (DndDelegate *)allocate(heap, &DND_TYPE_DELEGATE, sizeof(DndDelegate));
    if (delegate) {
        delegate->target = target;
        delegate->method = method;
        delegate->next = NULL;
    }
    return delegate;
}

void dnd_delegate_invoke(DndDelegate *delegate, void *argument) {
    if (!delegate || !delegate->method) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Delegate is null.");
        return;
    }
    for (DndDelegate *current = delegate; current; current = current->next) current->method(current->target, argument);
}

DndDelegate *dnd_delegate_combine(DndManagedHeap *heap, DndDelegate *first, DndDelegate *second) {
    if (!first) return second;
    if (!second) return first;
    DndObject *first_root = (DndObject *)first;
    DndObject *second_root = (DndObject *)second;
    DndObject *head_root = NULL;
    DndObject **slots[] = { &first_root, &second_root, &head_root };
    DndGcFrame frame;
    dnd_gc_frame_push(&frame, slots, 3);
    DndDelegate *tail = NULL;
    for (DndDelegate *current = first; current; current = current->next) {
        DndDelegate *copy = (DndDelegate *)allocate(heap, &DND_TYPE_DELEGATE, sizeof(DndDelegate));
        if (!copy) {
            dnd_gc_frame_pop(&frame);
            return NULL;
        }
        copy->target = current->target;
        copy->method = current->method;
        copy->managed_method = current->managed_method;
        copy->managed_has_target = current->managed_has_target;
        if (tail) tail->next = copy;
        else head_root = (DndObject *)copy;
        tail = copy;
    }
    tail->next = second;
    dnd_gc_frame_pop(&frame);
    return (DndDelegate *)head_root;
}

DndDelegate *dnd_delegate_remove(DndDelegate *source, DndDelegate *value) {
    if (!source || !value) return source;
    DndDelegate *previous = NULL;
    for (DndDelegate *p = source; p; previous = p, p = p->next) if (p->target == value->target && p->method == value->method && p->managed_method == value->managed_method && p->managed_has_target == value->managed_has_target) { if (previous) previous->next = p->next; else source = p->next; break; }
    return source;
}

DndDelegate *dnd_managed_delegate_new(DndManagedHeap *heap, DndObject *target, DndManagedMethod method, bool has_target) {
    if (!method || (has_target && !target)) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Delegate method or instance target is null.");
        return NULL;
    }
    DndObject **slots[] = { &target };
    DndGcFrame frame;
    dnd_gc_frame_push(&frame, slots, 1);
    DndDelegate *delegate = (DndDelegate *)allocate(heap, &DND_TYPE_DELEGATE, sizeof(DndDelegate));
    if (delegate) {
        delegate->target = target;
        delegate->managed_method = method;
        delegate->managed_has_target = has_target ? 1 : 0;
    }
    dnd_gc_frame_pop(&frame);
    return delegate;
}

intptr_t dnd_managed_delegate_invoke(DndDelegate *delegate, intptr_t *arguments, uint16_t argument_count) {
    if (!delegate || !delegate->managed_method) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Managed delegate is null.");
        return 0;
    }
    if (argument_count > 256 || (argument_count && !arguments)) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Invalid delegate argument buffer or count.");
        return 0;
    }
    /* Keep the whole invocation list and its targets alive during managed calls. */
    DndObject *root = (DndObject *)delegate;
    DndObject **slots[] = { &root };
    DndGcFrame frame;
    dnd_gc_frame_push(&frame, slots, 1);
    intptr_t result = 0;
    intptr_t call_args[257];
    for (DndDelegate *current = delegate; current; current = current->next) {
        if (!current->managed_method) {
            dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Cannot invoke a native callback as a managed delegate.");
            break;
        }
        if (current->managed_has_target) {
            call_args[0] = (intptr_t)current->target;
            for (uint16_t i = 0; i < argument_count; i++) call_args[i + 1u] = arguments[i];
            result = current->managed_method(call_args);
        } else {
            result = current->managed_method(arguments);
        }
        if (dnd_exception_kind() != DND_EXCEPTION_NONE) break;
    }
    dnd_gc_frame_pop(&frame);
    return result;
}

void dnd_roots_init(DndRootSet *roots, DndObject ***storage, size_t capacity) {
    roots->slots = storage;
    roots->count = 0;
    roots->capacity = capacity;
}

bool dnd_root_add(DndRootSet *roots, DndObject **slot) {
    if (!roots || !slot || roots->count == roots->capacity) return false;
    roots->slots[roots->count++] = slot;
    return true;
}

void dnd_gc_frame_push(DndGcFrame *frame, DndObject ***slots, size_t count) {
    frame->slots = slots;
    frame->count = count;
    frame->previous = gc_frames;
    gc_frames = frame;
}

void dnd_gc_frame_pop(DndGcFrame *frame) {
    if (gc_frames == frame) gc_frames = frame->previous;
}

static void mark_object(DndManagedHeap *heap, DndObject *object) {
    if (!object) return;
    DndHeapBlock *block = find_block_containing(heap, object);
    if (!block) return;
    object = block_object(block);
    if (block->free || block->marked || !object->type) return;
    block->marked = 1;
    const DndType *type = object->type;
    for (const DndType *current = type; current; current = current->base_type) {
        for (uint16_t i = 0; i < current->reference_count; i++) {
            uint32_t offset = current->reference_offsets[i];
            if (offset + sizeof(void *) <= block->size)
                mark_object(heap, *(DndObject **)((uint8_t *)object + offset));
        }
    }
    if ((type->flags & DND_TYPE_FLAG_ARRAY) != 0) {
        DndArray *array = (DndArray *)object;
        if (array->elements_are_references) {
            for (uint32_t i = 0; i < array->length; i++)
                mark_object(heap, *(DndObject **)(array->data + (size_t)i * array->element_size));
        } else if (array->element_type) {
            for (uint32_t i = 0; i < array->length; i++) {
                uint8_t *element = array->data + (size_t)i * array->element_size;
                for (const DndType *current = array->element_type; current; current = current->base_type)
                    for (uint16_t r = 0; r < current->reference_count; r++) {
                        uint32_t offset = current->reference_offsets[r];
                        if ((array->element_type->flags & DND_TYPE_FLAG_VALUE_TYPE) && offset >= sizeof(DndObject)) offset -= sizeof(DndObject);
                        if (offset + sizeof(void *) <= array->element_size)
                            mark_object(heap, *(DndObject **)(element + offset));
                    }
            }
        }
    }
}

void dnd_gc_set_stress(bool enabled) { gc_stress = enabled; }

void dnd_gc_collect(DndManagedHeap *heap, const DndRootSet *roots) {
    if (!heap) return;
    heap->collections++;
    for (DndHeapBlock *block = first_block(heap); block; block = block->next) block->marked = 0;
    if (roots)
        for (size_t i = 0; i < roots->count; i++)
            if (roots->slots[i]) mark_object(heap, *roots->slots[i]);
    if (exception_object) mark_object(heap, (DndObject *)exception_object);
    for (DndGcFrame *frame = gc_frames; frame; frame = frame->previous)
        for (size_t i = 0; i < frame->count; i++)
            if (frame->slots[i]) mark_object(heap, *frame->slots[i]);

    for (DndHeapBlock *block = first_block(heap); block; block = block->next)
        if (!block->free && !block->marked) {
            block->free = 1;
            block_object(block)->type = NULL;
        }

    rebuild_free_list(heap);
    while (heap->blocks) {
        DndHeapBlock *last = first_block(heap);
        DndHeapBlock *before = NULL;
        while (last->next) { before = last; last = last->next; }
        if (!last->free) break;
        heap->used = (size_t)((uint8_t *)last - heap->start);
        if (before) before->next = NULL;
        else heap->blocks = NULL;
    }
    rebuild_free_list(heap);
}

void dnd_exception_clear(void) { exception_kind = DND_EXCEPTION_NONE; exception_text = NULL; exception_object = NULL; }
void dnd_exception_enter_handler(void) { exception_kind = DND_EXCEPTION_NONE; exception_text = NULL; }
void dnd_exception_throw(DndExceptionKind kind, const char *message) { exception_kind = kind; exception_text = message; exception_object = NULL; }
void dnd_throw(DndObject *exception) { if (!exception) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Thrown exception is null."); return; } dnd_exception_throw_object((DndExceptionObject *)exception); }
void dnd_exception_throw_object(DndExceptionObject *exception) { exception_object=exception; exception_kind=exception?(DndExceptionKind)exception->kind:DND_EXCEPTION_NONE; exception_text=NULL; }
DndExceptionObject *dnd_exception_object(void) { return exception_object; }
DndExceptionKind dnd_exception_kind(void) { return exception_kind; }
const char *dnd_exception_message(void) { return exception_text ? exception_text : ""; }
