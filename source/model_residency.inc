/* Frame-budgeted residency for already validated immutable packages. The
 * retained descriptors are rebased only after a sliced SHA-256 comparison with
 * the original snapshot; restoration does not repeat the monolithic parser. */
enum {
  MR_COPY=1, MR_OFFSETS, MR_RETIRE_SKINS, MR_RETIRE_TEXTURES, MR_FREE_PACKAGE,
  MR_REBASE, MR_TEXTURE_SEARCH, MR_TEXTURE_COMPARE, MR_TEXTURE_COPY,
  MR_PREPARE_SKINS, MR_PUBLISH, MR_ABORT_TEXTURES, MR_ABORT_SKINS,
  MR_ABORT_REBASE, MR_ABORT_PACKAGE
};
typedef struct DndModelResidency {
  int aram, request, phase, error, cache;
  bool restore, committed, cancel_requested, charged;
  unsigned position, transfer_bytes, logical_bytes;
  unsigned mesh, skin, clip, instance, instance_token, retire_cursor;
  unsigned texture, texture_cursor, search, free_slot, compare;
  unsigned last_bytes, last_work;
  DndSha256 sha;
  DndSkinPrepareProgress skin_progress;
} DndModelResidency;

static void residency_cache_release(unsigned slot) {
  if (texture_cache[slot].refs == TEXTURE_LOADING ||
      --texture_cache[slot].refs == 0) {
    dnd_model_free(texture_cache[slot].data, &texture_cache_bytes);
    memset(&texture_cache[slot], 0, sizeof(texture_cache[slot]));
  }
}
static void residency_pending_texture(DndModel *m,DndModelResidency *w) {
  if (w->cache >= 0) {
    unsigned slot=(unsigned)w->cache;
    residency_cache_release(slot);
    if(!texture_cache[slot].refs && (w->free_slot==UINT_MAX || slot<w->free_slot))w->free_slot=slot;
    w->cache = -1;
  }
  if (w->charged) {
    m->bytes -= (m->texture[w->texture].size + 63u) & ~31u;
    w->charged = false;
  }
}
static int residency_done(DndModel *m,int result) {
  DndModelResidency *w=m->residency;
  m->residency=NULL;
  dnd_model_free(w,&m->bytes);
  dnd_model_release(m); /* The state owns a native model lease until DMA drains. */
  return result;
}
static int residency_invalid_progress(DndModel *m) {
  /* Owned valid progress cannot fail retirement. Refuse reuse if that private
   * invariant is broken: leave all remaining storage owned/unpublished so
   * ordinary model/instance disposal can reclaim it, rather than retry forever
   * or overwrite an existing staged allocation on a later prepare. */
  m->residency_disabled=true;
  return residency_done(m,-1);
}
static void residency_abort(DndModelResidency *w,int error) {
  if (w->error) return;
  w->error=error;
  if (!w->request) w->phase=MR_ABORT_TEXTURES;
}
static int residency_rebase(DndModel *m,DndModelResidency *w,unsigned maximum,
                             bool relative) {
  unsigned char *base=w->restore?m->staged:m->package;
  unsigned work=0;
  while (work<maximum && w->mesh<m->meshes) {
    DndMesh *mesh=&m->mesh[w->mesh++];
    if (relative) {
      mesh->data=(const unsigned char *)(uintptr_t)(mesh->data-base);
      mesh->index=(const unsigned char *)(uintptr_t)(mesh->index-base);
    } else {
      mesh->data=base+(uintptr_t)mesh->data;
      mesh->index=base+(uintptr_t)mesh->index;
    }
    work++;
  }
  while (work<maximum && w->skin<m->skins) {
    DndSkin *skin=&m->skin[w->skin++];
    skin->data=relative?(const unsigned char *)(uintptr_t)(skin->data-base):base+(uintptr_t)skin->data;
    work++;
  }
  while (work<maximum && w->clip<m->clips) {
    DndClip *clip=&m->clip[w->clip++];
    clip->tracks=relative?(const unsigned char *)(uintptr_t)(clip->tracks-base):base+(uintptr_t)clip->tracks;
    work++;
  }
  w->last_work=work;
  return w->mesh==m->meshes && w->skin==m->skins && w->clip==m->clips;
}
static int residency_retire_skins(DndModel *m,DndModelResidency *w,unsigned maximum) {
  unsigned used=0;
  while (used<maximum && w->instance<DND_INSTANCE_SLOTS) {
    DndInstance *in=dnd_instances[w->instance];
    if (!in || in->model!=m) {
      w->instance++;w->retire_cursor=0;w->instance_token=0;used++;continue;
    }
    if (w->instance_token!=(unsigned)in->token) {
      w->instance_token=(unsigned)in->token;w->retire_cursor=0;
    }
    unsigned before=w->retire_cursor;
    int rc=dnd_model_skin_retire_step(in,&w->retire_cursor,maximum-used);
    used+=w->retire_cursor-before;
    if (rc<0) return rc;
    if (!rc) break;
    w->instance++;w->retire_cursor=0;w->instance_token=0;
  }
  w->last_work=used;
  return w->instance==DND_INSTANCE_SLOTS;
}
static int residency_drop_textures(DndModel *m,DndModelResidency *w,unsigned maximum) {
  unsigned used=0;
  while (used<maximum && w->texture<m->textures) {
    DndTexture *t=&m->texture[w->texture++];
    if (t->data) {
      m->bytes-=(t->size+63u)&~31u;
      residency_cache_release((unsigned)t->cache);t->data=NULL;
    }
    used++;
  }
  w->last_work=used;
  return w->texture==m->textures;
}
int dnd_model_residency_begin(int token,int aram,int restore) {
  DndModel *m=dnd_model_find(token,false);
  if (!m || m->residency_disabled || m->residency || m->restoring || m->file || dnd_device_active() ||
      m->length<160 || (restore!=0 && restore!=1) || m->ready==(restore!=0) ||
      (!restore && !m->package) ||
      (restore && (!aram || !m->residency_digest_valid || !m->residency_offsets)) ||
      (!restore && !aram && !m->residency_digest_valid)) return -1;
  if (!restore && ((const unsigned *)m->package)[-8] < ((m->length+63u)&~31u)) return -1;
  DndModelResidency *w=dnd_model_alloc(sizeof(*w),&m->bytes,m->budget);
  if (!w) return -2;
  w->aram=aram;w->restore=restore!=0;w->cache=-1;w->free_slot=UINT_MAX;
  w->phase=aram?MR_COPY:MR_OFFSETS;
  dnd_sha256_init(&w->sha);
  if (restore) {
    m->staged=dnd_model_alloc_uninitialized(m->length,&m->bytes,m->budget);
    if (!m->staged) {dnd_model_free(w,&m->bytes);return -2;}
  }
  m->residency=w;m->refs++;
  return 0;
}
/* 0 waits for DMA; 2 made bounded progress; 1 committed; -3 canceled.
 * A demotion's retirement phase is an atomic commit: late cancellation is
 * ignored and completes successfully cold, matching synchronous commit rules. */
