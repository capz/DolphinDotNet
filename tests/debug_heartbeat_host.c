#define _POSIX_C_SOURCE 200809L
#include "dnd_debug_heartbeat.h"
#include "dnd_performance.h"
#include <poll.h>
#include <stdio.h>
#include <stdint.h>
/* Test-only observer: wait for real UDP readiness without sleeping or exposing
   the diagnostic descriptor through the production API. */
static int monitor_fd=-1;
int __real_dnd_np_monitor_open(void);
int __wrap_dnd_np_monitor_open(void) {monitor_fd=__real_dnd_np_monitor_open();return monitor_fd;}
int main(void) {
    dnd_performance_set_visible(1);dnd_debugger_telemetry[1]=1;
    char command;
    while(scanf(" %c",&command)==1) {
        if(command=='c') {
            unsigned host,port,high,low;
            if(scanf("%u %u %u %u",&host,&port,&high,&low)!=4)return 2;
            dnd_debugger_heartbeat[1]=host;dnd_debugger_heartbeat[2]=port;
            dnd_debugger_heartbeat[3]=high;dnd_debugger_heartbeat[4]=low;dnd_debugger_heartbeat[5]=1;
            puts("configured");
        } else if(command=='t') {
            unsigned long long now;int network,link;
            if(scanf("%llu %d %d",&now,&network,&link)!=3)return 2;
            dnd_debug_heartbeat_tick((uint64_t)now,network,link);
            printf("%d\n",dnd_debug_heartbeat_state());
        } else if(command=='w') {
            struct pollfd fd={monitor_fd,POLLIN,0};printf("%d\n",poll(&fd,1,2000)>0 && (fd.revents&POLLIN)!=0);
        } else return 2;
        fflush(stdout);
    }
    dnd_performance_set_visible(0);return 0;
}
