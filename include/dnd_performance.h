#ifndef DND_PERFORMANCE_H
#define DND_PERFORMANCE_H
#include <stdbool.h>
#include <stdint.h>

#define DND_PERFORMANCE_ROWS 9
#define DND_PERFORMANCE_COLUMNS 64
#define DND_CONNECTIVITY_INTERVAL_US UINT64_C(2000000)
#define DND_DEBUGGER_TELEMETRY_MAGIC UINT32_C(0x444e4431)
#ifdef DND_DEBUG_TELEMETRY
extern volatile uint32_t dnd_debugger_telemetry[2]; /* magic, last adapter report: 0 unknown / 1 connected / 2 disconnected */
#endif
typedef struct {
  uint32_t triangles, fps_tenths, frame_centimilliseconds;
  uint32_t draw_submissions, texture_binds;
  uint32_t gpu_wait_us, retrace_wait_us;
  uint32_t begin_copy_us, gate_reacquire_us, begin_copy_issued;
  uint32_t main_used, main_total, aram_used, aram_total;
  bool timing_valid, memory_valid, aram_available;
  int network_state; /* -1 absent, 0 configuring, 1 ready, -2 failed, 2 debugger TCP config */
  bool network_dhcp;
  int network_link; /* -1 unknown/busy/unsupported, 0 cable down, 1 cable up */
  uint32_t network_ip, network_mask, network_gateway;
} DndPerformanceStats;

/* Application-thread telemetry. No allocation or automatic ARAM initialization. */
void dnd_performance_set_visible(int visible);
int dnd_performance_visible(void);
void dnd_performance_begin_frame(void);
void dnd_performance_add_triangles(uint32_t triangles);
void dnd_performance_add_draw(uint32_t texture_binds);
int dnd_performance_render_stat(int kind);
void dnd_performance_compact_glyphs(int enabled);
int dnd_performance_glyph_runs(void);
void dnd_performance_frame_reuse(int enabled);
bool dnd_performance_begin_clear(uint32_t color);
void dnd_performance_clear_completed(void);
void dnd_performance_clear_invalidated(void);
void dnd_performance_begin_copy(uint32_t us,bool issued);
void dnd_performance_gate_wait(uint32_t us);
void dnd_performance_frame_waits(uint32_t gpu_us,uint32_t retrace_us);
void dnd_performance_presented(uint64_t microseconds);
bool dnd_performance_memory_due(uint64_t microseconds);
bool dnd_performance_connectivity_due(uint64_t microseconds);
void dnd_performance_connectivity_sampled(uint64_t microseconds, int link);
void dnd_performance_memory(uint64_t microseconds, uint32_t main_used,
                            uint32_t main_total, bool aram_available,
                            uint32_t aram_used, uint32_t aram_total);
const DndPerformanceStats *dnd_performance_stats(void);
void dnd_performance_network(int state, bool dhcp, uint32_t ip, uint32_t mask, uint32_t gateway);
int dnd_performance_format(char lines[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS]);
#endif
