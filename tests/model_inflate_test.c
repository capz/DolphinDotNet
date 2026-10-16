/* Original zlib streams, produced from 1024 zero bytes and a repeating 0..6
 * sequence. Their literals/matches exercise ufbx's packed Huffman shift counts
 * on the fast path, independent of optional desktop Assimp fixtures. */
#include "dnd_ufbx.h"
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

static const uint8_t zeros[] = {
    0x78, 0xda, 0x63, 0x60, 0x18, 0x05, 0xa3, 0x60, 0x14,
    0x8c, 0x54, 0x00, 0x00, 0x04, 0x00, 0x00, 0x01};
static const uint8_t sequence[] = {
    0x78, 0xda, 0x63, 0x60, 0x64, 0x62, 0x66, 0x61, 0x65, 0x63, 0x18, 0xa5,
    0x46, 0xa9, 0x51, 0x6a, 0xe4, 0x51, 0x00, 0xfb, 0x59, 0x0b, 0xfc};

static void check(const uint8_t *data, size_t size, unsigned period) {
  ufbx_inflate_retain retain = {0};
  for (unsigned pass = 0; pass < 2; pass++) {
    uint8_t output[1026];
    memset(output, 0xa5, sizeof(output));
    ufbx_inflate_input input = {0};
    input.data = data;
    input.data_size = input.total_size = size;
    /* Force the fast lookup width even for these small streams, then compare
     * the slow path while retaining reusable decode tables between calls. */
    input.internal_fast_bits = pass ? 8 : 10;
    assert(ufbx_inflate(output + 1, 1024, &input, &retain) == 1024);
    assert(output[0] == 0xa5 && output[1025] == 0xa5);
    for (unsigned i = 0; i < 1024; i++)
      assert(output[i + 1] == i % period);
  }
}

int main(void) {
  check(zeros, sizeof(zeros), 1);
  check(sequence, sizeof(sequence), 7);
  puts("Native FBX inflate fast/slow sample vectors and output bounds passed");
  return 0;
}
