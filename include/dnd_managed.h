#ifndef DND_MANAGED_H
#define DND_MANAGED_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef struct DndType DndType;
typedef struct DndObject DndObject;
typedef struct DndString DndString;
typedef struct DndArray DndArray;
typedef struct DndDelegate DndDelegate;
typedef struct DndExceptionObject DndExceptionObject;
typedef struct DndInterfaceEntry DndInterfaceEntry;

typedef void (*DndFinalizer)(DndObject *);
typedef void (*DndDelegateFn)(void *target, void *argument);
typedef intptr_t (*DndManagedMethod)(intptr_t *arguments);

enum {
    DND_TYPE_FLAG_NONE = 0,
    DND_TYPE_FLAG_ARRAY = 1u << 0,
    DND_TYPE_FLAG_VALUE_TYPE = 1u << 1
};

struct DndInterfaceEntry {
    const DndType *interface_type;
    uint16_t method_count;
    const DndManagedMethod *methods;
};

struct DndType {
    const char *name;
    const DndType *base_type;
    uint32_t instance_size;
    uint16_t interface_count;
    const DndType *const *interfaces;
    uint16_t reference_count;
    const uint32_t *reference_offsets;
    uint16_t flags;
    uint16_t vtable_count;
    const DndManagedMethod *vtable;
    uint16_t interface_map_count;
    const DndInterfaceEntry *interface_map;
};

struct DndObject {
    const DndType *type;
    uint32_t gc;
};

struct DndString {
    DndObject object;
    uint32_t length;
    uint16_t chars[];
};

struct DndArray {
    DndObject object;
    uint32_t length;
    uint32_t element_size;
    const DndType *element_type;
    uint8_t elements_are_references;
    uint8_t reserved[3];
    uint8_t data[];
};

struct DndExceptionObject {
    DndObject object;
    int32_t kind;
    DndString *message;
};

struct DndDelegate {
    DndObject object;
    void *target;
    DndDelegateFn method;
    DndDelegate *next;
    DndManagedMethod managed_method;
    uint8_t managed_has_target;
};

typedef struct {
    uint8_t *start;
    size_t capacity;
    size_t used;
    void *blocks;
    void *free_list;
    size_t collections;
} DndManagedHeap;

typedef struct {
    DndObject ***slots;
    size_t count;
    size_t capacity;
} DndRootSet;

typedef enum {
    DND_EXCEPTION_NONE = 0,
    DND_EXCEPTION_NULL_REFERENCE,
    DND_EXCEPTION_INDEX_OUT_OF_RANGE,
    DND_EXCEPTION_INVALID_CAST,
    DND_EXCEPTION_OUT_OF_MEMORY,
    DND_EXCEPTION_ARGUMENT,
    DND_EXCEPTION_MANAGED
} DndExceptionKind;

extern const DndType DND_TYPE_OBJECT;
extern const DndType DND_TYPE_STRING;
extern const DndType DND_TYPE_ARRAY;
extern const DndType DND_TYPE_DELEGATE;
extern const DndType DND_TYPE_BOXED_INT32;
extern const DndType DND_TYPE_EXCEPTION;

void dnd_managed_heap_init(DndManagedHeap *heap, void *memory, size_t size);
DndObject *dnd_object_new(DndManagedHeap *heap, const DndType *type);
DndString *dnd_string_from_utf8(DndManagedHeap *heap, const char *text);
DndString *dnd_string_concat(DndManagedHeap *heap, const DndString *a, const DndString *b);
bool dnd_string_equals(const DndString *a, const DndString *b);
uint32_t dnd_string_hash(const DndString *value);
int32_t dnd_string_index_of(const DndString *value, const DndString *needle);
DndString *dnd_string_substring(DndManagedHeap *heap, const DndString *value, uint32_t start, uint32_t length);
DndArray *dnd_managed_array_new(DndManagedHeap *heap, uint32_t length, uint32_t element_size);
DndArray *dnd_managed_array_new_typed(DndManagedHeap *heap, uint32_t length, uint32_t element_size, const DndType *element_type, bool elements_are_references);
void *dnd_managed_array_at(DndArray *array, uint32_t index);
uint32_t dnd_array_length(DndArray *array);
void *dnd_array_element_address(DndArray *array, uint32_t index);
int32_t dnd_array_load_i32(DndArray *array, uint32_t index);
DndObject *dnd_array_load_ref(DndArray *array, uint32_t index);
bool dnd_array_store_i32(DndArray *array, uint32_t index, int32_t value);
bool dnd_array_store_ref(DndArray *array, uint32_t index, DndObject *value);
bool dnd_array_clear(DndArray *array, uint32_t index, uint32_t length);
bool dnd_array_copy(DndArray *source, uint32_t source_index, DndArray *destination, uint32_t destination_index, uint32_t length);
bool dnd_object_reference_equals(const DndObject *a, const DndObject *b);
uint32_t dnd_object_hash(const DndObject *object);
const DndType *dnd_object_get_type(const DndObject *object);
bool dnd_type_is_assignable_from(const DndType *target, const DndType *actual);
DndObject *dnd_cast(DndObject *object, const DndType *target);
DndObject *dnd_isinst(DndObject *object, const DndType *target);
bool dnd_require_object(const DndObject *object);
DndObject *dnd_box_i32(DndManagedHeap *heap, int32_t value);
int32_t dnd_unbox_i32(DndObject *object);
DndObject *dnd_box_value(DndManagedHeap *heap, const DndType *type, const void *value, uint32_t size);
bool dnd_unbox_value(DndObject *object, const DndType *type, void *value, uint32_t size);
void dnd_value_init(void *value, uint32_t size);
void dnd_value_copy(void *destination, const void *source, uint32_t size);
DndManagedMethod dnd_virtual_resolve(const DndObject *object, uint16_t slot);
DndManagedMethod dnd_interface_resolve(const DndObject *object, const DndType *interface_type, uint16_t slot);
DndDelegate *dnd_delegate_new(DndManagedHeap *heap, void *target, DndDelegateFn method);
void dnd_delegate_invoke(DndDelegate *delegate, void *argument);
DndDelegate *dnd_delegate_combine(DndManagedHeap *heap, DndDelegate *first, DndDelegate *second);
DndDelegate *dnd_delegate_remove(DndDelegate *source, DndDelegate *value);
DndDelegate *dnd_managed_delegate_new(DndManagedHeap *heap, DndObject *target, DndManagedMethod method, bool has_target);
intptr_t dnd_managed_delegate_invoke(DndDelegate *delegate, intptr_t *arguments, uint16_t argument_count);

void dnd_roots_init(DndRootSet *roots, DndObject ***storage, size_t capacity);
bool dnd_root_add(DndRootSet *roots, DndObject **slot);
void dnd_gc_collect(DndManagedHeap *heap, const DndRootSet *roots);
void dnd_gc_set_stress(bool enabled);

typedef struct DndGcFrame {
    DndObject ***slots;
    size_t count;
    struct DndGcFrame *previous;
} DndGcFrame;
void dnd_gc_frame_push(DndGcFrame *frame, DndObject ***slots, size_t count);
void dnd_gc_frame_pop(DndGcFrame *frame);

void dnd_exception_clear(void);
void dnd_exception_enter_handler(void);
void dnd_exception_throw(DndExceptionKind kind, const char *message);
DndExceptionKind dnd_exception_kind(void);
const char *dnd_exception_message(void);
DndExceptionObject *dnd_exception_object(void);
void dnd_exception_throw_object(DndExceptionObject *exception);
void dnd_throw(DndObject *exception);
#endif
