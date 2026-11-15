#ifndef DND_IPL_SEQUENCE_H
#define DND_IPL_SEQUENCE_H
#include <stdbool.h>
#include <stdint.h>

/* Private, bounded IPL sequence machine. No file addresses, ports or bytecode
 * are part of the managed SDK contract. Its consumer is a native producer;
 * callbacks must never enter the managed runtime. */
#define DND_IPL_TRACKS 32
#define DND_IPL_STACK 8
#define DND_IPL_PARAMS 18
typedef struct {
  float value, target, step;
  unsigned remaining;
} DndIplParameter;
typedef struct {
  unsigned generation, owner, pc, wait, identifier, stack[DND_IPL_STACK];
  uint64_t born_block;
  unsigned stack_depth, loop_pc[DND_IPL_STACK], loop_count[DND_IPL_STACK], loop_depth;
  unsigned interrupt[8], interrupt_mask, pending, interrupt_pc, interrupt_wait;
  unsigned timer, timer_period, timer_count;
  unsigned ticks, tempo, timebase, mix[6], adsr[5], simple_oscillator, oscillator_route, sync_mask;
  uint16_t reg[32], port[16], imported, exported;
  int parent, child[16], transpose, last_key;
  float clock, oscillator_time;
  unsigned parameter_written, oscillator_modes[2], oscillator_slot;
  bool oscillator_enabled;
  DndIplParameter parameter[DND_IPL_PARAMS];
  bool active, in_interrupt, interrupts_enabled, independent, override_tempo;
  bool has_adsr, release_override, time_relative, registered;
} DndIplTrack;
typedef enum {
  DND_IPL_NOTE_ON, DND_IPL_NOTE_OFF, DND_IPL_TRACK_CLOSED,
  DND_IPL_TRACK_UPDATED, DND_IPL_SYNC
} DndIplEventKind;
typedef struct {
  DndIplEventKind kind;
  unsigned track, generation, owner, slot, key, velocity;
  unsigned duration, gate_percent, connection, release;
} DndIplEvent;
typedef void (*DndIplEventHandler)(void *, const DndIplEvent *);
typedef struct {
  const uint8_t *sequence;
  unsigned length, generation, work, fault, fault_pc, fault_opcode;
  uint64_t blocks;
  float tick_divisor;
  bool processing_tick, modern;
  DndIplTrack track[DND_IPL_TRACKS];
  DndIplEventHandler handler;
  void *context;
} DndIplSequence;
/* The sequence extent remains owned by the caller for the VM lifetime. */
bool dnd_ipl_sequence_init(DndIplSequence *, const uint8_t *, unsigned,
                           DndIplEventHandler, void *);
void dnd_ipl_sequence_modern(DndIplSequence *,bool);
bool dnd_ipl_sequence_block(DndIplSequence *);
float dnd_ipl_sequence_tick_rate(const DndIplSequence *,unsigned);
bool dnd_ipl_sequence_port(DndIplSequence *, unsigned identifier, unsigned port, unsigned value);
void dnd_ipl_sequence_stop_owner(DndIplSequence *, unsigned owner);
int dnd_ipl_sequence_open(DndIplSequence *, unsigned entry, unsigned owner);
#endif
