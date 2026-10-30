#include "dnd_asset_compat.h"
#include <string.h>
#ifdef DND_DEBUG_AUTOMATION
/* Separate optional debug record; no production asset system calls this path.
 * Entirely published on the application thread before the present breakpoint. */
DndAssetCompatRecord dnd_asset_compat_record __attribute__((used,aligned(32))) = {
    .magic=DND_ASSET_MAGIC,.version=DND_ASSET_VERSION,.index=0xffffffffu
};
void dnd_asset_compat_report(int status,int index,int total,int kind,
                             int scanned,const char *name) {
    if(status<DND_ASSET_SCANNING || status>DND_ASSET_FINISHED ||
       total<0 || total>32 || index < -1 || index>=total ||
       kind<0 || kind>3 || scanned<0 || scanned>4096)
        return;
    DndAssetCompatRecord *r=&dnd_asset_compat_record;
    /* Mark a new record only after fields/name are complete. */
    r->status=(uint32_t)status;
    r->index=(uint32_t)index;r->total=(uint32_t)total;
    r->kind=(uint32_t)kind;r->scanned=(uint32_t)scanned;
    size_t n=name?strlen(name):0;
    if(n>=sizeof r->name)n=sizeof r->name-1;
    if(n)memcpy(r->name,name,n);
    r->name[n]=0;
    r->serial++;
    if(r->serial==0)r->serial=1;
}
#else
void dnd_asset_compat_report(int status,int index,int total,int kind,
                             int scanned,const char *name) {
    (void)status;(void)index;(void)total;(void)kind;(void)scanned;(void)name;
}
#endif
