#define DND_CAPTURE_LAUNCH_ARGUMENTS
#define DND_TEST_LAUNCH_BOUNDS
#include "../source/launch.c"
#include <assert.h>
#include <stdio.h>
#include <string.h>

/* The capture fixture inspects bytes independently of managed UTF-8 decoding. */
DndString *dnd_string_from_utf8(DndManagedHeap *heap,const char *text) {
    (void)heap;return (DndString *)text;
}
int main(void) {
    char executable[]="cardb:/apps/example/default.dol";
    const char *argv[]={executable,"caption=\xc3\xbc",""};
    dnd_launch_capture(3,argv);executable[0]='X';
    assert(dnd_launch_count()==3);
    assert(!strcmp((const char *)dnd_launch_argument(NULL,0),"cardb:/apps/example/default.dol"));
    assert(!strcmp((const char *)dnd_launch_argument(NULL,1),"caption=\xc3\xbc"));
    assert(!strcmp((const char *)dnd_launch_argument(NULL,2),""));
    assert(!dnd_launch_argument(NULL,-1)&&!dnd_launch_argument(NULL,3));
    for(int count=-1;count<=65;count+=66){dnd_launch_capture(count,argv);assert(!dnd_launch_count());}
    dnd_launch_capture(0,NULL);assert(!dnd_launch_count());
    dnd_launch_capture(1,NULL);assert(!dnd_launch_count());
    const char *missing[]={"valid",NULL};dnd_launch_capture(2,missing);assert(!dnd_launch_count());
    char full[4097];memset(full,'a',4096);full[4096]=0;
    const char *large[]={full};dnd_launch_capture(1,large);assert(!dnd_launch_count());
    full[4095]=0;dnd_launch_capture(1,large);assert(dnd_launch_count()==1);
    const char *over[]={full,""};dnd_launch_capture(2,over);assert(!dnd_launch_count());
    const char *many[64];for(int i=0;i<64;i++)many[i]="x";
    dnd_launch_capture(64,many);assert(dnd_launch_count()==64);
    assert(launch_mem1_range(0x80000000u,1)&&launch_mem1_range(0x817fffffu,1));
    assert(!launch_mem1_range(0x817fffffu,2)&&!launch_mem1_range(0x81800000u,0));
    assert(!launch_mem1_range(0x7fffffffu,1)&&!launch_mem1_range(UINTPTR_MAX,4096));
    puts("Incoming launch context: immutable snapshot, missing/malformed argv, MEM1 bounds and 64/4096 limits passed");
    return 0;
}
