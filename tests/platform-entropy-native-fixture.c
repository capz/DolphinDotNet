#define _POSIX_C_SOURCE 200809L
#include "dnd_platform_entropy.h"
#include "dnd_jitterentropy_port.h"
#ifdef DND_ENTROPY_BRIDGE_TEST
#include "dnd_async.h"
#endif
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

/* Synthetic timing is used only to reach deterministic failure paths. Passing
 * these fixtures is not evidence of entropy on an emulated or physical console. */
enum Mode { FROZEN, COARSE, SYNTHETIC, DEADLINE, REAL_CLOCK };
enum { INVALID = -1, BUSY = -2, MEMORY = -3, HEALTH = -4, TIMEOUT = -5 };
enum { MAX_BLOCK = 128 * 1024, MAX_TOTAL = 192 * 1024 };
typedef struct { void *pointer; size_t bytes; } Allocation;
static Allocation allocations[32];
static enum Mode mode;
static uint64_t raw_calls, fake_ns, fake_ms;
static uint32_t synthetic_state;
static size_t live_bytes, peak_bytes, largest_block;
static unsigned allocation_calls, fail_allocation, free_calls;
static int probe_busy, busy_probed;
enum ResourceProbe { NO_PROBE, OVERSIZE_PROBE, TOTAL_PROBE };
static enum ResourceProbe resource_probe;
static int resource_probed;
static unsigned char *native_output;
static size_t native_output_length;
static int probe_partial_seed, partial_seed_observed;
static int probe_sample_budget, sample_budget_observed;
static uint64_t budget_raw_reads;
#ifdef DND_ENTROPY_BRIDGE_TEST
static int bridge_active, managed_gate, bridge_running, bridge_context;
static int bridge_begins, bridge_ends, bridge_pushes, bridge_pops;
static DndGcFrame *bridge_frame;
static DndArray *bridge_original, *bridge_relocated;
static void bridge_assert_native(void) {
    if (bridge_active) assert(!managed_gate && bridge_frame);
}
#else
static void bridge_assert_native(void) {}
#endif

static uint64_t monotonic_ns(void) {
    struct timespec now;
#if defined(CLOCK_MONOTONIC_RAW)
    assert(!clock_gettime(CLOCK_MONOTONIC_RAW, &now));
#else
    assert(!clock_gettime(CLOCK_MONOTONIC, &now));
#endif
    return (uint64_t)now.tv_sec * UINT64_C(1000000000) + (uint64_t)now.tv_nsec;
}

uint64_t dnd_platform_entropy_test_clock(void) {
    bridge_assert_native();
    if (probe_sample_budget && !sample_budget_observed) {
        sample_budget_observed = 1;
        unsigned consumed = 0;
        /* The recursive timestamp reads use the normal adapter budget while
         * the independent wall clock stays fixed. Mark the probe first so
         * only this initial timer call enters the bounded exhaustion loop. */
        while (dnd_jent_timestamp()) assert(++consumed <= 1000000);
        assert(dnd_platform_entropy_test_sample_count() == 1000000);
        budget_raw_reads = raw_calls;
        assert(!dnd_jent_timestamp());
        assert(raw_calls == budget_raw_reads);
        assert(dnd_platform_entropy_test_sample_count() == 1000000);
        return 0;
    }
    if (probe_partial_seed && !partial_seed_observed) {
        assert(native_output && native_output_length >= 48);
        unsigned published = 0;
        for (unsigned i = 0; i < 32; ++i) published |= native_output[i];
        if (published) {
            /* Observe the actual library's first published 32-byte block,
             * before the second collection completes. The remaining output
             * still has the adapter's initial zeroing, not a fabricated seed. */
            for (size_t i = 32; i < native_output_length; ++i) assert(!native_output[i]);
            partial_seed_observed = 1;
        }
    }
    if (resource_probe && !resource_probed) {
        resource_probed = 1;
        unsigned before = allocation_calls;
        if (resource_probe == OVERSIZE_PROBE) {
            assert(!dnd_jent_zalloc(MAX_BLOCK + 1));
            assert(allocation_calls == before);
        } else {
            size_t remaining = MAX_TOTAL - dnd_platform_entropy_test_live_bytes();
            unsigned added = 0;assert(remaining);
            while (remaining) {
                size_t bytes = remaining > MAX_BLOCK ? MAX_BLOCK : remaining;
                assert(dnd_jent_zalloc(bytes));remaining -= bytes;++added;
            }
            assert(!dnd_jent_zalloc(1));
            assert(allocation_calls == before + added);
        }
    }
    ++raw_calls;
    if (mode == FROZEN) return UINT64_C(1000000000);
    if (mode == COARSE) return (raw_calls / 4096u) * UINT64_C(1000000000);
    if (mode == SYNTHETIC) {
        synthetic_state ^= synthetic_state << 13;
        synthetic_state ^= synthetic_state >> 17;
        synthetic_state ^= synthetic_state << 5;
        fake_ns += 37u + (synthetic_state & 0x3ffffu);
        return fake_ns;
    }
    return monotonic_ns();
}

