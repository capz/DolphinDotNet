#include "dnd_managed.h"
#include <string.h>

const DndType DND_TYPE_OBJECT = {"System.Object", NULL, sizeof(DndObject), 0, NULL, 0, NULL, 0, 0};
const DndType DND_TYPE_STRING = {"System.String", &DND_TYPE_OBJECT, sizeof(DndString), 0, NULL, 0, NULL, 0, 0};
const DndType DND_TYPE_ARRAY = {"System.Array", &DND_TYPE_OBJECT, sizeof(DndArray), 0, NULL, 0, NULL, DND_TYPE_FLAG_ARRAY, 0};
static const uint32_t delegate_refs[] = { (uint32_t)offsetof(DndDelegate, target) };
const DndType DND_TYPE_DELEGATE = {"System.Delegate", &DND_TYPE_OBJECT, sizeof(DndDelegate), 0, NULL, 1, delegate_refs, 0, 0};

static DndGcFrame *gc_frames;
static DndExceptionKind exception_kind;
static const char *exception_text;
static DndManagedHeap *active_heap;

static size_t align8(size_t n) { return (n + 7u) & ~(size_t)7u; }
static bool in_heap(const DndManagedHeap *heap, const DndObject *o) {
    const uint8_t *p=(const uint8_t *)o;
    return heap && p>=heap->start && p<heap->start+heap->capacity;
}

static void rebuild_free_list(DndManagedHeap *heap) {
    heap->free_list=NULL;
    for (DndObject *o=heap->objects;o;o=o->next) {
        if (o->type==NULL) { o->marked=0; o->flags=0; }
    }
    DndObject *prev=NULL,*cur=heap->objects;
    while(cur) {
        if(cur->type==NULL) {
            while(cur->next && cur->next->type==NULL &&
                  (uint8_t*)cur+cur->size==(uint8_t*)cur->next) {
                DndObject *n=cur->next; cur->size+=n->size; cur->next=n->next;
            }
            cur->reserved=0;
            if(!heap->free_list)heap->free_list=cur;
        }
        prev=cur;cur=cur->next;
    }
    (void)prev;
}

static DndObject *allocate_from_free(DndManagedHeap *heap,const DndType *type,size_t bytes) {
    DndObject *prev=NULL;
    for(DndObject *o=heap->objects;o;prev=o,o=o->next) {
        if(o->type!=NULL || o->size<bytes)continue;
        size_t remaining=o->size-bytes;
        DndObject *next=o->next;
        if(remaining>=align8(sizeof(DndObject)+8)) {
            DndObject *tail=(DndObject*)((uint8_t*)o+bytes);
            memset(tail,0,sizeof(*tail));tail->size=(uint32_t)remaining;tail->next=next;
            o->size=(uint32_t)bytes;o->next=tail;
        }
        memset((uint8_t*)o+offsetof(DndObject,type),0,o->size);
        o->type=type;o->size=(uint32_t)(remaining>=align8(sizeof(DndObject)+8)?bytes:o->size);
        if(prev==NULL)heap->objects=o;
        rebuild_free_list(heap);
        return o;
    }
    return NULL;
}

static DndObject *allocate(DndManagedHeap *heap, const DndType *type, size_t bytes) {
    if (!heap || !type) return NULL;
    bytes=align8(bytes<sizeof(DndObject)?sizeof(DndObject):bytes);
    DndObject *reused=allocate_from_free(heap,type,bytes);
    if(reused)return reused;
    if(bytes>heap->capacity-heap->used) {
        dnd_gc_collect(heap,NULL);
        reused=allocate_from_free(heap,type,bytes);
        if(reused)return reused;
        if(bytes>heap->capacity-heap->used) {
            dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed heap exhausted.");
            return NULL;
        }
    }
    DndObject *o=(DndObject*)(heap->start+heap->used);
    heap->used+=bytes;memset(o,0,bytes);o->type=type;o->size=(uint32_t)bytes;
    if(!heap->objects)heap->objects=o;
    else { DndObject *tail=heap->objects;while(tail->next)tail=tail->next;tail->next=o; }
    return o;
}

void dnd_managed_heap_init(DndManagedHeap *heap, void *memory, size_t size) {
    heap->start=memory;heap->capacity=size;heap->used=0;heap->objects=NULL;heap->free_list=NULL;heap->collections=0;active_heap=heap;
}

DndObject *dnd_object_new(DndManagedHeap *heap,const DndType *type) {
    return allocate(heap,type,type->instance_size<sizeof(DndObject)?sizeof(DndObject):type->instance_size);
}

static size_t utf8_ascii_length(const char *s){size_t n=0;while(s&&*s++)n++;return n;}
DndString *dnd_string_from_utf8(DndManagedHeap *heap,const char *text) {
    if(!text)return NULL;size_t n=utf8_ascii_length(text);
    DndString *s=(DndString*)allocate(heap,&DND_TYPE_STRING,sizeof(DndString)+(n+1)*sizeof(uint16_t));
    if(!s)return NULL;s->length=(uint32_t)n;for(size_t i=0;i<n;i++)s->chars[i]=(uint8_t)text[i];s->chars[n]=0;return s;
}
DndString *dnd_string_concat(DndManagedHeap *heap,const DndString *a,const DndString *b) {
    uint32_t al=a?a->length:0,bl=b?b->length:0;DndString *s=(DndString*)allocate(heap,&DND_TYPE_STRING,sizeof(DndString)+((size_t)al+bl+1)*2);
    if(!s)return NULL;s->length=al+bl;if(a)memcpy(s->chars,a->chars,(size_t)al*2);if(b)memcpy(s->chars+al,b->chars,(size_t)bl*2);s->chars[s->length]=0;return s;
}
bool dnd_string_equals(const DndString *a,const DndString *b){if(a==b)return true;if(!a||!b||a->length!=b->length)return false;return memcmp(a->chars,b->chars,(size_t)a->length*2)==0;}

