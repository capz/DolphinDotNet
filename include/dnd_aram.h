#ifndef DND_ARAM_H
#define DND_ARAM_H
#include "dnd_managed.h"
/* Application-thread API; DMA callbacks touch only a native completion flag. */
bool dnd_aram_initialize(int reserved_bytes);
bool dnd_aram_available(void);
/* Committed ARAM: reserved lower region plus live rounded allocations. */
bool dnd_aram_memory_usage(uint32_t *used,uint32_t *total);
int dnd_aram_stat(int kind); /* 0 usable, 1 allocated, 2 free, 3 largest, 4 allocations */
int dnd_aram_allocate(int length); /* 0 means exhausted; tokens never reused */
int dnd_aram_retain(int token);
int dnd_aram_release(int token);
int dnd_aram_dispose(int token);
int dnd_aram_begin(int token,int offset,DndArray *array,int array_offset,int count,int write);
int dnd_aram_poll(DndArray *array,int array_offset);
/* Aligned zero-copy ARAM DMA to/from pinned native MEM1 buffers. */
int dnd_aram_begin_native(int token,int aram_offset,int buffer_token,int buffer_offset,int count,int write);
int dnd_aram_poll_native(void);
/* Ticketed bulk DMA. Priorities: 0 demand, 1 prefetch, 2 background. Requests
 * arbitrate in <=16 KiB chunks, preserve conflicting submission order, and hold
 * their ARAM/native-buffer owners until a terminal poll consumes the ticket.
 * Submission returns a positive ticket or negative errno (-EAGAIN if full).
 * Poll returns 0 pending, 1 success or negative errno, consuming terminal state.
 * Cancel never releases in-flight storage: poll until -ECANCELED after drainage.
 * The memory variant borrows aligned native memory (never a managed pointer);
 * its caller must keep that memory alive and untouched until terminal poll. */
int dnd_aram_request_native(int token,int aram_offset,int buffer_token,int buffer_offset,int count,int write,int priority);
int dnd_aram_request_memory(int token,int aram_offset,void *native_memory,int count,int write,int priority);
int dnd_aram_request_poll(int request_token);
int dnd_aram_request_cancel(int request_token);
/* Advance one bounded hardware step without consuming any ticket. Returns one
 * while nonterminal tickets remain, zero when only completed tickets remain.
 * Call with the same serialized execution gate as submit/poll. */
int dnd_aram_service_broker(void);
/* 0 live requests, 1 outstanding bytes, 2 peak requests, 3 read DMA bytes,
 * 4 write DMA bytes, 5 canceled requests; byte counters saturate at INT_MAX. */
int dnd_aram_broker_stat(int kind);
#ifdef DND_ARAM_TEST
void dnd_aram_test_fail_next(void);
bool dnd_aram_test_memory_conflict(uintptr_t first,unsigned first_bytes,int first_write,
                                  uintptr_t second,unsigned second_bytes,int second_write);
#endif
#endif
