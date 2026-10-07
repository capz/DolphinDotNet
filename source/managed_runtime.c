#include "dnd_managed.h"
#include <string.h>

const DndType DND_TYPE_OBJECT = {"System.Object", NULL, sizeof(DndObject), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_STRING = {"System.String", &DND_TYPE_OBJECT, sizeof(DndString), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_ARRAY = {"System.Array", &DND_TYPE_OBJECT, sizeof(DndArray), 0, NULL, 0, NULL, DND_TYPE_FLAG_ARRAY, 0, NULL, 0, NULL};
static const uint32_t delegate_refs[] = {(uint32_t)offsetof(DndDelegate, target)};
const DndType DND_TYPE_DELEGATE = {"System.Delegate", &DND_TYPE_OBJECT, sizeof(DndDelegate), 0, NULL, 1, delegate_refs, 0, 0, NULL, 0, NULL};
typedef struct { DndObject object; int32_t value; } DndBoxedInt32;
const DndType DND_TYPE_BOXED_INT32 = {"System.Int32", &DND_TYPE_OBJECT, sizeof(DndBoxedInt32), 0, NULL, 0, NULL, DND_TYPE_FLAG_VALUE_TYPE, 0, NULL, 0, NULL};
#define DND_SCALAR_TYPE(symbol,name,size) const DndType symbol = {name, &DND_TYPE_OBJECT, sizeof(DndObject)+(size), 0, NULL, 0, NULL, DND_TYPE_FLAG_VALUE_TYPE, 0, NULL, 0, NULL}
DND_SCALAR_TYPE(DND_TYPE_BOOLEAN,"System.Boolean",1);
DND_SCALAR_TYPE(DND_TYPE_BYTE,"System.Byte",1);
DND_SCALAR_TYPE(DND_TYPE_SBYTE,"System.SByte",1);
DND_SCALAR_TYPE(DND_TYPE_CHAR,"System.Char",2);
DND_SCALAR_TYPE(DND_TYPE_INT16,"System.Int16",2);
DND_SCALAR_TYPE(DND_TYPE_UINT16,"System.UInt16",2);
DND_SCALAR_TYPE(DND_TYPE_UINT32,"System.UInt32",4);
DND_SCALAR_TYPE(DND_TYPE_INT64,"System.Int64",8);

static const uint32_t exception_refs[] = {(uint32_t)offsetof(DndException, message)};
const DndType DND_TYPE_EXCEPTION = {"System.Exception", &DND_TYPE_OBJECT, sizeof(DndException), 0, NULL, 1, exception_refs, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_SYSTEM_EXCEPTION = {"System.SystemException", &DND_TYPE_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_INVALID_OPERATION_EXCEPTION = {"System.InvalidOperationException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_ARGUMENT_EXCEPTION = {"System.ArgumentException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_ARGUMENT_NULL_EXCEPTION = {"System.ArgumentNullException", &DND_TYPE_ARGUMENT_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_ARGUMENT_OUT_OF_RANGE_EXCEPTION = {"System.ArgumentOutOfRangeException", &DND_TYPE_ARGUMENT_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_INDEX_OUT_OF_RANGE_EXCEPTION = {"System.IndexOutOfRangeException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_NULL_REFERENCE_EXCEPTION = {"System.NullReferenceException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_INVALID_CAST_EXCEPTION = {"System.InvalidCastException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_NOT_SUPPORTED_EXCEPTION = {"System.NotSupportedException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
const DndType DND_TYPE_OUT_OF_MEMORY_EXCEPTION = {"System.OutOfMemoryException", &DND_TYPE_SYSTEM_EXCEPTION, sizeof(DndException), 0, NULL, 0, NULL, 0, 0, NULL, 0, NULL};
static DndException builtin_exception = {{ &DND_TYPE_EXCEPTION, 0 }, NULL};

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
static bool exception_is_pending;
static DndObject *exception_object;
static DndEhFrame *eh_frames;
static const char *exception_text;

static size_t align8(size_t n) { return (n + 7u) & ~(size_t)7u; }
static size_t block_header_size(void) { return align8(sizeof(DndHeapBlock)); }
static DndHeapBlock *first_block(DndManagedHeap *heap) { return (DndHeapBlock *)heap->blocks; }
static DndObject *block_object(DndHeapBlock *block) { return (DndObject *)((uint8_t *)block + block_header_size()); }
static DndHeapBlock *object_block(DndObject *object) { return (DndHeapBlock *)((uint8_t *)object - block_header_size()); }

static bool in_heap(const DndManagedHeap *heap, const DndObject *object) {
    const uint8_t *p = (const uint8_t *)object;
    return heap && p >= heap->start + block_header_size() && p < heap->start + heap->used;
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

uint16_t dnd_string_char_at(const DndString *value, int32_t index) {
    if (!value) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "String is null."); return 0; }
    if (index < 0 || (uint32_t)index >= value->length) { dnd_exception_throw(DND_EXCEPTION_INDEX_OUT_OF_RANGE, "String index out of range."); return 0; }
    return value->chars[index];
}

bool dnd_string_starts_with(const DndString *value, const DndString *prefix) {
    if (!value) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "String is null."); return false; }
    if (!prefix) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Prefix is null."); return false; }
    return prefix->length <= value->length && memcmp(value->chars, prefix->chars, (size_t)prefix->length * sizeof(uint16_t)) == 0;
}

bool dnd_string_ends_with(const DndString *value, const DndString *suffix) {
    if (!value) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "String is null."); return false; }
    if (!suffix) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Suffix is null."); return false; }
    return suffix->length <= value->length && memcmp(value->chars + value->length - suffix->length, suffix->chars, (size_t)suffix->length * sizeof(uint16_t)) == 0;
}

int32_t dnd_string_index_of(const DndString *value, const DndString *needle) {
    if (!value) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "String is null."); return -1; }
    if (!needle) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Search string is null."); return -1; }
    if (needle->length == 0) return 0;
    if (needle->length > value->length) return -1;
    uint32_t last = value->length - needle->length;
    for (uint32_t i = 0; i <= last; i++)
        if (memcmp(value->chars + i, needle->chars, (size_t)needle->length * sizeof(uint16_t)) == 0) return (int32_t)i;
    return -1;
}

DndString *dnd_string_substring(DndManagedHeap *heap, const DndString *value, int32_t start, int32_t length) {
    if (!value) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "String is null."); return NULL; }
    if (start < 0 || length < 0 || (uint32_t)start > value->length || (uint32_t)length > value->length - (uint32_t)start) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Substring range is invalid."); return NULL;
    }
    DndString *result = (DndString *)allocate(heap, &DND_TYPE_STRING, sizeof(DndString) + ((size_t)length + 1) * sizeof(uint16_t));
    if (!result) return NULL;
    result->length = (uint32_t)length;
    if (length) memcpy(result->chars, value->chars + start, (size_t)length * sizeof(uint16_t));
    result->chars[length] = 0;
    return result;
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

