# libogc2 raw binding generation

`python3 tools/gx-bindings/generate.py` regenerates all checked-in bindings offline
from `schema.json`. Import replacement `gx.h`, `gu.h`, `video.h` headers with
`--import-headers DIR`, review the ABI/schema changes, then regenerate.

The schema records public declarations and numeric constants from
[libogc2](https://github.com/extremscorner/libogc2), GX/GU/VIDEO headers.
No libogc implementation code is copied. These interfaces retain their upstream
names. libogc2 carries the original libogc copyright and zlib license; consult
the upstream headers for credits and licensing. Native wrappers compile against
the installed libogc2 SDK; CI verifies its pinned container version.

Only simple numeric macros are imported. Macro expressions requiring external
headers, data symbols, inline-only private helpers and native callback bodies are
not represented as managed values. Native structures use opaque addresses;
callbacks require native function pointers. Generated raw declarations are not a
managed emulation of GX on the desktop.
