#include "dnd_debug_heartbeat.h"
#include "dnd_performance.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static unsigned char sent[20],reply[21];
static int sends,opens,closes,reads,reply_size=-1,reply_port=2167,write_result=20,read_error,flood;
static uint32_t reply_ip=0xc0a80102;
int dnd_np_monitor_open(void){opens++;return 7;}
void dnd_np_close(int fd){assert(fd==7);closes++;}
int dnd_np_monitor_write(int fd,const void *data,int size,uint32_t ip,int port) {
    assert(fd==7 && size==20 && ip==0xc0a80102 && port==2167);
    memcpy(sent,data,20);sends++;return write_result;
}
int dnd_np_monitor_read(int fd,void *data,int size,uint32_t *ip,int *port) {
    assert(fd==7 && size==21);reads++;
    if(read_error){read_error=0;return -2;}
    if(flood){flood--;memset(data,0,21);return 21;}
    if(reply_size<0)return -1;
    *ip=reply_ip;*port=reply_port;memcpy(data,reply,(size_t)reply_size);
    int n=reply_size;reply_size=-1;return n;
}
static void ack(void){memcpy(reply,sent,20);reply[7]=2;reply_size=20;}
static void start(void) {
    dnd_performance_set_visible(0);
    dnd_performance_set_visible(1);
    dnd_debugger_telemetry[1]=1;
    dnd_debugger_heartbeat[1]=0xc0a80102;dnd_debugger_heartbeat[2]=2167;
    dnd_debugger_heartbeat[3]=0x12345678;dnd_debugger_heartbeat[4]=0x9abcdef0;dnd_debugger_heartbeat[5]=1;
    sends=opens=closes=reads=0;reply_size=-1;reply_ip=0xc0a80102;reply_port=2167;write_result=20;read_error=flood=0;
}
static void label(const char *expected) {
    char lines[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS];
    int rows=dnd_performance_format(lines);assert(strcmp(lines[rows-1],expected)==0);
}
int main(void) {
    start();dnd_debug_heartbeat_tick(0,-1,-1);label("DEBUG LIVE: WAITING FOR BBA");assert(opens==0 && sends==0 && reads==0);
    start();dnd_debug_heartbeat_tick(0,1,1);assert(sends==1 && opens==1);
    label("DEBUG LIVE: CHECKING");
    dnd_debug_heartbeat_tick(1999999,1,1);assert(sends==1);
    ack();dnd_debug_heartbeat_tick(2000000,1,1);label("DEBUG LIVE: CONNECTED");
    dnd_debug_heartbeat_tick(4000000,1,1);label("DEBUG LIVE: CONNECTED");
    dnd_debug_heartbeat_tick(6000000,1,1);label("DEBUG LIVE: CONNECTED");
    dnd_debug_heartbeat_tick(8000000,1,1);label("DEBUG LIVE: UNREACHABLE");
    ack();dnd_debug_heartbeat_tick(10000000,1,1);label("DEBUG LIVE: CONNECTED");
    int previous=sends;
    dnd_debug_heartbeat_tick(12000000,1,0);label("DEBUG LIVE: UNREACHABLE");assert(sends==previous && closes==1);
    dnd_debug_heartbeat_tick(14000000,1,1);label("DEBUG LIVE: CHECKING");assert(opens==2);
    dnd_debug_heartbeat_tick(16000000,0,-1);label("DEBUG LIVE: WAITING FOR BBA");
    dnd_debug_heartbeat_tick(18000000,1,1);label("DEBUG LIVE: CHECKING");
    dnd_performance_set_visible(0);previous=sends;int old_reads=reads,old_opens=opens;
    dnd_debug_heartbeat_tick(20000000,1,1);assert(sends==previous && reads==old_reads && opens==old_opens);
    dnd_performance_set_visible(1);dnd_debug_heartbeat_tick(20000000,1,1);assert(sends==previous+1);
    /* A reply from the wrong peer/session, a replay, an echo or a malformed
       datagram cannot establish liveness or extend a lease. */
    for(int fault=0;fault<8;fault++) {
        start();dnd_debug_heartbeat_tick(0,1,1);ack();
        if(fault==0)reply_ip++;
        if(fault==1)reply_port++;
        if(fault==2)reply[8]^=1;
        if(fault==3)reply[19]^=1;
        if(fault==4)reply[0]^=1;
        if(fault==5)reply[7]=1;
        if(fault==6)reply_size=19;
        if(fault==7)reply_size=21;
        dnd_debug_heartbeat_tick(2000000,1,1);assert(dnd_debug_heartbeat_state()==DND_LIVE_CHECKING);
        dnd_debug_heartbeat_tick(4000000,1,1);dnd_debug_heartbeat_tick(6000000,1,1);
        assert(dnd_debug_heartbeat_state()==DND_LIVE_LOST);
    }
    start();dnd_debug_heartbeat_tick(0,1,1);ack();unsigned char old_ack[20];memcpy(old_ack,reply,20);
    dnd_debug_heartbeat_tick(2000000,1,1);memcpy(reply,old_ack,20);reply_size=20;
    dnd_debug_heartbeat_tick(4000000,1,1);dnd_debug_heartbeat_tick(6000000,1,1);
    dnd_debug_heartbeat_tick(8000000,1,1);
    label("DEBUG LIVE: UNREACHABLE");
    start();dnd_debug_heartbeat_tick(0,1,1);ack();dnd_debug_heartbeat_tick(2000000,1,1);
    ack();dnd_debug_heartbeat_tick(20000000,1,1);label("DEBUG LIVE: CHECKING"); /* Pause/resume grace. */
    ack();dnd_debug_heartbeat_tick(22000000,1,1);label("DEBUG LIVE: CONNECTED");
    dnd_debugger_heartbeat[3]++;ack();dnd_debug_heartbeat_tick(24000000,1,1);label("DEBUG LIVE: CHECKING");
    ack();dnd_debug_heartbeat_tick(26000000,1,1);label("DEBUG LIVE: CONNECTED");
    dnd_debug_heartbeat_tick(1,1,1);label("DEBUG LIVE: CHECKING"); /* Clock reset. */
    dnd_debugger_telemetry[1]=2;label("DEBUG LIVE: DISCONNECTED");
    previous=sends;dnd_debug_heartbeat_tick(2000001,1,1);assert(sends==previous);
    dnd_debugger_heartbeat[5]=0;label("DEBUG LAST: DISCONNECTED");
    start();read_error=1;dnd_debug_heartbeat_tick(0,1,1);assert(closes==1 && sends==0);
    dnd_debug_heartbeat_tick(2000000,1,1);assert(opens==2 && sends==1);
    start();write_result=-2;dnd_debug_heartbeat_tick(0,1,1);assert(closes==1);
    write_result=20;dnd_debug_heartbeat_tick(2000000,1,1);assert(opens==2);
    start();flood=100;dnd_debug_heartbeat_tick(0,1,1);assert(reads==4 && sends==1);
    dnd_performance_set_visible(0);
    puts("Visible-only heartbeat cadence, loss/recovery, peer/session checks, pause and cleanup passed.");
}