uint64_t dnd_array_load_scalar(DndArray *array, uint32_t index, uint32_t size, bool sign_extend) {
    void *address = dnd_managed_array_at(array, index);
    if (!address || size == 0 || size > 8 || size > array->element_size) return 0;
    switch (size) {
        case 1: { uint8_t v; memcpy(&v, address, 1); return sign_extend ? (uint64_t)(int64_t)(int8_t)v : v; }
        case 2: { uint16_t v; memcpy(&v, address, 2); return sign_extend ? (uint64_t)(int64_t)(int16_t)v : v; }
        case 4: { uint32_t v; memcpy(&v, address, 4); return sign_extend ? (uint64_t)(int64_t)(int32_t)v : v; }
        case 8: { uint64_t v; memcpy(&v, address, 8); return v; }
        default: return 0;
    }
}

int32_t dnd_array_load_i32(DndArray *array, uint32_t index) {
    return (int32_t)dnd_array_load_scalar(array, index, 4u, true);
}

DndObject *dnd_array_load_ref(DndArray *array, uint32_t index) {
    void *address = dnd_managed_array_at(array, index);
    return address ? *(DndObject **)address : NULL;
}

bool dnd_array_store_scalar(DndArray *array, uint32_t index, uint64_t value, uint32_t size) {
    void *address = dnd_managed_array_at(array, index);
    if (!address || size == 0 || size > 8 || size > array->element_size) return false;
    switch (size) {
        case 1: { uint8_t v = (uint8_t)value; memcpy(address, &v, 1); break; }
        case 2: { uint16_t v = (uint16_t)value; memcpy(address, &v, 2); break; }
        case 4: { uint32_t v = (uint32_t)value; memcpy(address, &v, 4); break; }
        case 8: { uint64_t v = value; memcpy(address, &v, 8); break; }
        default: return false;
    }
    return true;
}

