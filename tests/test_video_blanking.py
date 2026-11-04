"""Check actual vendored VI register generation without a console toolchain."""
import importlib.util
import os
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("verify_video_source", ROOT / "scripts/verify_video_source.py")
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)
VENDORED = (ROOT / "vendor/libogc2/video.c").read_text()


def upstream_source():
    return VENDORED.replace(
        " video.c -- VIDEO subsystem (DolphinDotNet vendored correction)",
        " video.c -- VIDEO subsystem", 1,
    ).replace(
        " Modified by DolphinDotNet: preserve 10-bit VI blanking at 576p.\n"
        " Original copyright (C) 2004 - 2026",
        " Copyright (C) 2004 - 2026", 1,
    ).replace(verifier.CORRECTED, verifier.ORIGINAL, 1)


class VideoBlankingTests(unittest.TestCase):
    def test_source_guard_rejects_unrelated_changes_and_unpatched_video(self):
        upstream = upstream_source()
        verifier.verify(upstream, VENDORED)
        for changed in (upstream, VENDORED + "\n", VENDORED.replace("regs[0] =", "regs[1] =", 1)):
            with self.subTest(source=changed[-30:]), self.assertRaises(ValueError):
                verifier.verify(upstream, changed)
        with self.assertRaises(ValueError):
            verifier.verify(upstream.replace(verifier.ORIGINAL, ""), VENDORED)

    def test_actual_register_generator_preserves_field_length_and_ten_bit_ranges(self):
        # Compile the real function, including its position/scan arithmetic and
        # register writes. Only the surrounding hardware globals are replaced.
        begin = VENDORED.index("static inline void __setVerticalRegs(")
        end = VENDORED.index("\nstatic inline void __adjustPosition(", begin)
        function = VENDORED[begin:end]
        prelude = """
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
typedef uint8_t u8; typedef uint16_t u16; typedef uint32_t u32; typedef int32_t s32;
enum { VI_PROGRESSIVE=2, VI_3D=3 };
static struct { u32 nonInter; } HorVer;
static u16 regs[60]; static uint64_t changed;
#define VI_REGCHANGE(n) (UINT64_C(1) << (n))
"""
        checks = """
static unsigned half_lines(unsigned pre, unsigned post) {
    return 3*(regs[0]&15)+2*((regs[0]>>4)&1023)+(regs[pre]&1023)+(regs[post]&1023);
}
int main(void) {
    /* scan, ACV, EQU, odd/even PRB, odd/even PSB: PAL/NTSC/MPAL and double-strike */
    const unsigned timings[][7]={{2,480,12,48,48,6,6},{2,576,10,68,68,0,0},
        {0,240,6,24,25,3,2},{0,288,5,33,34,1,0},{0,240,6,24,25,3,2},
        {1,240,6,24,24,4,4},{1,288,5,33,33,0,0},{3,480,12,44,44,10,10}};
    unsigned count=0;
    for(unsigned t=0;t<sizeof timings/sizeof timings[0];t++) {
        const unsigned *v=timings[t];HorVer.nonInter=v[0];
        unsigned maximum=v[1]*(v[0]>=2?1:2);
        for(unsigned height=2;height<=maximum;height+=2) {
            const unsigned positions[]={0,(maximum-height)/2,maximum-height};
            for(unsigned p=0;p<3;p++) {
                u16 visible[60];
                __setVerticalRegs(positions[p],height,v[2],v[1],v[3],v[4],v[5],v[6],0);
                /* Exclude windows whose visible timing already exceeds the
                 * hardware fields: these cannot be represented by VI. */
                if(regs[6]>1023||regs[7]>1023||regs[8]>1023||regs[9]>1023)continue;
                memcpy(visible,regs,sizeof regs);
                unsigned odd=half_lines(7,6),even=half_lines(9,8);
                changed=0;
                __setVerticalRegs(positions[p],height,v[2],v[1],v[3],v[4],v[5],v[6],1);
                assert((regs[0]>>4)==0);
                assert(regs[6]<=1023&&regs[7]<=1023&&regs[8]<=1023&&regs[9]<=1023);
                assert(half_lines(7,6)==odd&&half_lines(9,8)==even);
                assert(changed==(VI_REGCHANGE(0)|VI_REGCHANGE(6)|VI_REGCHANGE(7)|VI_REGCHANGE(8)|VI_REGCHANGE(9)));
                __setVerticalRegs(positions[p],height,v[2],v[1],v[3],v[4],v[5],v[6],0);
                assert(!memcmp(visible,regs,sizeof regs));count++;
            }
        }
    }
    printf("%u actual VI blank/unblank register cases passed.\\n",count);
}
"""
        with tempfile.TemporaryDirectory(prefix="dnd-video-blanking-") as directory:
            folder = Path(directory)
            source = folder / "registers.c"
            binary = folder / "registers"
            compiler = shlex.split(os.environ.get("CC", "cc"))
            source.write_text(prelude + function + checks)
            for flags in ([], ["-O3"]):
                subprocess.run([*compiler, "-std=c11", "-Wall", "-Wextra", "-Werror", *flags,
                                str(source), "-o", str(binary)], check=True)
                subprocess.run([str(binary)], check=True)
            # Negative control: the identical register invariants reject the
            # original function, so replacing the overlay with stock VIDEO fails.
            source.write_text(prelude + function.replace(verifier.CORRECTED, verifier.ORIGINAL) + checks)
            subprocess.run([*compiler, "-std=c11", str(source), "-o", str(binary)], check=True)
            result = subprocess.run([str(binary)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            self.assertNotEqual(0, result.returncode, "Stock 576p blanking unexpectedly passed")


if __name__ == "__main__":
    unittest.main()
