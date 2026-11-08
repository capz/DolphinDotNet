# Experimental P6 offline image mover

This Linux/POSIX research tool operates on **regular offline image files only**.
Keep the source image unmounted. `move` creates a new working image, verifies its
complete SHA-256 against the original, and retains the original as the read-only
backup. It refuses an existing output or journal directory. No block-device
adapter or normal `Defrag.Execute` integration is available.

From the repository root, with Python 3 and a C11 compiler:

```sh
python3 tools/raw-move/raw_move.py plan original.img --path GAME.ISO \
  --first 0 --count 100 --destination 50000
python3 tools/raw-move/raw_move.py move original.img --output working.img \
  --path GAME.ISO --first 0 --count 100 --destination 50000 \
  --journal-target /path/to/new-journal-a --journal-scratch /other/path/to/new-journal-b
python3 tools/raw-move/raw_move.py inspect working.img --backup original.img \
  --journal-target /path/to/new-journal-a --journal-scratch /other/path/to/new-journal-b
python3 tools/raw-move/raw_move.py recover working.img --backup original.img \
  --journal-target /path/to/new-journal-a --journal-scratch /other/path/to/new-journal-b
```

The numbers above are illustrative. `first` is a zero-based logical file-cluster
index; `count` is a cluster count; `destination` is a physical filesystem cluster
number, starting at 2. Preflight requires the entire destination interval to be
free. `--partition` selects an explicit starting sector (default 0); no partition
autodetection or partition-table writes occur. A move window can leave multiple
file extents; the result reports the actual count. A fresh whole-file free run
can produce a single extent. Occupied destinations and cycles are unsupported.

The image filesystem must support persistent extended attributes. The driver
anchors a random generation and the complete trusted request hash on the working
image before sealing the journals, and verifies both on recovery. Preserve those
attributes: copying or replacing the working image invalidates recovery identity.
Retain both journals, their original paths, the working image and the original
backup until recovery and independent verification finish. Each new move needs a
fresh working clone, verified backup and new journals. Do not reuse an old journal
for a later move or edit authoritative records by hand.

`recover` rolls back an uncommitted mapping or completes a committed mapping.
Missing/stale media, invalid records and ambiguous initial preparation stop with
an error and keep the image quarantined. If preparation stopped before a complete
journal was sealed, no image data has changed and the preserved original remains
the recovery source. The tool never guesses, repairs unrelated filesystem damage,
or automatically deletes recovery evidence. `inspect` reports journal state;
run `recover` and an independent read-only checker before mounting a completed
image. Advisory file locks require all other processes to respect exclusive use.

Supported: 512-byte sectors; FAT32 with one/two mirrored FATs or one explicitly
active FAT; exFAT 1.0 with one FAT, valid main/backup boot regions, valid upcase
checksum and a contiguous allocation bitmap. FAT/NoFatChain conversion and entry
sets crossing directory clusters are journaled. Unknown extensions, dirty media,
TexFAT, fragmented allocation bitmaps, protected/GCI files, directories, ownership
ambiguity, files above INT32_MAX and insufficient caller workspace are rejected.

Run the acceptance suites with `python3 scripts/ci_raw_move.py SUITE`, where
`SUITE` is `quick`, `faults`, `large`, `sanitize`, `independent`, or `powerpc`.
Independent checks require dosfstools/exfatprogs; PowerPC checks require the Linux
PowerPC cross compiler, static C library and qemu-user. The [P6 design](../../docs/iso-defrag-p6-experimental.md)
records the protocol and the remaining physical-device acceptance gates.
