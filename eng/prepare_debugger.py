#!/usr/bin/env python3
"""Fetch the reviewed debugger source dependency; never changes a user's checkout."""
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def prepare():
    config = json.loads((ROOT / 'eng/debugger-source.json').read_text())
    revision = config['revision']
    if not re.fullmatch(r'[0-9a-f]{40}', revision):
        raise ValueError('Debugger revision must be a full reviewed Git commit SHA')
    target = ROOT / 'out/debuggers'
    def git(*args, capture=False):
        return subprocess.run(['git', '-C', str(target), *args], check=True,
                              text=True, stdout=subprocess.PIPE if capture else None).stdout
    if target.exists():
        if not (target / '.git').is_dir():
            raise RuntimeError(f'Refusing to overwrite a non-Git dependency directory: {target}')
        if git('remote', 'get-url', 'origin', capture=True).strip() != config['repository']:
            raise RuntimeError('Debugger dependency origin differs from the reviewed manifest')
        if git('status', '--porcelain', capture=True).strip():
            raise RuntimeError('Debugger dependency has local changes; preserve them before updating the pin')
        current = subprocess.run(['git', '-C', str(target), 'rev-parse', '--verify', 'HEAD'],
                                 text=True, capture_output=True)
        if current.returncode == 0 and current.stdout.strip() == revision:
            return target
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', 'init', str(target)], check=True)
        git('remote', 'add', 'origin', config['repository'])
    git('fetch', '--depth', '1', 'origin', revision)
    git('checkout', '--detach', revision)
    if git('rev-parse', 'HEAD', capture=True).strip() != revision:
        raise RuntimeError('Debugger checkout does not match the reviewed commit')
    return target

if __name__ == '__main__':
    print(prepare())
