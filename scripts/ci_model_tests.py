#!/usr/bin/env python3
"""Execute model loader, production GX recording and AOT/ARAM host acceptance.

Allocation instrumentation uses separate translation units so Apple's linker
needs no GNU --wrap support. The ordinary GNU wrapper build remains supported.
"""
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def run(*args):
    print("+", " ".join(map(str, args)), flush=True)
    subprocess.run([str(arg) for arg in args], cwd=ROOT, check=True, timeout=600,
                   env={**os.environ, "DND_ASYNC_MANUAL": "1"})


def main(fixtures):
    fixtures = Path(fixtures).resolve()
    work = fixtures / "native-checks"
    work.mkdir(parents=True, exist_ok=True)
    cc = os.environ.get("CC", "cc")
    flags = ["-pthread", "-O1", "-g", "-std=c11", "-Wall", "-Wextra", "-Werror", "-Iinclude"]
    common = ["source/managed_runtime.c", "source/async_runtime.c"]
    model = sorted(str(p.relative_to(ROOT)) for p in (ROOT / "source").glob("model*.c"))
    runtime = common + ["source/storage.c", "source/contiguous_write.c", "source/file_optimize.c"] + model + [
        "source/graphics_device.c", "source/draw_resources.c", "source/graphics_gx.c", "source/performance.c"]
    inflate = work / "model-inflate"
    run(cc, *flags, "tests/model_inflate_test.c", "source/model_ufbx.c", "-lm", "-o", inflate)
    run(inflate)
    native = work / "model-native"
    run(cc, *flags, "tests/model_test.c", *runtime, "-lm", "-o", native)
    run(native, fixtures)

    # Compile the recording fixture normally, and redirect allocations only in
    # production translation units. Its __real_* calls still reach libc.
    gxflags = flags + ["-DDND_MODEL_GX_TEST", "-Itests/model_effect_sdk",
                       "-ffunction-sections", "-fdata-sections"]
    fixture = work / "recording.o"
    run(cc, *gxflags, "-DDND_MODEL_ALLOC_COMPILE_TEST", "-c", "tests/model_effect_test.c", "-o", fixture)
    objects = [fixture]
    for source in common + ["source/model.c", "source/model_animation.c", "source/model_graphics.c",
                            "source/graphics_device.c", "source/draw_resources.c", "source/performance.c"]:
        obj = work / (Path(source).stem + ".o")
        run(cc, *gxflags, "-Dmalloc=__wrap_malloc", "-Dcalloc=__wrap_calloc",
            "-Drealloc=__wrap_realloc", "-Daligned_alloc=__wrap_aligned_alloc",
            "-c", source, "-o", obj)
        objects.append(obj)
    recording = work / "model-recording"
    dead_strip = "-Wl,-dead_strip" if sys.platform == "darwin" else "-Wl,--gc-sections"
    run(cc, *flags, *objects, dead_strip, "-lm", "-o", recording)
    run(recording)

    run("dotnet", "build", "tools/DolphinDotNet.Compiler", "-c", "Release", "-p:DndBuildNative=false")
    run("dotnet", "build", "tests/ModelSmoke", "-c", "Release", "-p:DndBuildNative=false")
    generated = work / "model-smoke.c"
    compiler = ROOT / "tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll"
    run("dotnet", compiler, "--aot", "tests/ModelSmoke/bin/Release/net8.0/ModelSmoke.dll", generated)
    smoke = work / "model-smoke"
    # AOT emits shared static inline helpers that this entry point may not use.
    # Clang diagnoses those even at -O1. Keep the exception confined to generated
    # code; every runtime and host source still compiles with strict warnings.
    generated_object = work / "model-smoke-generated.o"
    run(cc, *flags, "-Wno-unused-function", "-c", generated, "-o", generated_object)
    run(cc, *flags, generated_object, "tests/model_host.c", "source/text.c", "source/aram.c",
        *runtime, "-lm", "-o", smoke)
    run(smoke, fixtures)
    # Smoke mutates cache/stream fixtures; regenerate before the GC-stress run.
    run("dotnet", "run", "--project", "tests/ModelAssetTests", "-c", "Release", "--", fixtures)
    run(smoke, fixtures, "--gc-stress")


if __name__ == "__main__":
    main(sys.argv[1])
