#!/usr/bin/env python3
"""Build a local feed; never uploads packages. Run with .NET 8 on PATH."""
import argparse
import pathlib
import re
import subprocess
import shutil
import tempfile
import xml.etree.ElementTree as ET
from prepare_debugger import prepare

ROOT = pathlib.Path(__file__).resolve().parents[1]
DEFAULT_VERSION = ET.parse(ROOT / 'eng/Package.props').findtext('.//Version')

def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default=DEFAULT_VERSION)
    parser.add_argument('--output', default=str(ROOT / 'out/packages'))
    parser.add_argument('--include-tools', action='store_true', help='Also pack optional dnd-assets and dnd-save-image local/global tools')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', args.version):
        parser.error('version must be a three-part NuGet version, optionally with a prerelease suffix')
    output = pathlib.Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    debugger = prepare()
    run('dotnet', 'restore', str(debugger / 'tools/DolphinDotNet.DebugAdapter'))
    for tool in ('Compiler', 'Build', 'Effects'):
        run('dotnet', 'restore', f'tools/DolphinDotNet.{tool}')
    for project in ('src/DolphinDotNet.Core', 'src/DolphinDotNet.GameCube', 'sdk/DolphinDotNet.Sdk'):
        run('dotnet', 'pack', project, '-c', 'Release', '-o', str(output), f'-p:Version={args.version}')
    with tempfile.TemporaryDirectory(prefix='dnd-templates-') as temporary:
        staging = pathlib.Path(temporary)
        shutil.copytree(ROOT / 'templates', staging / 'templates', ignore=shutil.ignore_patterns('obj', 'bin'))
        shutil.copytree(ROOT / 'eng', staging / 'eng', ignore=shutil.ignore_patterns('__pycache__'))
        shutil.copy2(ROOT / 'README.md', staging / 'README.md')
        shutil.copy2(ROOT / 'LICENSE', staging / 'LICENSE')
        for project in (staging / 'templates/content').glob('*/*.csproj'):
            project.write_text(project.read_text().replace(DEFAULT_VERSION, args.version))
        run('dotnet', 'pack', str(staging / 'templates'), '-c', 'Release', '-o', str(output), f'-p:Version={args.version}')
    if args.include_tools:
        for tool in ('Assets', 'SaveImage'):
            run('dotnet', 'pack', f'tools/DolphinDotNet.{tool}', '-c', 'Release', '-o', str(output), f'-p:Version={args.version}')
    print(f'Local packages: {output}')

if __name__ == '__main__':
    main()
