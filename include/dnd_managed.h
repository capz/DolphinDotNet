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

typedef void (*DndFinalizer)(DndObject *);
typedef void (*DndDelegateFn)(void *target, void *argument);

struct DndType {
    const char *name;
    const DndType *base_type;
    uint32_t instance_size;
    uint16_t interface_count;
    const DndType *const *interfaces;
};

struct DndObject {
    const DndType *type;
    uint32_t size;
    uint8_t marked;
    uint8_t flags;
    uint16_t reserved;
    DndObject *next;
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
    uint8_t data[];
};

struct DndDelegate {
    DndObject object;
    void *target;
    DndDelegateFn method;
};

typedef struct {
    uint8_t *start;
    size_t capacity;
    size_t used;
    DndObject *objects;
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
    DND_EXCEPTION_ARGUMENT
} DndExceptionKind;

extern const DndType DND_TYPE_OBJECT;
extern const DndType DND_TYPE_STRING;
extern const DndType DND_TYPE_ARRAY;
extern const DndType DND_TYPE_DELEGATE;

void dnd_managed_heap_init(DndManagedHeap *heap, void *memory, size_t size);
DndObject *dnd_object_new(DndManagedHeap *heap, const DndType *type);
DndString *dnd_string_from_utf8(DndManagedHeap *heap, const char *text);
DndString *dnd_string_concat(DndManagedHeap *heap, const DndString *a, const DndString *b);
bool dnd_string_equals(const DndString *a, const DndString *b);
DndArray *dnd_managed_array_new(DndManagedHeap *heap, uint32_t length, uint32_t element_size);
void *dnd_managed_array_at(DndArray *array, uint32_t index);
bool dnd_type_is_assignable_from(const DndType *target, const DndType *actual);
DndObject *dnd_cast(DndObject *object, const DndType *target);
DndDelegate *dnd_delegate_new(DndManagedHeap *heap, void *target, DndDelegateFn method);
void dnd_delegate_invoke(DndDelegate *delegate, void *argument);

void dnd_roots_init(DndRootSet *roots, DndObject ***storage, size_t capacity);
bool dnd_root_add(DndRootSet *roots, DndObject **slot);
void dnd_gc_collect(DndManagedHeap *heap, const DndRootSet *roots);

void dnd_exception_clear(void);
void dnd_exception_throw(DndExceptionKind kind, const char *message);
DndExceptionKind dnd_exception_kind(void);
const char *dnd_exception_message(void);
#endif
