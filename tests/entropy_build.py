"""Check entropy compiler isolation, incremental rebuilds and atomic failure."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def verify():
    root = Path(__file__).resolve().parent.parent
    spec = importlib.util.spec_from_file_location("build_entropy", root / "scripts/build_entropy.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    compiler = shutil.which("cc")
    archiver = shutil.which("ar")
    assert compiler and archiver, "Host C compiler and ar are required"
    with tempfile.TemporaryDirectory(prefix="dnd-entropy-build-") as folder:
        scratch = Path(folder)
        source = scratch / "source/jitterentropy"
        (source / "src").mkdir(parents=True)
        (source / "LICENSE").write_text("fixture license\n")
        (source / "UPSTREAM.md").write_text(helper.REVISION + "\n")
        port = source.parent / "dnd_jitterentropy_port.h"
        port.write_text("/* port fixture */\n")
        header = source / "fixture.h"
        header.write_text("#define FIXTURE_VALUE 1\n")
        for index, name in enumerate(helper.SOURCES):
            (source / "src" / name).write_text(
                '#include "fixture.h"\n'
                '#if defined(__OPTIMIZE__) || defined(__LTO__)\n'
                '#error timing-sensitive source optimized\n'
                '#endif\n'
                f'int entropy_fixture_{index}(void) {{ return FIXTURE_VALUE; }}\n')
        commands = scratch / "commands.jsonl"
        wrapper = scratch / "cc-wrapper"
        wrapper.write_text(
            "#!" + sys.executable + "\nimport json,subprocess,sys\n"
            f'with open({str(commands)!r}, "a") as out: out.write(json.dumps(sys.argv[1:])+"\\n")\n'
            f'sys.exit(subprocess.call([{compiler!r}]+sys.argv[1:]))\n')
        wrapper.chmod(0o755)
        output = scratch / "build"

        def build(flags="-O3 -flto=auto -fwhole-program -ffat-lto-objects -fuse-linker-plugin"):
            helper.build(output, source, str(wrapper), archiver, flags)

        def compile_commands():
            return [args for args in map(json.loads, commands.read_text().splitlines()) if "-c" in args]

        build()
        first = compile_commands()
        assert len(first) == len(helper.SOURCES)
        for args in first:
            assert "-O0" in args and "-fno-lto" in args and "-fwrapv" in args
            assert not any(arg.startswith("-O") and arg != "-O0" for arg in args)
            assert not any(arg.startswith("-flto") for arg in args)
            assert "-fwhole-program" not in args and "-fuse-linker-plugin" not in args
        library = output / "libdndentropy.a"
        first_archive = library.read_bytes()
        first_stamp = (output / "configuration.json").read_bytes()
        build()
        assert compile_commands() == first, "Unchanged inputs rebuilt the entropy core"
        build("-O2 -flto")
        assert compile_commands() == first, "Ineffective app optimization switches rebuilt the entropy core"
        header.write_text("#define FIXTURE_VALUE 2\n")
        build()
        assert len(compile_commands()) == 2 * len(first), "Header change did not rebuild entropy core"
        port.write_text("/* port fixture changed */\n")
        build()
        assert len(compile_commands()) == 3 * len(first), "Port change did not rebuild entropy core"
        valid_archive = library.read_bytes()
        valid_stamp = (output / "configuration.json").read_bytes()
        (source / "src" / helper.SOURCES[-1]).write_text("#error intentional build failure\n")
        try:
            build()
        except subprocess.CalledProcessError:
            pass
        else:
            raise AssertionError("Invalid entropy source unexpectedly built")
        assert library.read_bytes() == valid_archive, "Failed build replaced the valid entropy archive"
        assert (output / "configuration.json").read_bytes() == valid_stamp, "Failed build updated the cache stamp"
        assert first_archive and first_stamp
    print("Entropy build isolates -O0/no-LTO, tracks source/port changes and preserves valid output on failure.")


if __name__ == "__main__":
    verify()
