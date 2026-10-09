#include "dnd_network.h"
#include <network.h>
#include <string.h>
#include <errno.h>
int dnd_network_init(void){
 /*
  * Current libogc exposes the BSD-style net_* socket API but no longer
  * provides the legacy net_init entry point for GameCube. Network-interface
  * configuration is platform/application specific, so initialization is a
  * successful no-op here; opening a socket remains the first native action.
  */
 return 0;
}
int dnd_udp_open(uint16_t port){
 int s=net_socket(AF_INET,SOCK_DGRAM,IPPROTO_IP); if(s<0)return s;
 unsigned long nonblocking=1;
 if(net_ioctl(s,FIONBIO,&nonblocking)<0){int error=errno;net_close(s);errno=error;return -1;}
 if(port){struct sockaddr_in a;memset(&a,0,sizeof(a));a.sin_family=AF_INET;a.sin_port=htons(port);a.sin_addr.s_addr=INADDR_ANY;if(net_bind(s,(struct sockaddr*)&a,sizeof(a))<0){net_close(s);return -1;}}
 return s;
}
int dnd_udp_send(int s,const char *ip,uint16_t port,const void *data,size_t size){
 struct sockaddr_in a;memset(&a,0,sizeof(a));a.sin_family=AF_INET;a.sin_port=htons(port);
 if(inet_aton(ip,&a.sin_addr)==0)return -1;
 return net_sendto(s,data,size,0,(struct sockaddr*)&a,sizeof(a));
}
int dnd_udp_receive(int s,void *data,size_t cap){int r=net_recv(s,data,cap,0);if(r<0&&(errno==EAGAIN||errno==EWOULDBLOCK))return 0;return r;}
void dnd_network_close(int s){if(s>=0)net_close(s);}