bool dnd_array_store_i32(DndArray *array, uint32_t index, int32_t value) {
    return dnd_array_store_scalar(array, index, (uint32_t)value, 4u);
}

bool dnd_array_store_ref(DndArray *array, uint32_t index, DndObject *value) {
    void *address = dnd_managed_array_at(array, index);
    if (!address) return false;
    if (value && array->element_type && !dnd_type_is_assignable_from(array->element_type, value->type)) {
        dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Array element type mismatch.");
        return false;
    }
    *(DndObject **)address = value;
    return true;
}

int64_t dnd_array_long_length(DndArray *array) { return (int64_t)dnd_array_length(array); }

static bool dnd_array_dimension_ok(DndArray *array, int32_t dimension) {
    if (!array) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Array is null."); return false; }
    if (dimension != 0) { dnd_exception_throw(DND_EXCEPTION_INDEX_OUT_OF_RANGE, "Array dimension out of range."); return false; }
    return true;
}

int32_t dnd_array_get_length(DndArray *array, int32_t dimension) {
    return dnd_array_dimension_ok(array, dimension) ? (int32_t)array->length : 0;
}

int32_t dnd_array_get_lower_bound(DndArray *array, int32_t dimension) {
    return dnd_array_dimension_ok(array, dimension) ? 0 : 0;
}

int32_t dnd_array_get_upper_bound(DndArray *array, int32_t dimension) {
    return dnd_array_dimension_ok(array, dimension) ? (int32_t)array->length - 1 : -1;
}

bool dnd_array_clear(DndArray *array, int32_t index, int32_t length) {
    if (!array) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Array is null."); return false; }
    if (index < 0 || length < 0 || (uint32_t)index > array->length || (uint32_t)length > array->length - (uint32_t)index) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Array range out of bounds."); return false;
    }
    memset(array->data + (size_t)(uint32_t)index * array->element_size, 0, (size_t)(uint32_t)length * array->element_size);
    return true;
}

bool dnd_array_copy(DndArray *source, int32_t source_index, DndArray *destination, int32_t destination_index, int32_t length) {
    if (!source || !destination) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Array is null."); return false; }
    if (source_index < 0 || destination_index < 0 || length < 0 ||
        (uint32_t)source_index > source->length || (uint32_t)length > source->length - (uint32_t)source_index ||
        (uint32_t)destination_index > destination->length || (uint32_t)length > destination->length - (uint32_t)destination_index ||
        source->element_size != destination->element_size) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Array copy range or element size mismatch."); return false;
    }
    if (source->elements_are_references && destination->elements_are_references && destination->element_type) {
        for (int32_t i = 0; i < length; i++) {
            DndObject *value = *(DndObject **)(source->data + (size_t)(source_index + i) * source->element_size);
            if (value && !dnd_type_is_assignable_from(destination->element_type, value->type)) {
                dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Array element type mismatch."); return false;
            }
        }
    } else if (source->elements_are_references != destination->elements_are_references) {
        dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Array element type mismatch."); return false;
    }
    memmove(destination->data + (size_t)(uint32_t)destination_index * destination->element_size,
            source->data + (size_t)(uint32_t)source_index * source->element_size,
            (size_t)(uint32_t)length * source->element_size);
    return true;
}

