#ifdef GEKKO
#include "dnd_network.h"
#include "dnd_network_platform.h"
#include <gccore.h>
#include <network.h>
#include <ogc/lwp_watchdog.h>
#include <errno.h>
#include <string.h>
static volatile int state=-1;
static int error_code;
static lwp_t worker;
static unsigned char worker_stack[8192] __attribute__((aligned(32)));
static struct in_addr config_ip,config_mask,config_gateway;
static int config_dhcp;
static int result(int r){if(r>=0)return r;error_code=r < -1?-r:errno;return error_code==EAGAIN||error_code==EWOULDBLOCK||error_code==EINPROGRESS||error_code==EALREADY?DND_NET_WAIT:DND_NET_ERROR;}
static struct sockaddr_in addr(uint32_t ip,int port){struct sockaddr_in a;memset(&a,0,sizeof(a));a.sin_len=sizeof(a);a.sin_family=AF_INET;a.sin_addr.s_addr=htonl(ip);a.sin_port=htons((uint16_t)port);return a;}
static void *configure(void *unused){(void)unused;int r=if_configex(&config_ip,&config_mask,&config_gateway,config_dhcp!=0);__sync_synchronize();state=r<0?-2:1;return NULL;}
int dnd_np_start(uint32_t ip,uint32_t mask,uint32_t gateway,int dhcp){if(state==0)return DND_NET_WAIT;if(state==1)return 0;if(state==-2)return DND_NET_ERROR;config_ip.s_addr=htonl(ip);config_mask.s_addr=htonl(mask);config_gateway.s_addr=htonl(gateway);config_dhcp=dhcp;state=0;if(LWP_CreateThread(&worker,configure,NULL,worker_stack,sizeof(worker_stack),70)!=0){state=-2;return DND_NET_ERROR;}return 0;}
int dnd_np_status(void){__sync_synchronize();return state;}
uint32_t dnd_np_time(void){return (uint32_t)ticks_to_millisecs(gettime());}
int dnd_np_error(void){return error_code;}
static int nonblock(int fd){unsigned long value=1;int r=net_ioctl(fd,FIONBIO,&value);if(r<0){result(r);net_close(fd);return DND_NET_ERROR;}return fd;}
int dnd_np_open(int stream){int fd=net_socket(AF_INET,stream?SOCK_STREAM:SOCK_DGRAM,stream?IPPROTO_TCP:IPPROTO_UDP);return fd<0?result(fd):nonblock(fd);}
int dnd_np_bind(int fd,uint32_t ip,int port){struct sockaddr_in a=addr(ip,port);return result(net_bind(fd,(struct sockaddr*)&a,sizeof(a)));}
int dnd_np_listen(int fd,int backlog){return result(net_listen(fd,backlog));}
int dnd_np_accept(int fd,uint32_t *ip,int *port){struct sockaddr_in a;socklen_t n=sizeof(a);int s=net_accept(fd,(struct sockaddr*)&a,&n);if(s<0)return result(s);*ip=ntohl(a.sin_addr.s_addr);*port=ntohs(a.sin_port);return nonblock(s);}
int dnd_np_connect(int fd,uint32_t ip,int port){struct sockaddr_in a=addr(ip,port);return result(net_connect(fd,(struct sockaddr*)&a,sizeof(a)));}
int dnd_np_connected(int fd){struct pollsd p={fd,POLLOUT,0};int r=net_poll(&p,1,0);if(r<0)return result(r);if(!r)return DND_NET_WAIT;int e=0;socklen_t n=sizeof(e);r=net_getsockopt(fd,SOL_SOCKET,SO_ERROR,&e,&n);if(r<0)return result(r);if(e){error_code=e;return DND_NET_ERROR;}return 0;}
int dnd_np_read(int fd,void *b,int count,int stream,uint32_t *ip,int *port){if(stream)return result(net_recv(fd,b,(size_t)count,0));struct sockaddr_in a;socklen_t n=sizeof(a);int r=net_recvfrom(fd,b,(size_t)count,0,(struct sockaddr*)&a,&n);if(r>=0){*ip=ntohl(a.sin_addr.s_addr);*port=ntohs(a.sin_port);}return result(r);}
int dnd_np_write(int fd,const void *b,int count,int stream,uint32_t ip,int port){if(stream)return result(net_send(fd,b,(size_t)count,0));struct sockaddr_in a=addr(ip,port);return result(net_sendto(fd,b,(size_t)count,0,(struct sockaddr*)&a,sizeof(a)));}
int dnd_np_endpoint(int fd,uint32_t *ip,int *port,int remote){struct sockaddr_in a;socklen_t n=sizeof(a);int r=remote?net_getpeername(fd,(struct sockaddr*)&a,&n):net_getsockname(fd,(struct sockaddr*)&a,&n);if(r<0)return result(r);*ip=ntohl(a.sin_addr.s_addr);*port=ntohs(a.sin_port);return 0;}
int dnd_np_option(int fd,int opt,int value){return opt==0?result(net_setsockopt(fd,IPPROTO_TCP,TCP_NODELAY,&value,sizeof(value))):opt==1?result(net_setsockopt(fd,SOL_SOCKET,SO_BROADCAST,&value,sizeof(value))):DND_NET_ERROR;}
void dnd_np_close(int fd){net_close(fd);}
#endif
