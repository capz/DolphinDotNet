#include "protocol.h"
#include "resident.h"
#include <gccore.h>
#include <network.h>
#include <ogc/usbgecko.h>
#include <ogc/lwp_watchdog.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#ifndef DND_LAUNCHER_PORT
#define DND_LAUNCHER_PORT 4299
#endif
#ifndef DND_LAUNCHER_IP
#define DND_LAUNCHER_IP ""
#define DND_LAUNCHER_MASK "255.255.255.0"
#define DND_LAUNCHER_GATEWAY "0.0.0.0"
#endif

typedef struct {
    DndElfPlan plan;
    _Alignas(32) uint8_t stub[512];
    _Alignas(32) uint8_t image[];
} Stage;
typedef struct { int socket, channel; uint64_t deadline; Stage *stage; } Connection;
static uint8_t *reservation;
static uintptr_t reservation_end;
static volatile int listener=-2;
static char local_ip[16], netmask[16], gateway[16];
static _Alignas(32) uint8_t network_stack[32768];
extern const uint8_t dnd_launcher_stub_start[], dnd_launcher_stub_end[];
_Static_assert(sizeof(DndElfSegment)==16 && offsetof(DndElfPlan,segments)==8, "ELF trampoline ABI");

static int transfer(Connection *c, void *buffer, size_t size, bool sending) {
    while (gettime()<c->deadline) {
        int n;
        if (c->socket>=0) {
            n=sending ? net_send(c->socket,buffer,size,0) : net_recv(c->socket,buffer,size,0);
            if (n>=0) return n; /* Zero is TCP EOF. */
            int error=n < -1 ? -n : errno;
            if (error!=EAGAIN && error!=EWOULDBLOCK && error!=EINTR) return -1;
        } else {
            if (!usb_isgeckoalive(c->channel)) return -1;
            n=sending ? usb_sendbuffer_safe_ex(c->channel,buffer,(int)size,1)
                      : usb_recvbuffer_safe_ex(c->channel,buffer,(int)size,1);
            if (n>0) return n;
        }
        usleep(1000);
    }
    return -1;
}
static int receive(void *p, void *b, size_t n) { return transfer(p,b,n,false); }
static int transmit(void *p, const void *b, size_t n) { return transfer(p,(void *)b,n,true); }
static uint8_t *allocate_image(void *p, size_t size, uintptr_t *address, size_t *allocation_size) {
    Connection *c=p;
    size_t extent=(sizeof(Stage)+size+31)&~(size_t)31;
    if (extent>reservation_end-(uintptr_t)reservation) return NULL;
    c->stage=(Stage *)(reservation_end-extent);
    *address=(uintptr_t)c->stage; *allocation_size=extent;
    return c->stage->image;
}
static void status(void *p, unsigned state) {
    Connection *c=p;
    if (state==DND_READY) printf("IDE connected over %s. READY TO RECEIVE ELF.\n",c->socket>=0 ? "BBA" : "USB Gecko");
    else if (state==DND_UPLOAD) printf("Receiving ELF...\n");
    else if (state==DND_ACCEPTED) printf("ELF validated. Waiting for IDE execute request.\n");
    else if (state==DND_EXECUTING) printf("Execution acknowledged. Starting application.\n");
    fflush(stdout);
}
static void serve(Connection c) {
    c.deadline=gettime()+secs_to_ticks(60);
    DndLauncherIo io={&c,receive,transmit,allocate_image,status,DND_LAUNCHER_MAX_IMAGE};
    DndElfPlan plan;
    uint8_t *image=NULL;
    int result=dnd_launcher_session(&io,&image,&plan);
    if (c.socket>=0) { net_shutdown(c.socket,2); net_close(c.socket); }
    if (result==1) {
        size_t stub_size=(size_t)(dnd_launcher_stub_end-dnd_launcher_stub_start);
        if (stub_size>sizeof(c.stage->stub)) abort();
        c.stage->plan=plan;
        memcpy(c.stage->stub,dnd_launcher_stub_start,stub_size);
        DCFlushRange(c.stage->stub,(u32)stub_size);
        ICInvalidateRange(c.stage->stub,(u32)stub_size);
        if (listener>=0) net_close(listener);
        /* Shutdown device/DMA/thread callbacks while old code and stack still exist.
         * The relocated stub then uses registers only, even over the old loader. */
        SYS_ResetSystem(SYS_SHUTDOWN,0,0);
        IRQ_Disable();
        /* The target CRT reads these before its kernel/heap allocations. Keep
         * our pristine image out of its arena even if shutdown updated lowmem. */
        *(volatile uint32_t *)0x80000034=DND_RESIDENT_BASE;
        *(volatile uint32_t *)0x80000038=DND_RESIDENT_BASE;
        *(volatile uint32_t *)0x800000ec=DND_RESIDENT_BASE;
        *(volatile uint32_t *)0x80000030=0;
        *(volatile uint32_t *)0x800000f4=0;
        DCFlushRange((void *)0x80000020,32);
        DCFlushRange((void *)0x800000e0,32);
        ((void (*)(DndElfPlan *,void *))c.stage->stub)(&c.stage->plan,image);
        __builtin_unreachable();
    }
    if (c.socket<0) usb_flush(c.channel);
    printf(result==0 ? "Readiness check complete. Waiting for executable.\n" : "Transfer rejected or disconnected. Waiting for a new handshake.\n");
}
static void *start_network(void *unused) {
    (void)unused;
    snprintf(local_ip,sizeof(local_ip),"%s",DND_LAUNCHER_IP);
    snprintf(netmask,sizeof(netmask),"%s",DND_LAUNCHER_MASK);
    snprintf(gateway,sizeof(gateway),"%s",DND_LAUNCHER_GATEWAY);
    if (if_config(local_ip,netmask,gateway,strlen(DND_LAUNCHER_IP)==0)<0) { listener=-1; return NULL; }
    int s=net_socket(AF_INET,SOCK_STREAM,IPPROTO_IP);
    if (s<0) { listener=-1; return NULL; }
    struct sockaddr_in address={0};
    address.sin_len=sizeof(address); address.sin_family=AF_INET; address.sin_port=htons(DND_LAUNCHER_PORT); address.sin_addr.s_addr=INADDR_ANY;
    unsigned long nonblocking=1;
    if (net_bind(s,(struct sockaddr *)&address,sizeof(address))<0 || net_listen(s,1)<0 || net_ioctl(s,FIONBIO,&nonblocking)<0) {
        net_close(s); listener=-1; return NULL;
    }
    __sync_synchronize();
    listener=s;
    return NULL;
}
int main(void) {
    /* Reserve a high MEM1 staging arena before video/network allocations. Actual
     * staging grows down from its top; overlapping target segments are rejected. */
    reservation_end=(uintptr_t)SYS_GetArenaHi() & ~(uintptr_t)31;
    reservation=SYS_AllocArenaMemHi(16u*1024u*1024u,32);
    VIDEO_Init(); PAD_Init();
    GXRModeObj *mode=VIDEO_GetPreferredMode(NULL);
    void *framebuffer=MEM_K0_TO_K1(SYS_AllocateFramebuffer(mode));
    console_init(framebuffer,20,20,mode->fbWidth-40,mode->xfbHeight-40,mode->fbWidth*VI_DISPLAY_PIX_SZ);
    VIDEO_Configure(mode); VIDEO_SetNextFramebuffer(framebuffer); VIDEO_SetBlack(false); VIDEO_Flush(); VIDEO_WaitVSync();
    printf("DolphinDotNet ELF Launcher v1 (resident return enabled)\n\n");
    if (!reservation || reservation_end>DND_RESIDENT_BASE || (uintptr_t)reservation<0x80003100u) {
        printf("Cannot reserve ELF staging memory.\n"); for (;;) VIDEO_WaitVSync();
    }
    printf("Applications return here on normal libogc exit.\nTop 2 MiB reserved; upload memory checked separately.\n\n");
    printf("USB Gecko slots A/B: waiting for IDE handshake.\nBBA: configuring network...\n");
    lwp_t thread;
    if (LWP_CreateThread(&thread,start_network,NULL,network_stack,sizeof(network_stack),80)<0) listener=-1;
    bool reported=false;
    for (;;) {
        if (!reported && listener!=-2) {
            reported=true;
            if (listener>=0) printf("BBA READY: %s:%u (upload; GDB uses its own port).\n",local_ip,DND_LAUNCHER_PORT);
            else printf("BBA unavailable; USB Gecko is still enabled.\n");
        }
        for (int channel=0; channel<2; channel++)
            if (usb_isgeckoalive(channel) && usb_checkrecv(channel)) serve((Connection){.socket=-1,.channel=channel});
        if (listener>=0) {
            struct sockaddr_in peer; socklen_t peer_size=sizeof(peer);
            int s=net_accept(listener,(struct sockaddr *)&peer,&peer_size);
            if (s>=0) {
                unsigned long nonblocking=1;
                if (net_ioctl(s,FIONBIO,&nonblocking)<0) net_close(s);
                else serve((Connection){.socket=s,.channel=-1});
            }
        }
        usleep(1000);
    }
}
