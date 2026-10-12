#include "dnd_network.h"
#include "dnd_network_platform.h"
#include <string.h>
#ifdef DND_NET_TRACE
#include <stdio.h>
#endif
#define NET_SLOTS 12
#define UDP_CAP 65507
typedef struct { int token,fd,stream,connecting; } NetSlot;
static NetSlot slots[NET_SLOTS];
static int serial=1;
typedef struct { DndException exception;int error; } NetException;
const DndType DND_TYPE_EXTERNAL_EXCEPTION={"System.Runtime.InteropServices.ExternalException",&DND_TYPE_SYSTEM_EXCEPTION,sizeof(DndException),0,NULL,0,NULL,0,0,NULL,0,NULL};
const DndType DND_TYPE_SOCKET_EXCEPTION={"System.Net.Sockets.SocketException",&DND_TYPE_EXTERNAL_EXCEPTION,sizeof(NetException),0,NULL,0,NULL,0,0,NULL,0,NULL};
DndObject *dnd_net_failure(DndManagedHeap *heap,int error,int result){DndObject *root=dnd_object_new(heap,&DND_TYPE_SOCKET_EXCEPTION);if(!root)return NULL;DndObject **roots[]={&root};DndGcFrame frame;dnd_gc_frame_push(&frame,roots,1);((NetException*)root)->error=error;((NetException*)root)->exception.message=dnd_string_from_utf8(heap,result==-5?"Datagram truncated.":result==-3?"Socket disposed.":result==-4?"Socket capacity exhausted.":"Network operation failed.");dnd_gc_frame_pop(&frame);return root;}
int dnd_net_failure_code(DndObject *object){return object&&object->type==&DND_TYPE_SOCKET_EXCEPTION?((NetException*)object)->error:0;}
/* One application-thread staging area detects truncation without managed pointers
   escaping the bridge. No native worker touches sockets or these buffers. */
static unsigned char datagram[UDP_CAP+1];
static NetSlot *slot(int token){for(int i=0;i<NET_SLOTS;i++)if(slots[i].token==token&&token>0)return &slots[i];return NULL;}
static int valid(DndArray *a,int offset,int count){return a&&a->element_size==1&&offset>=0&&count>=0&&(uint32_t)offset<=a->length&&(uint32_t)count<=a->length-(uint32_t)offset;}
static int endpoint(DndArray *a,uint32_t ip,int port){if(!a||a->element_size!=4||a->length<2)return DND_NET_ERROR;int32_t values[2]={(int32_t)ip,port};memcpy(a->data,values,sizeof(values));return 0;}
static int add(int fd,int stream){if(fd<0)return fd;for(int i=0;i<NET_SLOTS;i++)if(!slots[i].token){if(serial==2147483647){dnd_np_close(fd);return DND_NET_LIMIT;}slots[i]=(NetSlot){serial++,fd,stream,0};return slots[i].token;}dnd_np_close(fd);return DND_NET_LIMIT;}
int dnd_net_start(int ip,int mask,int gateway,int dhcp){return dnd_np_start((uint32_t)ip,(uint32_t)mask,(uint32_t)gateway,dhcp);}
int dnd_net_status(void){return dnd_np_status();}
int dnd_net_time(void){return (int)dnd_np_time();}
int dnd_net_error(void){return dnd_np_error();}
int dnd_net_open(int stream){if(dnd_np_status()!=1)return DND_NET_ERROR;return add(dnd_np_open(stream),stream);}
int dnd_net_bind(int t,int ip,int port){NetSlot *s=slot(t);return !s?DND_NET_STALE:port<0||port>65535?DND_NET_ERROR:dnd_np_bind(s->fd,(uint32_t)ip,port);}
int dnd_net_listen(int t,int backlog){NetSlot *s=slot(t);return !s?DND_NET_STALE:!s->stream||backlog<1||backlog>NET_SLOTS?DND_NET_ERROR:dnd_np_listen(s->fd,backlog);}
int dnd_net_accept(int t,DndArray *ep){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;if(!s->stream||!ep||ep->element_size!=4||ep->length<2)return DND_NET_ERROR;uint32_t ip=0;int port=0,fd=dnd_np_accept(s->fd,&ip,&port);if(fd<0)return fd;int h=add(fd,1);if(h>0)endpoint(ep,ip,port);return h;}
int dnd_net_connect(int t,int ip,int port){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;if(port<1||port>65535)return DND_NET_ERROR;int r=dnd_np_connect(s->fd,(uint32_t)ip,port);s->connecting=r==DND_NET_WAIT;return r;}
int dnd_net_connected(int t){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;if(!s->connecting)return 0;int r=dnd_np_connected(s->fd);if(r!=DND_NET_WAIT)s->connecting=0;return r;}
int dnd_net_read(int t,DndArray *b,int off,int count,DndArray *ep){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;if(!valid(b,off,count))return DND_NET_ERROR;if(s->stream&&count==0)return 0;if(!s->stream&&(!ep||ep->element_size!=4||ep->length<2))return DND_NET_ERROR;uint32_t ip=0;int port=0;int n=dnd_np_read(s->fd,s->stream?b->data+off:datagram,s->stream?count:UDP_CAP+1,s->stream,&ip,&port);if(n<0)return n;if(!s->stream){endpoint(ep,ip,port);if(n>count||n>UDP_CAP)return DND_NET_TRUNCATED;memcpy(b->data+off,datagram,(size_t)n);}return n;}
int dnd_net_write(int t,DndArray *b,int off,int count,int ip,int port){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;if(!valid(b,off,count)||(!s->stream&&(count>UDP_CAP||port<1||port>65535)))return DND_NET_ERROR;
#ifdef DND_NET_TRACE
fprintf(stderr,"send token=%d stream=%d ip=%x port=%d bytes=%d\n",t,s->stream,(unsigned)ip,port,count);
#endif
return dnd_np_write(s->fd,b->data+off,count,s->stream,(uint32_t)ip,port);}
int dnd_net_endpoint(int t,DndArray *ep,int remote){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;uint32_t ip=0;int port=0;int r=dnd_np_endpoint(s->fd,&ip,&port,remote);return r<0?r:endpoint(ep,ip,port);}
int dnd_net_option(int t,int opt,int value){NetSlot *s=slot(t);return !s?DND_NET_STALE:dnd_np_option(s->fd,opt,value);}
int dnd_net_close(int t){NetSlot *s=slot(t);if(!s)return DND_NET_STALE;dnd_np_close(s->fd);memset(s,0,sizeof(*s));return 0;}
int dnd_net_stop(void){if(dnd_np_status()==0)return DND_NET_WAIT;for(int i=0;i<NET_SLOTS;i++)if(slots[i].token)dnd_net_close(slots[i].token);return 0;}
