#include "dnd_ipl_sequence.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct {unsigned on,off,closed,last_key,last_owner,last_program;} Events;
typedef struct {DndIplSequence sequence;Events events;} Fixture;
static void event(void *context,const DndIplEvent *e) {
  Fixture *f=context;
  if(e->kind==DND_IPL_NOTE_ON) {
    f->events.on++;f->events.last_key=e->key;f->events.last_owner=e->owner;
    f->events.last_program=f->sequence.track[e->track].reg[6];
    assert(e->velocity<=127&&e->slot<=7);
  }
  if(e->kind==DND_IPL_NOTE_OFF)f->events.off++;
  if(e->kind==DND_IPL_TRACK_CLOSED)f->events.closed++;
}
static void start(Fixture *f,const uint8_t *s,unsigned bytes) {
  memset(f,0,sizeof(*f));assert(dnd_ipl_sequence_init(&f->sequence,s,bytes,event,f));
}
static void blocks(Fixture *f,unsigned n) {
  for(unsigned i=0;i<n;i++)assert(dnd_ipl_sequence_block(&f->sequence));
}
int main(void) {
  Fixture *f=calloc(1,sizeof(*f));assert(f);
  /* Original generated score: a finite note, two waits and a note-off. Tick 3
   * is reached at DSP block 5 (the first frame of that block is sample 400). */
  const uint8_t finite[]={0xac,6,0,3,60,1,90,0x80,3,0x81,0x80,2,0xff};
  start(f,finite,sizeof(finite));blocks(f,1);assert(f->events.on==1&&f->events.off==0);
  blocks(f,4);assert(f->events.off==0);
  blocks(f,1);assert(f->events.off==1);
  blocks(f,2);assert(!f->sequence.track[0].active&&f->events.closed==1);
  /* Intro parameter, interpolated update, source-key transpose and a gated
   * note. Its wait is authored by the score; no caller repeat scheduler. */
  const uint8_t gated[]={0x98,0,64,0x9a,0,32,4,0xd9,0xff,65,8,80,50,4,0xff};
  start(f,gated,sizeof(gated));blocks(f,1);
  assert(f->events.last_key==64&&f->sequence.track[0].wait==4);
  assert(f->sequence.track[0].parameter[0].value==.5f);
  blocks(f,2);assert(f->sequence.track[0].parameter[0].value==.4375f);
  /* A port wakes a waiting root through its registered interrupt, reads an
   * immutable table by a paired address register, then restores the wait. */
  uint8_t irq[64]={0xd0,0x12,0x34,0x56,0x78,0xe5,0xdf,3,0,0,20,0x80,100,0xc8,0,0,0,11};
  const uint8_t handler[]={0xcb,0,0,0xa4,16,0,0xac,17,0,60,0xaa,0,6,0x28,0,0xe3};
  memcpy(irq+20,handler,sizeof(handler));irq[60]=7;irq[61]=9;
  start(f,irq,sizeof(irq));blocks(f,1);
  assert(dnd_ipl_sequence_port(&f->sequence,0x12345678,0,1));blocks(f,2);
  assert(f->sequence.track[0].reg[6]==9&&!f->sequence.track[0].in_interrupt);
  assert(f->sequence.track[0].wait==99);
  assert(!dnd_ipl_sequence_port(&f->sequence,123,0,1));
  assert(!dnd_ipl_sequence_port(&f->sequence,0x12345678,16,1));
  /* Bounded calls and authored loops; note source metadata is untouched. */
  uint8_t calls[64]={0xc9,0,3,0xc4,0,0,0,20,0xca,0xff};
  const uint8_t phrase[]={62,1,70,0x80,1,0x81,0xc6,0};memcpy(calls+20,phrase,sizeof(phrase));
  start(f,calls,sizeof(calls));blocks(f,8);assert(f->events.on==3&&f->events.off==3);
  /* Cue ownership follows child replacement and closing the complete tree. */
  uint8_t tree[64]={0x80,100,0xc8,0,0,0,0};
  const uint8_t parent[]={0xc1,0,0,0,40,0x80,100,0xff};memcpy(tree+20,parent,sizeof(parent));
  const uint8_t child[]={64,1,90,0x80,100,0xff};memcpy(tree+40,child,sizeof(child));
  start(f,tree,sizeof(tree));assert(dnd_ipl_sequence_open(&f->sequence,20,77)>=0);blocks(f,1);
  assert(f->events.last_owner==77&&f->events.last_key==64);
  dnd_ipl_sequence_stop_owner(&f->sequence,77);assert(f->sequence.track[0].active&&f->events.closed==2);
  /* Early IPL's root clock is 5/7 ticks per DSP block, including children
   * with a different absolute-gate timebase. Tick-created children begin at
   * the next block, while the initial root children execute immediately. */
  uint8_t clocks[64]={0x80,10,0xc1,0,0,0,32,0x88,255,255,0xc8,0,0,0,7};
  const uint8_t delayed[]={0xfe,0,80,67,1,80,0x80,100,0xff};
  memcpy(clocks+32,delayed,sizeof(delayed));start(f,clocks,sizeof(clocks));blocks(f,15);
  assert(f->events.on==0&&f->sequence.track[1].active);
  blocks(f,1);assert(f->events.on==1&&f->events.last_key==67);
  assert(dnd_ipl_sequence_tick_rate(&f->sequence,1)==(float)18000/25200);
  blocks(f,1385);assert(f->sequence.track[0].ticks==1000);
  start(f,finite,sizeof(finite));f->sequence.tick_divisor=24021.375f;
  assert(dnd_ipl_sequence_tick_rate(&f->sequence,0)==(float)18000/24021.375f);
  /* A modern port handler may open a child before the next root tick. Its
   * first wait participates in that tick, while children opened during tick
   * traversal initialize once at their turn in the child tree. */
  uint8_t modern_birth[128]={0xc1,0,0,0,24,0x80,100,0xff};
  const uint8_t listening[]={0xd0,0,2,0x10,1,0xe5,0xdf,3,0,0,64,0x80,100,0xc8,0,0,0,35};
  const uint8_t opening[]={0xc1,0,0,0,96,0xe3};
  const uint8_t waiting[]={0x80,1,66,1,100,0x80,100,0xff};
  memcpy(modern_birth+24,listening,sizeof(listening));memcpy(modern_birth+64,opening,sizeof(opening));memcpy(modern_birth+96,waiting,sizeof(waiting));
  start(f,modern_birth,sizeof(modern_birth));dnd_ipl_sequence_modern(&f->sequence,true);f->sequence.tick_divisor=24021.375f;
  blocks(f,1);f->sequence.track[0].clock=.5f;
  assert(dnd_ipl_sequence_port(&f->sequence,0x21001,0,1));blocks(f,1);
  assert(f->events.on==1&&f->events.last_key==66&&f->sequence.track[2].ticks==1);
  /* No malformed input can cause an unbounded zero-time cycle or an unchecked
   * operand/table/stack access. Every fault stops further execution. */
  const uint8_t spin[]={0xc8,0,0,0,0};start(f,spin,sizeof(spin));
  assert(!dnd_ipl_sequence_block(&f->sequence)&&f->sequence.work==4097);
  assert(!dnd_ipl_sequence_block(&f->sequence));
  const uint8_t recurse[]={0xc4,0,0,0,0};start(f,recurse,sizeof(recurse));
  assert(!dnd_ipl_sequence_block(&f->sequence)&&f->sequence.track[0].stack_depth==DND_IPL_STACK);
  const uint8_t truncated[]={0xac,6,0};start(f,truncated,sizeof(truncated));
  assert(!dnd_ipl_sequence_block(&f->sequence));
  const uint8_t unknown[]={0xf7};start(f,unknown,sizeof(unknown));
  assert(!dnd_ipl_sequence_block(&f->sequence)&&f->sequence.fault_opcode==0xf7);
  assert(!dnd_ipl_sequence_init(&f->sequence,NULL,1,event,f));
  free(f);puts("Bounded IPL sequence timing, ownership, ports, tables, interrupts and malformed-input tests passed");
}
