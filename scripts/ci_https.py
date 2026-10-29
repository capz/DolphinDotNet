#!/usr/bin/env python3
"""Execute SDK HTTPS through AOT, native TLS, real sockets and local certificates."""
import importlib.util
import json
import os
from pathlib import Path
import socket
import ssl
import subprocess
import sys
import tempfile
import threading
import time
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tests'))
from https_fixtures import certificates, main as native_acceptance
from network_fixtures import dns_udp, ready
sys.path.insert(0,str(ROOT/'scripts'))
from build_tls import build

def run(*args,**kwargs):subprocess.run(list(map(str,args)),cwd=ROOT,check=True,**kwargs)

def main():
    run('dotnet','build','tools/DolphinDotNet.Compiler','-c','Release')
    with tempfile.TemporaryDirectory(prefix='dnd-https-') as temporary:
        folder=Path(temporary);certificates(folder)
        sanitize=['-fsanitize=address,undefined','-fno-omit-frame-pointer'] if os.environ.get('DND_HTTPS_SANITIZE')=='1' else []
        native=folder/'tls';build(native,'cc','ar',' '.join(['-O2',*sanitize]),ROOT/'include/dnd_tls_config.h',os.environ.get('DND_MBEDTLS_SOURCE'))
        source=Path(os.environ.get('DND_MBEDTLS_SOURCE',native/'source'))
        flags=['-std=c11','-pthread','-O1','-DDND_TLS','-Iinclude','-I'+str(source/'include'),'-ffunction-sections','-fdata-sections',*sanitize]
        dead='-Wl,-dead_strip' if sys.platform=='darwin' else '-Wl,--gc-sections'
        sources=['source/network.c','source/network_posix.c','source/tls.c','source/timing.c']
        client=folder/'native-client'
        run('cc',*flags,'tests/tls_client_test.c',*sources,native/'libdndtls.a',dead,'-o',client)
        native_acceptance([str(client)])
        configuration_test=folder/'tls-config'
        run('cc',*flags,'tests/tls_configuration_test.c',native/'libdndtls.a',dead,'-o',configuration_test)
        run(configuration_test,folder/'ca.pem',folder/'entropy.bin')
        threading.Thread(target=dns_udp,daemon=True).start()
        if not ready[0].wait(5):raise RuntimeError('DNS fixture failed')
        context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);context.maximum_version=ssl.TLSVersion.TLSv1_2;context.load_cert_chain(folder/'server.pem',folder/'server.key')
        listeners=[]
        for _ in range(2):
            listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(8);listeners.append(listener)
        ports=[s.getsockname()[1] for s in listeners]
        errors=[]
        def peer(connection,index):
            try:
                connection.settimeout(10)
                with context.wrap_socket(connection,server_side=True) as stream:
                    request=b''
                    while b'\r\n\r\n' not in request:
                        part=stream.recv(4096)
                        if not part:return
                        request+=part
                    path=request.split(b' ')[1]
                    if index==1 and b'authorization:' in request.lower():errors.append('credentials crossed origins')
                    if path==b'/relative':reply=b'HTTP/1.1 302 Found\r\nLocation: /final\r\nContent-Length: 0\r\n\r\n'
                    elif path==b'/network':reply=f'HTTP/1.1 302 Found\r\nLocation: //localhost:{ports[0]}/final\r\nContent-Length: 0\r\n\r\n'.encode()
                    elif path==b'/cross':reply=f'HTTP/1.1 302 Found\r\nLocation: https://localhost:{ports[1]}/final\r\nContent-Length: 0\r\n\r\n'.encode()
                    elif path==b'/downgrade':reply=f'HTTP/1.1 302 Found\r\nLocation: http://localhost:{ports[0]}/final\r\nContent-Length: 0\r\n\r\n'.encode()
                    elif path==b'/truncated':reply=b'HTTP/1.1 200 OK\r\nContent-Length: 50\r\n\r\nshort'
                    elif path==b'/chunked':reply=b'HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n1\r\nO\r\n1\r\nK\r\n0\r\n\r\n'
                    else:reply=b'HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK'
                    for byte in reply:stream.sendall(bytes([byte]))
            except (ssl.SSLError,ConnectionError,TimeoutError,OSError):pass
        def serve(listener,index):
            while True:
                try:connection,_=listener.accept()
                except OSError:return
                threading.Thread(target=peer,args=(connection,index),daemon=True).start()
        for i,listener in enumerate(listeners):threading.Thread(target=serve,args=(listener,i),daemon=True).start()
        ca=json.dumps((folder/'ca.pem').read_text());entropy=','.join(map(str,os.urandom(48)));utc=int(time.time())
        project=folder/'HttpsSmoke.csproj'
        project.write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="{ROOT}/src/DolphinDotNet.Core/DolphinDotNet.Core.csproj" /></ItemGroup></Project>')
        (folder/'Program.cs').write_text('''using System;using System.IO;using System.Net.Http;using System.Threading.Tasks;
using Dolphin.Network;using Scheduler=Dolphin.Threading.Tasks.Scheduler;
class Program {
static void Wait(Task task){for(int i=0;i<2000000&&!task.IsCompleted;i++){NetworkRuntime.Pump();Scheduler.Pump(16);}task.GetAwaiter().GetResult();}
static int Main(){try{
Wait(NetworkRuntime.StartAsync(new NetworkOptions{DnsServers=new[]{Dolphin.Network.IPAddress.Loopback},DnsPort=15353},default));
var ca=Dolphin.Text.Encoding.ASCII.GetBytes(CA);var seed=new byte[]{ENTROPY};TlsConfiguration.Initialize(ca,seed,UTC);
for(int i=0;i<seed.Length;i++)if(seed[i]!=0)return 1;
using(var client=new HttpClient()){
client.DefaultRequestHeaders.Add("authorization","test-secret");
foreach(string path in new[]{"/final","/relative","/network","/cross","/chunked"}){var task=client.GetStringAsync(URL+path);Wait(task);if(task.GetAwaiter().GetResult()!="OK")return 2;}
try{Wait(client.GetStringAsync(URL+"/downgrade"));return 3;}catch(IOException){}
var streamTask=client.GetStreamAsync(URL+"/chunked");Wait(streamTask);using(var stream=streamTask.GetAwaiter().GetResult()){var b=new byte[8];int total=0;while(true){var r=stream.ReadAsync(b,0,b.Length);Wait(r);int n=r.GetAwaiter().GetResult();if(n==0)break;total+=n;}if(total!=2)return 4;}
try{Wait(client.GetStringAsync(WRONG));return 5;}catch(IOException){}
try{Wait(client.GetStringAsync(URL+"/truncated"));return 7;}catch(Exception){}
var cancelled=new System.Threading.CancellationTokenSource();cancelled.Cancel();try{Wait(client.GetAsync(URL+"/final",cancelled.Token));return 8;}catch(OperationCanceledException){}
}
TlsConfiguration.Reset();using(var client=new HttpClient()){try{Wait(client.GetStringAsync(URL+"/final"));return 6;}catch(IOException){}}
NetworkRuntime.Stop();return 0;
}catch(Exception e){Console.WriteLine(e.Message);return 99;}}
}'''.replace('CA',ca).replace('ENTROPY',entropy).replace('UTC',str(utc)).replace('URL',json.dumps(f'https://localhost:{ports[0]}')).replace('WRONG',json.dumps(f'https://wrong.example:{ports[0]}/final')))
        try:
            for configuration in ('Debug','Release'):
                run('dotnet','build',project,'-c',configuration)
                generated=folder/(configuration+'.c');binary=folder/configuration
                run('dotnet','tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--aot',folder/f'bin/{configuration}/net8.0/HttpsSmoke.dll',generated)
                bundle=folder/(configuration+'-bundle')
                run('dotnet','tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll','--bundle',folder/f'bin/{configuration}/net8.0/HttpsSmoke.dll',bundle)
                makefile=(bundle/'Makefile').read_text()
                assert '\tpython3 scripts/build_tls.py' in makefile and '-DDND_TLS' in makefile and '-ldndtls' in makefile
                assert (bundle/'source/tls.c').is_file() and (bundle/'include/dnd_tls_config.h').is_file() and (bundle/'scripts/build_tls.py').is_file()
                run('cc',*flags,generated,'tests/network_host.c',*sources,'source/managed_runtime.c','source/async_runtime.c','source/numeric.c','source/text.c',native/'libdndtls.a',dead,'-lm','-o',binary)
                run(binary,env={**os.environ,'DND_ASYNC_MANUAL':'1'},timeout=60)
                run(binary,'--gc-stress',env={**os.environ,'DND_ASYNC_MANUAL':'1'},timeout=60)
            if errors:raise AssertionError(errors)
        finally:
            for listener in listeners:listener.close()
        print('SDK HTTPS Debug/Release AOT + GC stress: verified buffered/streamed/chunked bodies, relative redirects, credential isolation, downgrade rejection and missing configuration passed.')
if __name__=='__main__':main()
