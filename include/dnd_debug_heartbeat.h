#ifndef DND_DEBUG_HEARTBEAT_H
#define DND_DEBUG_HEARTBEAT_H
#include <stdint.h>
#define DND_DEBUG_HEARTBEAT_MAGIC UINT32_C(0x444e4842)
#define DND_DEBUG_HEARTBEAT_TIMEOUT_US UINT64_C(6000000)
enum { DND_LIVE_DISABLED, DND_LIVE_CHECKING, DND_LIVE_CONNECTED,
       DND_LIVE_LOST, DND_LIVE_DISCONNECTED, DND_LIVE_WAIT_NETWORK };
#ifdef DND_DEBUG_TELEMETRY
/* Signature, host IPv4, UDP port, session token high/low, enabled. */
extern volatile uint32_t dnd_debugger_heartbeat[6];
#endif
void dnd_debug_heartbeat_tick(uint64_t now, int network, int link);
void dnd_debug_heartbeat_hide(void);
int dnd_debug_heartbeat_state(void);
#endif
