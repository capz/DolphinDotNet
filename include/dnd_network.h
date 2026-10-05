#ifndef DND_NETWORK_H
#define DND_NETWORK_H
#include <stddef.h>
#include <stdint.h>
int dnd_network_init(void);
int dnd_udp_open(uint16_t local_port);
int dnd_udp_send(int socket_fd,const char *ipv4,uint16_t port,const void *data,size_t size);
int dnd_udp_receive(int socket_fd,void *data,size_t capacity);
void dnd_network_close(int socket_fd);
#endif