DndArray *dnd_managed_array_new_typed(DndManagedHeap *heap,uint32_t length,uint32_t element_size,const DndType *element_type,bool refs) {
    if(element_size&&length>SIZE_MAX/element_size){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Array size overflow.");return NULL;}
    DndArray *a=(DndArray*)allocate(heap,&DND_TYPE_ARRAY,sizeof(DndArray)+(size_t)length*element_size);
    if(!a)return NULL;a->length=length;a->element_size=element_size;a->element_type=element_type;a->elements_are_references=refs?1:0;return a;
}
DndArray *dnd_managed_array_new(DndManagedHeap *heap,uint32_t length,uint32_t element_size){return dnd_managed_array_new_typed(heap,length,element_size,NULL,false);}
void *dnd_managed_array_at(DndArray *a,uint32_t index){if(!a){dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE,"Array is null.");return NULL;}if(index>=a->length){dnd_exception_throw(DND_EXCEPTION_INDEX_OUT_OF_RANGE,"Array index out of range.");return NULL;}return a->data+(size_t)index*a->element_size;}

bool dnd_type_is_assignable_from(const DndType *target,const DndType *actual){if(!target||!actual)return false;for(const DndType*t=actual;t;t=t->base_type)if(t==target)return true;for(const DndType*t=actual;t;t=t->base_type)for(uint16_t i=0;i<t->interface_count;i++)if(t->interfaces[i]==target)return true;return false;}
DndObject *dnd_cast(DndObject *object,const DndType *target){if(!object)return NULL;if(dnd_type_is_assignable_from(target,object->type))return object;dnd_exception_throw(DND_EXCEPTION_INVALID_CAST,"Invalid managed cast.");return NULL;}
DndDelegate *dnd_delegate_new(DndManagedHeap *heap,void *target,DndDelegateFn method){if(!method){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Delegate method is null.");return NULL;}DndDelegate*d=(DndDelegate*)allocate(heap,&DND_TYPE_DELEGATE,sizeof(DndDelegate));if(d){d->target=target;d->method=method;}return d;}
void dnd_delegate_invoke(DndDelegate*d,void*argument){if(!d||!d->method){dnd_exception_throw(DND_EXCEPTION_NULL_REFERENCE,"Delegate is null.");return;}d->method(d->target,argument);}

void dnd_roots_init(DndRootSet*r,DndObject***storage,size_t capacity){r->slots=storage;r->count=0;r->capacity=capacity;}
bool dnd_root_add(DndRootSet*r,DndObject**slot){if(!r||!slot||r->count==r->capacity)return false;r->slots[r->count++]=slot;return true;}
void dnd_gc_frame_push(DndGcFrame*f,DndObject***slots,size_t count){f->slots=slots;f->count=count;f->previous=gc_frames;gc_frames=f;}
void dnd_gc_frame_pop(DndGcFrame*f){if(gc_frames==f)gc_frames=f->previous;}

static void mark_object(DndManagedHeap *heap,DndObject *o) {
    if(!o||!in_heap(heap,o)||o->type==NULL||o->marked)return;o->marked=1;
    const DndType *type=o->type;
    for(const DndType*t=type;t;t=t->base_type)for(uint16_t i=0;i<t->reference_count;i++) {
        uint32_t off=t->reference_offsets[i];if(off+sizeof(void*)<=o->size)mark_object(heap,*(DndObject**)((uint8_t*)o+off));
    }
    if(type==&DND_TYPE_ARRAY) {
        DndArray *a=(DndArray*)o;
        if(a->elements_are_references)for(uint32_t i=0;i<a->length;i++)mark_object(heap,*(DndObject**)(a->data+(size_t)i*a->element_size));
        else if(a->element_type&&a->element_type->reference_count)for(uint32_t i=0;i<a->length;i++)for(uint16_t r=0;r<a->element_type->reference_count;r++)mark_object(heap,*(DndObject**)(a->data+(size_t)i*a->element_size+a->element_type->reference_offsets[r]));
    }
}
void dnd_gc_collect(DndManagedHeap *heap,const DndRootSet *roots) {
    if(!heap)return;heap->collections++;
    for(DndObject*o=heap->objects;o;o=o->next)o->marked=0;
    if(roots)for(size_t i=0;i<roots->count;i++)if(roots->slots[i])mark_object(heap,*roots->slots[i]);
    for(DndGcFrame*f=gc_frames;f;f=f->previous)for(size_t i=0;i<f->count;i++)if(f->slots[i])mark_object(heap,*f->slots[i]);
    for(DndObject*o=heap->objects;o;o=o->next)if(o->type&& !o->marked)o->type=NULL;
    rebuild_free_list(heap);
    while(heap->objects) {
        DndObject *last=heap->objects,*before=NULL;while(last->next){before=last;last=last->next;}
        if(last->type!=NULL)break;
        heap->used=(size_t)((uint8_t*)last-heap->start);
        if(before)before->next=NULL;else heap->objects=NULL;
    }
    rebuild_free_list(heap);
}

void dnd_exception_clear(void){exception_kind=DND_EXCEPTION_NONE;exception_text=NULL;}
void dnd_exception_throw(DndExceptionKind kind,const char*message){exception_kind=kind;exception_text=message;}
DndExceptionKind dnd_exception_kind(void){return exception_kind;}
const char*dnd_exception_message(void){return exception_text?exception_text:"";}
