#include "dnd_network_platform.h"
#include <ogc/exi.h>
#include <assert.h>
#include <stddef.h>
#include <stdio.h>
static int get_id_ok, lock_ok, select_ok, write_ok, read_ok, deselect_ok;
static int locks, selects, writes, reads, deselects, unlocks;
static u32 adapter_id;
static unsigned char status;
static void reset(void) {
    get_id_ok=lock_ok=select_ok=write_ok=read_ok=deselect_ok=1;
    locks=selects=writes=reads=deselects=unlocks=0;
    adapter_id=0x04020200; status=1;
}
int EXI_GetID(int channel,int device,u32 *id) {
    assert(channel==0 && device==2); *id=adapter_id; return get_id_ok;
}
int EXI_Lock(int channel,int device,void *callback) {
    assert(channel==0 && device==2 && callback==NULL); locks++; return lock_ok;
}
int EXI_Select(int channel,int device,int speed) {
    assert(channel==0 && device==2 && speed==EXI_SPEED32MHZ); selects++; return select_ok;
}
int EXI_ImmEx(int channel,void *buffer,u32 length,u32 mode) {
    assert(channel==0);
    if(mode==EXI_WRITE) {
        assert(length==4 && *(u32 *)buffer==0x80003100); writes++; return write_ok;
    }
    assert(mode==EXI_READ && length==1); reads++; *(unsigned char *)buffer=status; return read_ok;
}
int EXI_Deselect(int channel) { assert(channel==0); deselects++; return deselect_ok; }
int EXI_Unlock(int channel) { assert(channel==0); unlocks++; return 1; }
int main(void) {
    reset(); assert(dnd_np_link()==1); assert(writes==1 && reads==1 && deselects==1 && unlocks==1);
    reset(); status=2; assert(dnd_np_link()==1);
    reset(); status=0; assert(dnd_np_link()==0);
    reset(); status=0x80; assert(dnd_np_link()==0);
    reset(); status=0xff; assert(dnd_np_link()==-1);
    reset(); adapter_id=0x04020100; assert(dnd_np_link()==-1); assert(locks==0 && selects==0);
    reset(); get_id_ok=0; assert(dnd_np_link()==-1); assert(locks==0);
    reset(); lock_ok=0; assert(dnd_np_link()==-1); assert(selects==0 && unlocks==0);
    reset(); select_ok=0; assert(dnd_np_link()==-1); assert(writes==0 && deselects==0 && unlocks==1);
    reset(); write_ok=0; assert(dnd_np_link()==-1); assert(reads==0 && deselects==1 && unlocks==1);
    reset(); read_ok=0; assert(dnd_np_link()==-1); assert(deselects==1 && unlocks==1);
    reset(); deselect_ok=0; assert(dnd_np_link()==-1); assert(unlocks==1);
    puts("BBA link probe, contention and failure cleanup passed.");
}
