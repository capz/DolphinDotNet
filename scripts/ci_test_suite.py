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
    compiler="tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll"
    name=Path(project).stem
    dll=Path(project).parent/"bin/Release/net8.0"/(name+".dll")
    run("dotnet",compiler,"--aot",dll,output)
    run("cc","-std=c11","-Wall","-Wextra","-Iinclude","-c",output,"-o",str(output)+".o")
def main(area):
    if area=="native":
        try: run("make","-C","tests","test")
        finally: run("make","-C","tests","clean")
    elif area=="compiler":
        test("tools/DolphinDotNet.Compiler.Tests")
        for p in ("NumericSmoke","StringSmoke","WishlistSmoke","ExactNumberSmoke"):
            test("tests/"+p)
        with tempfile.TemporaryDirectory() as tmp:
            for p in ("ControlFlowSmoke","GenericSharingSmoke"):
                aot("tests/"+p+"/"+p+".csproj",Path(tmp)/(p+".c"))
    elif area=="storage":
        for p in ("StorageSmoke","AramSmoke","LibrarySmoke","DiscIdentityTests",
                  "DiscBrowserNavigationTests","ConnectionSmoke","InputSmoke"):
            build("tests/"+p+"/"+p+".csproj")
        test("tests/SampleRecordsTests")
    elif area=="network":
        for p in ("AsyncSmoke","AutomaticAsyncSmoke","AutomaticTaskSmoke",
                  "NetworkSmoke","TimingSmoke","InputContextSmoke"):
            build("tests/"+p+"/"+p+".csproj")
        run("python3","-m","unittest","discover","-s","tests","-p","test_network_fixtures.py")
        test("tests/SampleDownloadTests")
    elif area=="audio":
        test("tests/AudioAssetTests")
        for p in ("AudioFormatsSmoke","PongRulesSmoke","SnakeRulesSmoke",
                  "GreenMenRulesSmoke","FoxRulesSmoke","KartRulesSmoke"):
            build("tests/"+p+"/"+p+".csproj")
    elif area=="graphics":
        test("tests/EffectCompilerTests")
        with tempfile.TemporaryDirectory() as tmp:
            run("dotnet","run","--project","tests/ModelAssetTests","-c","Release","--",tmp)
        for p in ("GraphicsSmoke","ModelSmoke","ModelAsyncSmoke","TevMaterialTests"):
            build("tests/"+p+"/"+p+".csproj")
    elif area=="assets":
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
            for p in ("HelloGameCube","GraphicsSmoke","AramSmoke","NetworkSmoke"):
                aot("tests/"+p+"/"+p+".csproj",Path(tmp)/(p+".c"))
    else: raise ValueError("Unknown framework area: "+area)
if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("area",choices=("native","compiler","storage","network","audio","graphics","assets","sdk","gamecube"))
    main(parser.parse_args().area)
