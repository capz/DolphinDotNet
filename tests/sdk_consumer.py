#!/usr/bin/env python3
"""Exercise actual nupkgs with a fresh cache and external projects (no upload)."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--feed', type=Path, default=ROOT / 'out/packages')
    parser.add_argument('--workspace', type=Path)
    parser.add_argument('--native', action='store_true', help='Also build an external Debug GameCube DOL/ELF')
    args = parser.parse_args()
    work = args.workspace or Path(tempfile.mkdtemp(prefix='dnd external consumer '))
    work = work.resolve()
    if work.exists() and any(work.iterdir()):
        parser.error('--workspace must be empty; consumer tests never overwrite an existing workspace')
    work.mkdir(parents=True, exist_ok=True)
    feed = work / 'relocated feed'
    feed.mkdir(exist_ok=True)
    for package in args.feed.glob('*.nupkg'):
        shutil.copy2(package, feed)
    sdk_packages = list(feed.glob('DolphinDotNet.Sdk.*.nupkg'))
    if len(sdk_packages) != 1:
        parser.error('--feed must contain exactly one SDK version')
    sdk_package = sdk_packages[0]
    with ZipFile(sdk_package) as archive:
        import xml.etree.ElementTree as ET
        metadata = ET.fromstring(archive.read('DolphinDotNet.Sdk.nuspec'))
        version = next(e.text for e in metadata.iter() if e.tag.endswith('}version') or e.tag == 'version')
        for required in ('Sdk/Sdk.props', 'tools/compiler/native/source/main.c', 'tools/build/DolphinDotNet.Build.dll', 'tools/debugger/DolphinDotNet.DebugAdapter.dll'):
            assert required in archive.namelist(), required
    env = os.environ.copy()
    env.update(NUGET_PACKAGES=str(work / 'packages'), DOTNET_CLI_HOME=str(work / 'cli'), DOTNET_CLI_TELEMETRY_OPTOUT='1')
    def run(*command, cwd=work, success=True):
        result = subprocess.run(command, cwd=cwd, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        print(result.stdout, flush=True)
        if (result.returncode == 0) != success:
            raise RuntimeError(f'Unexpected exit {result.returncode}: {command}')
        return result.stdout
    def write(path, content):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content)
    from xml.sax.saxutils import escape
    write(work / 'NuGet.Config', f'<configuration><packageSources><clear/><add key="local" value="{escape(str(feed))}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    run('dotnet', 'new', 'install', str(next(feed.glob('DolphinDotNet.Templates.*.nupkg'))))
    if list(feed.glob('DolphinDotNet.Assets.*.nupkg')):
        run('dotnet', 'new', 'tool-manifest')
        for package, command in (('DolphinDotNet.Assets', 'dnd-assets'), ('DolphinDotNet.SaveImage', 'dnd-save-image')):
            run('dotnet', 'tool', 'install', package, '--version', version)
            run('dotnet', 'tool', 'run', command, '--', '--help')
    for template in ('gamecube', 'gamecube-game', 'gamecube-lib'):
        project = work / template
        run('dotnet', 'new', template, '-o', str(project))
        run('dotnet', 'build', str(project), *(['-p:DndBuildNative=false'] if template != 'gamecube-lib' else []))
        # Design-time evaluation must not invoke Docker or compile native code.
        run('dotnet', 'msbuild', str(next(project.glob('*.csproj'))), '-t:BuildDol', '-p:DesignTimeBuild=true')
        assert not list(project.rglob('*.dol'))
    effects = work / 'ExternalEffects'
    effects.mkdir()
    shutil.copy2(ROOT / 'samples/ShaderDemo/toon.dndfx.json', effects)
    write(effects / 'ExternalEffects.csproj', f'<Project Sdk="DolphinDotNet.Sdk/{version}"><ItemGroup><PackageReference Include="DolphinDotNet.GameCube" Version="{version}" /><DndEffect Include="toon.dndfx.json" GeneratedSource="CompiledEffect.g.cs" ClassName="CompiledEffect" /></ItemGroup></Project>')
    write(effects / 'Program.cs', 'internal static class Program { static int Main() => 0; }')
    run('dotnet', 'build', str(effects), '-p:DndBuildNative=false')
    assert (effects / 'CompiledEffect.g.cs').stat().st_size > 0
    # Real NuGet dependency closure: application -> First -> Second. Neither
    # package's assembly name starts with DolphinDotNet.
    for name, body, dependency in (
        ('Second', 'public static class Leaf { public static int Value() => 41; }', ''),
        ('First', 'public static class Middle { public static int Value() => Leaf.Value() + 1; }', '<PackageReference Include="Consumer.Second" Version="1.0.0" />')):
        project = work / name
        write(project / f'{name}.csproj', f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><PackageId>Consumer.{name}</PackageId></PropertyGroup><ItemGroup>{dependency}</ItemGroup></Project>')
        write(project / 'Logic.cs', body)
        run('dotnet', 'pack', str(project), '-o', str(feed))
    game = work / 'gamecube'
    csproj = next(game.glob('*.csproj'))
    text = csproj.read_text().replace('</ItemGroup>', '<PackageReference Include="Consumer.First" Version="1.0.0" /></ItemGroup>')
    csproj.write_text(text)
    write(game / 'Program.cs', 'internal static class Program { static int Main() => Middle.Value(); }')
    run('dotnet', 'build', str(game), '-c', 'Debug', '-p:DndBuildNative=false')
    output = game / 'bin/Debug/net8.0'
    compiler = work / 'packages/dolphindotnet.sdk' / version / 'tools/compiler/DolphinDotNet.Compiler.dll'
    dll = output / (csproj.stem + '.dll')
    run('dotnet', str(compiler), '--bundle', str(dll), str(work / 'bundle'), '--debug-symbols')
    generated = (work / 'bundle/source/generated_program.c').read_text()
    assert 'Middle' in generated and 'Leaf' in generated
    write(work / 'host.c', '#include "dnd_managed.h"\n#include <assert.h>\nextern intptr_t dnd_aot_entry(DndManagedHeap*);\nint main(void) { unsigned char memory[65536]; DndManagedHeap heap; dnd_managed_heap_init(&heap,memory,sizeof memory); assert(dnd_aot_entry(&heap)==42); return 0; }')
    run('dotnet', str(compiler), '--aot', str(dll), str(work / 'host-program.c'))
    native = compiler.parent / 'native'
    run('cc', '-pthread', '-std=c11', '-I' + str(native / 'include'), str(work / 'host-program.c'), str(work / 'host.c'), str(native / 'source/managed_runtime.c'), str(native / 'source/async_runtime.c'), str(native / 'source/numeric.c'), str(native / 'source/text.c'), '-lm', '-o', str(work / 'host'))
    run(str(work / 'host'))
    assert 'Consumer.Second.dll' in {p.name for p in output.iterdir()} or (output / 'Second.dll').exists()
    # The diagnostic must identify the missing transitive implementation.
    leaf = output / 'Second.dll'
    leaf.rename(leaf.with_suffix('.hidden'))
    message = run('dotnet', str(compiler), '--aot', str(dll), str(work / 'bad.c'), success=False)
    assert 'DND1001' in message and 'Second' in message
    leaf.with_suffix('.hidden').rename(leaf)
    write(game / 'Program.cs', 'internal static class Program { static int Main() { System.Console.Beep(); return 0; } }')
    run('dotnet', 'build', str(game), '-p:DndBuildNative=false')
    release_dll = game / 'bin/Debug/net8.0' / (csproj.stem + '.dll')
    message = run('dotnet', str(compiler), '--aot', str(release_dll), str(work / 'unsupported.c'), success=False)
    assert 'DND1003' in message and 'Beep' in message and 'IL_' in message
    write(game / 'Program.cs', 'internal static class Program { static int Main() => Middle.Value(); } public static class Leaf { public static int Value() => 0; }')
    run('dotnet', 'build', str(game), '-p:DndBuildNative=false')
    message = run('dotnet', str(compiler), '--aot', str(dll), str(work / 'duplicate.c'), success=False)
    assert 'DND1002' in message and 'Leaf' in message
    if args.native:
        # External Pong retains onboarding smoke probes while exercising only
        # package SDK/API references and a relocated NuGet cache.
        pong = work / 'ExternalPong'
        pong.mkdir(exist_ok=True)
        for source in (ROOT / 'samples/Pong').glob('*.cs'):
            shutil.copy2(source, pong)
        for source in (ROOT / 'samples/Common').glob('*.cs'):
            shutil.copy2(source, pong)
        write(pong / 'ExternalPong.csproj', f'<Project Sdk="DolphinDotNet.Sdk/{version}"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="DolphinDotNet.GameCube" Version="{version}" /></ItemGroup></Project>')
        run('dotnet', 'build', str(pong), '-c', 'Debug', '-p:DndBuildBackend=Docker')
        native_output = pong / 'bin/Debug/net8.0'
        for suffix in ('dol', 'elf', 'dnd-debug.json', 'dnd-addresses.json'):
            assert (native_output / f'ExternalPong.{suffix}').stat().st_size > 0
        # Validate launch configuration without starting an interactive emulator.
        config = json.loads((native_output / 'dolphin-run.json').read_text())
        assert Path(config['Dol']).is_file()
        if os.name != 'nt':
            fake = work / 'fake dolphin'
            write(fake, '#!/bin/sh\nprintf "%s\\n" "$@" > "$DND_TEST_LAUNCH_LOG"\n')
            env['DND_TEST_LAUNCH_LOG'] = str(work / 'launch-args.txt')
            fake.chmod(0o755)
            driver = native_output / 'DolphinDotNet.Build.dll'
            run('dotnet', str(driver), 'run', '--config', str(native_output / 'dolphin-run.json'), '--dolphin', str(fake), '--gdb-port', '2166')
            assert str(native_output / 'ExternalPong.dol') in (work / 'launch-args.txt').read_text()
            ini = Path(config['UserDirectory']) / 'rider-debug/Config/Dolphin.ini'
            assert 'GDBPort = 2166' in ini.read_text() and 'CPUThread = False' in ini.read_text()
    print(f'External SDK consumer tests passed: {work}', flush=True)

if __name__ == '__main__':
    main()
