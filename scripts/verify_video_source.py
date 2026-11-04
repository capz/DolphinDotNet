#!/usr/bin/env python3
"""Allow only the documented 576p black-timing correction to pinned libogc2."""
from pathlib import Path
import sys

ORIGINAL = """\t\tprbodd += ((tmp<<1)-2);
\t\tprbeven += ((tmp<<1)-2);
\t\tpsbodd += 2;
\t\tpsbeven += 2;"""
CORRECTED = """		/* PRB and PSB are 10-bit fields. Preserve the original black
		 * timing when it fits; move any pre-blank overflow to post-blank
		 * so 576p keeps its full field length and retrace interrupts. */
		u32 blank = (u32)tmp<<1;
		u32 pre = blank-2;
		u32 maxpre = prbodd>prbeven ? prbodd : prbeven;
		if(pre>1023-maxpre) pre = 1023-maxpre;
		prbodd += pre;
		prbeven += pre;
		psbodd += blank-pre;
		psbeven += blank-pre;"""


def verify(upstream: str, vendored: str) -> None:
    expected = upstream.replace(
        " video.c -- VIDEO subsystem",
        " video.c -- VIDEO subsystem (DolphinDotNet vendored correction)", 1,
    ).replace(
        " Copyright (C) 2004 - 2026",
        " Modified by DolphinDotNet: preserve 10-bit VI blanking at 576p.\n"
        " Original copyright (C) 2004 - 2026", 1,
    ).replace(ORIGINAL, CORRECTED, 1)
    if upstream.count(ORIGINAL) != 1 or vendored != expected:
        raise ValueError("Vendored VIDEO source differs from pinned upstream beyond intended correction")


if __name__ == "__main__":
    try:
        verify(Path(sys.argv[1]).read_text(), Path(sys.argv[2]).read_text())
    except ValueError as error:
        raise SystemExit(str(error)) from error
    print("DND_VIDEO_SOURCE_VERIFIED")
