#include "dnd_gcloader.h"

#include <assert.h>
#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef struct {
    uint32_t first_sector, file_length;
    unsigned commands, reads;
    int fail_command, command_result, read_result;
    uint32_t responses[4];
    unsigned char header[DND_GCLOADER_HEADER_SIZE];
} Fixture;

static int issue(void *context, const uint32_t words[3], uint32_t *response) {
    Fixture *f = context;
    const uint32_t expected[4][3] = {
        {0xb0000000u, 0, 0}, {0xb3000001u, 0, 1},
        {0, f->file_length, f->first_sector}, {0xb3000002u, 0, 0}
    };
    assert(f->commands < 4);
    unsigned index = f->commands++;
    assert(!memcmp(words, expected[index], sizeof(expected[index])));
    assert(f->reads == 0);
    if ((int)index == f->fail_command) return f->command_result;
    *response = f->responses[index];
    return 0;
}

static int read_mapped(void *context, uint32_t offset, void *buffer,
                       uint32_t length) {
    Fixture *f = context;
    assert(f->commands == 4 && f->reads == 0);
    assert(offset == 0 && length == DND_GCLOADER_HEADER_SIZE);
    assert((uintptr_t)buffer % 32 == 0);
    ++f->reads;
    if (f->read_result) return f->read_result;
    memcpy(buffer, f->header, length);
    return 0;
}

static void reset(Fixture *f, unsigned char *expected) {
    memset(f, 0, sizeof(*f));
    f->first_sector = 8192;
    f->file_length = 1459978240u;
    f->fail_command = -1;
    f->command_result = -ETIMEDOUT;
    f->responses[0] = 0xaaaaaaaau;
    for (unsigned i = 0; i < DND_GCLOADER_HEADER_SIZE; ++i)
        expected[i] = (unsigned char)(i * 37u + 11u);
    memcpy(expected, "AMTE01", 6);
    memcpy(expected + 0x1c, "\xc2\x33\x9f\x3d", 4);
    memcpy(expected + 0x20, "AnimalTown version 1.2.3", 24);
    memcpy(f->header, expected, sizeof(f->header));
}

static int select_image(Fixture *f, const unsigned char *expected) {
    DndGcLoaderIo io = {f, issue, read_mapped};
    return dnd_gcloader_select_contiguous(&io, f->first_sector,
                                         f->file_length, expected);
}

int main(void) {
    Fixture f;
    unsigned char expected[DND_GCLOADER_HEADER_SIZE];
    reset(&f, expected);
    assert(select_image(&f, expected) == 0);
    assert(f.commands == 4 && f.reads == 1);

    /* The game ID is the same across updates. Verify the complete header,
     * including the title/version and the final byte, rather than just ID. */
    const unsigned mismatch_offsets[] = {0x20, DND_GCLOADER_HEADER_SIZE - 1};
    for (unsigned i = 0; i < sizeof(mismatch_offsets) / sizeof(*mismatch_offsets); ++i) {
        reset(&f, expected);
        f.header[mismatch_offsets[i]] ^= 1;
        assert(!memcmp(f.header, expected, 0x20));
        assert(select_image(&f, expected) == -EIO);
        assert(f.commands == 4 && f.reads == 1);
    }

    for (int i = 0; i < 4; ++i) {
        reset(&f, expected);
        f.fail_command = i;
        assert(select_image(&f, expected) == -ETIMEDOUT);
        assert(f.commands == (unsigned)i + 1 && f.reads == 0);
        reset(&f, expected);
        f.responses[i] ^= 1;
        assert(select_image(&f, expected) == (i == 0 ? -ENODEV : -EIO));
        assert(f.commands == (unsigned)i + 1 && f.reads == 0);
    }

    reset(&f, expected);
    f.read_result = -ETIMEDOUT;
    assert(select_image(&f, expected) == -ETIMEDOUT);
    assert(f.commands == 4 && f.reads == 1);
    /* A malformed adapter must not turn a positive result into success. */
    reset(&f, expected);
    f.fail_command = 2;
    f.command_result = 1;
    assert(select_image(&f, expected) == -EIO);
    assert(f.commands == 3 && f.reads == 0);
    reset(&f, expected);
    f.read_result = 1;
    assert(select_image(&f, expected) == -EIO);

    reset(&f, expected);
    DndGcLoaderIo io = {&f, issue, read_mapped};
    assert(dnd_gcloader_select_contiguous(NULL, 0, f.file_length, expected) == -EINVAL);
    assert(dnd_gcloader_select_contiguous(&io, 0, f.file_length, NULL) == -EINVAL);
    io.command = NULL;
    assert(dnd_gcloader_select_contiguous(&io, 0, f.file_length, expected) == -EINVAL);
    io.command = issue;
    io.read_mapped = NULL;
    assert(dnd_gcloader_select_contiguous(&io, 0, f.file_length, expected) == -EINVAL);
    assert(f.commands == 0 && f.reads == 0);

    reset(&f, expected);
    expected[0x1f] ^= 1;
    assert(select_image(&f, expected) == -EINVAL);
    assert(f.commands == 0 && f.reads == 0);
    reset(&f, expected);
    f.file_length = DND_GCLOADER_HEADER_SIZE - 1;
    assert(select_image(&f, expected) == -EINVAL);
    assert(f.commands == 0 && f.reads == 0);

    reset(&f, expected);
    io = (DndGcLoaderIo){&f, issue, read_mapped};
    assert(dnd_gcloader_select_contiguous(&io, (uint64_t)UINT32_MAX + 1,
                                         f.file_length, expected) == -EOVERFLOW);
    assert(dnd_gcloader_select_contiguous(&io, 0,
                                         (uint64_t)UINT32_MAX + 1, expected) == -EOVERFLOW);
    assert(dnd_gcloader_select_contiguous(&io, UINT32_MAX,
                                         DND_GCLOADER_HEADER_SIZE, expected) == -EOVERFLOW);
    assert(f.commands == 0 && f.reads == 0);

    /* Byte length is exact; only the range check rounds to SD sectors. */
    reset(&f, expected);
    f.file_length = DND_GCLOADER_HEADER_SIZE;
    f.first_sector = UINT32_MAX - 2;
    assert(select_image(&f, expected) == 0);
    reset(&f, expected);
    f.file_length = DND_GCLOADER_HEADER_SIZE;
    f.first_sector = UINT32_MAX - 1;
    assert(select_image(&f, expected) == -EOVERFLOW);
    assert(f.commands == 0 && f.reads == 0);
    reset(&f, expected);
    f.file_length = UINT32_MAX;
    f.first_sector = 0;
    assert(select_image(&f, expected) == 0);

    puts("GC Loader mapping protocol tests passed");
    return 0;
}
