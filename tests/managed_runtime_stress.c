#include "dnd_managed.h"
#include <assert.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

typedef struct Node { DndObject object; DndObject *left; DndObject *right; int32_t value; } Node;
static const uint32_t node_refs[]={ (uint32_t)offsetof(Node,left),(uint32_t)offsetof(Node,right) };
static const DndType NODE={ "StressNode",&DND_TYPE_OBJECT,sizeof(Node),0,NULL,2,node_refs,0,0,NULL,0,NULL };

typedef struct Inner { DndObject object; DndObject *reference; } Inner;
static const uint32_t inner_refs[]={ (uint32_t)offsetof(Inner,reference) };
static const DndType INNER={ "Inner",&DND_TYPE_OBJECT,sizeof(Inner),0,NULL,1,inner_refs,DND_TYPE_FLAG_VALUE_TYPE,0,NULL,0,NULL };

static intptr_t target_value(intptr_t *args){ return ((Node*)args[0])->value; }

static void cycle_and_shared_graph(DndManagedHeap *heap){
    Node *a=(Node*)dnd_object_new(heap,&NODE),*b=(Node*)dnd_object_new(heap,&NODE),*c=(Node*)dnd_object_new(heap,&NODE);
    assert(a&&b&&c); a->left=(DndObject*)b;a->right=(DndObject*)c;b->left=(DndObject*)a;b->right=(DndObject*)c;c->left=(DndObject*)c;c->value=77;
    DndObject *root=(DndObject*)a;DndObject **slots[]={&root};DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
    for(int i=0;i<64;i++){dnd_gc_collect(heap,NULL);assert(((Node*)((Node*)root)->right)->value==77);}
    dnd_gc_frame_pop(&frame);
}

static void interior_root(DndManagedHeap *heap){
    Node *node=(Node*)dnd_object_new(heap,&NODE);assert(node);node->value=1234;
    DndObject *interior=(DndObject*)&node->value;DndObject **slots[]={&interior};DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
    dnd_gc_collect(heap,NULL);assert(node->object.type==&NODE&&node->value==1234);dnd_gc_frame_pop(&frame);
}

static void value_array_trace(DndManagedHeap *heap){
    DndArray *array=dnd_managed_array_new_typed(heap,2,(uint32_t)(sizeof(Inner)-sizeof(DndObject)),&INNER,false);assert(array);
    Node *child=(Node*)dnd_object_new(heap,&NODE);assert(child);child->value=55;
    Inner *element=(Inner*)((uint8_t*)dnd_array_element_address(array,0)-sizeof(DndObject)); /* payload offsets are object-header based */
    element->reference=(DndObject*)child;
    DndObject *root=(DndObject*)array;DndObject **slots[]={&root};DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
    dnd_gc_collect(heap,NULL);assert(child->object.type==&NODE&&child->value==55);dnd_gc_frame_pop(&frame);
}

static void delegate_target_root(DndManagedHeap *heap){
    Node *target=(Node*)dnd_object_new(heap,&NODE);assert(target);target->value=91;
    DndDelegate *delegate=dnd_managed_delegate_new(heap,(DndObject*)target,target_value,true);assert(delegate);
    DndObject *root=(DndObject*)delegate;DndObject **slots[]={&root};DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
    dnd_gc_collect(heap,NULL);assert(dnd_managed_delegate_invoke(delegate,NULL,0)==91);dnd_gc_frame_pop(&frame);
}

static void boxing_stress(DndManagedHeap *heap){
    DndObject *box=NULL;DndObject **slots[]={&box};DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
    for(int32_t i=0;i<128;i++){box=dnd_box_i32(heap,i);assert(box);dnd_gc_collect(heap,NULL);assert(dnd_unbox_i32(box)==i);}
    dnd_gc_frame_pop(&frame);
}

static void exhaustion_and_reuse(void){
    uint8_t memory[768];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));
    Node *root=(Node*)dnd_object_new(&heap,&NODE);assert(root);DndObject *root_object=(DndObject*)root;DndObject **slots[]={&root_object};DndRootSet roots;dnd_roots_init(&roots,slots,1);assert(dnd_root_add(&roots,&root_object));
    Node *tail=root;for(;;){Node *next=(Node*)dnd_object_new(&heap,&NODE);if(!next)break;tail->left=(DndObject*)next;tail=next;}
    assert(dnd_exception_kind()==DND_EXCEPTION_OUT_OF_MEMORY);dnd_exception_clear();
    root_object=NULL;dnd_gc_collect(&heap,&roots);size_t used=heap.used;DndObject *again=dnd_object_new(&heap,&NODE);assert(again);assert(heap.used<=used);
}

static void invalid_operations(DndManagedHeap *heap){
    Node *node=(Node*)dnd_object_new(heap,&NODE);assert(node);
    assert(dnd_cast((DndObject*)node,&DND_TYPE_STRING)==NULL&&dnd_exception_kind()==DND_EXCEPTION_INVALID_CAST);dnd_exception_clear();
    assert(dnd_unbox_i32((DndObject*)node)==0&&dnd_exception_kind()==DND_EXCEPTION_INVALID_CAST);dnd_exception_clear();
    DndArray *a=dnd_managed_array_new(heap,1,sizeof(int32_t));assert(a);assert(!dnd_array_store_i32(a,9,1)&&dnd_exception_kind()==DND_EXCEPTION_INDEX_OUT_OF_RANGE);dnd_exception_clear();
}

int main(void){
    uint8_t memory[32768];DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,sizeof(memory));
    cycle_and_shared_graph(&heap);dnd_gc_collect(&heap,NULL);
    interior_root(&heap);dnd_gc_collect(&heap,NULL);
    value_array_trace(&heap);dnd_gc_collect(&heap,NULL);
    delegate_target_root(&heap);dnd_gc_collect(&heap,NULL);
    boxing_stress(&heap);dnd_gc_collect(&heap,NULL);
    invalid_operations(&heap);dnd_gc_collect(&heap,NULL);
    dnd_gc_set_stress(true);
    DndObject *root=dnd_object_new(&heap,&NODE);DndObject **slots[]={&root};DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
    for(int i=0;i<128;i++){Node *n=(Node*)dnd_object_new(&heap,&NODE);assert(n);n->value=i;((Node*)root)->left=(DndObject*)n;assert(((Node*)((Node*)root)->left)->value==i);}
    dnd_gc_frame_pop(&frame);dnd_gc_set_stress(false);
    exhaustion_and_reuse();
    printf("runtime stress passed: collections=%zu used=%zu/%zu\n",heap.collections,heap.used,heap.capacity);
    return 0;
}
