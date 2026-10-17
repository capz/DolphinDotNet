#include "dnd_hardware.h"
#include "dnd_input.h"
#include "dnd_storage.h"
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <ogc/card.h>
#include <ogc/dvd.h>
#include <ogc/exi.h>
#include <ogc/si.h>
#include <sdcard/gcsd.h>
#include <stdint.h>
#include <stdbool.h>

/* Presence: Absent=0, Present=1, Busy=2, Unknown=3, Unsupported=4. */
enum { ABSENT, PRESENT, BUSY, UNKNOWN, UNSUPPORTED };
static int valid(int kind,int port) {
    if ((kind==0 || kind==1) && port>=0 && port<4) return 1;
    if ((kind==2 || kind==3) && (port==4 || port==5)) return 1;
    if (kind==4 && port==7) return 1;
    if ((kind==5 || kind==6) && port==6) return 1;
    if ((kind==7 || kind==8) && port==8) return 1;
    if (kind==9 && port==9) return 1;
    return 0;
}
static int media(int kind,int port) {
    if (kind==2) return port; /* native memory card */
    if (kind==3) return port-4; /* slot A/B SD */
    if (kind==4) return 2; /* SD2SP2 */
    if (kind==7) return 3; /* dvd */
    if (kind==8) return 6; /* GC Loader */
    return -1;
}
static int sd_probe(int index) {
    DISC_INTERFACE *disc = index==0 ? get_io_gcsda() :
                           index==1 ? get_io_gcsdb() : get_io_gcsd2();
    if (!disc || !disc->startup || !disc->isInserted) return UNKNOWN;
    if (!disc->startup(disc)) return ABSENT;
    return disc->isInserted(disc) ? PRESENT : ABSENT;
}
static int ser1_id(u32 *id) {
    /* Locking inside EXI_GetID is non-destructive; BUSY is not ABSENT. */
    return EXI_GetID(EXI_CHANNEL_0,EXI_DEVICE_2,id) ? PRESENT : BUSY;
}
int dnd_hardware_probe(int kind,int port,int extended) {
    if (!valid(kind,port))return UNSUPPORTED;
    if (kind==0) {
        return (dnd_input_flags(port) & DND_INPUT_CONNECTED) ? PRESENT :
               extended ? ABSENT : UNKNOWN;
    }
    if (kind==1) {
        if (!extended) return UNKNOWN;
        u32 type=SI_Probe(port); /* libogc2 normalizes attached SI IDs. */
        if (type==SI_GBA) return PRESENT;
        if (type==SI_ERROR_BUSY) return BUSY;
        return type==SI_ERROR_NO_RESPONSE ? ABSENT : UNKNOWN;
    }
    if (kind==9) return UNKNOWN; /* GBP uses high-speed parallel/ARAM bus. */
    if (kind==2 && extended) {
        /* A cached mount is not physical presence: CARD_ProbeEx starts with
         * EXI_ProbeEx, including for an attached card. Do not reset or mount it. */
        if (dnd_storage_is_mounted(port-4)) return BUSY; /* Slot owned by SD. */
        s32 blocks=0,sector=0;
        int rc=CARD_ProbeEx(port-4,&blocks,&sector);
        if (rc>=0) return PRESENT;
        if (rc==CARD_ERROR_NOCARD || rc==CARD_ERROR_WRONGDEVICE) return ABSENT;
        if (rc==CARD_ERROR_BUSY) return BUSY;
        return UNKNOWN;
    }
    int m=media(kind,port);
    if (m>=0 && dnd_storage_is_mounted(m)) return PRESENT;
    if (kind==2 && dnd_storage_is_mounted(port-4)) return BUSY;
    if (kind==3 && dnd_storage_is_mounted(port)) return BUSY;
    if (!extended) return UNKNOWN;
    if (kind==3 || kind==4) return sd_probe(m);
    if (kind==5 || kind==6) {
        u32 id=0;
        int status=ser1_id(&id);
        if(status!=PRESENT)return status;
        if(kind==5)return (id&0xffffff00u)==0x04020200u ? PRESENT : ABSENT;
        /* Modem is intentionally not identified without a verified model ID. */
        return UNKNOWN;
    }
    if (kind==7) {
        if (dnd_storage_is_mounted(6)) return BUSY;
        int status=DVD_CheckDisk();
        return status>0?PRESENT:status==0?ABSENT:UNKNOWN;
    }
    if (kind==8) return UNKNOWN; /* Do not startup/reset drive to detect ODE. */
    return UNSUPPORTED;
}
int dnd_hardware_id(int kind,int port) {
    if (!valid(kind,port))return 0;
    if (kind==0 && port<4) {
        u32 type=0;
        return PAD_GetType(port,&type) ? (int)type : 0;
    }
    if (kind==1 && port<4)return (int)SI_GetType(port);
    if (kind==5 && port==6) {
        uint32_t id=0;
        return ser1_id(&id)==PRESENT?(int)id:0;
    }
    return 0;
}
int dnd_platform_reset_button(void) { return SYS_ResetButtonDown()!=0; }
int dnd_platform_reboot(int mode) {
    if(mode==0) SYS_ResetSystem(SYS_RESTART,0,FALSE);
    else if(mode==1) SYS_ResetSystem(SYS_HOTRESET,0,FALSE);
    else if(mode==2) SYS_ResetSystem(SYS_RETURNTOMENU,0,TRUE);
    return 0; /* libogc2 reset normally does not return. */
}
#else
int dnd_hardware_probe(int kind,int port,int extended) {
    (void)extended;
    if ((kind<=1 && kind>=0 && port>=0 && port<=3) ||
        ((kind==2 || kind==3)&&(port==4 || port==5)) ||
        (kind==4&&port==7) ||
        ((kind==5||kind==6)&&port==6) ||
        ((kind==7||kind==8)&&port==8) ||
        (kind==9&&port==9))
        return 4; /* Unsupported on host, not absent. */
    return 4;
}
int dnd_hardware_id(int kind,int port) {(void)kind;(void)port;return 0;}
int dnd_platform_reset_button(void) {return 0;}
int dnd_platform_reboot(int mode) {(void)mode;return 0;}
#endif
