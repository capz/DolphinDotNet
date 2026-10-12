#define _POSIX_C_SOURCE 200809L
#include "dnd_network.h"
#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
static DndArray *array(unsigned count,unsigned element){DndArray *a=calloc(1,sizeof(DndArray)+count*element);assert(a);a->length=count;a->element_size=element;return a;}
static int wait_read(int h,DndArray *data,DndArray *ep){for(int i=0;i<100000;i++){int r=dnd_net_read(h,data,0,(int)data->length,ep);if(r!=DND_NET_WAIT)return r;}return DND_NET_WAIT;}
int main(void){assert(dnd_net_start(0,0,0,1)==0);assert(dnd_net_status()==1);DndArray *ep=array(2,4),*source=array(8,1),*small=array(1,1),*bytes=array(8,1);int server=dnd_net_open(0),client=dnd_net_open(0);assert(server>0&&client>0);assert(dnd_net_bind(server,0x7f000001,0)==0);assert(dnd_net_endpoint(server,ep,0)==0);int endpoint[2];memcpy(endpoint,ep->data,sizeof(endpoint));int port=endpoint[1];assert(dnd_net_read(server,bytes,0,8,ep)==DND_NET_WAIT);assert(dnd_net_write(client,source,0,0,0x7f000001,port)==0);assert(wait_read(server,bytes,ep)==0);assert(dnd_net_write(client,source,0,8,0x7f000001,port)==8);assert(wait_read(server,small,ep)==DND_NET_TRUNCATED);assert(dnd_net_read(server,bytes,0,8,ep)==DND_NET_WAIT);assert(dnd_net_read(server,bytes,7,2,ep)==DND_NET_ERROR);assert(dnd_net_close(client)==0);int next=dnd_net_open(0);assert(next>0&&next!=client);assert(dnd_net_read(client,bytes,0,8,ep)==DND_NET_STALE);assert(dnd_net_close(next)==0);assert(dnd_net_close(server)==0);int handles[12];for(int i=0;i<12;i++){handles[i]=dnd_net_open(0);assert(handles[i]>0);}assert(dnd_net_open(0)==DND_NET_LIMIT);for(int i=0;i<12;i++)assert(dnd_net_close(handles[i])==0);free(ep);free(source);free(small);free(bytes);puts("native networking lifecycle/limits passed");return 0;}
