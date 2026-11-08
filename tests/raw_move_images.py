#!/usr/bin/env python3
"""Independent mkfs/fsck fixtures for the offline P6 image executable."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools/raw-move"))
from raw_move import build, clone


def command(*args, ok=True):
    p = subprocess.run([str(x) for x in args], capture_output=True, text=True)
    if ok and p.returncode:
        raise AssertionError(f"{args}\n{p.stdout}\n{p.stderr}")
    return p


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for part in iter(lambda: f.read(32768), b""):
            h.update(part)
    return h.hexdigest()


def u16(b, at): return struct.unpack_from("<H", b, at)[0]
def u32(b, at): return struct.unpack_from("<I", b, at)[0]
def u64(b, at): return struct.unpack_from("<Q", b, at)[0]
def p16(b, at, x): struct.pack_into("<H", b, at, x)
def p32(b, at, x): struct.pack_into("<I", b, at, x)
def p64(b, at, x): struct.pack_into("<Q", b, at, x)


def entry_sum(data):
    value = 0
    for i, b in enumerate(data):
        if i in (2, 3):
            continue
        value = (((value & 1) << 15) + (value >> 1) + b) & 65535
    return value


def exfat_entry(name, first, size, nofat=False, attributes=32):
    encoded = name.encode("utf-16-le")
    length = len(encoded) // 2
    names = (length + 14) // 15
    result = bytearray((2 + names) * 32)
    result[0:2] = bytes((0x85, 1 + names))
    p16(result, 4, attributes)
    stamp = (((2020 - 1980) << 9 | 1 << 5 | 1) << 16) | 12 << 11
    for at in (8, 12, 16): p32(result, at, stamp)
    result[22:25] = b"\x80\x80\x80"
    result[32:36] = bytes((0xc0, 3 if nofat else 1, 0, length))
    name_hash = 0
    for b in name.upper().encode("utf-16-le"):
        name_hash = (((name_hash & 1) << 15) + (name_hash >> 1) + b) & 65535
    p16(result, 36, name_hash)
    p64(result, 40, size);p32(result, 52, first);p64(result, 56, size)
    for i in range(names):
        result[(i + 2) * 32] = 0xc1
        part = encoded[i * 30:(i + 1) * 30]
        result[(i + 2) * 32 + 2:(i + 2) * 32 + 2 + len(part)] = part
    p16(result, 2, entry_sum(result))
    return result


class Image:
    def __init__(self, path, exfat, nofat=False, cluster_bytes=4096, fragmented_bitmap=False, valid_mode=None):
        self.path, self.exfat, self.nofat = path, exfat, nofat
        with path.open("wb") as f: f.truncate(64 * 1024 * 1024)
        if exfat:
            command("mkfs.exfat", "-c", cluster_bytes, path)
        else:
            command("mkfs.fat", "-F", "32", "-s", "1", path)
        with path.open("r+b", buffering=0) as f:
            boot = f.read(512)
            if exfat:
                self.fat, self.fat_size = u32(boot, 80), u32(boot, 84)
                self.heap, self.root = u32(boot, 88), u32(boot, 96)
                self.spc, self.fats = 1 << boot[109], 1
            else:
                self.fat, self.fat_size, self.fats = u16(boot, 14), u32(boot, 36), boot[16]
                self.spc, self.root = boot[13], u32(boot, 44)
                self.heap = self.fat + self.fats * self.fat_size
            self.bpc = self.spc * 512
            self.sources = list(range(50, 54)) if nofat else [50, 70, 90, 110]
            self.data = hashlib.shake_256(b"DolphinDotNet independent P6 ISO fixture").digest(4 * self.bpc - 11)
            self.valid_bytes = 0 if valid_mode == "zero" else len(self.data) // 2 + 123 if valid_mode else len(self.data)
            self.logical_data = self.data[:self.valid_bytes] + bytes(len(self.data) - self.valid_bytes)
            self.keep = hashlib.shake_256(b"unrelated protected file").digest(self.bpc)
            self.name = "日本" + "x" * 240 + ".iso" if exfat else "GAME.ISO"
            for i, c in enumerate(self.sources):
                part = self.data[i * self.bpc:(i + 1) * self.bpc].ljust(self.bpc, b"\xcc")
                f.seek(self.lba(c) * 512);f.write(part)
                if not nofat:self.put_fat(f, c, self.sources[i + 1] if i < 3 else (0xffffffff if exfat else 0x0fffffff))
            f.seek(self.lba(60) * 512);f.write(self.keep)
            if not exfat:self.put_fat(f, 60, 0x0fffffff)
            f.seek(self.lba(self.root) * 512);root = bytearray(f.read(self.bpc))
            free_at = next(i for i in range(0, self.bpc, 32) if root[i] == 0)
            if exfat:
                bitmap = next(root[i:i + 32] for i in range(0, free_at, 32) if root[i] == 0x81)
                self.bitmap_first, self.bitmap_size = u32(bitmap, 20), u64(bitmap, 24)
                f.seek(self.lba(self.bitmap_first) * 512);bits = bytearray(f.read(self.bitmap_size))
                root_chain = [self.root]
                # Force the selected entry set across sectors AND clusters.
                while len(root_chain) * self.bpc < 480 + len(exfat_entry(self.name, 50, len(self.data), nofat)) + 96:
                    c = next(c for c in range(120, 190) if not bits[(c - 2) // 8] & (1 << ((c - 2) % 8)))
                    bits[(c - 2) // 8] |= 1 << ((c - 2) % 8)
                    self.put_fat(f, root_chain[-1], c);self.put_fat(f, c, 0xffffffff)
                    root_chain.append(c)
                for c in self.sources + [60]:bits[(c - 2) // 8] |= 1 << ((c - 2) % 8)
                if fragmented_bitmap:
                    # A spec-described layout unsupported by the external checker.
                    needed = (self.bitmap_size + self.bpc - 1) // self.bpc
                    old, c = [], self.bitmap_first
                    for _ in range(needed):
                        old.append(c);f.seek(self.fat * 512 + c * 4);c = struct.unpack("<I", f.read(4))[0]
                    chain = [self.bitmap_first] + [500 + i * 3 for i in range(1, needed)]
                    for c in old:
                        bits[(c - 2) // 8] &= ~(1 << ((c - 2) % 8))
                        self.put_fat(f, c, 0)
                    for i, c in enumerate(chain):
                        bits[(c - 2) // 8] |= 1 << ((c - 2) % 8)
                        self.put_fat(f, c, chain[i + 1] if i + 1 < len(chain) else 0xffffffff)
                    for i, c in enumerate(chain):
                        f.seek(self.lba(c) * 512);f.write(bits[i * self.bpc:(i + 1) * self.bpc].ljust(self.bpc, b"\0"))
                else:
                    f.seek(self.lba(self.bitmap_first) * 512);f.write(bits)
                directory = bytearray(len(root_chain) * self.bpc);directory[:self.bpc] = root
                assert free_at <= 480
                for at in range(free_at, 480, 32):directory[at] = 0x05
                selected = exfat_entry(self.name, 50, len(self.data), nofat)
                p64(selected, 40, self.valid_bytes);p16(selected, 2, entry_sum(selected))
                self.entry_position = 480
                directory[480:480 + len(selected)] = selected
                keep = exfat_entry("keep.bin", 60, len(self.keep), True, 1)
                directory[480 + len(selected):480 + len(selected) + len(keep)] = keep
                for i, c in enumerate(root_chain):
                    f.seek(self.lba(c) * 512);f.write(directory[i * self.bpc:(i + 1) * self.bpc])
                self.root_chain = root_chain
                self.original_directory = bytes(directory)
            else:
                self.entry_position = free_at
                for name, first, length in ((b"GAME    ISO", 50, len(self.data)), (b"SAVE    GCI", 60, len(self.keep))):
                    e = bytearray(32);e[:11] = name;e[11] = 32
                    p16(e, 26, first);p32(e, 28, length)
                    for at in (16, 18, 24):p16(e, at, (2020 - 1980) << 9 | 1 << 5 | 1)
                    root[free_at:free_at + 32] = e;free_at += 32
                f.seek(self.lba(self.root) * 512);f.write(root)
                for loc in (u16(boot, 48), u16(boot, 48) + u16(boot, 50)):
                    f.seek(loc * 512);info = bytearray(f.read(512))
                    if u32(info, 488) != 0xffffffff:p32(info, 488, u32(info, 488) - 5)
                    f.seek(loc * 512);f.write(info)
                self.root_chain = [self.root]
                self.original_directory = bytes(root)
        self.check(path)
        self.backup_digest = digest(path)

    def lba(self, c): return self.heap + (c - 2) * self.spc

    def put_fat(self, f, cluster, value):
        for copy in range(self.fats):
            f.seek((self.fat + copy * self.fat_size) * 512 + cluster * 4)
            f.write(struct.pack("<I", value))

    def check(self, path):
        p = command("fsck.exfat" if self.exfat else "fsck.fat", "-n", path, ok=False)
        assert p.returncode == 0, (p.stdout, p.stderr)

    def verify(self, path):
        self.check(path)
        with path.open("rb") as f:
            directory = b""
            for c in self.root_chain:
                f.seek(self.lba(c) * 512);directory += f.read(self.bpc)
            e = directory[self.entry_position:]
            # Independent field allowlist: timestamps, names, attributes,
            # neighboring/protected entries and system directory entries stay exact.
            observed, original = bytearray(directory), bytearray(self.original_directory)
            allowed = (2, 3, 52, 53, 54, 55) if self.exfat else (20, 21, 26, 27)
            for offset in allowed:
                observed[self.entry_position + offset] = original[self.entry_position + offset] = 0
            if self.exfat:
                observed[self.entry_position + 33] &= ~2
                original[self.entry_position + 33] &= ~2
            assert observed == original, "Directory changed outside authorized fields"
            with self.path.open("rb") as backup:
                f.seek(0);backup.seek(0)
                reserved = 24 if self.exfat else self.fat
                assert f.read(reserved * 512) == backup.read(reserved * 512), "Boot/FSInfo metadata changed"
            first = u32(e, 52) if self.exfat else u16(e, 26) | u16(e, 20) << 16
            size = u64(e, 56) if self.exfat else u32(e, 28)
            nofat = self.exfat and bool(e[33] & 2)
            data, seen, c = bytearray(), set(), first
            while len(data) < size:
                assert c >= 2 and c not in seen
                seen.add(c);f.seek(self.lba(c) * 512);data += f.read(self.bpc)
                if not nofat:
                    f.seek(self.fat * 512 + c * 4);c = struct.unpack("<I", f.read(4))[0]
                    if not self.exfat:c &= 0x0fffffff
                else:c += 1
            assert bytes(data[:size]) == self.data
            logical = bytes(data[:self.valid_bytes]) + bytes(size - self.valid_bytes)
            assert logical == self.logical_data
            f.seek(self.lba(60) * 512);assert f.read(self.bpc) == self.keep
            if self.exfat:
                length = (e[1] + 1) * 32
                assert entry_sum(e[:length]) == u16(e, 2)
                assert u64(e, 40) == self.valid_bytes
        assert digest(self.path) == self.backup_digest, "Original backup image was modified"
        return nofat


def execute(binary, fixture, work, stem, first=1, count=2, destination=200, fail=None):
    clone(fixture.path, work)
    a, b = Path(str(stem) + "-a"), Path(str(stem) + "-b")
    args = [binary, "execute", work, fixture.path, a, b, 0, fixture.name, first, count, destination]
    if fail is not None:args += ["--fail-after", fail]
    p = command(*args, ok=fail is None)
    return a, b, p


def main():
    p = argparse.ArgumentParser();p.add_argument("--binary", type=Path);args = p.parse_args()
    for tool in ("mkfs.fat", "fsck.fat", "mkfs.exfat", "fsck.exfat"):
        if not shutil.which(tool):raise SystemExit(f"Required independent tool missing: {tool}")
    with tempfile.TemporaryDirectory(prefix="dnd-p6-independent-") as directory:
        work = Path(directory);binary = args.binary or work / "raw-move-image"
        if not args.binary:build(binary)
        for variant, exfat, nofat, cluster_bytes, valid_mode in (("fat32", False, False, 512, None),
                ("exfat-chain", True, False, 4096, None), ("exfat-nofat", True, True, 512, None),
                ("exfat-partial-vdl", True, False, 512, "partial"), ("exfat-zero-vdl", True, True, 512, "zero")):
            fixture = Image(work / (variant + ".img"), exfat, nofat, cluster_bytes, valid_mode=valid_mode)
            planned = command(binary, "plan", fixture.path, 0, fixture.name, 1, 2, 200)
            assert json.loads(planned.stdout)["fileBytes"] == len(fixture.data)
            assert json.loads(planned.stdout)["fileSha256"] == hashlib.sha256(fixture.logical_data).hexdigest()
            print(json.dumps({"fixture": variant, "backupSha256": fixture.backup_digest,
                              "fileSha256": hashlib.sha256(fixture.logical_data).hexdigest()}), flush=True)
            assert digest(fixture.path) == fixture.backup_digest
            target = work / (variant + "-moved.img")
            destination = 5000 if nofat else 200
            a, b, result = execute(binary, fixture, target, work / variant, destination=destination)
            assert json.loads(result.stdout)["state"] == 7
            fixture.verify(target)
            command(binary, "recover", target, fixture.path, a, b)
            for attribute, bad in (("user.dnd_p6_identity", b"\x7f" * 8),
                                   ("user.dnd_p6_request", b"\x7f" * 32)):
                trusted = os.getxattr(target, attribute)
                before = digest(target)
                os.setxattr(target, attribute, bad)
                assert command(binary, "recover", target, fixture.path, a, b, ok=False).returncode
                assert digest(target) == before
                os.setxattr(target, attribute, trusted)
            missing = Path(str(b) + "-missing")
            b.rename(missing)
            assert command(binary, "recover", target, fixture.path, a, b, ok=False).returncode
            assert digest(target) == before
            missing.rename(b)
            fixture.verify(target)
            # The optional lookup cache is never recovery authority.
            for journal in (a, b):
                for index in journal.glob("lba_*.idx"):index.unlink()
            command(binary, "recover", target, fixture.path, a, b)
            if nofat:
                # Restoring the middle run must safely convert FAT -> NoFatChain.
                restored = work / (variant + "-restored.img")
                clone(target, restored)
                command(binary, "execute", restored, target, work / (variant + "-restore-a"), work / (variant + "-restore-b"),
                        0, fixture.name, 1, 2, 51)
                assert fixture.verify(restored), "Contiguous result must use NoFatChain"
            for label, first, count, dest in (("head", 0, 1, 70000 if not exfat else 300),
                                             ("tail", 3, 1, 400)):
                endpoint = work / f"{variant}-{label}.img"
                execute(binary, fixture, endpoint, work / f"{variant}-{label}", first, count, dest)
                fixture.verify(endpoint)
            events = json.loads(result.stdout)["events"]
            for i, point in enumerate((1, events // 2, events - 8, events - 2)):
                interrupted = work / f"{variant}-cut-{i}.img"
                ja, jb, failed = execute(binary, fixture, interrupted, work / f"{variant}-cut-{i}", destination=destination, fail=point)
                assert failed.returncode != 0
                recovery = command(binary, "recover", interrupted, fixture.path, ja, jb, ok=False)
                if recovery.returncode:
                    assert digest(interrupted) == fixture.backup_digest, (recovery.stdout, recovery.stderr)
                else:
                    fixture.verify(interrupted)
                    command(binary, "recover", interrupted, fixture.path, ja, jb)
            print(f"Independent mkfs/fsck, logical SHA, protected file and recovery passed: {variant}", flush=True)
        fragmented = Image(work / "fragmented-bitmap.img", True, True, 512, fragmented_bitmap=True)
        planned = command(binary, "plan", fragmented.path, 0, fragmented.name, 1, 2, 5000, ok=False)
        assert planned.returncode != 0
        assert digest(fragmented.path) == fragmented.backup_digest
        print("Fragmented exFAT bitmap rejected read-only pending independent consumer acceptance", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
