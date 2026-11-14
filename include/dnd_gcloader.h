#ifndef DND_GCLOADER_H
#define DND_GCLOADER_H

#include <stdint.h>

#define DND_GCLOADER_HEADER_SIZE 0x440u

/* All callbacks return zero on success or a negative errno on failure.
 * The platform adapter must queue commands with a bounded timeout; the read
 * must use the physical DVD interface, not the mounted image filesystem. */
typedef struct {
    void *context;
    int (*command)(void *context, const uint32_t words[3], uint32_t *response);
    int (*read_mapped)(void *context, uint32_t byte_offset,
                       void *aligned_buffer, uint32_t length);
} DndGcLoaderIo;

/* Select a previously verified, fully contiguous image. first_sector is an
 * absolute SD-card LBA in 512-byte sectors, including any partition offset.
 * file_length is the exact file size in bytes, not its allocated cluster size.
 * expected_header must be read from that file before calling this function.
 *
 * Success includes physical readback of the entire GameCube header. This
 * operation does not write the card or intentionally reset/remount the drive.
 * A timeout may trigger the platform's drive reset, leaving an uncertain
 * mapping: keep both image files and do
 * not continue to destructive cleanup after any error. */
int dnd_gcloader_select_contiguous(
    const DndGcLoaderIo *io, uint64_t first_sector, uint64_t file_length,
    const unsigned char expected_header[DND_GCLOADER_HEADER_SIZE]);

#endif
