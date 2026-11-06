#!/usr/bin/env python3
"""Framework-owned CI domain runner. No sample source or sample repository."""
import argparse
import subprocess
import tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def run(*args):
    print("+", " ".join(map(str,args)),flush=True)
    subprocess.run([str(x) for x in args],cwd=ROOT,check=True)
def build(project):
    run("dotnet","build",project,"-c","Release","-p:DndBuildNative=false")
def test(project):
    run("dotnet","run","--project",project,"-c","Release","-p:DndBuildNative=false")
def aot(project,output):
    build(project)
    compiler=ROOT/"tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll"
    name=Path(project).stem
    dll=Path(project).parent/"bin/Release/net8.0"/(name+".dll")
    run("dotnet",compiler,"--aot",dll,output)
    run("cc","-std=c11","-Wall","-Wextra","-Iinclude","-c",output,"-o",str(output)+".o")
def main(area):
    if area=="native":
        run("python3","-m","unittest","discover","-s","tests","-p","test_ci_native.py")
        run("python3","-m","unittest","discover","-s","tests","-p","test_video_blanking.py")
        run("python3","-m","unittest","discover","-s","tests","-p","test_debug_hardware_automation.py")
        run("python3","-m","unittest","discover","-s","tests","-p","test_hardware_asset_corpus.py")
        run("python3","-m","unittest","discover","-s","tests","-p","test_hardware_asset_compatibility.py")
        # The shipped release runtime uses O3/LTO. Execute assertions under
        # those flags too; clean between modes because make does not track CFLAGS.
        for optimization in ("", " -O3 -flto -fno-strict-aliasing"):
            run("make","-C","tests","clean")
            try:
                run("make","-C","tests","test",
                    "CFLAGS=-D_DARWIN_C_SOURCE -std=c11 -Wall -Wextra -Werror -I../include"+optimization)
            finally: run("make","-C","tests","clean")
    elif area=="compiler":
        # Compiler.Tests is independent of the CLI executable; the AOT
        # integration fixtures below need the compiler itself built first.
        build("tools/DolphinDotNet.Compiler/DolphinDotNet.Compiler.csproj")
        build("tests/RefLoadSmoke/RefLoadSmoke.csproj")
        build("tests/LeafSafepointSmoke/LeafSafepointSmoke.csproj")
        test("tools/DolphinDotNet.Compiler.Tests")
        # Numeric and string facade contracts must execute against the
        # GameCube AOT runtime, not locale-sensitive desktop CLR behavior.
        with tempfile.TemporaryDirectory() as tmp:
            for p in ("AotSmoke","ControlFlowSmoke","GenericSharingSmoke"):
                aot("tests/"+p+"/"+p+".csproj",Path(tmp)/(p+".c"))
            for p,host in (("SystemFirmwareSmoke","system_firmware_host.c"),
                           ("NumericSmoke","numeric_host.c"),
                           ("StringSmoke","string_host.c"),
                           ("WishlistSmoke","wishlist_host.c"),
                           ("ExactNumberSmoke","wishlist_host.c")):
                generated=Path(tmp)/(p+".c")
                aot("tests/"+p+"/"+p+".csproj",generated)
                binary=Path(tmp)/p
                run("cc","-pthread","-O1","-std=c11","-Iinclude",generated,
                    "tests/"+host,"source/numeric.c","source/text.c",
                    "source/managed_runtime.c","source/async_runtime.c",
                    "-lm","-o",binary)
                run(binary)
    elif area=="storage":
        for p in ("StorageSmoke","AramSmoke","LibrarySmoke","DiscIdentityTests",
                  "ConnectionSmoke","InputSmoke"):
            build("tests/"+p+"/"+p+".csproj")
    elif area=="network":
        run("python3","scripts/ci_https.py")
        for p in ("AsyncSmoke","AutomaticAsyncSmoke","AutomaticTaskSmoke",
                  "NetworkSmoke","TimingSmoke","InputContextSmoke"):
            build("tests/"+p+"/"+p+".csproj")
        run("python3","-m","unittest","discover","-s","tests","-p","test_network_fixtures.py")
    elif area=="audio":
        test("tests/AudioAssetTests")
        build("tests/AudioFormatsSmoke/AudioFormatsSmoke.csproj")
        with tempfile.TemporaryDirectory() as tmp:
            run("python3","tests/generate_audio_fixtures.py",tmp)
            for source in ("audio_native_test","audio_formats_test"):
                binary=Path(tmp)/source
                run("cc","-D_DARWIN_C_SOURCE","-std=c11","-O1","-Wall","-Wextra","-Werror","-Iinclude",
                    "tests/"+source+".c","source/audio.c","-lm","-o",binary)
                if source=="audio_formats_test":run(binary,tmp)
                else:run(binary)
    elif area=="graphics":
        run("python3","scripts/ci_display.py") # DisplayConfigurationSmoke
        run("python3","scripts/ci_graphics_phase_one.py")
        run("python3","scripts/ci_graphics_phase_two.py")
        run("python3","scripts/ci_mesh_streams.py") # MeshStreamsSmoke (Debug/Release AOT + GC stress)
        build("tests/GraphicsPhaseOneSmoke/GraphicsPhaseOneSmoke.csproj")
        build("tests/GraphicsPhaseTwoSmoke/GraphicsPhaseTwoSmoke.csproj")
        test("tests/TextureStreamingTests")
        test("tests/EffectCompilerTests")
        with tempfile.TemporaryDirectory() as tmp:
            run("dotnet","run","--project","tests/ModelAssetTests","-c","Release","--",tmp)
            run("python3","scripts/ci_model_tests.py",tmp)
        for p in ("GraphicsSmoke","SystemFirmwareSmoke","IplFontSmoke","ModelSmoke","ModelAsyncSmoke","TevMaterialTests"):
            build("tests/"+p+"/"+p+".csproj")
    elif area=="assets":
        run("python3","-m","unittest","discover","-s","tests","-p","test_system_firmware_captures.py")
        test("tests/GameCubeFormatsTests")
        test("tests/SystemFirmwareTests")
        # SystemFirmwareReadSmoke: execute the generated C against the synthetic
        # console/file bridge; building the managed fixture alone misses the ABI.
        run("python3","tests/system_firmware_aot.py")
        run("python3","tests/system_firmware_inventory.py")
        test("tests/JSystemFixtureHostTests")
        run("python3","-m","unittest","discover","-s","tests","-p","test_jsystem_hardware_regression.py")
        test("tests/HardwareAssetCompatibilityPolicyTests")
        run("python3", "tests/gcft_cli_smoke.py")
        test("tests/SaveImageTests")
        test("tests/EffectCompilerTests")
        build("tools/DolphinDotNet.Imaging/DolphinDotNet.Imaging.csproj")
    elif area=="sdk":
        run("python3","eng/pack.py","--include-tools")
        with tempfile.TemporaryDirectory() as tmp:
            run("python3","tests/sdk_consumer.py","--workspace",Path(tmp)/"consumer")
    elif area=="gamecube":
        # Host compiler checks verify AOT/ABI from self-contained fixture projects.
        # Hardware/emulator execution belongs in independent console acceptance.
        build("tools/DolphinDotNet.Compiler")
        with tempfile.TemporaryDirectory() as tmp:
            for p in ("JSystemHardwareFixture","SystemFirmwareHardwareSmoke","HelloGameCube","GraphicsSmoke","IplFontSmoke","AramSmoke","NetworkSmoke",
                      "ControllerDolphinSmoke","DolLauncherSmoke",
                      "DebuggerStackSmoke","DebuggerInspectionSmoke","RetailAssetCorpus","HardwareAssetCompatibility"):
                aot("tests/"+p+"/"+p+".csproj",Path(tmp)/(p+".c"))
    else: raise ValueError("Unknown framework area: "+area)
if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("area",choices=("native","compiler","storage","network","audio","graphics","assets","sdk","gamecube"))
    main(parser.parse_args().area)
