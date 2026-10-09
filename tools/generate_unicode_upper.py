#!/usr/bin/env python3
"""Regenerate simple uppercase pairs from ICU release-77-1 UnicodeData.txt.

Source: https://github.com/unicode-org/icu/blob/release-77-1/icu4c/source/data/unidata/UnicodeData.txt
Usage: python tools/generate_unicode_upper.py UnicodeData.txt include/dnd_unicode_upper.h
The checked-in table keeps target builds independent of Python/network access.
"""
import pathlib
import sys

pairs = []
for line in pathlib.Path(sys.argv[1]).read_text().splitlines():
    fields = line.split(";")
    if len(fields) > 12 and fields[12]:
        pairs.append((int(fields[0], 16), int(fields[12], 16)))
header = """/* Unicode 16 simple uppercase mapping from ICU release-77-1 UnicodeData.txt.
 * Unicode data license: https://www.unicode.org/license.txt
 * Copyright Unicode, Inc. See docs/unicode-license.txt. */
static const struct {uint32_t from,to;} dnd_unicode_upper[] = {
"""
pathlib.Path(sys.argv[2]).write_text(header + "".join(
    " {0x%xu,0x%xu},\n" % pair for pair in pairs) + "};\n")
