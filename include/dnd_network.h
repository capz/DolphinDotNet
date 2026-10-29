#ifndef DND_NETWORK_H
#define DND_NETWORK_H
#include <stddef.h>
#include <stdint.h>
#include "dnd_managed.h"
#include "dnd_tls.h"
/* Nonblocking bridge results. Errors are negative; bytes and handles >= 0. */
enum { DND_NET_WAIT=-1, DND_NET_ERROR=-2, DND_NET_STALE=-3,
       DND_NET_LIMIT=-4, DND_NET_TRUNCATED=-5 };
int dnd_net_start(int address,int mask,int gateway,int dhcp);
int dnd_net_status(void);
int dnd_net_time(void);
int dnd_net_open(int stream);
int dnd_net_bind(int token,int address,int port);
int dnd_net_listen(int token,int backlog);
int dnd_net_accept(int token,DndArray *endpoint);
int dnd_net_connect(int token,int address,int port);
int dnd_net_connected(int token);
int dnd_net_read(int token,DndArray *buffer,int offset,int count,DndArray *endpoint);
int dnd_net_write(int token,DndArray *buffer,int offset,int count,int address,int port);
int dnd_net_endpoint(int token,DndArray *endpoint,int remote);
int dnd_net_option(int token,int option,int value);
int dnd_net_close(int token);
int dnd_net_stop(void);
int dnd_net_error(void);
extern const DndType DND_TYPE_SOCKET_EXCEPTION;
extern const DndType DND_TYPE_EXTERNAL_EXCEPTION;
DndObject *dnd_net_failure(DndManagedHeap *heap,int error,int result);
int dnd_net_failure_code(DndObject *exception);
int dnd_network_init(void);
int dnd_udp_open(uint16_t local_port);
int dnd_udp_send(int socket_fd,const char *ipv4,uint16_t port,const void *data,size_t size);
int dnd_udp_receive(int socket_fd,void *data,size_t capacity);
void dnd_network_close(int socket_fd);
#endif
