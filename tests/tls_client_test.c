#define _POSIX_C_SOURCE 200809L
#include "dnd_network.h"
#include "dnd_tls.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
const DndType DND_TYPE_SYSTEM_EXCEPTION={0};
void dnd_exception_throw(DndExceptionKind kind,const char *message){(void)kind;(void)message;abort();}
static DndArray *bytes(size_t n){DndArray *a=calloc(1,sizeof(*a)+n);assert(a);a->length=(uint32_t)n;a->element_size=1;return a;}
static DndArray *file(const char *name){FILE *f=fopen(name,"rb");assert(f);fseek(f,0,SEEK_END);long n=ftell(f);rewind(f);assert(n>0);DndArray *a=bytes((size_t)n);assert(fread(a->data,1,(size_t)n,f)==(size_t)n);fclose(f);return a;}
static void pause_poll(void){struct timespec t={0,1000000};nanosleep(&t,NULL);}
int main(int argc,char **argv){
 assert(argc==7);DndArray *roots=file(argv[1]),*seed=file(argv[2]);int64_t utc=strtoll(argv[5],NULL,10);int expect=atoi(argv[6]);
 assert(dnd_net_tls_configure(roots,seed,utc)==0);assert(dnd_net_start(0,0,0,1)==0);
 int t=dnd_net_open(1);assert(t>0);int r=dnd_net_connect(t,0x7f000001,atoi(argv[3]));
 for(int i=0;r==-1&&i<5000;i++){pause_poll();r=dnd_net_connected(t);}assert(r==0);
 size_t length=strlen(argv[4]);DndString *host=calloc(1,sizeof(*host)+length*2);assert(host);host->length=(uint32_t)length;
 for(size_t i=0;i<length;i++)host->chars[i]=(unsigned char)argv[4][i];
 assert(dnd_net_tls_start(t,host)==0);assert(dnd_net_tls_reset()<0);
 for(int i=0;i<10000;i++){r=dnd_net_tls_handshake(t);if(r!=-1)break;pause_poll();}
 if(!expect){assert(r<0&&r!=-1);assert(dnd_net_close(t)==0);assert(dnd_net_tls_reset()==0);free(host);free(roots);free(seed);return 0;}
 assert(r==0);DndArray *request=bytes(64);const char *text="GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n";int count=(int)strlen(text);memcpy(request->data,text,(size_t)count);
 int offset=0;for(int i=0;offset<count&&i<10000;i++){r=dnd_net_write(t,request,offset,count-offset,0,0);assert(r>=-1);if(r>0)offset+=r;else pause_poll();}assert(offset==count);
 DndArray *response=bytes(256);offset=0;
 for(int i=0;i<10000;i++){r=dnd_net_read(t,response,offset,256-offset,NULL);assert(r>=-1);if(r>0){offset+=r;if(offset>=2&&response->data[offset-2]=='O'&&response->data[offset-1]=='K')break;}else pause_poll();}
 assert(offset>2&&strstr((char*)response->data,"200 OK")&&response->data[offset-2]=='O'&&response->data[offset-1]=='K');
 assert(dnd_net_close(t)==0);assert(dnd_net_read(t,response,0,1,NULL)==-3);assert(dnd_net_tls_reset()==0);
 memset(seed->data,0,seed->length);assert(dnd_net_tls_configure(roots,seed,utc)<0);
 assert(dnd_net_tls_reset()==0);free(host);free(roots);free(seed);free(request);free(response);return 0;
}
