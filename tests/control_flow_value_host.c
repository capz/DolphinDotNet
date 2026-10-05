#include <stdint.h>
#include <stdio.h>

intptr_t dnd_value_aot_entry(void);

int main(void)
{
    intptr_t result=dnd_value_aot_entry();
    if(result!=52)
    {
        fprintf(stderr,"value-ir control-flow result: %ld (expected 52)\n",(long)result);
        return 1;
    }
    return 0;
}
