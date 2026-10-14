#define _POSIX_C_SOURCE 200809L
#include <stdint.h>
#include <stdio.h>
#include <time.h>
#include "dnd_managed.h"
#include "dnd_async.h"
#include "dnd_aram.h"

static struct timespec started;
void dnd_console_write_line(const char *text) {
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    double elapsed = (double)(now.tv_sec - started.tv_sec)
        + (double)(now.tv_nsec - started.tv_nsec) / 1000000000.0;
    printf("automatic async: +%.3fs %s\n", elapsed, text);
    fflush(stdout);
}

extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(int argc, char **argv) {
    static unsigned char storage[256 * 1024];
    DndManagedHeap heap;
    (void)argv;
    clock_gettime(CLOCK_MONOTONIC, &started);
    dnd_managed_heap_init(&heap, storage, sizeof(storage));
    dnd_gc_set_stress(argc > 1);
    intptr_t result = dnd_value_aot_entry(&heap);
    if (result || dnd_exception_pending() || dnd_async_running() || dnd_aram_stat(4) != 0) {
        fprintf(stderr, "automatic async smoke: result=%ld exception=%d type=%s\n",
            (long)result, (int)dnd_exception_kind(), dnd_exception_message());
        DndException *error = (DndException *)dnd_exception_object();
        if (error && error->message) {
            fputs("message: ", stderr);
            for (uint32_t i = 0; i < error->message->length && i < 256; i++)
                fputc(error->message->chars[i] < 128 ? error->message->chars[i] : '?', stderr);
            fputc('\n', stderr);
        }
        return 1;
    }
    printf("automatic async smoke passed: heap=%zu collections=%zu\n", heap.used, heap.collections);
    return 0;
}
