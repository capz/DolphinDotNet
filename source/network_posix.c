#define _POSIX_C_SOURCE 200809L
#ifndef GEKKO
#include "dnd_network.h"
#include "dnd_network_platform.h"
#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <netinet/tcp.h>
#include <poll.h>
#include <sys/socket.h>
#include <time.h>
#include <unistd.h>
#include <string.h>
#ifdef DND_NET_TRACE
#include <stdio.h>
#endif
static int error_code,state;
static uint32_t configured_ip,configured_mask,configured_gateway;
static int configured_dhcp;
static int result(int r){if(r>=0)return r;error_code=errno;return errno==EAGAIN||errno==EWOULDBLOCK||errno==EINPROGRESS||errno==EALREADY?DND_NET_WAIT:DND_NET_ERROR;}
static struct sockaddr_in addr(uint32_t ip,int port){struct sockaddr_in a;memset(&a,0,sizeof(a));a.sin_family=AF_INET;a.sin_addr.s_addr=htonl(ip);a.sin_port=htons((uint16_t)port);return a;}
static int nonblock(int fd){int f=fcntl(fd,F_GETFL,0);if(f<0||fcntl(fd,F_SETFL,f|O_NONBLOCK)<0){error_code=errno;close(fd);return DND_NET_ERROR;}return fd;}
int dnd_np_start(uint32_t ip,uint32_t mask,uint32_t gateway,int dhcp){configured_ip=ip;configured_mask=mask;configured_gateway=gateway;configured_dhcp=dhcp;state=1;return 0;}
int dnd_np_status(void){return state?1:-1;}
int dnd_np_snapshot(uint32_t *ip,uint32_t *mask,uint32_t *gateway,int *dhcp){*ip=configured_ip;*mask=configured_mask;*gateway=configured_gateway;*dhcp=configured_dhcp;return dnd_np_status();}
uint32_t dnd_np_time(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return (uint32_t)((uint64_t)t.tv_sec*1000+(uint64_t)t.tv_nsec/1000000);}
int dnd_np_error(void){return error_code;}
int dnd_np_open(int stream){int fd=socket(AF_INET,stream?SOCK_STREAM:SOCK_DGRAM,stream?IPPROTO_TCP:IPPROTO_UDP);return fd<0?result(fd):nonblock(fd);}
int dnd_np_bind(int fd,uint32_t ip,int port){struct sockaddr_in a=addr(ip,port);return result(bind(fd,(struct sockaddr*)&a,sizeof(a)));}
int dnd_np_listen(int fd,int backlog){return result(listen(fd,backlog));}
int dnd_np_accept(int fd,uint32_t *ip,int *port){struct sockaddr_in a;socklen_t size=sizeof(a);int s=accept(fd,(struct sockaddr*)&a,&size);if(s<0)return result(s);*ip=ntohl(a.sin_addr.s_addr);*port=ntohs(a.sin_port);return nonblock(s);}
int dnd_np_connect(int fd,uint32_t ip,int port){struct sockaddr_in a=addr(ip,port);return result(connect(fd,(struct sockaddr*)&a,sizeof(a)));}
int dnd_np_connected(int fd){struct pollfd p={fd,POLLOUT,0};int r=poll(&p,1,0);if(r<0)return result(r);if(!r)return DND_NET_WAIT;int e=0;socklen_t n=sizeof(e);if(getsockopt(fd,SOL_SOCKET,SO_ERROR,&e,&n)<0)return result(-1);if(e){error_code=e;return DND_NET_ERROR;}return 0;}
int dnd_np_read(int fd,void *b,int count,int stream,uint32_t *ip,int *port){if(stream)return result((int)recv(fd,b,(size_t)count,0));struct sockaddr_in a;socklen_t n=sizeof(a);int r=(int)recvfrom(fd,b,(size_t)count,0,(struct sockaddr*)&a,&n);if(r>=0){*ip=ntohl(a.sin_addr.s_addr);*port=ntohs(a.sin_port);
#ifdef DND_NET_TRACE
fprintf(stderr,"UDP received bytes=%d ip=%x port=%d\n",r,(unsigned)*ip,*port);
#endif
}return result(r);}
int dnd_np_write(int fd,const void *b,int count,int stream,uint32_t ip,int port){if(stream)return result((int)send(fd,b,(size_t)count,MSG_NOSIGNAL));struct sockaddr_in a=addr(ip,port);return result((int)sendto(fd,b,(size_t)count,0,(struct sockaddr*)&a,sizeof(a)));}
int dnd_np_endpoint(int fd,uint32_t *ip,int *port,int remote){struct sockaddr_in a;socklen_t n=sizeof(a);int r=remote?getpeername(fd,(struct sockaddr*)&a,&n):getsockname(fd,(struct sockaddr*)&a,&n);if(r<0)return result(r);*ip=ntohl(a.sin_addr.s_addr);*port=ntohs(a.sin_port);return 0;}
int dnd_np_option(int fd,int opt,int value){return opt==0?result(setsockopt(fd,IPPROTO_TCP,TCP_NODELAY,&value,sizeof(value))):opt==1?result(setsockopt(fd,SOL_SOCKET,SO_BROADCAST,&value,sizeof(value))):DND_NET_ERROR;}
void dnd_np_close(int fd){close(fd);}
#endif
