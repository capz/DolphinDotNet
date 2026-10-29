/* Exercise provider lifecycle and the finite entropy budget without a server. */
#include "../source/tls.c"
#include <assert.h>
#include <stdio.h>
static int64_t clock_us=1000000;
int64_t dnd_timing_now(void){return clock_us;}
int dnd_np_read(int fd,void *b,int n,int stream,uint32_t *ip,int *port){(void)fd;(void)b;(void)n;(void)stream;(void)ip;(void)port;return -1;}
int dnd_np_write(int fd,const void *b,int n,int stream,uint32_t ip,int port){(void)fd;(void)b;(void)n;(void)stream;(void)ip;(void)port;return -1;}
static DndArray *array(size_t size){DndArray *a=calloc(1,sizeof(*a)+size);assert(a);a->element_size=1;a->length=(uint32_t)size;return a;}
static DndArray *read_file(const char *name){FILE *f=fopen(name,"rb");assert(f);fseek(f,0,SEEK_END);long n=ftell(f);rewind(f);assert(n>0);DndArray *a=array((size_t)n);assert(fread(a->data,1,(size_t)n,f)==(size_t)n);fclose(f);return a;}
int main(int argc,char **argv){
 assert(argc==3);DndArray *ca=read_file(argv[1]),*seed=read_file(argv[2]);
 assert(dnd_tls_begin(1,1,"localhost")<0);
 assert(dnd_net_tls_configure(NULL,seed,1800000000)<0);
 assert(dnd_net_tls_configure(ca,NULL,1800000000)<0);
 assert(dnd_net_tls_configure(ca,seed,0)<0);
 assert(dnd_net_tls_configure(ca,seed,INT64_MAX)<0);
 seed->length=47;assert(dnd_net_tls_configure(ca,seed,1800000000)<0);seed->length=48;
 assert(dnd_net_tls_configure(ca,seed,1800000000)==0);
 assert(tls_time(NULL)==1800000000);clock_us+=3000000;assert(tls_time(NULL)==1800000003);
 for(int i=1;i<=4;i++)assert(dnd_tls_begin(i,i,"localhost")==0);
 assert(dnd_tls_begin(5,5,"localhost")<0);assert(dnd_tls_begin(1,1,"localhost")<0);
 assert(dnd_net_tls_reset()<0&&dnd_net_tls_configure(ca,seed,1800000000)<0);
 unsigned char out[32];assert(dnd_tls_read(1,out,1)<0&&dnd_tls_write(1,out,1)<0);
 for(int i=1;i<=4;i++){dnd_tls_close(i);dnd_tls_close(i);}
 /* Initial entropy cannot be silently reused for a reseed after its stack frame
  * has returned. Exhaustion fails closed; callers must provide fresh entropy. */
 int failed=0;for(int i=0;i<4100;i++)if(mbedtls_ctr_drbg_random(&random_state,out,sizeof out)!=0){failed=1;break;}assert(failed);
 assert(dnd_net_tls_reset()==0);
 ca->data[0]=0;assert(dnd_net_tls_configure(ca,seed,1800000000)<0);
 assert(dnd_tls_begin(1,1,"localhost")<0);assert(dnd_net_tls_reset()==0);
 free(ca);free(seed);puts("TLS configuration bounds, monotonic time, session limits, lifecycle and entropy exhaustion passed.");
}
