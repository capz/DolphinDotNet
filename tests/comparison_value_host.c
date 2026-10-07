#include <stdint.h>
#include <stdio.h>
#include "dnd_managed.h"

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
extern intptr_t dnd_value_Program_EqualityCase(void);
extern intptr_t dnd_value_Program_HashCase(void);
extern intptr_t dnd_value_Program_CompareCase(void);
extern intptr_t dnd_value_Program_NullableDefaultCase(void);

int main(void)
{
    unsigned char storage[4096];
    DndManagedHeap heap;
    dnd_managed_heap_init(&heap,storage,sizeof(storage));
    dnd_exception_clear(); intptr_t result=dnd_value_aot_entry(&heap); int total_ex=(int)dnd_exception_kind();
    dnd_exception_clear(); intptr_t equality=dnd_value_Program_EqualityCase(); int equality_ex=(int)dnd_exception_kind();
    dnd_exception_clear(); intptr_t hash=dnd_value_Program_HashCase(); int hash_ex=(int)dnd_exception_kind();
    dnd_exception_clear(); intptr_t compare=dnd_value_Program_CompareCase(); int compare_ex=(int)dnd_exception_kind();
    dnd_exception_clear(); intptr_t nullable_case=dnd_value_Program_NullableDefaultCase(); int nullable_ex=(int)dnd_exception_kind();
    dnd_exception_clear();
    fprintf(stderr,"comparison cases equality=%ld/%d hash=%ld/%d compare=%ld/%d nullable=%ld/%d total=%ld/%d\n",(long)equality,equality_ex,(long)hash,hash_ex,(long)compare,compare_ex,(long)nullable_case,nullable_ex,(long)result,total_ex);
    if(total_ex!=DND_EXCEPTION_NONE){
        fprintf(stderr,"comparison smoke exception=%d\n",total_ex);
        return 2;
    }
    if(result!=0){
        fprintf(stderr,"comparison smoke result=%ld\n",(long)result);
        return 1;
    }
    return 0;
}
