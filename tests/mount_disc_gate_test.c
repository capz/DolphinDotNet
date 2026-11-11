#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
typedef uint64_t sec_t;
typedef struct DvmDisc DvmDisc;
typedef struct {
  void (*destroy)(DvmDisc *);
  bool (*read_sectors)(DvmDisc *,void *,sec_t,sec_t,bool);
  bool (*write_sectors)(DvmDisc *,const void *,sec_t,sec_t,bool);
  bool (*flush)(DvmDisc *);
} DvmDiscIface;
struct DvmDisc {const DvmDiscIface *vt;};
static bool dvmDiscFlush(DvmDisc *d){return d->vt->flush(d);}
#include "../source/storage_disc_gate.inc"
static int reads,writes,flushes,destroyed;
static bool partial;
static void destroy(DvmDisc *d){destroyed++;free(d);}
static bool read_sectors(DvmDisc *d,void *data,sec_t sector,sec_t count,bool p)
{(void)d;(void)data;assert(sector==7&&count==2);reads++;partial=p;return true;}
static bool write_sectors(DvmDisc *d,const void *data,sec_t sector,sec_t count,bool p)
{(void)d;(void)data;assert(sector==7&&count==2);writes++;partial=p;return true;}
static bool flush(DvmDisc *d){(void)d;flushes++;return true;}
int main(void) {
  static const DvmDiscIface backend={destroy,read_sectors,write_sectors,flush};
  MountDiscGate *gate=calloc(1,sizeof(*gate));assert(gate);gate->inner=calloc(1,sizeof(DvmDisc));assert(gate->inner);
  gate->inner->vt=&backend;gate->base.vt=&gate_interface;
  assert(gate_read(&gate->base,NULL,7,2,true)&&reads==1&&partial);
  assert(gate_write(&gate->base,NULL,7,2,false)&&writes==1&&!partial);
  assert(gate_flush(&gate->base)&&flushes==1);
  gate->removed=true;
  assert(!gate_read(&gate->base,NULL,7,2,false)&&reads==1);
  assert(!gate_write(&gate->base,NULL,7,2,false)&&writes==1);
  assert(gate_flush(&gate->base)&&flushes==1); /* Cache teardown discards writeback. */
  gate_destroy(&gate->base);assert(destroyed==1);
}
