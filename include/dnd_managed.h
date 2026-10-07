#ifndef DND_MANAGED_H
#define DND_MANAGED_H
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <setjmp.h>

typedef struct DndType DndType;
typedef struct DndObject DndObject;
typedef struct DndString DndString;
typedef struct DndArray DndArray;
typedef struct DndDelegate DndDelegate;
typedef struct DndException DndException;
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

struct DndException {
    DndObject object;
    DndString *message;
};

struct DndDelegate {
    DndObject object;
    void *target;
    DndDelegateFn method;
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
    DND_EXCEPTION_INVALID_OPERATION
} DndExceptionKind;

extern const DndType DND_TYPE_OBJECT;
extern const DndType DND_TYPE_STRING;
extern const DndType DND_TYPE_ARRAY;
extern const DndType DND_TYPE_DELEGATE;
extern const DndType DND_TYPE_BOXED_INT32;
extern const DndType DND_TYPE_BOOLEAN;
extern const DndType DND_TYPE_BYTE;
extern const DndType DND_TYPE_SBYTE;
extern const DndType DND_TYPE_CHAR;
extern const DndType DND_TYPE_INT16;
extern const DndType DND_TYPE_UINT16;
extern const DndType DND_TYPE_UINT32;
extern const DndType DND_TYPE_INT64;
extern const DndType DND_TYPE_EXCEPTION;
extern const DndType DND_TYPE_SYSTEM_EXCEPTION;
extern const DndType DND_TYPE_INVALID_OPERATION_EXCEPTION;
extern const DndType DND_TYPE_ARGUMENT_EXCEPTION;
extern const DndType DND_TYPE_ARGUMENT_NULL_EXCEPTION;
extern const DndType DND_TYPE_ARGUMENT_OUT_OF_RANGE_EXCEPTION;
extern const DndType DND_TYPE_INDEX_OUT_OF_RANGE_EXCEPTION;
extern const DndType DND_TYPE_NULL_REFERENCE_EXCEPTION;
extern const DndType DND_TYPE_INVALID_CAST_EXCEPTION;
extern const DndType DND_TYPE_NOT_SUPPORTED_EXCEPTION;
extern const DndType DND_TYPE_OUT_OF_MEMORY_EXCEPTION;

void dnd_managed_heap_init(DndManagedHeap *heap, void *memory, size_t size);
DndObject *dnd_object_new(DndManagedHeap *heap, const DndType *type);
DndString *dnd_string_from_utf8(DndManagedHeap *heap, const char *text);
DndString *dnd_string_concat(DndManagedHeap *heap, const DndString *a, const DndString *b);
bool dnd_string_equals(const DndString *a, const DndString *b);
DndArray *dnd_managed_array_new(DndManagedHeap *heap, uint32_t length, uint32_t element_size);
DndArray *dnd_managed_array_new_typed(DndManagedHeap *heap, uint32_t length, uint32_t element_size, const DndType *element_type, bool elements_are_references);
void *dnd_managed_array_at(DndArray *array, uint32_t index);
uint32_t dnd_array_length(DndArray *array);
void *dnd_array_element_address(DndArray *array, uint32_t index);
int32_t dnd_array_load_i32(DndArray *array, uint32_t index);
uint64_t dnd_array_load_scalar(DndArray *array, uint32_t index, uint32_t size, bool sign_extend);
DndObject *dnd_array_load_ref(DndArray *array, uint32_t index);
bool dnd_array_store_i32(DndArray *array, uint32_t index, int32_t value);
bool dnd_array_store_scalar(DndArray *array, uint32_t index, uint64_t value, uint32_t size);
bool dnd_array_store_ref(DndArray *array, uint32_t index, DndObject *value);
bool dnd_type_is_assignable_from(const DndType *target, const DndType *actual);
DndObject *dnd_cast(DndObject *object, const DndType *target);
DndObject *dnd_isinst(DndObject *object, const DndType *target);
bool dnd_require_object(const DndObject *object);
DndObject *dnd_box_i32(DndManagedHeap *heap, int32_t value);
DndObject *dnd_box_scalar(DndManagedHeap *heap, const DndType *type, uint64_t value, uint32_t size);
uint64_t dnd_unbox_scalar(DndObject *object, const DndType *type, uint32_t size);
int32_t dnd_unbox_i32(DndObject *object);
DndManagedMethod dnd_virtual_resolve(const DndObject *object, uint16_t slot);
DndManagedMethod dnd_interface_resolve(const DndObject *object, const DndType *interface_type, uint16_t slot);
DndDelegate *dnd_delegate_new(DndManagedHeap *heap, void *target, DndDelegateFn method);
void dnd_delegate_invoke(DndDelegate *delegate, void *argument);
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

typedef struct DndEhFrame {
    jmp_buf environment;
    struct DndEhFrame *previous;
    DndGcFrame *gc_snapshot;
} DndEhFrame;
void dnd_eh_push(DndEhFrame *frame);
void dnd_eh_pop(DndEhFrame *frame);
bool dnd_exception_pending(void);
DndObject *dnd_exception_object(void);
DndException *dnd_exception_new(DndManagedHeap *heap, const DndType *type, DndString *message);
DndString *dnd_exception_get_message(DndException *exception);
bool dnd_exception_matches(const DndType *type);
void dnd_exception_begin_catch(void);
void dnd_exception_rethrow(void);
void dnd_exception_rethrow_current(void);

void dnd_exception_clear(void);
void dnd_exception_throw(DndExceptionKind kind, const char *message);
void dnd_exception_throw_object(DndObject *exception);
DndExceptionKind dnd_exception_kind(void);
const char *dnd_exception_message(void);
#endif
