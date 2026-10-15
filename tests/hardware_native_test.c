#include "dnd_hardware.h"
#include <assert.h>
#include <stdio.h>
int main(void) {
    assert(dnd_hardware_probe(0,0,0)==4);
    assert(dnd_hardware_probe(2,4,1)==4);
    assert(dnd_hardware_probe(5,6,1)==4);
    assert(dnd_hardware_probe(9,9,0)==4);
    assert(dnd_hardware_probe(99,99,1)==4);
    assert(dnd_hardware_id(5,6)==0);
    assert(!dnd_platform_reset_button());
    /* Do not invoke reboot in automated tests. */
    puts("Hardware host unsupported and reset-button contract passed");
    return 0;
}