uint64_t dnd_platform_entropy_test_monotonic_ms(void) {
    bridge_assert_native();
    if (probe_partial_seed && partial_seed_observed) return 30001;
    if (mode == DEADLINE) return ++fake_ms;
    if (mode == REAL_CLOCK) return monotonic_ns() / UINT64_C(1000000);
    return 0; /* Isolate health/allocation failures from elapsed-time checks. */
}

static void all_zero(const unsigned char *bytes, size_t count) {
    for (size_t i = 0; i < count; ++i) assert(bytes[i] == 0);
}

void *dnd_platform_entropy_test_alloc(size_t bytes) {
    bridge_assert_native();
    ++allocation_calls;
    assert(bytes > 0 && bytes <= MAX_BLOCK);
    assert(live_bytes + bytes <= MAX_TOTAL);
    if (probe_busy && !busy_probed) {
        unsigned char nested[64];memset(nested, 0xa5, sizeof(nested));
        busy_probed = 1;
        assert(dnd_platform_entropy_fill(nested, sizeof(nested), 10) == BUSY);
        all_zero(nested, sizeof(nested));
    }
    if (fail_allocation && allocation_calls == fail_allocation) return NULL;
    void *pointer = malloc(bytes);assert(pointer);
    memset(pointer, 0xa5, bytes);
    unsigned slot = 0;
    while (slot < 32 && allocations[slot].pointer) ++slot;
    assert(slot < 32);
    allocations[slot] = (Allocation){pointer, bytes};
    live_bytes += bytes;
    if (live_bytes > peak_bytes) peak_bytes = live_bytes;
    if (bytes > largest_block) largest_block = bytes;
    return pointer;
}

void dnd_platform_entropy_test_free(void *pointer, size_t bytes) {
    bridge_assert_native();
    if (!pointer) return;
    unsigned slot = 0;
    while (slot < 32 && allocations[slot].pointer != pointer) ++slot;
    assert(slot < 32 && allocations[slot].bytes == bytes);
    /* The adapter must wipe the full native allocation before returning it. */
    all_zero(pointer, bytes);
    live_bytes -= bytes;
    allocations[slot] = (Allocation){0};
    ++free_calls;free(pointer);
}

static void reset(enum Mode next_mode, unsigned fail_at) {
    assert(!native_output && live_bytes == 0 && dnd_platform_entropy_test_live_bytes() == 0);
    memset(allocations, 0, sizeof(allocations));
    mode = next_mode;raw_calls = 0;fake_ns = 0;fake_ms = 0;
    synthetic_state = 0x76a5b913u;
    peak_bytes = largest_block = 0;
    allocation_calls = free_calls = 0;fail_allocation = fail_at;
    probe_busy = busy_probed = 0;
    resource_probe = NO_PROBE;resource_probed = 0;
    probe_partial_seed = partial_seed_observed = 0;
    probe_sample_budget = sample_budget_observed = 0;budget_raw_reads = 0;
}

static int call(unsigned length, uint32_t timeout_ms, int expected) {
    struct { unsigned char before[8], bytes[64], after[8]; } output;
    memset(&output, 0x5a, sizeof(output));memset(output.bytes, 0xa5, 64);
    assert(!native_output);native_output = output.bytes;native_output_length = length;
    int status = dnd_platform_entropy_fill(output.bytes, length, timeout_ms);
    native_output = NULL;native_output_length = 0;
    assert(status == expected);
    for (unsigned i = 0; i < 8; ++i)assert(output.before[i] == 0x5a && output.after[i] == 0x5a);
    if (status) all_zero(output.bytes, length);
    else {
        unsigned nonzero = 0;for (unsigned i = 0; i < length; ++i) nonzero |= output.bytes[i];
        assert(nonzero);memset(output.bytes, 0, length);
    }
    for (unsigned i = length; i < 64; ++i) assert(output.bytes[i] == 0xa5);
    assert(live_bytes == 0 && dnd_platform_entropy_test_live_bytes() == 0);
    assert(peak_bytes <= MAX_TOTAL && largest_block <= MAX_BLOCK);
    assert(dnd_platform_entropy_test_peak_bytes() <= MAX_TOTAL);
    return status;
}

