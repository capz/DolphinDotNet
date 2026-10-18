#!/usr/bin/env python3
"""Validate the installable VSIX includes its engine, command and isolated adapter runtime."""
from pathlib import Path
import sys
from zipfile import ZipFile
import xml.etree.ElementTree as ET


def verify(path):
    with ZipFile(path) as archive:
        names=set(archive.namelist())
        for required in ('extension.vsixmanifest','AdapterRegistration.pkgdef','DolphinDotNet.VisualStudio.dll',
                         'adapter/DolphinDotNet.DebugAdapter.exe','adapter/DolphinDotNet.DebugAdapter.dll',
                         'adapter/DolphinDotNet.DebugAdapter.runtimeconfig.json','adapter/DolphinDotNet.DebugAdapter.deps.json',
                         'adapter/DolphinDotNet.Debugger.dll','adapter/System.IO.Ports.dll','adapter/coreclr.dll'):
            if required not in names:raise ValueError('Missing VSIX file: '+required)
        registration=archive.read('AdapterRegistration.pkgdef').decode('utf-8-sig')
        for required in ('{CD5691AC-E370-44AB-94CC-5940CD02A820}','{DAB324E9-7B35-454C-ACA8-F6BB0D5C8673}',
                         '$PackageFolder$\\adapter\\DolphinDotNet.DebugAdapter.exe','"LocalsScopeName"="Locals"'):
            if required not in registration:raise ValueError('Missing adapter registration: '+required)
        manifest=ET.fromstring(archive.read('extension.vsixmanifest'))
        ns={'v':'http://schemas.microsoft.com/developer/vsx-schema/2011'}
        target=manifest.find('v:Installation/v:InstallationTarget',ns)
        if target is None or target.attrib.get('Version')!='[17.8,18.0)':raise ValueError('Unexpected VS support range')
        if 'System.Private.CoreLib.dll' in names:raise ValueError('Adapter runtime leaked into VS extension assembly directory')
        print(f'{Path(path).name}: VSIX engine, package and self-contained Windows adapter verified')


if __name__=='__main__':verify(sys.argv[1])
