#include "dnd_tls.h"
#include "dnd_network_platform.h"
#include "dnd_timing.h"
#include <string.h>
#include <stdlib.h>
#ifdef DND_TLS
#include "mbedtls/ssl.h"
#include "mbedtls/ssl_ciphersuites.h"
#include "mbedtls/ctr_drbg.h"
#include "mbedtls/x509_crt.h"
#include "mbedtls/platform.h"
#include "mbedtls/platform_util.h"
mbedtls_ms_time_t mbedtls_ms_time(void){return dnd_timing_now()/1000;}
#define TLS_SLOTS 4
static struct {int token,fd,ready;mbedtls_ssl_context ssl;} sessions[TLS_SLOTS];
static mbedtls_x509_crt roots;
static mbedtls_ssl_config config;
static mbedtls_ctr_drbg_context random_state;
static int configured,initialized,last_error;
static int64_t epoch,started;
static mbedtls_time_t tls_time(mbedtls_time_t *out){
    int64_t elapsed=dnd_timing_now()-started;
    int64_t now=elapsed<0?0:epoch+elapsed/1000000;
    if(now>2147483647)now=0;
    if(out)*out=(mbedtls_time_t)now;
    return (mbedtls_time_t)now;
}
static int active(void){for(int i=0;i<TLS_SLOTS;i++)if(sessions[i].token)return 1;return 0;}
static int find(int token){for(int i=0;i<TLS_SLOTS;i++)if(token>0&&sessions[i].token==token)return i;return -1;}
static int failure(int code){last_error=code;return -2;}
static int result(int r){if(r==MBEDTLS_ERR_SSL_WANT_READ||r==MBEDTLS_ERR_SSL_WANT_WRITE)return -1;return r<0?failure(r):r;}
struct seed_input {unsigned char bytes[64];size_t size;int used;};
static int entropy(void *context,unsigned char *out,size_t count){
    struct seed_input *s=context;
    if(s->used||count>s->size)return MBEDTLS_ERR_CTR_DRBG_ENTROPY_SOURCE_FAILED;
    memcpy(out,s->bytes,count);s->used=1;return 0;
}
int dnd_net_tls_reset(void){
    if(active())return failure(-2);
    configured=0;
    if(initialized){mbedtls_ssl_config_free(&config);mbedtls_x509_crt_free(&roots);mbedtls_ctr_drbg_free(&random_state);initialized=0;}
    return 0;
}
int dnd_net_tls_configure(DndArray *ca,DndArray *seed,int64_t utc){
    if(active())return failure(-2);
    /* Invalidate previous configuration even when replacement input is invalid. */
    dnd_net_tls_reset();
    if(!ca||!seed||ca->elements_are_references||seed->elements_are_references||ca->element_size!=1||seed->element_size!=1||ca->length<1||ca->length>262144||seed->length<48||seed->length>64||utc<1704067200||utc>2147483647)return failure(-2);
    unsigned nonzero=0;for(unsigned i=0;i<seed->length;i++)nonzero|=seed->data[i];if(!nonzero)return failure(-2);
    unsigned char *pem=malloc((size_t)ca->length+1);if(!pem)return failure(-4);
    memcpy(pem,ca->data,ca->length);pem[ca->length]=0;
    mbedtls_x509_crt_init(&roots);mbedtls_ssl_config_init(&config);mbedtls_ctr_drbg_init(&random_state);initialized=1;
    int r=mbedtls_x509_crt_parse(&roots,pem,(size_t)ca->length+1);free(pem);
    if(r!=0){dnd_net_tls_reset();return failure(r?r:-2);}
    static struct seed_input input;memset(&input,0,sizeof input);input.size=seed->length;memcpy(input.bytes,seed->data,seed->length);
    r=mbedtls_ctr_drbg_seed(&random_state,entropy,&input,(const unsigned char*)"DolphinDotNet HTTPS v1",sizeof("DolphinDotNet HTTPS v1")-1);
    mbedtls_platform_zeroize(&input,sizeof input);
    /* The supplied seed is consumed only once. Further entropy is never fabricated.
     * After the bounded DRBG interval, reconfiguration with fresh entropy is required. */
    input.used=1;
    mbedtls_ctr_drbg_set_reseed_interval(&random_state,4096);
    if(r==0)r=mbedtls_ssl_config_defaults(&config,MBEDTLS_SSL_IS_CLIENT,MBEDTLS_SSL_TRANSPORT_STREAM,MBEDTLS_SSL_PRESET_DEFAULT);
    if(r!=0){dnd_net_tls_reset();return failure(r);}
    epoch=utc;started=dnd_timing_now();mbedtls_platform_set_time(tls_time);
    mbedtls_ssl_conf_rng(&config,mbedtls_ctr_drbg_random,&random_state);
    mbedtls_ssl_conf_authmode(&config,MBEDTLS_SSL_VERIFY_REQUIRED);
    mbedtls_ssl_conf_ca_chain(&config,&roots,NULL);
    mbedtls_ssl_conf_min_tls_version(&config,MBEDTLS_SSL_VERSION_TLS1_2);
    mbedtls_ssl_conf_max_tls_version(&config,MBEDTLS_SSL_VERSION_TLS1_2);
    static const char *alpn[]={"http/1.1",NULL};mbedtls_ssl_conf_alpn_protocols(&config,alpn);
    static const int suites[]={MBEDTLS_TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        MBEDTLS_TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
        MBEDTLS_TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
        MBEDTLS_TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,0};
    mbedtls_ssl_conf_ciphersuites(&config,suites);
    configured=1;last_error=0;return 0;
}
static int send_bytes(void *context,const unsigned char *buffer,size_t count){
    int fd=*(int*)context;int n=dnd_np_write(fd,buffer,(int)count,1,0,0);
    return n==-1?MBEDTLS_ERR_SSL_WANT_WRITE:n<0?MBEDTLS_ERR_SSL_INTERNAL_ERROR:n;
}
static int receive_bytes(void *context,unsigned char *buffer,size_t count){
    uint32_t ip=0;int port=0;int n=dnd_np_read(*(int*)context,buffer,(int)count,1,&ip,&port);
    return n==-1?MBEDTLS_ERR_SSL_WANT_READ:n==0?MBEDTLS_ERR_SSL_CONN_EOF:n<0?MBEDTLS_ERR_SSL_INTERNAL_ERROR:n;
}
int dnd_tls_begin(int token,int fd,const char *host){
    if(!configured||token<=0||find(token)>=0||!host||!host[0])return failure(-2);
    int i;for(i=0;i<TLS_SLOTS;i++)if(!sessions[i].token)break;if(i==TLS_SLOTS)return failure(-4);
    mbedtls_ssl_init(&sessions[i].ssl);sessions[i].token=token;sessions[i].fd=fd;
    int r=mbedtls_ssl_setup(&sessions[i].ssl,&config);
    if(!r)r=mbedtls_ssl_set_hostname(&sessions[i].ssl,host);
    if(r){dnd_tls_close(token);return failure(r);}
    mbedtls_ssl_set_bio(&sessions[i].ssl,&sessions[i].fd,send_bytes,receive_bytes,NULL);return 0;
}
int dnd_tls_handshake(int token){
    int i=find(token);if(i<0)return failure(-3);if(sessions[i].ready)return 0;
    int r=mbedtls_ssl_handshake_step(&sessions[i].ssl);if(r)return result(r);
    if(!mbedtls_ssl_is_handshake_over(&sessions[i].ssl))return -1;
    if(mbedtls_ssl_get_verify_result(&sessions[i].ssl)!=0)return failure(-2);
    sessions[i].ready=1;return 0;
}
int dnd_tls_read(int token,void *buffer,int count){
    int i=find(token);if(i<0||!sessions[i].ready)return failure(-3);if(!count)return 0;
    int r=mbedtls_ssl_read(&sessions[i].ssl,buffer,(size_t)count);
    if(r==MBEDTLS_ERR_SSL_PEER_CLOSE_NOTIFY)return 0;
    return r==0?failure(MBEDTLS_ERR_SSL_CONN_EOF):result(r);
}
int dnd_tls_write(int token,const void *buffer,int count){
    int i=find(token);if(i<0||!sessions[i].ready)return failure(-3);if(!count)return 0;
    return result(mbedtls_ssl_write(&sessions[i].ssl,buffer,(size_t)count));
}
void dnd_tls_close(int token){int i=find(token);if(i>=0){mbedtls_ssl_free(&sessions[i].ssl);memset(&sessions[i],0,sizeof sessions[i]);}}
int dnd_net_tls_error(void){return last_error;}
#else
int dnd_net_tls_configure(DndArray *a,DndArray *b,int64_t t){(void)a;(void)b;(void)t;return -2;}
int dnd_net_tls_reset(void){return 0;}
int dnd_tls_begin(int t,int f,const char *h){(void)t;(void)f;(void)h;return -2;}
int dnd_tls_handshake(int t){(void)t;return -2;}
int dnd_tls_read(int t,void *b,int n){(void)t;(void)b;(void)n;return -2;}
int dnd_tls_write(int t,const void *b,int n){(void)t;(void)b;(void)n;return -2;}
void dnd_tls_close(int t){(void)t;}
int dnd_net_tls_error(void){return -2;}
#endif
