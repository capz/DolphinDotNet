#include "dnd_gcloader.h"

#include <errno.h>
#include <stdint.h>
#include <string.h>

static int command(const DndGcLoaderIo *io, uint32_t a, uint32_t b,
                   uint32_t c, uint32_t expected, int response_error) {
    const uint32_t words[3] = {a, b, c};
    uint32_t response = UINT32_MAX;
    int rc = io->command(io->context, words, &response);
    if (rc) return rc < 0 ? rc : -EIO;
    return response == expected ? 0 : response_error;
}

int dnd_gcloader_select_contiguous(
    const DndGcLoaderIo *io, uint64_t first_sector, uint64_t file_length,
    const unsigned char expected_header[DND_GCLOADER_HEADER_SIZE]) {
    static const unsigned char magic[4] = {0xc2, 0x33, 0x9f, 0x3d};
    if (!io || !io->command || !io->read_mapped || !expected_header)
        return -EINVAL;
    if (file_length < DND_GCLOADER_HEADER_SIZE ||
        memcmp(expected_header + 0x1c, magic, sizeof(magic)))
        return -EINVAL;
    if (first_sector > UINT32_MAX || file_length > UINT32_MAX)
        return -EOVERFLOW;
    uint64_t sectors = (file_length + 511u) / 512u;
    if (first_sector + sectors > (uint64_t)UINT32_MAX + 1u)
        return -EOVERFLOW;

    int rc = command(io, 0xb0000000u, 0, 0, 0xaaaaaaaau, -ENODEV);
    if (rc) return rc;
    rc = command(io, 0xb3000001u, 0, 1, 0, -EIO);
    if (rc) return rc;
    rc = command(io, 0, (uint32_t)file_length, (uint32_t)first_sector, 0, -EIO);
    if (rc) return rc;
    rc = command(io, 0xb3000002u, 0, 0, 0, -EIO);
    if (rc) return rc;

    _Alignas(32) unsigned char actual_header[DND_GCLOADER_HEADER_SIZE];
    rc = io->read_mapped(io->context, 0, actual_header, sizeof(actual_header));
    if (rc) return rc < 0 ? rc : -EIO;
    return memcmp(expected_header, actual_header, sizeof(actual_header)) ? -EIO : 0;
}
