#ifndef DND_TLS_H
#define DND_TLS_H
#include "dnd_managed.h"
#ifdef DND_TLS
#define MBEDTLS_CONFIG_FILE "dnd_tls_config.h"
#endif
int dnd_net_tls_configure(DndArray *roots,DndArray *seed,int64_t unix_seconds);
int dnd_net_tls_reset(void);
int dnd_net_tls_start(int token,DndString *host);
int dnd_net_tls_handshake(int token);
int dnd_net_tls_error(void);
int dnd_tls_begin(int token,int fd,const char *host);
int dnd_tls_handshake(int token);
int dnd_tls_read(int token,void *buffer,int count);
int dnd_tls_write(int token,const void *buffer,int count);
void dnd_tls_close(int token);
#endif
