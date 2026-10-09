#include "dnd_card_transaction.h"
#include <errno.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#define HEADER 64
#define MAX_SAVE (16*1024*1024)
static void put(uint8_t *p,uint32_t value){p[0]=(uint8_t)(value>>24);p[1]=(uint8_t)(value>>16);p[2]=(uint8_t)(value>>8);p[3]=(uint8_t)value;}
static uint32_t get(const uint8_t *p){return ((uint32_t)p[0]<<24)|((uint32_t)p[1]<<16)|((uint32_t)p[2]<<8)|p[3];}
static uint32_t checksum(const uint8_t *p,int length){uint32_t value=2166136261u;for(int i=0;i<length;i++)value=(value^p[i])*16777619u;return value;}
int dnd_card_recover(const DndCardTransaction *t){
 int size=t->read(t->context,DND_CARD_JOURNAL,NULL,0);if(size==-ENOENT)return 0;if(size<0)return size;if(size<HEADER||size>MAX_SAVE+HEADER+1024*1024)return -EIO;
 uint8_t *record=malloc((size_t)size);if(!record)return -ENOMEM;int rc=t->read(t->context,DND_CARD_JOURNAL,record,size);
 if(rc!=size){free(record);return rc<0?rc:-EIO;}
 bool empty=true;for(int i=0;i<HEADER;i++)if(record[i]){empty=false;break;}
 if(empty){free(record);return t->remove(t->context,DND_CARD_JOURNAL);}
 uint32_t length=get(record+8),flags=get(record+12);char *name=(char*)record+16;
 if(memcmp(record,"DNDTRN01",8)||!length||length>MAX_SAVE||length>(uint32_t)size-HEADER||flags>7||!memchr(name,0,33)||!name[0]||!strcmp(name,DND_CARD_JOURNAL)||get(record+56)!=checksum(record,52)){free(record);return -EIO;}
 if(get(record+52)!=checksum(record+HEADER,(int)length)){
  // The immutable record is not complete: final replacement never started.
  free(record);return t->remove(t->context,DND_CARD_JOURNAL);
 }
 rc=t->remove(t->context,name);if(rc==-ENOENT)rc=0;
 if(rc>=0)rc=t->write(t->context,name,record+HEADER,(int)length);
 if(rc>=0)rc=t->metadata(t->context,name,(flags&1)!=0,(flags&2)!=0,(flags&4)!=0);
 if(rc>=0)rc=t->remove(t->context,DND_CARD_JOURNAL);
 free(record);return rc;
}
int dnd_card_replace(const DndCardTransaction *t,const char *name,const void *data,int length,bool metadata,bool banner,bool icon){
 if(!t||!name||!data||!name[0]||strlen(name)>32||!strcmp(name,DND_CARD_JOURNAL)||length<=0||length>MAX_SAVE)return -EINVAL;
 int rc=dnd_card_recover(t);if(rc<0)return rc;
 uint8_t *record=calloc(1,(size_t)length+HEADER);if(!record)return -ENOMEM;memcpy(record,"DNDTRN01",8);put(record+8,(uint32_t)length);put(record+12,(metadata?1u:0u)|(banner?2u:0u)|(icon?4u:0u));strcpy((char*)record+16,name);memcpy(record+HEADER,data,(size_t)length);put(record+52,checksum(record+HEADER,length));put(record+56,checksum(record,52));
 rc=t->write(t->context,DND_CARD_JOURNAL,record,length+HEADER);free(record);
 if(rc<0){/* Staging failure cannot affect the previous save. Remove only this
              incomplete record; a failed removal is retried at recovery. */
  t->remove(t->context,DND_CARD_JOURNAL);return rc;
 }
 return dnd_card_recover(t);
}
