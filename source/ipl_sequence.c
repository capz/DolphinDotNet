#include "dnd_ipl_sequence.h"
#include <limits.h>
#include <math.h>
#include <string.h>

/* This interpreter implements the early IPL dialect, with a shared instruction
 * budget across every track and interrupt. Unsupported operations fault rather
 * than becoming silent no-ops or plausible finite substitutes. */
static void fail(DndIplSequence *s, unsigned pc, unsigned op) {
  if (!s->fault) { s->fault=1; s->fault_pc=pc; s->fault_opcode=op; }
}
static unsigned read_at(DndIplSequence *s, unsigned at, unsigned n) {
  if (n>4 || at>s->length || n>s->length-at) { fail(s,at,256); return 0; }
  unsigned v=0; for(unsigned i=0;i<n;i++)v=(v<<8)|s->sequence[at+i]; return v;
}
static unsigned take(DndIplSequence *s,DndIplTrack *t,unsigned n) {
  unsigned v=read_at(s,t->pc,n);
  if(!s->fault)t->pc+=n;
  return v;
}
static void emit(DndIplSequence *s,unsigned track,DndIplEventKind kind,
                 unsigned slot,unsigned key,unsigned velocity,unsigned duration,
                 unsigned gate,unsigned connection,unsigned release) {
  DndIplTrack *t=&s->track[track];
  DndIplEvent e={kind,track,t->generation,t->owner,slot,key,velocity,duration,gate,connection,release};
  if(s->handler)s->handler(s->context,&e);
}
static unsigned reg(DndIplSequence *s,DndIplTrack *t,unsigned r) {
  if(r<32)return t->reg[r];
  if(r==0x20)return t->reg[6]>>8;
  if(r==0x21)return t->reg[6]&255;
  if(r==0x22)return t->reg[0]<<8|t->reg[1];
  if(r==0x23)return t->reg[4]<<16|t->reg[5];
  if(r>=0x28&&r<=0x2b)return t->reg[16+(r-0x28)*2]<<16|t->reg[17+(r-0x28)*2];
  if(r==0x2c) {
    unsigned v=0;for(unsigned i=0;i<16;i++)if(t->child[i]>=0&&s->track[t->child[i]].active)v|=1u<<i;return v;
  }
  if(r==0x30)return t->loop_depth?t->loop_count[t->loop_depth-1]:0;
  if(r>=0x40&&r<0x50)return t->port[r-0x40];
  fail(s,t->pc,257);return 0;
}
static void put(DndIplSequence *s,DndIplTrack *t,unsigned r,unsigned v) {
  if(r>=0x28&&r<=0x2b) {
    unsigned at=16+(r-0x28)*2;t->reg[at]=(uint16_t)(v>>16);t->reg[at+1]=(uint16_t)v;return;
  }
  if(r==0x20){v=(v&255)<<8|(t->reg[6]&255);r=6;}
  else if(r==0x21){v=(t->reg[6]&0xff00)|(v&255);r=6;}
  else if(r==0x22){t->reg[0]=(uint16_t)((v>>8)&255);r=1;}
  if(r>=32){fail(s,t->pc,258);return;}
  if(r<3)v&=255;
  t->reg[r]=(uint16_t)v;t->reg[3]=r<3?(uint16_t)(int16_t)(int8_t)v:(uint16_t)v;
}
static void close_track(DndIplSequence *s,unsigned n) {
  DndIplTrack *t=&s->track[n];if(!t->active)return;
  for(unsigned i=0;i<16;i++)if(t->child[i]>=0)close_track(s,(unsigned)t->child[i]);
  emit(s,n,DND_IPL_TRACK_CLOSED,0,0,0,0,0,0,0);t->active=false;
  if(t->parent>=0)for(unsigned i=0;i<16;i++)if(s->track[t->parent].child[i]==(int)n)s->track[t->parent].child[i]=-1;
}
static int open_track(DndIplSequence *s,int parent,unsigned flags,unsigned entry,unsigned owner) {
  if(entry>=s->length||s->generation==UINT_MAX){fail(s,entry,259);return -1;}
  unsigned n=0;
  for(;n<DND_IPL_TRACKS;n++)if(!s->track[n].active)break;
  if(n==DND_IPL_TRACKS){fail(s,entry,260);return -1;}
  DndIplTrack *t=&s->track[n];memset(t,0,sizeof(*t));
  t->active=true;t->generation=++s->generation;t->owner=owner;t->pc=entry;t->parent=parent;t->born_block=s->blocks;
  t->tempo=150;t->timebase=120;t->interrupts_enabled=true;t->time_relative=true;
  t->independent=(flags&0x40)!=0;t->reg[7]=12;t->reg[10]=32767;t->last_key=60;
  for(unsigned i=0;i<16;i++)t->child[i]=-1;
  for(unsigned i=0;i<8;i++)t->interrupt[i]=UINT_MAX;
  for(unsigned i=0;i<DND_IPL_PARAMS;i++)t->parameter[i].value=t->parameter[i].target=1;
  t->parameter[1].value=t->parameter[1].target=0;
  t->parameter[2].value=t->parameter[2].target=0;
  t->parameter[3].value=t->parameter[3].target=.5f;
  t->parameter[4].value=t->parameter[4].target=0;
  t->parameter[5].value=t->parameter[5].target=0;
  t->parameter[16].value=t->parameter[16].target=.5f;
  t->parameter[17].value=t->parameter[17].target=0;
  for(unsigned i=12;i<16;i++)t->parameter[i].value=t->parameter[i].target=0;
  for(unsigned i=0;i<4;i++)t->mix[i]=((i+1)<<4)|(i<2?4:5);
  t->simple_oscillator=0;t->oscillator_modes[1]=1;
  if(parent>=0) {
    DndIplTrack *p=&s->track[parent];t->tempo=p->tempo;t->timebase=p->timebase;
    if(!(flags&0x80))memcpy(t->reg,p->reg,sizeof(t->reg));
    if(!t->independent) {
      memcpy(t->mix,p->mix,sizeof(t->mix));
    }
  }
  return (int)n;
}
static bool condition(DndIplSequence *s,DndIplTrack *t,unsigned c) {
  unsigned v=t->reg[3];
  switch(c&15){case 0:return true;case 1:return v==0;case 2:return v!=0;case 3:return v==1;case 4:return v>=32768;case 5:return v<32768;default:fail(s,t->pc,261);return false;}
}
static void write_port(DndIplTrack *t,unsigned port,unsigned value) {
  t->port[port]=(uint16_t)value;t->imported|=(uint16_t)(1u<<port);
  if(port<2)t->pending|=1u<<(port+3);
}
static int signed_byte(unsigned value) {return (int)(int8_t)value;}
static int value(DndIplSequence *s,DndIplTrack *t,unsigned mode) {
  unsigned v;
  switch(mode&12) {
    case 0:return (int16_t)reg(s,t,take(s,t,1));
    case 4:return (int)take(s,t,1);
    case 8:v=take(s,t,1);return (int16_t)(v<<8);
    default:return (int16_t)take(s,t,2);
  }
}
static void register_op(DndIplSequence *s,DndIplTrack *t,unsigned op) {
  unsigned mode=op&12,action=op&3,width=0;
  if(op==0xa9){mode=0;action=5;}
  if(op==0xaa){unsigned format=take(s,t,1);mode=format&12;width=(format>>4)+1;action=4;}
  if(op==0xab){unsigned format=take(s,t,1);mode=format&12;action=16+(format>>4);}
  unsigned r=take(s,t,1),base=action==4?reg(s,t,take(s,t,1)):0;
  int v=value(s,t,mode),old=(int16_t)reg(s,t,r);
  if(action==0){}
  else if(action==1)v=old+(mode==4?signed_byte((unsigned)v):v);
  else if(action==2){unsigned product=(unsigned)(old*v);put(s,t,4,product>>16);put(s,t,5,product);return;}
  else if(action==3){t->reg[3]=(uint16_t)(old-v);return;}
  else if(action==4) {
    if(v<0||width>3||(unsigned)v>(UINT_MAX-base)/width){fail(s,t->pc,262);return;}
    v=(int)read_at(s,base+(unsigned)v*width,width);
  } else if(action==5)v=old-v;
  else {fail(s,t->pc,op);return;}
  put(s,t,r,(unsigned)v);
}
static void timed_parameter(DndIplSequence *s,DndIplTrack *t,unsigned op) {
  unsigned p=take(s,t,1);int v=value(s,t,op);unsigned duration=0;
  if((op&3)==1)duration=reg(s,t,take(s,t,1));
  else if((op&3)==2)duration=take(s,t,1);
  else if((op&3)==3)duration=take(s,t,2);
  if(p>=DND_IPL_PARAMS){fail(s,t->pc,op);return;}
  t->parameter_written|=1u<<p;
  DndIplParameter *a=&t->parameter[p];a->target=(float)v/32768;
  a->remaining=duration;a->step=duration?(a->target-a->value)/(float)duration:0;
  if(!duration)a->value=a->target;
}
static unsigned branch_target(DndIplSequence *s,DndIplTrack *t,unsigned flags) {
  unsigned target;
  if(flags&128) {
    target=reg(s,t,take(s,t,1));
    if(flags&64) {
      unsigned base=flags&32?reg(s,t,take(s,t,1)):take(s,t,3);
      if(target>(UINT_MAX-base)/3){fail(s,t->pc,263);return 0;}
      target=read_at(s,base+target*3,3);
    }
  } else target=take(s,t,3);
  return target;
}
static void execute(DndIplSequence *s,unsigned n) {
  DndIplTrack *t=&s->track[n];
  while(t->active&&!t->wait&&!s->fault) {
    unsigned at=t->pc,op=take(s,t,1);
    if(++s->work>4096){fail(s,at,op);return;}
    if(op<128) {
      unsigned options=take(s,t,1),velocity=take(s,t,1),key=op;
      if(options&128)key=reg(s,t,key);
      if(velocity>=128)velocity=reg(s,t,velocity-128);
      int transpose=t->transpose;for(int p=t->parent;p>=0;p=s->track[p].parent)transpose+=s->track[p].transpose;
      int note=(int)key+transpose;unsigned slot=options&7,gate=100,duration=UINT_MAX;
      if(!slot) {
        gate=take(s,t,1);if(gate>=128)gate=reg(s,t,gate-128);
        unsigned bytes=(options>>3)&3;duration=take(s,t,bytes);
        if(bytes==1&&duration>=128)duration=reg(s,t,duration-128);
      } else if(options&24)slot=reg(s,t,slot-1);
      if(slot>7||note<0||note>127||velocity>127||gate>100){fail(s,at,op);return;}
      emit(s,n,DND_IPL_NOTE_ON,slot,(unsigned)note,velocity,duration,gate,(options>>5)&3,0);
      t->last_key=note;if(duration!=UINT_MAX)t->wait=duration;
    } else if(op==0x80||op==0x88)t->wait=take(s,t,op==0x80?1:2);
    else if(op>=0x81&&op<=0x87)emit(s,n,DND_IPL_NOTE_OFF,op-0x80,0,0,0,0,0,0);
    else if(op>=0x90&&op<=0x9f)timed_parameter(s,t,op);
    else if(op>=0xa0&&op<=0xaf)register_op(s,t,op);
    else if(op==0xc1) {
      unsigned flags=take(s,t,1),entry=take(s,t,3),slot=flags&15;
      if(t->child[slot]>=0)close_track(s,(unsigned)t->child[slot]);
      int child=open_track(s,(int)n,flags,entry,t->owner);t->child[slot]=child;
      if(child>=0){if(s->processing_tick&&!s->modern)s->track[child].born_block=s->blocks+1;else execute(s,(unsigned)child);}
    } else if(op==0xc4||op==0xc8) {
      unsigned flags=take(s,t,1),target=branch_target(s,t,flags);
      if(condition(s,t,flags)) {
        if(target>=s->length){fail(s,at,op);return;}
        if(op==0xc4){if(t->stack_depth==DND_IPL_STACK){fail(s,at,op);return;}t->stack[t->stack_depth++]=t->pc;}
        t->pc=target;
      }
    } else if(op==0xc5||op==0xc6) {
      unsigned flags=op==0xc6?take(s,t,1):0;
      if(condition(s,t,flags)){if(t->stack_depth)t->pc=t->stack[--t->stack_depth];else close_track(s,n);}
    } else if(op==0xc9) {
      unsigned count=take(s,t,2);if(t->loop_depth==DND_IPL_STACK){fail(s,at,op);return;}
      t->loop_count[t->loop_depth]=count;t->loop_pc[t->loop_depth++]=t->pc;
    } else if(op==0xca) {
      if(!t->loop_depth){fail(s,at,op);return;}
      unsigned i=t->loop_depth-1;
      if(!t->loop_count[i]||--t->loop_count[i])t->pc=t->loop_pc[i];else t->loop_depth--;
    } else if(op==0xcb) {
      unsigned port=take(s,t,1),r=take(s,t,1);
      if(port>15){fail(s,at,op);return;}put(s,t,r,t->port[port]);t->imported&=(uint16_t)~(1u<<port);
    } else if(op==0xcd||op==0xce) {
      unsigned port=take(s,t,1);if(port>15){fail(s,at,op);return;}
      put(s,t,3,((op==0xcd?t->imported:t->exported)>>port)&1);
    } else if(op==0xd0) {unsigned high=take(s,t,2);t->identifier=high<<16|take(s,t,2);}
    else if(op==0xd2) {
      unsigned route=take(s,t,1),v=reg(s,t,take(s,t,1));int child=t->child[route>>4];
      if(child>=0)write_port(&s->track[child],route&15,v);
    } else if(op==0xd4)t->last_key=(int)take(s,t,1)+t->transpose;
    else if(op==0xd5)t->time_relative=take(s,t,1)!=0;
    else if(op==0xd6){t->simple_oscillator=take(s,t,1);t->oscillator_enabled=true;t->oscillator_slot=s->modern&&t->simple_oscillator==1?0:1;
      if(s->modern&&t->simple_oscillator==1)t->has_adsr=false;
      if(t->simple_oscillator>1)fail(s,at,op);}
    else if(op==0xd8){for(unsigned i=0;i<5;i++)t->adsr[i]=take(s,t,2);t->has_adsr=true;t->release_override=true;t->oscillator_modes[0]=3;}
    else if(op==0xd9)t->transpose=signed_byte(take(s,t,1));
    else if(op==0xda) {
      unsigned slot=take(s,t,1);if(slot>15){fail(s,at,op);return;}
      if(t->child[slot]>=0)close_track(s,(unsigned)t->child[slot]);
    } else if(op==0xdc){t->sync_mask=take(s,t,2);emit(s,n,DND_IPL_TRACK_UPDATED,0,0,0,0,0,0,0);}
    else if(op==0xdd) {
      unsigned bus=take(s,t,1),routing=take(s,t,s->modern?2:1);if(bus>5){fail(s,at,op);return;}t->mix[bus]=routing|(s->modern?0x1000000u:0);
    } else if(op==0xdf) {
      unsigned irq=take(s,t,1),entry=take(s,t,3);if(irq>7||entry>=s->length){fail(s,at,op);return;}
      t->interrupt[irq]=entry;t->interrupt_mask|=1u<<irq;
    } else if(op==0xe0) {
      unsigned irq=take(s,t,1);if(irq>7){fail(s,at,op);return;}t->interrupt_mask&=~(1u<<irq);
    } else if(op==0xe1){t->interrupts_enabled=true;t->in_interrupt=false;}
    else if(op==0xe2)t->interrupts_enabled=false;
    else if(op==0xe3) {
      if(!t->in_interrupt){fail(s,at,op);return;}
      t->pc=t->interrupt_pc;t->wait=t->interrupt_wait;t->in_interrupt=false;t->interrupts_enabled=true;
    } else if(op==0xe4) {
      t->timer_count=take(s,t,1);t->timer_period=take(s,t,2);t->timer=t->timer_period;
    } else if(op==0xe5||op==0xe6)t->registered=op==0xe5;
    else if(op==0xe7)emit(s,n,DND_IPL_SYNC,0,take(s,t,2),0,0,0,0,0);
    else if(op==0xe8)emit(s,n,DND_IPL_TRACK_UPDATED,0,0,0,0,0,0,2);
    else if(op==0xe9)emit(s,n,DND_IPL_TRACK_UPDATED,0,0,0,0,0,0,1);
    else if(op==0xf0){unsigned route=take(s,t,1),index=route>>4,mode=route&15;
      if(index>1||mode>3)fail(s,at,op);else {t->oscillator_modes[index]=mode;if(index==1&&mode==1)t->oscillator_time=0;}}
    else if(op==0xfd){unsigned tempo=take(s,t,2);if(!tempo||tempo>1000){fail(s,at,op);return;}t->tempo=tempo;t->override_tempo=true;}
    else if(op==0xfe){unsigned base=take(s,t,2);if(!base||base>1000){fail(s,at,op);return;}t->timebase=base;}
    else if(op==0xff)close_track(s,n);
    else fail(s,at,op);
  }
}
static void interrupt(DndIplSequence *s,DndIplTrack *t) {
  unsigned pending=t->pending&t->interrupt_mask;
  if(!pending||!t->interrupts_enabled||t->in_interrupt)return;
  unsigned i=0;while(!(pending&(1u<<i)))i++;
  if(t->interrupt[i]>=s->length){fail(s,t->pc,264);return;}
  t->pending&=~(1u<<i);t->interrupt_pc=t->pc;t->interrupt_wait=t->wait;
  t->pc=t->interrupt[i];t->wait=0;t->in_interrupt=true;t->interrupts_enabled=false;
}
bool dnd_ipl_sequence_init(DndIplSequence *s,const uint8_t *data,unsigned bytes,
                          DndIplEventHandler handler,void *context) {
  if(!s||!data||!bytes||bytes>4096)return false;
  memset(s,0,sizeof(*s));s->sequence=data;s->length=bytes;s->handler=handler;s->context=context;s->tick_divisor=25200;
  return open_track(s,-1,0,0,0)==0;
}
int dnd_ipl_sequence_open(DndIplSequence *s,unsigned entry,unsigned owner) {
  if(!s||s->fault||!owner)return -1;
  return open_track(s,-1,0,entry,owner);
}
bool dnd_ipl_sequence_port(DndIplSequence *s,unsigned identifier,unsigned port,unsigned v) {
  if(!s||s->fault||port>15||v>65535)return false;
  int found=-1;
  for(unsigned i=0;i<DND_IPL_TRACKS;i++)if(s->track[i].active&&s->track[i].registered&&s->track[i].identifier==identifier) {
    if(found>=0)return false;
    found=(int)i;
  }
  if(found<0)return false;
  write_port(&s->track[found],port,v);return true;
}
void dnd_ipl_sequence_stop_owner(DndIplSequence *s,unsigned owner) {
  if(!s||!owner)return;
  for(unsigned i=0;i<DND_IPL_TRACKS;i++)if(s->track[i].active&&s->track[i].owner==owner)close_track(s,i);
}
float dnd_ipl_sequence_tick_rate(const DndIplSequence *s,unsigned track) {
  const DndIplTrack *t=&s->track[track];
  while(t->parent>=0)t=&s->track[t->parent];
  float rate=(float)t->timebase*(float)t->tempo;
  return rate/s->tick_divisor;
}
void dnd_ipl_sequence_modern(DndIplSequence *s,bool modern) {
  s->modern=modern;
  if(modern) {
    s->track[0].mix[0]=0x1000150;s->track[0].mix[1]=0x1000210;
    s->track[0].mix[2]=0x1000352;s->track[0].mix[3]=0x1000412;
  }
}
static void modern_tick_tree(DndIplSequence *s,unsigned n) {
  DndIplTrack *t=&s->track[n];
  if(!t->active||t->born_block==s->blocks)return;
  t->ticks++;
  if(t->timer&&!--t->timer) {
    t->pending|=1u<<6;if(!t->timer_count||--t->timer_count)t->timer=t->timer_period;
  }
  if(t->wait)t->wait--;
  interrupt(s,t);execute(s,n);
  for(unsigned p=0;p<DND_IPL_PARAMS;p++) {
    DndIplParameter *a=&t->parameter[p];
    if(a->remaining){a->value+=a->step;if(!--a->remaining)a->value=a->target;}
  }
  for(unsigned slot=0;slot<16&&!s->fault;slot++)
    if(t->child[slot]>=0)modern_tick_tree(s,(unsigned)t->child[slot]);
}
static bool linked_child(const DndIplSequence *s,unsigned n) {
  int parent=s->track[n].parent;if(parent<0)return false;
  for(unsigned i=0;i<16;i++)if(s->track[parent].child[i]==(int)n)return true;
  return false;
}
bool dnd_ipl_sequence_block(DndIplSequence *s) {
  if(!s||s->fault)return false;
  s->work=0;
  if(!s->blocks)execute(s,0);
  if(s->modern) {
    for(unsigned i=0;i<DND_IPL_TRACKS&&!s->fault;i++) {
      DndIplTrack *t=&s->track[i];if(!t->active)continue;
      interrupt(s,t);execute(s,i);
    }
    for(unsigned i=0;i<DND_IPL_TRACKS&&!s->fault;i++) {
      DndIplTrack *t=&s->track[i];if(!t->active||linked_child(s,i)||t->born_block==s->blocks)continue;
      t->clock+=dnd_ipl_sequence_tick_rate(s,i);s->processing_tick=true;
      while(t->clock>=1&&t->active&&!s->fault){t->clock-=1;modern_tick_tree(s,i);}
      s->processing_tick=false;
    }
    s->blocks++;return !s->fault;
  }
  for(unsigned i=0;i<DND_IPL_TRACKS&&!s->fault;i++) {
    s->processing_tick=false;DndIplTrack *t=&s->track[i];if(!t->active||t->born_block>s->blocks)continue;
    interrupt(s,t);execute(s,i);
    if(t->born_block==s->blocks)continue;
    t->clock+=dnd_ipl_sequence_tick_rate(s,i);
    s->processing_tick=true;
    while(t->clock>=1&&t->active&&!s->fault) {
      t->clock-=1;t->ticks++;
      for(unsigned p=0;p<DND_IPL_PARAMS;p++) {
        DndIplParameter *a=&t->parameter[p];
        if(a->remaining){a->value+=a->step;if(!--a->remaining)a->value=a->target;}
      }
      if(t->timer&&!--t->timer) {
        t->pending|=1u<<6;
        if(!t->timer_count||--t->timer_count)t->timer=t->timer_period;
      }
      if(t->wait)t->wait--;
      interrupt(s,t);execute(s,i);
    }
  }
  s->processing_tick=false;s->blocks++;return !s->fault;
}