int32_t dnd_array_index_of(DndArray *array, uint64_t value, uint32_t size, bool reference, int32_t start, int32_t count) {
    if (!array) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Array is null."); return -1; }
    if (start < 0 || count < 0 || (uint32_t)start > array->length || (uint32_t)count > array->length - (uint32_t)start) {
        dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Array search range out of bounds."); return -1;
    }
    if (reference) {
        DndObject *needle = (DndObject *)(uintptr_t)value;
        for (int32_t i = 0; i < count; i++)
            if (*(DndObject **)(array->data + (size_t)(start + i) * array->element_size) == needle) return start + i;
        return -1;
    }
    if (size == 0 || size > 8 || size > array->element_size) return -1;
    uint8_t needle[8] = {0};
    switch (size) {
        case 1: { uint8_t v=(uint8_t)value; memcpy(needle,&v,1); break; }
        case 2: { uint16_t v=(uint16_t)value; memcpy(needle,&v,2); break; }
        case 4: { uint32_t v=(uint32_t)value; memcpy(needle,&v,4); break; }
        case 8: { uint64_t v=value; memcpy(needle,&v,8); break; }
        default: return -1;
    }
    for (int32_t i = 0; i < count; i++)
        if (memcmp(array->data + (size_t)(start + i) * array->element_size, needle, size) == 0) return start + i;
    return -1;
}

bool dnd_type_is_assignable_from(const DndType *target, const DndType *actual) {
    if (!target || !actual) return false;
    for (const DndType *type = actual; type; type = type->base_type)
        if (type == target) return true;
    for (const DndType *type = actual; type; type = type->base_type)
        for (uint16_t i = 0; i < type->interface_count; i++)
            if (type->interfaces[i] == target) return true;
    return false;
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

DndObject *dnd_box_scalar(DndManagedHeap *heap, const DndType *type, uint64_t value, uint32_t size) {
    if (!type || size == 0 || size > 8) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Unsupported scalar box size."); return NULL; }
    DndObject *object = allocate(heap, type, sizeof(DndObject) + size);
    if (!object) return NULL;
    memcpy((uint8_t *)object + sizeof(DndObject), &value, size);
    return object;
}

uint64_t dnd_unbox_scalar(DndObject *object, const DndType *type, uint32_t size) {
    uint64_t value = 0;
    if (!object) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Cannot unbox null."); return 0; }
    if (object->type != type || size == 0 || size > 8) { dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Boxed scalar type mismatch."); return 0; }
    memcpy(&value, (uint8_t *)object + sizeof(DndObject), size);
    return value;
}

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
    }
    return delegate;
}

void dnd_delegate_invoke(DndDelegate *delegate, void *argument) {
    if (!delegate || !delegate->method) {
        dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Delegate is null.");
        return;
    }
    delegate->method(delegate->target, argument);
}

DndDelegate *dnd_managed_delegate_new(DndManagedHeap *heap, DndObject *target, DndManagedMethod method, bool has_target) {
    if (!method) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Delegate method is null."); return NULL; }
    DndDelegate *delegate = (DndDelegate *)allocate(heap, &DND_TYPE_DELEGATE, sizeof(DndDelegate));
    if (!delegate) return NULL;
    delegate->target = target; delegate->managed_method = method; delegate->managed_has_target = has_target ? 1 : 0;
    return delegate;
}

