#!/usr/bin/env python3
"""Build the managed DOL-argv caller against this explicit source SDK."""
import argparse,subprocess,xml.etree.ElementTree as ET
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--target',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
sdk=Path(__file__).resolve().parents[2];out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
data=a.target.read_bytes()
if len(data)>65536:raise ValueError('Keep the native test target below the managed heap budget')
(out/'Payload.g.cs').write_text('internal static class ArgvPayload { internal static readonly byte[] Data=new byte[]{'+','.join(str(b) for b in data)+'};}\n')
root=ET.Element('Project');g=ET.SubElement(root,'PropertyGroup');ET.SubElement(g,'ImportDirectoryBuildProps').text='false'
ET.SubElement(root,'Import',Project=str(sdk/'sdk/DolphinDotNet.Sdk/Sdk/Sdk.props'))
g=ET.SubElement(root,'PropertyGroup')
for key,value in {'TargetFramework':'net8.0','OutputType':'Exe','AssemblyName':'DolArgumentsSmoke','EnableDefaultCompileItems':'false','DndBuildNative':'true','DndBuildBackend':'Docker'}.items():ET.SubElement(g,key).text=value
items=ET.SubElement(root,'ItemGroup');ET.SubElement(items,'Compile',Include=str(sdk/'tests/DolArgumentsSmoke/Program.cs'));ET.SubElement(items,'Compile',Include=str(out/'Payload.g.cs'));ET.SubElement(items,'ProjectReference',Include=str(sdk/'src/DolphinDotNet.GameCube/DolphinDotNet.GameCube.csproj'))
ET.SubElement(root,'Import',Project=str(sdk/'sdk/DolphinDotNet.Sdk/Sdk/Sdk.targets'))
ET.indent(root);project=out/'DolArgumentsSmoke.csproj';ET.ElementTree(root).write(project,encoding='unicode')
subprocess.run(['dotnet','build',str(project),'-c','Release'],check=True)
