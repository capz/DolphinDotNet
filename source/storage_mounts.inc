/* Included by storage.c: one registry for managed, native and asset I/O paths. */
#define MOUNT_EVENT_CAP 16
static DndMountInfo mount_registry[7];
static DndMountChange mount_events[MOUNT_EVENT_CAP];
static unsigned mount_event_head, mount_event_count;
static bool mount_event_overflow, mount_polling;
static int64_t mount_sequence, mount_identity;
static bool filesystem_device(int d) { return d>=0 && d<7 && d!=4 && d!=5; }
static void mount_enqueue(int d,int reason) {
  DndMountInfo *info=&mount_registry[d];
  snprintf(info->root_path,sizeof(info->root_path),"%s:/",prefixes[d]);
  info->change_sequence=++mount_sequence;
  if(mount_event_count==MOUNT_EVENT_CAP) {
    mount_event_overflow=true;
    mount_event_head=(mount_event_head+1)%MOUNT_EVENT_CAP;
    mount_event_count--;
  }
  unsigned at=(mount_event_head+mount_event_count++)%MOUNT_EVENT_CAP;
  mount_events[at]=(DndMountChange){.info=*info,.reason=reason};
}
static int64_t mount_generation(int d) { return filesystem_device(d)?mount_registry[d].mount_id:0; }
static void mount_record_success(int d,const DndMountConfiguration *configuration) {
  if(!filesystem_device(d))return;
  DndMountInfo *info=&mount_registry[d];
  info->configuration=*configuration;
  info->state=1;info->presence=1;info->last_error=0;
  info->mount_id=++mount_identity;
  info->source_mount_id=configuration->kind==3?mount_generation(configuration->source_device):0;
  mount_enqueue(d,1);info->last_mount_sequence=info->change_sequence;
  /* The queued snapshot must include the successful-mount ordering too. */
  mount_events[(mount_event_head+mount_event_count-1)%MOUNT_EVENT_CAP].info=*info;
}
static void mount_record_failure(int d,int error) {
  if(filesystem_device(d))mount_registry[d].last_error=error;
}
static void mount_record_unmounted(int d) {
  if(!filesystem_device(d))return;
  mount_registry[d].state=2;mount_registry[d].presence=3;
  mount_enqueue(d,2);
}
bool dnd_storage_get_mount_info(int d,DndMountInfo *info) {
  storage_barrier();
  if(!filesystem_device(d)||!info)return false;
  *info=mount_registry[d];info->configuration.device=d;
  snprintf(info->root_path,sizeof(info->root_path),"%s:/",prefixes[d]);
  if(!info->configuration.kind) {
    info->configuration.source_device=-1;info->configuration.partition_lba=-1;
    info->presence=3;
  }
  return true;
}
int dnd_storage_last_mount_device(void) {
  storage_barrier();int found=-1;int64_t latest=0;
  for(int d=0;d<7;d++)if(filesystem_device(d)&&mount_registry[d].last_mount_sequence>latest) {
    latest=mount_registry[d].last_mount_sequence;found=d;
  }
  return found;
}
bool dnd_storage_mount_valid(int d,int64_t id) {
  storage_barrier();return filesystem_device(d)&&id>0&&mounted[d]&&mount_generation(d)==id;
}
static bool mount_handle_valid(int d,int64_t id) {
  if(dnd_storage_mount_valid(d,id))return true;
  fail(ENODEV,"Filesystem access belongs to an inactive mount.");return false;
}
static bool mount_configuration_matches(const DndMountConfiguration *wanted,const DndMountConfiguration *actual) {
  if(wanted->device!=actual->device||wanted->kind!=actual->kind)return false;
  if(wanted->kind==2)return wanted->disc_format==actual->disc_format;
  if(wanted->kind==3)return !strcasecmp(wanted->image_path,actual->image_path)&&
    (wanted->source_device<0||(wanted->source_device==actual->source_device&&
      (wanted->source_partition_lba<0||wanted->source_partition_lba==actual->source_partition_lba)&&
      (!wanted->source_filesystem||wanted->source_filesystem==actual->source_filesystem)&&
      wanted->source_cache_pages==actual->source_cache_pages&&wanted->source_sectors_per_page==actual->source_sectors_per_page));
  return (wanted->partition_lba<0||wanted->partition_lba==actual->partition_lba)&&
    (!wanted->filesystem||wanted->filesystem==actual->filesystem)&&
    wanted->cache_pages==actual->cache_pages&&wanted->sectors_per_page==actual->sectors_per_page;
}
static int mount_presence(int d) {
#ifdef DND_GAMECUBE_STORAGE
  return dnd_gc_storage_presence(d);
#else
  struct stat st;
  if(stat(roots[d],&st)==0)return S_ISDIR(st.st_mode)?1:0;
  return errno==ENOENT||errno==ENODEV?0:3;
#endif
}
static void mount_removed(int d) {
  if(!filesystem_device(d)||!mounted[d])return;
#ifdef DND_GAMECUBE_STORAGE
  /* Fence the physical device before closing handles or flushing a cache. */
  dnd_gc_storage_invalidate(d);
#endif
  if(d==iso_source_device&&mounted[3])mount_removed(3);
  mounted[d]=false;
  mount_registry[d].state=3;mount_registry[d].presence=0;mount_registry[d].last_error=ENODEV;
  for(int i=0;i<HANDLE_CAP;i++) {
    if(files[i].token&&files[i].device==d&&files[i].fd>=0) {
      close(files[i].fd);files[i].fd=-1;files[i].dirty=false;
    }
    if(directories[i].token&&directories[i].device==d&&directories[i].dir) {
      closedir(directories[i].dir);directories[i].dir=NULL;
    }
  }
  if(contiguous_tokens[d]) {
    /* Do not attempt journal mutation on missing/replaced media. Recover it
     * through the existing journal validation on the next successful mount. */
    if(contiguous_writers[d].fd>=0)close(contiguous_writers[d].fd);
    contiguous_writers[d].fd=-1;contiguous_writers[d].active=false;
    contiguous_tokens[d]=0;
  }
#ifdef DND_GAMECUBE_STORAGE
  dnd_gc_unmount(d);
#endif
  if(d==3)iso_source_device=-1;
  mount_enqueue(d,3);
}
void dnd_storage_poll(void) {
  storage_barrier();if(mount_polling)return;mount_polling=true;
  for(int d=0;d<7;d++)if(filesystem_device(d)&&mounted[d]) {
    int presence;
    if(d==3&&iso_source_device>=0)presence=mounted[iso_source_device]?mount_presence(iso_source_device):0;
    else presence=mount_presence(d);
    mount_registry[d].presence=presence;
    if(presence==0)mount_removed(d);
  }
  mount_polling=false;
}
static void mount_check_io(int error) {
  (void)error;
  /* An I/O error alone is not proof of removal. Probe only already-started
   * interfaces; unknown/busy must never invalidate an active volume. */
  dnd_storage_poll();
}
bool dnd_storage_next_mount_change(DndMountChange *change) {
  storage_barrier();if(!change)return false;
  if(mount_event_overflow) {
    memset(change,0,sizeof(*change));change->reason=4;
    change->info.configuration.device=-1;change->info.change_sequence=mount_sequence;
    mount_event_overflow=false;mount_event_head=mount_event_count=0;return true;
  }
  if(!mount_event_count)return false;
  *change=mount_events[mount_event_head];mount_event_head=(mount_event_head+1)%MOUNT_EVENT_CAP;
  mount_event_count--;return true;
}
static bool mount_configuration_valid(const DndMountConfiguration *c) {
  if(!c||!filesystem_device(c->device))return false;
  if(c->kind==1)return c->device!=3&&c->partition_lba>=-1&&c->filesystem>=0&&c->filesystem<=2&&
    c->cache_pages>=1&&c->cache_pages<=64&&!(c->cache_pages&(c->cache_pages-1))&&
    c->sectors_per_page>=1&&c->sectors_per_page<=128&&!(c->sectors_per_page&(c->sectors_per_page-1));
  if(c->device!=3)return false;
  if(c->kind==2)return c->disc_format==1||c->disc_format==2;
  return c->kind==3&&c->disc_format==2&&memchr(c->image_path,0,sizeof(c->image_path))&&c->image_path[0];
}
static int mount_attempt(const DndMountConfiguration *configuration) {
  if(mounted[configuration->device])return mount_configuration_matches(configuration,
    &mount_registry[configuration->device].configuration)?0:4;
  /* Catch only around the legacy throwing bridges, retaining their contracts. */
  DndEhFrame frame;dnd_eh_push(&frame);
  if(setjmp(frame.environment)) {
    DndExceptionKind kind=dnd_exception_kind();dnd_eh_pop(&frame);dnd_exception_clear();
    int result=kind==DND_EXCEPTION_NOT_SUPPORTED?5:kind==DND_EXCEPTION_ARGUMENT?6:
      kind==DND_EXCEPTION_DRIVE_NOT_FOUND?2:7;
    mount_record_failure(configuration->device,result==5?ENOSYS:result==6?EINVAL:result==2?ENODEV:EIO);
    return result;
  }
  bool success;
  if(configuration->kind==1)success=storage_mount_volume(configuration->device,configuration);
  else if(configuration->kind==2)success=dnd_storage_mount_disc(configuration->disc_format);
  else {
    struct {DndObject object;uint32_t length;uint16_t chars[PATH_CAP];} text={0};
    text.object.type=&DND_TYPE_STRING;
    /* Native mount configuration paths are UTF-8, not byte-expanded UTF-16. */
    const unsigned char *p=(const unsigned char *)configuration->image_path;text.length=0;
    while(*p) {
      uint32_t c=*p++;
      if(c>=0x80) {
        unsigned extra=c>=0xf0?3:c>=0xe0?2:c>=0xc2?1:4;
        if(extra==4){fail(EINVAL,"Invalid UTF-8 image path.");break;}
        c&=extra==3?7:extra==2?15:31;
        for(unsigned n=0;n<extra;n++){if((*p&0xc0)!=0x80){fail(EINVAL,"Invalid UTF-8 image path.");break;}c=(c<<6)|(*p++&63);}
        if(c>0x10ffff||(c>=0xd800&&c<=0xdfff)||(extra==1&&c<0x80)||(extra==2&&c<0x800)||(extra==3&&c<0x10000))fail(EINVAL,"Invalid UTF-8 image path.");
      }
      if(c>=0x10000){c-=0x10000;text.chars[text.length++]=(uint16_t)(0xd800+(c>>10));c=0xdc00+(c&1023);}
      text.chars[text.length++]=(uint16_t)c;
    }
    success=dnd_storage_mount_disc_image((DndString *)&text);
  }
  dnd_eh_pop(&frame);
  return success?0:2;
}
static bool mount_canonical_image(const char *path,char output[PATH_CAP]) {
  DndEhFrame frame;dnd_eh_push(&frame);
  if(setjmp(frame.environment)){dnd_eh_pop(&frame);dnd_exception_clear();return false;}
  int d=normalize(path,output);dnd_eh_pop(&frame);return d>=0;
}
static void mount_release_dependency(int device) {
  DndEhFrame frame;dnd_eh_push(&frame);
  if(setjmp(frame.environment)){dnd_eh_pop(&frame);dnd_exception_clear();return;}
  dnd_storage_unmount(device);dnd_eh_pop(&frame);
}
int dnd_storage_mount_configuration(const DndMountConfiguration *requested) {
  storage_barrier();if(!mount_configuration_valid(requested))return 6;
  DndMountConfiguration copy=*requested;const DndMountConfiguration *c=&copy;
  if(c->kind==3&&!mount_canonical_image(requested->image_path,copy.image_path))return 6;
  if(mounted[c->device]) {
    if(mount_configuration_matches(c,&mount_registry[c->device].configuration))return 0;
    mount_record_failure(c->device,EBUSY);return 4;
  }
  int dependency=-1;bool acquired=false;
  if(c->kind==3) {
    const char *colon=strchr(c->image_path,':');
    dependency=colon?device_name(c->image_path,(size_t)(colon-c->image_path)):-1;
    if(!filesystem_device(dependency)||dependency==3)return 6;
    if(c->source_device>=0) {
      if(c->source_device!=dependency)return 6;
      DndMountConfiguration source={.device=dependency,.kind=1,.partition_lba=c->source_partition_lba,
        .filesystem=c->source_filesystem,.cache_pages=c->source_cache_pages,
        .sectors_per_page=c->source_sectors_per_page,.source_device=-1};
      if(!mount_configuration_valid(&source))return 6;
      acquired=!mounted[dependency];int result=mount_attempt(&source);
      if(result){mount_record_failure(c->device,result==4?EBUSY:ENODEV);return result;}
    }else if(!mounted[dependency]){mount_record_failure(c->device,ENODEV);return 2;}
  }
  int result=mount_attempt(c);
  if(result&&acquired)mount_release_dependency(dependency);
  return result;
}
int dnd_storage_remount(int d) {
  storage_barrier();if(!filesystem_device(d))return 6;
  if(!mount_registry[d].configuration.kind)return 1;
  DndMountConfiguration copy=mount_registry[d].configuration;
  return dnd_storage_mount_configuration(&copy);
}
/* The wire format is private to NativeStorage, deliberately independent of C
 * struct padding, host endianness, sizeof(long), and managed object layout. */
