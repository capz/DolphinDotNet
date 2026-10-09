#include "dnd_card_transaction.h"
#include <assert.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
typedef struct {char name[33];unsigned char *data;int length,flags;} Save;
typedef struct {Save slots[4];int step,fail;} Card;
static int fault(Card *c){return c->fail&&++c->step>=c->fail;}
static Save *find(Card *c,const char *name){for(int i=0;i<4;i++)if(!strcmp(c->slots[i].name,name))return c->slots+i;return NULL;}
static int read_save(void *context,const char *name,void *data,int length){Card *c=context;if(fault(c))return -EIO;Save *s=find(c,name);if(!s)return -ENOENT;if(!data)return s->length;if(length>s->length)length=s->length;memcpy(data,s->data,(size_t)length);return length;}
static int write_save(void *context,const char *name,const void *data,int length){Card *c=context;if(fault(c))return -EIO;assert(!find(c,name));Save *s=NULL;for(int i=0;i<4;i++)if(!c->slots[i].name[0]){s=c->slots+i;break;}if(!s)return -ENOSPC;strcpy(s->name,name);s->length=length;s->data=calloc(1,(size_t)length);assert(s->data);
 for(int offset=0;offset<length;offset+=512){if(fault(c))return -EIO;int n=length-offset;if(n>512)n=512;memcpy(s->data+offset,(const char*)data+offset,(size_t)n);}return fault(c)?-EIO:0;}
static int remove_save(void *context,const char *name){Card *c=context;if(fault(c))return -EIO;Save *s=find(c,name);if(!s)return -ENOENT;free(s->data);memset(s,0,sizeof(*s));return 0;}
static int metadata(void *context,const char *name,bool m,bool b,bool i){Card *c=context;if(fault(c))return -EIO;Save *s=find(c,name);assert(s);s->flags=(m?1:0)|(b?2:0)|(i?4:0);return 0;}
int main(void){
 unsigned char old[8192],next[16384];memset(old,17,sizeof(old));memset(next,29,sizeof(next));
 for(int failure=1;failure<100;failure++){
  Card card={0};DndCardTransaction t={&card,read_save,write_save,remove_save,metadata};assert(write_save(&card,"save",old,sizeof(old))==0);card.step=0;card.fail=failure;
  int result=dnd_card_replace(&t,"save",next,sizeof(next),true,true,false);card.fail=0;
  assert(dnd_card_recover(&t)==0);Save *save=find(&card,"save");assert(save);bool is_old=save->length==(int)sizeof(old)&&!memcmp(save->data,old,sizeof(old));bool is_new=save->length==(int)sizeof(next)&&!memcmp(save->data,next,sizeof(next))&&save->flags==3;assert(is_old||is_new);if(result>=0)assert(is_new);assert(!find(&card,DND_CARD_JOURNAL));
  for(int i=0;i<4;i++)free(card.slots[i].data);
 }
 puts("native-card resize transaction fault matrix passed");return 0;
}
