#include "dnd_managed.h"
#include <string.h>

const DndType DND_TYPE_OBJECT = {"System.Object", NULL, sizeof(DndObject), 0, NULL};
const DndType DND_TYPE_STRING = {"System.String", &DND_TYPE_OBJECT, sizeof(DndString), 0, NULL};
const DndType DND_TYPE_ARRAY = {"System.Array", &DND_TYPE_OBJECT, sizeof(DndArray), 0, NULL};
const DndType DND_TYPE_DELEGATE = {"System.Delegate", &DND_TYPE_OBJECT, sizeof(DndDelegate), 0, NULL};

static DndExceptionKind exception_kind;
static const char *exception_text;

static size_t align8(size_t n) { return (n + 7u) & ~(size_t)7u; }

static DndObject *allocate(DndManagedHeap *heap, const DndType *type, size_t bytes) {
    if (!heap || !type) return NULL;
    bytes = align8(bytes);
    if (bytes > heap->capacity - heap->used) {
        dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY, "Managed heap exhausted.");
        return NULL;
    }
    DndObject *o = (DndObject *)(heap->start + heap->used);
    heap->used += bytes;
    memset(o, 0, bytes);
    o->type = type;
    o->size = (uint32_t)bytes;
    o->next = heap->objects;
    heap->objects = o;
    return o;
}

void dnd_managed_heap_init(DndManagedHeap *heap, void *memory, size_t size) {
    heap->start = memory; heap->capacity = size; heap->used = 0; heap->objects = NULL;
}

DndObject *dnd_object_new(DndManagedHeap *heap, const DndType *type) {
    return allocate(heap, type, type->instance_size < sizeof(DndObject) ? sizeof(DndObject) : type->instance_size);
}

static size_t utf8_ascii_length(const char *s) {
    size_t n = 0; while (s && *s++) n++; return n;
}

DndString *dnd_string_from_utf8(DndManagedHeap *heap, const char *text) {
    if (!text) return NULL;
    size_t n = utf8_ascii_length(text);
    DndString *s = (DndString *)allocate(heap, &DND_TYPE_STRING, sizeof(DndString) + (n + 1) * sizeof(uint16_t));
    if (!s) return NULL;
    s->length = (uint32_t)n;
    for (size_t i = 0; i < n; i++) s->chars[i] = (uint8_t)text[i];
    s->chars[n] = 0;
    return s;
}

DndString *dnd_string_concat(DndManagedHeap *heap, const DndString *a, const DndString *b) {
    uint32_t al = a ? a->length : 0, bl = b ? b->length : 0;
    DndString *s = (DndString *)allocate(heap, &DND_TYPE_STRING, sizeof(DndString) + ((size_t)al + bl + 1) * 2);
    if (!s) return NULL;
    s->length = al + bl;
    if (a) memcpy(s->chars, a->chars, (size_t)al * 2);
    if (b) memcpy(s->chars + al, b->chars, (size_t)bl * 2);
    s->chars[s->length] = 0;
    return s;
}

bool dnd_string_equals(const DndString *a, const DndString *b) {
    if (a == b) return true;
    if (!a || !b || a->length != b->length) return false;
    return memcmp(a->chars, b->chars, (size_t)a->length * 2) == 0;
}

DndArray *dnd_managed_array_new(DndManagedHeap *heap, uint32_t length, uint32_t element_size) {
    if (element_size && length > SIZE_MAX / element_size) return NULL;
    DndArray *a = (DndArray *)allocate(heap, &DND_TYPE_ARRAY, sizeof(DndArray) + (size_t)length * element_size);
    if (!a) return NULL;
    a->length = length; a->element_size = element_size; return a;
}

void *dnd_managed_array_at(DndArray *a, uint32_t index) {
    if (!a) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Array is null."); return NULL; }
    if (index >= a->length) { dnd_exception_throw(DND_EXCEPTION_INDEX_OUT_OF_RANGE, "Array index out of range."); return NULL; }
    return a->data + (size_t)index * a->element_size;
}

bool dnd_type_is_assignable_from(const DndType *target, const DndType *actual) {
    if (!target || !actual) return false;
    for (const DndType *t = actual; t; t = t->base_type) if (t == target) return true;
    for (const DndType *t = actual; t; t = t->base_type)
        for (uint16_t i = 0; i < t->interface_count; i++) if (t->interfaces[i] == target) return true;
    return false;
}

DndObject *dnd_cast(DndObject *object, const DndType *target) {
    if (!object) return NULL;
    if (dnd_type_is_assignable_from(target, object->type)) return object;
    dnd_exception_throw(DND_EXCEPTION_INVALID_CAST, "Invalid managed cast.");
    return NULL;
}

DndDelegate *dnd_delegate_new(DndManagedHeap *heap, void *target, DndDelegateFn method) {
    if (!method) { dnd_exception_throw(DND_EXCEPTION_ARGUMENT, "Delegate method is null."); return NULL; }
    DndDelegate *d = (DndDelegate *)allocate(heap, &DND_TYPE_DELEGATE, sizeof(DndDelegate));
    if (d) { d->target = target; d->method = method; }
    return d;
}

void dnd_delegate_invoke(DndDelegate *d, void *argument) {
    if (!d || !d->method) { dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE, "Delegate is null."); return; }
    d->method(d->target, argument);
}

void dnd_roots_init(DndRootSet *r, DndObject ***storage, size_t capacity) {
    r->slots = storage; r->count = 0; r->capacity = capacity;
}
bool dnd_root_add(DndRootSet *r, DndObject **slot) {
    if (!r || !slot || r->count == r->capacity) return false;
    r->slots[r->count++] = slot; return true;
}

static void mark(DndObject *o) { if (o) o->marked = 1; }

/* Phase 1 GC: roots and object accounting. Object fields become traceable once
 * compiler-emitted type metadata contains reference offsets. The bump heap is
 * intentionally non-compacting; collection currently reclaims only a completely
 * unreachable heap, making semantics safe while the metadata format evolves. */
void dnd_gc_collect(DndManagedHeap *heap, const DndRootSet *roots) {
    if (!heap) return;
    for (DndObject *o = heap->objects; o; o = o->next) o->marked = 0;
    if (roots) for (size_t i = 0; i < roots->count; i++) if (roots->slots[i]) mark(*roots->slots[i]);
    bool any = false;
    for (DndObject *o = heap->objects; o; o = o->next) if (o->marked) { any = true; break; }
    if (!any) { heap->used = 0; heap->objects = NULL; }
}

void dnd_exception_clear(void) { exception_kind = DND_EXCEPTION_NONE; exception_text = NULL; }
void dnd_exception_throw(DndExceptionKind kind, const char *message) { exception_kind = kind; exception_text = message; }
DndExceptionKind dnd_exception_kind(void) { return exception_kind; }
const char *dnd_exception_message(void) { return exception_text ? exception_text : ""; }
