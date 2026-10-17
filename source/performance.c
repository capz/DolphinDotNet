#include "dnd_performance.h"
#include "dnd_debug_heartbeat.h"
#include <stdio.h>
#include <string.h>

#define FRAME_WINDOW 32
static DndPerformanceStats stats = {.network_state = -1, .network_link = -1};
#ifdef DND_DEBUG_TELEMETRY
volatile uint32_t dnd_debugger_telemetry[2] = {DND_DEBUGGER_TELEMETRY_MAGIC, 0};
#endif
static int visible;
static uint64_t intervals[FRAME_WINDOW], interval_sum, previous_present, memory_time;
static unsigned interval_count, interval_head;
static bool have_present;
static uint64_t connectivity_time;
static bool have_connectivity;

static void reset_timing(void) {
  memset(intervals, 0, sizeof(intervals));
  interval_sum = 0;
  interval_count = interval_head = 0;
  have_present = false;
  stats.timing_valid = false;
  stats.fps_tenths = stats.frame_centimilliseconds = 0;
}

void dnd_performance_set_visible(int enabled) {
  if (visible == !!enabled) return;
  visible = !!enabled;
  reset_timing();
  stats.memory_valid = false;
  have_connectivity = false;
  stats.network_link = -1;
#ifdef DND_DEBUG_TELEMETRY
  dnd_debug_heartbeat_hide();
#endif
}
int dnd_performance_visible(void) { return visible; }
void dnd_performance_begin_frame(void) { stats.triangles = 0; }
void dnd_performance_add_triangles(uint32_t triangles) {
  stats.triangles = triangles > UINT32_MAX - stats.triangles
                        ? UINT32_MAX : stats.triangles + triangles;
}

void dnd_performance_presented(uint64_t now) {
  if (!visible) return;
  if (!have_present || now <= previous_present) {
    reset_timing();
    previous_present = now;
    have_present = true;
    return;
  }
  uint64_t elapsed = now - previous_present;
  previous_present = now;
  interval_sum -= intervals[interval_head];
  // Consecutive monotonic intervals telescope to a duration no larger than now.
  // A clock reset above clears the window, so this sum cannot overflow.
  intervals[interval_head] = elapsed;
  interval_sum += elapsed;
  interval_head = (interval_head + 1) % FRAME_WINDOW;
  if (interval_count < FRAME_WINDOW) interval_count++;
  stats.fps_tenths = (uint32_t)(UINT64_C(10000000) * interval_count / interval_sum);
  uint64_t centims = elapsed / 10 + (elapsed % 10 >= 5);
  stats.frame_centimilliseconds = centims > UINT32_MAX ? UINT32_MAX : (uint32_t)centims;
  stats.timing_valid = true;
}

bool dnd_performance_memory_due(uint64_t now) {
  return visible && (!stats.memory_valid || now < memory_time || now - memory_time >= 250000);
}
bool dnd_performance_connectivity_due(uint64_t now) {
  return visible && (!have_connectivity || now < connectivity_time ||
                     now - connectivity_time >= DND_CONNECTIVITY_INTERVAL_US);
}
void dnd_performance_connectivity_sampled(uint64_t now, int link) {
  connectivity_time = now;
  have_connectivity = true;
  stats.network_link = link == 0 || link == 1 ? link : -1;
}
void dnd_performance_memory(uint64_t now, uint32_t main_used, uint32_t main_total,
                            bool aram_available, uint32_t aram_used, uint32_t aram_total) {
  memory_time = now;
  stats.main_total = main_total;
  stats.main_used = main_used > main_total ? main_total : main_used;
  stats.memory_valid = main_total != 0;
  stats.aram_available = aram_available && aram_total != 0;
  stats.aram_total = aram_total;
  stats.aram_used = aram_used > aram_total ? aram_total : aram_used;
}
const DndPerformanceStats *dnd_performance_stats(void) { return &stats; }