intptr_t dnd_managed_delegate_invoke(DndDelegate *delegate, intptr_t *arguments, uint16_t argument_count) {
    if (!delegate || !delegate->managed_method) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Managed delegate is null."); return 0; }
    if (!delegate->managed_has_target) return delegate->managed_method(arguments);
    intptr_t call_args[argument_count + 1u]; call_args[0] = (intptr_t)delegate->target;
    for (uint16_t i=0;i<argument_count;i++) call_args[i+1u]=arguments[i];
    return delegate->managed_method(call_args);
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
    if (!object || !in_heap(heap, object)) return;
    DndHeapBlock *block = object_block(object);
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
    if (type == &DND_TYPE_ARRAY) {
        DndArray *array = (DndArray *)object;
        if (array->elements_are_references) {
            for (uint32_t i = 0; i < array->length; i++)
                mark_object(heap, *(DndObject **)(array->data + (size_t)i * array->element_size));
        } else if (array->element_type && array->element_type->reference_count) {
            for (uint32_t i = 0; i < array->length; i++)
                for (uint16_t r = 0; r < array->element_type->reference_count; r++)
                    mark_object(heap, *(DndObject **)(array->data + (size_t)i * array->element_size +
                        array->element_type->reference_offsets[r] - sizeof(DndObject)));
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

void dnd_eh_push(DndEhFrame *frame) {
    frame->previous = eh_frames;
    frame->gc_snapshot = gc_frames;
    eh_frames = frame;
}

void dnd_eh_pop(DndEhFrame *frame) {
    if (eh_frames == frame) eh_frames = frame->previous;
}

bool dnd_exception_pending(void) { return exception_is_pending; }
DndObject *dnd_exception_object(void) { return exception_object; }
DndException *dnd_exception_new(DndManagedHeap *heap, const DndType *type, DndString *message) {
    DndException *exception=(DndException *)allocate(heap,type?type:&DND_TYPE_EXCEPTION,sizeof(DndException));
    if(exception) exception->message=message;
    return exception;
}
DndString *dnd_exception_get_message(DndException *exception) { return exception?exception->message:NULL; }
bool dnd_exception_matches(const DndType *type) { return exception_object && type && dnd_type_is_assignable_from(type, exception_object->type); }
void dnd_exception_begin_catch(void) { exception_is_pending = false; }

void dnd_exception_clear(void) { exception_kind = DND_EXCEPTION_NONE; exception_text = NULL; exception_object = NULL; exception_is_pending = false; }

void dnd_exception_rethrow(void) {
    if (exception_kind == DND_EXCEPTION_NONE) return;
    exception_is_pending = true;
    if (!eh_frames) return;
    DndEhFrame *target = eh_frames;
    eh_frames = target->previous;
    gc_frames = target->gc_snapshot;
    longjmp(target->environment, 1);
}

void dnd_exception_rethrow_current(void) { dnd_exception_rethrow(); }

void dnd_exception_throw_object(DndObject *exception) {
    if (!exception) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Cannot throw null."); return; }
    exception_kind = DND_EXCEPTION_ARGUMENT;
    exception_text = exception->type ? exception->type->name : "System.Exception";
    exception_object = exception;
    exception_is_pending = true;
    dnd_exception_rethrow();
}

void dnd_exception_throw(DndExceptionKind kind, const char *message) {
    exception_kind = kind;
    exception_text = message;
    const DndType *type = kind==DND_EXCEPTION_NULL_REFERENCE?&DND_TYPE_NULL_REFERENCE_EXCEPTION:
        kind==DND_EXCEPTION_INDEX_OUT_OF_RANGE?&DND_TYPE_INDEX_OUT_OF_RANGE_EXCEPTION:
        kind==DND_EXCEPTION_INVALID_CAST?&DND_TYPE_INVALID_CAST_EXCEPTION:
        kind==DND_EXCEPTION_OUT_OF_MEMORY?&DND_TYPE_OUT_OF_MEMORY_EXCEPTION:
        kind==DND_EXCEPTION_ARGUMENT?&DND_TYPE_ARGUMENT_EXCEPTION:
        kind==DND_EXCEPTION_INVALID_OPERATION?&DND_TYPE_INVALID_OPERATION_EXCEPTION:&DND_TYPE_EXCEPTION;
    builtin_exception.object.type = type;
    builtin_exception.message = NULL;
    exception_object = (DndObject *)&builtin_exception;
    exception_is_pending = true;
    dnd_exception_rethrow();
}
DndExceptionKind dnd_exception_kind(void) { return exception_kind; }
const char *dnd_exception_message(void) { return exception_text ? exception_text : ""; }