static DndArray *mount_wire(DndManagedHeap *heap,const DndMountInfo *info,int reason) {
  const DndMountConfiguration *c=&info->configuration;
  int64_t fields[]={1,info->state,c->device,c->kind,c->disc_format,c->filesystem,
    c->partition_lba,c->cache_pages,c->sectors_per_page,c->source_device,info->source_mount_id,
    info->mount_id,info->last_mount_sequence,info->change_sequence,info->last_error,info->presence,reason,
    c->source_partition_lba,c->source_filesystem,c->source_cache_pages,c->source_sectors_per_page};
  size_t path=strlen(c->image_path),header=sizeof(fields);
  DndArray *data=dnd_managed_array_new_typed(heap,(uint32_t)(header+path),1,&DND_TYPE_BYTE,false);
  if(!data)return NULL;
  for(unsigned n=0;n<sizeof(fields)/sizeof(fields[0]);n++)for(unsigned b=0;b<8;b++)
    data->data[n*8+b]=(uint8_t)((uint64_t)fields[n]>>(b*8));
  memcpy(data->data+header,c->image_path,path);return data;
}
DndArray *dnd_storage_mount_snapshot(DndManagedHeap *heap,int d) {
  DndMountInfo info;
  if(!dnd_storage_get_mount_info(d,&info)){fail(EINVAL,"Not a filesystem device.");return NULL;}
  return mount_wire(heap,&info,0);
}
DndArray *dnd_storage_mount_change(DndManagedHeap *heap) {
  storage_barrier();DndMountChange change;
  if(mount_event_overflow) {
    memset(&change,0,sizeof(change));change.reason=4;change.info.configuration.device=-1;
    change.info.change_sequence=mount_sequence;
  }else if(mount_event_count)change=mount_events[mount_event_head];
  else return NULL;
  /* Allocate before consuming: an allocation failure must not silently lose
   * a transition. No managed allocation occurs after the queue is advanced. */
  DndArray *data=mount_wire(heap,&change.info,change.reason);
  if(data)dnd_storage_next_mount_change(&change);
  return data;
}
bool dnd_storage_mount_handler_equal(DndObject *first,DndObject *second) {
  if(first==second)return true;
  if(!first||!second)return false;
  const DndDelegate *a=(DndDelegate *)first,*b=(DndDelegate *)second;
  return a->target==b->target&&a->method==b->method&&a->managed_method==b->managed_method&&
    a->managed_has_target==b->managed_has_target;
}
int dnd_storage_mount_configured(int d,int kind,int format,DndString *image,int64_t partition,
                                int filesystem,int pages,int sectors,int source,int64_t source_partition,
                                int source_fs,int source_pages,int source_sectors) {
  DndMountConfiguration c={.device=d,.kind=kind,.disc_format=format,.partition_lba=partition,
    .filesystem=filesystem,.cache_pages=pages,.sectors_per_page=sectors,.source_device=source,
    .source_partition_lba=source_partition,.source_filesystem=source_fs,
    .source_cache_pages=source_pages,.source_sectors_per_page=source_sectors};
  /* Managed factory validation guarantees a non-null string; use a local EH
   * scope so malformed Unicode is a result, not an unexpected thrown failure. */
  DndEhFrame frame;dnd_eh_push(&frame);
  if(setjmp(frame.environment)){dnd_eh_pop(&frame);dnd_exception_clear();return 6;}
  bool valid=utf8(image,c.image_path);dnd_eh_pop(&frame);
  return valid?dnd_storage_mount_configuration(&c):6;
}
int dnd_storage_open_mounted(int d,int64_t id,DndString *relative,int mode,int access,int share) {
  storage_barrier();if(!mount_handle_valid(d,id))return 0;
  char tail[PATH_CAP],full[PATH_CAP];
  if(!utf8(relative,tail))return 0;
  if(strchr(tail,':')||tail[0]=='/'||tail[0]=='\\'){fail(EINVAL,"Expected a relative filesystem path.");return 0;}
  int length=snprintf(full,sizeof(full),"%s:/%s",prefixes[d],tail);
  if(length<0||length>=PATH_CAP){fail(ENAMETOOLONG,"Path too long.");return 0;}
  /* No managed allocation or scheduler boundary between generation validation
   * and opening the native handle. The UTF-8 bridge preserves normal checks. */
  return dnd_fs_open_utf8_native(full,mode,access,share);
}
