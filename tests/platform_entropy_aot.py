#!/usr/bin/env python3
"""Entropy facade/AOT control-flow tests; no entropy-quality claim or fallback."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parents[1]


def run(*args,env=None):
    print("+"," ".join(map(str,args)),flush=True)
    subprocess.run(list(map(str,args)),cwd=ROOT,check=True,env=env)


def main():
    dotnet=os.environ.get("DOTNET","dotnet")
    run(dotnet,"build","tools/DolphinDotNet.Compiler","-c","Release","-v","quiet","-p:NuGetAudit=false")
    run(dotnet,"build","tests/PlatformEntropySmoke","-c","Release","-v","quiet","-p:NuGetAudit=false")
    compiler=ROOT/"tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll"
    assembly=ROOT/"tests/PlatformEntropySmoke/bin/Release/net8.0/PlatformEntropySmoke.dll"
    with tempfile.TemporaryDirectory(prefix="dnd-entropy-aot-") as directory:
        work=Path(directory);generated=work/"managed.c";bundle=work/"bundle"
        run(dotnet,compiler,"--aot",assembly,generated)
        assert "dnd_platform_entropy_fill_array(" in generated.read_text(), "Missing native entropy mapping"
        run(dotnet,compiler,"--bundle",assembly,bundle)
        makefile=(bundle/"Makefile").read_text()
        for token in ("-DDND_PLATFORM_ENTROPY","-ldndentropy","dnd-entropy:","scripts/build_entropy.py",
                      "$(ENTROPY_LIBRARY)"):
            assert token in makefile, "Missing entropy build integration: "+token
        for path in ("source/platform_entropy.c","source/dnd_jitterentropy_port.h",
                     "include/dnd_platform_entropy.h","scripts/build_entropy.py",
                     "source/jitterentropy/LICENSE","source/jitterentropy/UPSTREAM.md",
                     "source/jitterentropy/src/jitterentropy-noise.c"):
            assert (bundle/path).is_file(), "Missing entropy bundle dependency: "+path
        rejected=subprocess.run([dotnet,str(compiler),"--bundle",str(assembly),str(work/"wii"),"--platform","wii"],
                                cwd=ROOT,capture_output=True,text=True)
        assert rejected.returncode!=0 and "GameCube native target only" in rejected.stderr+rejected.stdout, \
            "Entropy provider was not rejected on an unsupported native target"
        dead_strip="-Wl,-dead_strip" if sys.platform=="darwin" else "-Wl,--gc-sections"
        sources=[generated,"tests/platform_entropy_host.c","source/managed_runtime.c","source/async_runtime.c",
                 "source/numeric.c","source/text.c","source/timing.c"]
        for optimization in ([],["-O3","-flto","-fno-strict-aliasing"]):
            binary=work/"smoke"
            run(os.environ.get("CC","cc"),"-pthread","-std=c11","-D_DEFAULT_SOURCE","-D_DARWIN_C_SOURCE",
                "-Wall","-Wextra","-Werror","-Wno-unused-function","-ffunction-sections","-fdata-sections",
                "-Iinclude",*optimization,*sources,dead_strip,"-lm","-o",binary)
            for manual in (True,False):
                env=dict(os.environ)
                if manual:env["DND_ASYNC_MANUAL"]="1"
                else:env.pop("DND_ASYNC_MANUAL",None)
                run(binary,env=env)
                run(binary,"--gc-stress",env=env)


if __name__=="__main__":main()
