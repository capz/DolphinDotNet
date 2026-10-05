#include <stdint.h>
#include <stdio.h>

intptr_t dnd_value_aot_entry(void);

int main(void)
{
    intptr_t result=dnd_value_aot_entry();
    if(result!=10)
    {
        fprintf(stderr,"value-ir control-flow result: %ld (expected 10)\n",(long)result);
        return 1;
    }
    return 0;
}