static void invalid_inputs(void) {
    unsigned char bytes[80];memset(bytes, 0xa5, sizeof(bytes));
    assert(dnd_platform_entropy_fill(NULL, 48, 10) == INVALID);
    for (size_t i = 0; i < 4; ++i) {
        size_t length = (size_t[]){0, 47, 65, SIZE_MAX}[i];
        assert(dnd_platform_entropy_fill(bytes, length, 10) == INVALID);
        for (size_t n = 0; n < sizeof(bytes); ++n) assert(bytes[n] == 0xa5);
    }
    reset(SYNTHETIC, 0);call(48, 0, INVALID);
    reset(SYNTHETIC, 0);call(64, 30001, INVALID);
    assert(allocation_calls == 0 && raw_calls == 0);
}

static void deterministic_failures(void) {
    reset(FROZEN, 0);call(48, 10, HEALTH);assert(raw_calls < 100000);
    reset(COARSE, 0);call(64, 10, HEALTH);assert(raw_calls < 100000);
    reset(DEADLINE, 0);call(48, 1, TIMEOUT);assert(fake_ms < 1000);

    /* A predictable test timestamp sequence is intentionally not credited as
     * entropy. It reaches the allocation/cleanup paths in the actual library. */
    reset(SYNTHETIC, 0);call(48, 10, 0);
    unsigned successful_allocations = allocation_calls;
    assert(successful_allocations > 0 && free_calls > 0);
    printf("Synthetic control-flow baseline: allocations=%u peak=%zu maxblock=%zu samples=%llu (not entropy qualification)\n",
           allocation_calls, peak_bytes, largest_block,
           (unsigned long long)dnd_platform_entropy_test_sample_count());
    for (unsigned fail_at = 1; fail_at <= successful_allocations; ++fail_at) {
        reset(SYNTHETIC, fail_at);call(64, 10, MEMORY);
    }
    reset(SYNTHETIC, 1);probe_busy = 1;call(48, 10, MEMORY);assert(busy_probed);
    reset(SYNTHETIC, 0);resource_probe = OVERSIZE_PROBE;
    call(48, 10, MEMORY);assert(resource_probed);
    reset(SYNTHETIC, 0);resource_probe = TOTAL_PROBE;
    call(64, 10, MEMORY);assert(resource_probed && peak_bytes == MAX_TOTAL);
    reset(SYNTHETIC, 0);probe_sample_budget = 1;
    call(64, 10, TIMEOUT);
    assert(sample_budget_observed && raw_calls == budget_raw_reads);
    assert(dnd_platform_entropy_test_sample_count() == 1000000);
    for (unsigned length = 48; length <= 64; length += 16) {
        reset(SYNTHETIC, 0);probe_partial_seed = 1;
        call(length, 10, TIMEOUT);assert(partial_seed_observed);
    }
    puts("Partial-seed timeout: actual first 32-byte block observed, then full 48/64-byte output wiped.");
    puts("Native entropy invalid/busy/allocator/source/deadline failures: zero output, wiped allocations and bounded memory passed.");
}

#ifdef DND_ENTROPY_BRIDGE_TEST
extern int dnd_platform_entropy_fill_array(DndArray *output, int32_t timeout_ms);

