#ifndef DND_PERFORMANCE_H
#define DND_PERFORMANCE_H
#include <stdbool.h>
#include <stdint.h>

#define DND_PERFORMANCE_ROWS 4
#define DND_PERFORMANCE_COLUMNS 64
typedef struct {
  uint32_t triangles, fps_tenths, frame_centimilliseconds;
  uint32_t main_used, main_total, aram_used, aram_total;
  bool timing_valid, memory_valid, aram_available;
} DndPerformanceStats;

/* Application-thread telemetry. No allocation or automatic ARAM initialization. */
void dnd_performance_set_visible(int visible);
int dnd_performance_visible(void);
void dnd_performance_begin_frame(void);
void dnd_performance_add_triangles(uint32_t triangles);
void dnd_performance_presented(uint64_t microseconds);
bool dnd_performance_memory_due(uint64_t microseconds);
void dnd_performance_memory(uint64_t microseconds, uint32_t main_used,
                            uint32_t main_total, bool aram_available,
                            uint32_t aram_used, uint32_t aram_total);
const DndPerformanceStats *dnd_performance_stats(void);
void dnd_performance_format(char lines[DND_PERFORMANCE_ROWS][DND_PERFORMANCE_COLUMNS]);
#endif
