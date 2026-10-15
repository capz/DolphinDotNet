# ufbx

Upstream: https://github.com/ufbx/ufbx
Pin: 1d097696e33b367bb3152b175aeb2d95193f9b6e
License: MIT option in LICENSE.

Unmodified header/source snapshots are stored as include/dnd_ufbx.h and
include/dnd_ufbx_impl.h. source/model_ufbx.c sets the documented header-path,
no-stdio and global allocator hooks, and explicitly enables UFBX_UBSAN's defined
shift/pointer paths on every compiler (GCC does not reliably auto-detect UBSan).
Native scene loading supplies mounted-volume streams and model-budget allocators.
See docs/model-import-dependencies.md for hashes.