bool dnd_async_running(void) { assert(managed_gate);return bridge_running; }
int dnd_async_context(void) { assert(managed_gate);return bridge_context; }
void dnd_gc_frame_push(DndGcFrame *frame, DndObject ***slots, size_t count) {
    assert(managed_gate && !bridge_frame && count == 1);
    assert(*slots[0] == (DndObject *)bridge_original);
    frame->slots = slots;frame->count = count;frame->previous = NULL;
    bridge_frame = frame;++bridge_pushes;
}
void dnd_gc_frame_pop(DndGcFrame *frame) {
    assert(managed_gate && bridge_frame == frame);
    assert(*frame->slots[0] == (DndObject *)bridge_relocated);
    bridge_frame = NULL;++bridge_pops;
}
int dnd_async_native_begin(void) {
    assert(managed_gate && bridge_frame);
    all_zero(bridge_original->data, bridge_original->length);
    managed_gate = 0;++bridge_begins;
    return bridge_running ? 17 : -1;
}
void dnd_async_native_end(int context) {
    assert(!managed_gate && bridge_frame);
    assert(context == (bridge_running ? 17 : -1));
    /* Emulate relocation while the native seed is being collected. Accessing
     * output->data before reacquiring the gate would alter this old object. */
    all_zero(bridge_original->data, bridge_original->length);
    memcpy(bridge_relocated, bridge_original, sizeof(DndArray) + bridge_original->length);
    *bridge_frame->slots[0] = (DndObject *)bridge_relocated;
    managed_gate = 1;++bridge_ends;
}
static void bridge_call(enum Mode next_mode, unsigned fail_at, int running,
                        int context, int expected) {
    reset(next_mode, fail_at);
    bridge_original = calloc(1, sizeof(DndArray) + 64);
    bridge_relocated = calloc(1, sizeof(DndArray) + 64);
    assert(bridge_original && bridge_relocated);
    bridge_original->length = 64;bridge_original->element_size = 1;
    memset(bridge_original->data, 0xa5, 64);
    bridge_running = running;bridge_context = context;managed_gate = 1;
    bridge_begins = bridge_ends = bridge_pushes = bridge_pops = 0;
    bridge_active = 1;
    int result = dnd_platform_entropy_fill_array(bridge_original, next_mode == DEADLINE ? 1 : 10);
    bridge_active = 0;
    assert(result == expected && managed_gate && !bridge_frame);
    all_zero(bridge_original->data, 64);
    if (running && context != 1) {
        assert(!bridge_begins && !bridge_ends && !bridge_pushes && !bridge_pops);
        assert(!allocation_calls && !raw_calls);
    } else {
        assert(bridge_begins == 1 && bridge_ends == 1 && bridge_pushes == 1 && bridge_pops == 1);
        if (result) all_zero(bridge_relocated->data, 64);
        else {
            unsigned nonzero = 0;for (unsigned i = 0; i < 64; ++i) nonzero |= bridge_relocated->data[i];
            assert(nonzero);
        }
    }
    memset(bridge_original->data, 0, 64);memset(bridge_relocated->data, 0, 64);
    free(bridge_original);free(bridge_relocated);
    bridge_original = bridge_relocated = NULL;
    assert(live_bytes == 0 && dnd_platform_entropy_test_live_bytes() == 0);
}
static void managed_bridge(void) {
    bridge_call(SYNTHETIC, 0, 1, 0, BUSY);
    bridge_call(SYNTHETIC, 0, 1, 1, 0);
    bridge_call(SYNTHETIC, 0, 0, 0, 0);
    bridge_call(FROZEN, 0, 1, 1, HEALTH);
    bridge_call(SYNTHETIC, 1, 1, 1, MEMORY);
    bridge_call(DEADLINE, 0, 1, 1, TIMEOUT);
    puts("Managed entropy bridge: rooted relocation, gate release/reacquire, application-thread rejection and zero failure output passed.");
}
#endif

int main(int argc, char **argv) {
    invalid_inputs();deterministic_failures();
#ifdef DND_ENTROPY_BRIDGE_TEST
    managed_bridge();
#endif
    if (argc == 2 && !strcmp(argv[1], "--real-clock")) {
        reset(REAL_CLOCK, 0);
        unsigned char bytes[64];memset(bytes, 0xa5, sizeof(bytes));
        int result = dnd_platform_entropy_fill(bytes, sizeof(bytes), 10000);
        assert(result == 0 || result == HEALTH || result == TIMEOUT);
        if (result)all_zero(bytes, sizeof(bytes));
        memset(bytes, 0, sizeof(bytes));
        assert(live_bytes == 0 && dnd_platform_entropy_test_live_bytes() == 0);
        printf("Actual host clock: status=%d peak=%zu maxblock=%zu samples=%llu; physical GameCube entropy quality is not established.\n",
               result, peak_bytes, largest_block,
               (unsigned long long)dnd_platform_entropy_test_sample_count());
    }
    return 0;
}
