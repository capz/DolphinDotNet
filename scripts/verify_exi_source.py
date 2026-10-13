#!/usr/bin/env python3
"""Guard the vendored libogc2 EXI implementation against upstream drift."""
import pathlib
import sys

upstream = pathlib.Path(sys.argv[1]).read_text()
vendored = pathlib.Path(sys.argv[2]).read_text()
expected = upstream.replace(
    "exi.c -- EXI subsystem",
    "exi.c -- EXI subsystem (DolphinDotNet vendored correction)",
    1,
).replace(
    "Copyright (C) 2004 - 2026",
    "Modified by DolphinDotNet: corrected EXI_Sync polling register.\nOriginal copyright (C) 2004 - 2026",
    1,
).replace(
    "while(_exiReg[7+nChn][0]&0x0001);",
    "while(_exiReg[nChn][3]&0x0001);",
    1,
)
if expected != vendored or upstream.count("while(_exiReg[7+nChn][0]&0x0001);") != 1:
    raise SystemExit("Vendored EXI source differs from pinned upstream beyond intended correction")
print("DND_EXI_SOURCE_VERIFIED")
