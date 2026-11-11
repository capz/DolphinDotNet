/* A gate below libdvm's cache prevents writeback to removed or replaced
 * media during handle/cache teardown. It owns the original physical disc. */
typedef struct { DvmDisc base; DvmDisc *inner; bool removed; } MountDiscGate;
static void gate_destroy(DvmDisc *disc) {
  MountDiscGate *gate=(MountDiscGate *)disc;
  gate->inner->vt->destroy(gate->inner);free(gate);
}
static bool gate_read(DvmDisc *disc,void *data,sec_t sector,sec_t count,bool partial) {
  MountDiscGate *gate=(MountDiscGate *)disc;
  return !gate->removed&&gate->inner->vt->read_sectors(gate->inner,data,sector,count,partial);
}
static bool gate_write(DvmDisc *disc,const void *data,sec_t sector,sec_t count,bool partial) {
  MountDiscGate *gate=(MountDiscGate *)disc;
  return !gate->removed&&gate->inner->vt->write_sectors(gate->inner,data,sector,count,partial);
}
static bool gate_flush(DvmDisc *disc) {
  MountDiscGate *gate=(MountDiscGate *)disc;
  return gate->removed||dvmDiscFlush(gate->inner);
}
static const DvmDiscIface gate_interface={gate_destroy,gate_read,gate_write,gate_flush};