static void memory_line(char *line, const char *label, uint32_t used, uint32_t total) {
  uint32_t u = (uint32_t)((uint64_t)used * 100 / 1048576);
  uint32_t t = (uint32_t)((uint64_t)total * 100 / 1048576);
  snprintf(line, DND_PERFORMANCE_COLUMNS, "%s: %u.%02u / %u.%02u MIB",
           label, (unsigned)(u / 100), (unsigned)(u % 100), (unsigned)(t / 100), (unsigned)(t % 100));
}
void dnd_performance_network(int state, bool dhcp, uint32_t ip, uint32_t mask, uint32_t gateway) {
  stats.network_state = state;
  stats.network_dhcp = dhcp;
  stats.network_ip = ip;
  stats.network_mask = mask;
  stats.network_gateway = gateway;
}
static void address_line(char *line, const char *label, uint32_t address, bool available) {
  if (!available) snprintf(line, DND_PERFORMANCE_COLUMNS, "%s: --", label);
  else snprintf(line, DND_PERFORMANCE_COLUMNS, "%s: %u.%u.%u.%u", label,
                (unsigned)(address >> 24), (unsigned)((address >> 16) & 255),
                (unsigned)((address >> 8) & 255), (unsigned)(address & 255));
}
int dnd_performance_format(char lines[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS]) {
  memset(lines, 0, DND_PERFORMANCE_ROWS * DND_PERFORMANCE_COLUMNS);
  if (stats.timing_valid)
    snprintf(lines[0], DND_PERFORMANCE_COLUMNS, "FPS: %u.%u  FRAME: %u.%02u MS",
             (unsigned)(stats.fps_tenths / 10), (unsigned)(stats.fps_tenths % 10),
             (unsigned)(stats.frame_centimilliseconds / 100), (unsigned)(stats.frame_centimilliseconds % 100));
  else snprintf(lines[0], DND_PERFORMANCE_COLUMNS, "FPS: --  FRAME: -- MS");
  snprintf(lines[1], DND_PERFORMANCE_COLUMNS, "TRIS: %u", (unsigned)stats.triangles);
  if (stats.memory_valid) memory_line(lines[2], "MAIN", stats.main_used, stats.main_total);
  else snprintf(lines[2], DND_PERFORMANCE_COLUMNS, "MAIN: --");
  if (stats.aram_available) memory_line(lines[3], "ARAM", stats.aram_used, stats.aram_total);
  else snprintf(lines[3], DND_PERFORMANCE_COLUMNS, "ARAM: NOT INITIALIZED");
  int row = 4;
  if (stats.network_state != -1) {
    const char *state = stats.network_state == 0 ? "CONFIGURING" : stats.network_state == 1 ? "READY" :
                        stats.network_state == 2 ? "DEBUG TCP CONFIG" : "CONFIG FAILED";
    const char *link = stats.network_state == 1 || stats.network_state == 2 ?
        (stats.network_link == 1 ? " - LINK UP" : stats.network_link == 0 ? " - LINK DOWN" : " - LINK ?") : "";
    snprintf(lines[row++], DND_PERFORMANCE_COLUMNS, "BBA: %s - %s%s", state, stats.network_dhcp ? "DHCP" : "STATIC", link);
    bool available = stats.network_state == 1 || stats.network_state == 2;
    address_line(lines[row++], "IP", stats.network_ip, available);
    address_line(lines[row++], "MASK", stats.network_mask, available);
    address_line(lines[row++], "GW", stats.network_gateway, available);
  }
  const char *debugger = "UNAVAILABLE";
#ifdef DND_DEBUG_TELEMETRY
  uint32_t report = dnd_debugger_telemetry[1];
  debugger = report == 1 ? "CONNECTED" : report == 2 ? "DISCONNECTED" : "UNKNOWN";
#endif
  const char *label="DEBUG LAST";
#ifdef DND_DEBUG_TELEMETRY
  int live=dnd_debug_heartbeat_state();
  if(live!=DND_LIVE_DISABLED) {
    label="DEBUG LIVE";
    debugger=live==DND_LIVE_CONNECTED?"CONNECTED":live==DND_LIVE_LOST?"UNREACHABLE":
        live==DND_LIVE_DISCONNECTED?"DISCONNECTED":live==DND_LIVE_WAIT_NETWORK?"WAITING FOR BBA":"CHECKING";
  }
#endif
  snprintf(lines[row++], DND_PERFORMANCE_COLUMNS, "%s: %s", label, debugger);
  return row;
}
