# Consolidated memory-card records

`MemoryCard.OpenRecordBlocks` creates or opens one browser item with one banner
and icon. Feed it to `CardRecordStore` to replace named records independently.
The caller owns the mount, exclusively owns the file, and disposes its store
references before unmounting. Only standard 8 KiB sector cards are supported.

Two independent card sectors hold checksummed versioned directories. A write
allocates sectors referenced by neither checkpoint, copies live neighbors when
packing small records, writes and physically reads back the data, then replaces
the older directory and reads that back. Opening validates both roots and every
referenced record, selecting the newest complete checkpoint. Never overwrite a
previously committed sector before publishing its replacement. Sequence overflow,
capacity exhaustion and a full record directory fail without discarding data.
After an ambiguous I/O failure reopen; do not reuse the old in-memory allocator.

There are at most 64 ASCII-named records, 6..256 payload sectors, two fixed
checkpoint sectors, and two browser artwork sectors. `AvailableBlocks` excludes
both checkpoints' live data. Reserve capacity for both live records and updates;
it is not the card's filesystem free-block count. The container is fixed-size:
growing/migrating requires a separately verified copy, never delete-and-recreate.
Existing files without this format are refused by the block adapter.

RAM consists of two 8 KiB indexes, a staging sector, small allocation maps and
the requested record/update buffers. Writes never allocate a complete file-sized
journal. Small records can share sectors. A debug toggle does not rewrite town
data. Directory metadata is set once at creation. Container identity is checked
before every sector access; all calls also require a mounted card.

Application-level records remain separate schemas. Applications must use their
own publication protocol for transactions across several records or cards and
must not interpret a missing card as deletion. IPL/card-manager deletion removes
the entire container; individual record deletion is an application operation.

Validation: `dotnet run --project tests/CardRecordStoreTests -c Release` exercises
every byte cut in data/checkpoint writes, unchanged neighbors, packing, bounded
small updates, fallback, deletion and capacity. `make -C tests card_blocks_test`
checks the native aligned adapter, mutable libogc file length, metadata, identity
and existing-file guards. These host fixtures do not qualify physical flash
power-loss behavior, Swiss or physical Memory Card 251 operation.
