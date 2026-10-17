#ifndef DND_NETWORK_PLATFORM_H
#define DND_NETWORK_PLATFORM_H
#include <stddef.h>
#include <stdint.h>
int dnd_np_start(uint32_t ip,uint32_t mask,uint32_t gateway,int dhcp);
int dnd_np_status(void);
/* Read-only configured-address snapshot. Never initializes or polls the network. */
int dnd_np_snapshot(uint32_t *ip,uint32_t *mask,uint32_t *gateway,int *dhcp);
uint32_t dnd_np_time(void);
int dnd_np_open(int stream);
int dnd_np_bind(int fd,uint32_t ip,int port);
int dnd_np_listen(int fd,int backlog);
int dnd_np_accept(int fd,uint32_t *ip,int *port);
int dnd_np_connect(int fd,uint32_t ip,int port);
int dnd_np_connected(int fd);
int dnd_np_read(int fd,void *buffer,int count,int stream,uint32_t *ip,int *port);
int dnd_np_write(int fd,const void *buffer,int count,int stream,uint32_t ip,int port);
int dnd_np_endpoint(int fd,uint32_t *ip,int *port,int remote);
int dnd_np_option(int fd,int option,int value);
void dnd_np_close(int fd);
int dnd_np_error(void);
#endif
