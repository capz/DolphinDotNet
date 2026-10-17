#include "dnd_debug_heartbeat.h"
#ifdef DND_DEBUG_TELEMETRY
#include "dnd_network_platform.h"
#include "dnd_performance.h"
#include <stdbool.h>
#include <string.h>

volatile uint32_t dnd_debugger_heartbeat[6] = {DND_DEBUG_HEARTBEAT_MAGIC,0,0,0,0,0};
static int socket_fd=-1, live_state=DND_LIVE_CHECKING;
static uint32_t host, port, token_high, token_low, sequence;
static uint64_t started, last_tick, sent_at, acknowledged_at;
static bool initialized, awaiting, acknowledged, network_ready;

static uint32_t word(const unsigned char *p) {
    return (uint32_t)p[0]<<24 | (uint32_t)p[1]<<16 | (uint32_t)p[2]<<8 | p[3];
}
static void put(unsigned char *p,uint32_t value) {
    p[0]=(unsigned char)(value>>24);p[1]=(unsigned char)(value>>16);
    p[2]=(unsigned char)(value>>8);p[3]=(unsigned char)value;
}
static void close_socket(void) {
    if(socket_fd>=0)dnd_np_close(socket_fd);
    socket_fd=-1;awaiting=false;
}
void dnd_debug_heartbeat_hide(void) {
    close_socket();initialized=false;acknowledged=false;network_ready=false;live_state=DND_LIVE_CHECKING;
}
int dnd_debug_heartbeat_state(void) {
    if(dnd_debugger_heartbeat[0]!=DND_DEBUG_HEARTBEAT_MAGIC || dnd_debugger_heartbeat[5]!=1)
        return DND_LIVE_DISABLED;
    if(dnd_debugger_telemetry[1]==2)return DND_LIVE_DISCONNECTED;
    return live_state;
}
void dnd_debug_heartbeat_tick(uint64_t now,int network,int link) {
    if(!dnd_performance_visible())return;
    uint32_t next_host=dnd_debugger_heartbeat[1],next_port=dnd_debugger_heartbeat[2];
    uint32_t high=dnd_debugger_heartbeat[3],low=dnd_debugger_heartbeat[4];
    if(dnd_debug_heartbeat_state()==DND_LIVE_DISABLED || dnd_debugger_telemetry[1]!=1 ||
       !next_host || !next_port || next_port>65535 || (!high && !low)) {
        dnd_debug_heartbeat_hide();return;
    }
    if(!initialized || next_host!=host || next_port!=port || high!=token_high || low!=token_low ||
       now<last_tick || now-last_tick>DND_CONNECTIVITY_INTERVAL_US*2) {
        close_socket();host=next_host;port=next_port;token_high=high;token_low=low;
        started=now;acknowledged=false;initialized=true;network_ready=false;live_state=DND_LIVE_CHECKING;
    } else if(now-last_tick<DND_CONNECTIVITY_INTERVAL_US)return;
    last_tick=now;
    if(link==0) {close_socket();network_ready=false;live_state=DND_LIVE_LOST;return;}
    if(network!=1) {close_socket();network_ready=false;live_state=DND_LIVE_WAIT_NETWORK;return;}
    if(!network_ready) {
        network_ready=true;started=now;acknowledged=false;live_state=DND_LIVE_CHECKING;
    }
    if(socket_fd<0)socket_fd=dnd_np_monitor_open();
    if(socket_fd<0) {live_state=DND_LIVE_LOST;return;}
    /* Drain a bounded number of datagrams. Never accept a different peer,
       session, sequence, oversized response or a reply delayed across a pause. */
    for(int i=0;i<4;i++) {
        unsigned char packet[21];uint32_t ip=0;int remote_port=0;
        int n=dnd_np_monitor_read(socket_fd,packet,sizeof(packet),&ip,&remote_port);
        if(n==-2) {close_socket();live_state=DND_LIVE_LOST;return;}
        if(n<0)break;
        if(n==20 && ip==host && (uint32_t)remote_port==port && awaiting &&
           word(packet)==DND_DEBUG_HEARTBEAT_MAGIC && word(packet+4)==2 &&
           word(packet+8)==token_high && word(packet+12)==token_low && word(packet+16)==sequence &&
           now>=sent_at && now-sent_at<DND_DEBUG_HEARTBEAT_TIMEOUT_US) {
            acknowledged_at=now;acknowledged=true;awaiting=false;live_state=DND_LIVE_CONNECTED;
        }
    }
    uint64_t origin=acknowledged?acknowledged_at:started;
    if(now>=origin && now-origin>=DND_DEBUG_HEARTBEAT_TIMEOUT_US)live_state=DND_LIVE_LOST;
    unsigned char packet[20];
    if(++sequence==0)++sequence;
    put(packet,DND_DEBUG_HEARTBEAT_MAGIC);put(packet+4,1);
    put(packet+8,token_high);put(packet+12,token_low);put(packet+16,sequence);
    awaiting=dnd_np_monitor_write(socket_fd,packet,sizeof(packet),host,(int)port)==20;
    if(!awaiting)close_socket();
    sent_at=now;
}
#else
void dnd_debug_heartbeat_tick(uint64_t now,int network,int link){(void)now;(void)network;(void)link;}
void dnd_debug_heartbeat_hide(void){}
int dnd_debug_heartbeat_state(void){return DND_LIVE_DISABLED;}
#endif
