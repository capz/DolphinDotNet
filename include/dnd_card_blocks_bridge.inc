/* Include after storage.c's card_arguments helper. */
int dnd_card_blocks_io(int d,DndString *name,int operation,int block,int identity,DndArray *data){
  storage_barrier();char n[PATH_CAP];
  if(!card_arguments(d,name,n)||!data||data->element_size!=1)return -EINVAL;
#ifdef DND_GAMECUBE_STORAGE
  extern int dnd_gc_card_blocks_io(int,const char*,int,int,int,void*,int);
  return dnd_gc_card_blocks_io(d,n,operation,block,identity,data->data,(int)data->length);
#else
  (void)operation;(void)block;(void)identity;return -ENOTSUP;
#endif
}