int dnd_model_residency_step(int token,int byte_budget,int work_budget,int cancel) {
  DndModel *m=dnd_model_find(token,true);
  if (!m || !m->residency || byte_budget<32 || byte_budget>65536 ||
      (byte_budget&31) || work_budget<1 || work_budget>1024) return -1;
  if (dnd_device_active()) return 0;
  DndModelResidency *w=m->residency;
  w->last_bytes=w->last_work=0;
  if ((cancel || m->closed) && !w->committed) residency_abort(w,-3);
  if (w->request) {
    if (w->error && !w->cancel_requested) {
      (void)dnd_aram_request_cancel(w->request);w->cancel_requested=true;
    }
    int rc=dnd_aram_request_poll(w->request);
    if (!rc) return 0;
    w->request=0;w->cancel_requested=false;
    if (rc<0 && !w->error) residency_abort(w,-1);
    if (w->error) {w->phase=MR_ABORT_TEXTURES;return 2;}
    unsigned char *bytes=(w->restore?m->staged:m->package)+w->position;
    dnd_sha256_update(&w->sha,bytes,w->logical_bytes);
    w->last_bytes=w->logical_bytes;
    w->position+=w->transfer_bytes;
    return 2;
  }
  switch (w->phase) {
  case MR_COPY: {
    unsigned length=(m->length+31u)&~31u;
    if (w->position==length) {
      unsigned char digest[32];dnd_sha256_finish(&w->sha,digest);
      if (w->restore) {
        if (memcmp(digest,m->residency_digest,32)) {residency_abort(w,-1);return 2;}
        w->phase=MR_REBASE;
      } else {
        memcpy(m->residency_digest,digest,32);m->residency_digest_valid=true;
        w->phase=MR_OFFSETS;
      }
      return 2;
    }
    unsigned bytes=length-w->position;
    if (bytes>(unsigned)byte_budget) bytes=(unsigned)byte_budget;
    w->logical_bytes=m->length-w->position;
    if (w->logical_bytes>bytes) w->logical_bytes=bytes;
    unsigned char *address=(w->restore?m->staged:m->package)+w->position;
    int request=dnd_aram_request_memory(w->aram,(int)w->position,address,(int)bytes,
                                        w->restore?0:1,w->restore?0:2);
    if (request==-EAGAIN || request==-EBUSY) return 0;
    if (request<0) {residency_abort(w,request==-ENOMEM?-2:-1);return 2;}
    w->request=request;w->transfer_bytes=bytes;return 2;
  }
  case MR_OFFSETS:
    if (!w->committed) {w->committed=true;m->ready=false;}
    if (residency_rebase(m,w,(unsigned)work_budget,true)) {
      m->residency_offsets=true;w->phase=MR_RETIRE_SKINS;
    }
    return 2;
  case MR_RETIRE_SKINS: {
    int rc=residency_retire_skins(m,w,(unsigned)work_budget);
    if(rc<0)return residency_invalid_progress(m);
    if (rc>0) {
      w->texture=0;w->phase=MR_RETIRE_TEXTURES;
    }
    return 2;
  }
  case MR_RETIRE_TEXTURES:
    if (residency_drop_textures(m,w,(unsigned)work_budget)) w->phase=MR_FREE_PACKAGE;
    return 2;
  case MR_FREE_PACKAGE:
    dnd_model_free(m->package,&m->bytes);m->package=NULL;
    return residency_done(m,1);
  case MR_REBASE:
    if (residency_rebase(m,w,(unsigned)work_budget,false)) {
      m->residency_offsets=false;
      w->texture_cursor=dnd_model_u32(m->staged+16+3*16+4)+4;
      w->texture=0;w->search=0;w->free_slot=UINT_MAX;w->phase=MR_TEXTURE_SEARCH;
    }
    return 2;
  case MR_TEXTURE_SEARCH: {
    if (w->texture==m->textures) {
      w->instance=0;w->instance_token=0;
      memset(&w->skin_progress,0,sizeof(w->skin_progress));
      w->phase=MR_PREPARE_SKINS;return 2;
    }
    DndTexture *t=&m->texture[w->texture];
    unsigned charge=(t->size+63u)&~31u;
    if (charge>m->budget || m->bytes>m->budget-charge) {residency_abort(w,-2);return 2;}
    while (w->last_work<(unsigned)work_budget && w->search<512) {
      unsigned slot=w->search++;w->last_work++;
      if (!texture_cache[slot].refs) {
        if (w->free_slot==UINT_MAX) w->free_slot=slot;
      } else if (texture_cache[slot].refs!=TEXTURE_LOADING &&
                 texture_cache[slot].size==t->size && texture_cache[slot].hash==t->hash) {
        texture_cache[slot].refs++;w->cache=(int)slot;
        m->bytes+=charge;w->charged=true;w->compare=0;
        w->phase=MR_TEXTURE_COMPARE;return 2;
      }
    }
    if (w->search<512) return 2;
    /* A free slot seen in an earlier turn may have been taken by another load. */
    if (w->free_slot==UINT_MAX) {residency_abort(w,-2);return 2;}
    if (texture_cache[w->free_slot].refs) {
      w->free_slot=UINT_MAX;w->search=0;
      return 2;
    }
    unsigned slot=w->free_slot;
    void *data=dnd_model_alloc_uninitialized(t->size,&texture_cache_bytes,DND_MODEL_LIMIT);
    if (!data) {residency_abort(w,-2);return 2;}
    texture_cache[slot].data=data;texture_cache[slot].size=t->size;
    texture_cache[slot].hash=t->hash;texture_cache[slot].refs=TEXTURE_LOADING;
    w->cache=(int)slot;w->charged=true;m->bytes+=charge;w->compare=0;
    w->phase=MR_TEXTURE_COPY;return 2;
  }
  case MR_TEXTURE_COMPARE:
  case MR_TEXTURE_COPY: {
    DndTexture *t=&m->texture[w->texture];
    const unsigned char *source=m->staged+w->texture_cursor+28;
    unsigned bytes=t->size-w->compare;if(bytes>(unsigned)byte_budget)bytes=(unsigned)byte_budget;
    unsigned char *target=(unsigned char *)texture_cache[w->cache].data+w->compare;
    w->last_bytes=bytes;
    if (w->phase==MR_TEXTURE_COMPARE) {
      if (memcmp(target,source+w->compare,bytes)) {
        residency_pending_texture(m,w);w->phase=MR_TEXTURE_SEARCH;return 2;
      }
    } else {
      memcpy(target,source+w->compare,bytes);
#ifdef DND_GAMECUBE_STORAGE
      DCFlushRange(target,bytes);
#endif
    }
    w->compare+=bytes;w->last_bytes=bytes;
    if (w->compare==t->size) {
      if(texture_cache[w->cache].refs==TEXTURE_LOADING)texture_cache[w->cache].refs=1;
      t->cache=w->cache;t->data=texture_cache[w->cache].data;
      w->cache=-1;w->charged=false;w->texture_cursor+=28+t->size;w->texture++;
      w->search=0;w->free_slot=UINT_MAX;w->phase=MR_TEXTURE_SEARCH;
    }
    return 2;
  }
  case MR_PREPARE_SKINS:
    while (w->last_work<(unsigned)work_budget && w->instance<DND_INSTANCE_SLOTS) {
      DndInstance *in=dnd_instances[w->instance];
      if (!in || in->model!=m) {w->instance++;w->last_work++;continue;}
      if (w->instance_token!=(unsigned)in->token) {
        w->instance_token=(unsigned)in->token;memset(&w->skin_progress,0,sizeof(w->skin_progress));
      }
      int rc=dnd_model_skin_prepare_step(in,&w->skin_progress,(unsigned)work_budget-w->last_work);
      w->last_work+=w->skin_progress.work;
      if(rc<0){residency_abort(w,rc==-2?-2:-1);return 2;}
      if(!rc)return 2;
      w->instance++;w->instance_token=0;memset(&w->skin_progress,0,sizeof(w->skin_progress));
    }
    if(w->instance==DND_INSTANCE_SLOTS)w->phase=MR_PUBLISH;
    return 2;
  case MR_PUBLISH:
    m->package=m->staged;m->staged=NULL;m->ready=true;
    return residency_done(m,1);
  case MR_ABORT_TEXTURES:
    residency_pending_texture(m,w);
    if (!w->restore) return residency_done(m,w->error);
    w->texture=0;w->phase=MR_ABORT_SKINS;w->instance=0;w->retire_cursor=0;w->instance_token=0;
    return 2;
  case MR_ABORT_SKINS: {
    int rc=residency_retire_skins(m,w,(unsigned)work_budget);
    if(rc<0)return residency_invalid_progress(m);
    if(rc>0)w->phase=MR_ABORT_REBASE;
    return 2;
  }
  case MR_ABORT_REBASE:
    if(!residency_drop_textures(m,w,(unsigned)work_budget))return 2;
    /* Only descriptors already rebased by this restore need converting back. */
    w->phase=MR_ABORT_PACKAGE;w->texture=0;return 2;
  case MR_ABORT_PACKAGE: {
    unsigned used=0;
    while(used<(unsigned)work_budget && w->mesh) {
      DndMesh *mesh=&m->mesh[--w->mesh];
      mesh->data=(const unsigned char *)(uintptr_t)(mesh->data-m->staged);
      mesh->index=(const unsigned char *)(uintptr_t)(mesh->index-m->staged);used++;
    }
    while(used<(unsigned)work_budget && w->skin) {
      DndSkin *skin=&m->skin[--w->skin];skin->data=(const unsigned char *)(uintptr_t)(skin->data-m->staged);used++;
    }
    while(used<(unsigned)work_budget && w->clip) {
      DndClip *clip=&m->clip[--w->clip];clip->tracks=(const unsigned char *)(uintptr_t)(clip->tracks-m->staged);used++;
    }
    w->last_work=used;
    if(w->mesh||w->skin||w->clip)return 2;
    m->residency_offsets=true;dnd_model_free(m->staged,&m->bytes);m->staged=NULL;
    return residency_done(m,w->error);
  }
  default:return -1;
  }
}
int dnd_model_residency_stat(int token,int kind) {
  DndModel *m=dnd_model_find(token,true);
  if(!m||!m->residency)return 0;
  DndModelResidency *w=m->residency;
  return kind==0?w->phase:kind==1?(int)w->last_bytes:kind==2?(int)w->last_work:0;
}
