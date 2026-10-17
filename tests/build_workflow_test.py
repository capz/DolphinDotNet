"""Cross-platform integration tests for the real driver and MSBuild SDK.

The probe records launch arguments; actual DOL compilation/boot stays in the
GameCube CI job. No installed Dolphin, Docker daemon or console SDK is required.
"""
import json
import os
from pathlib import Path
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[1]
driver = root / "tools/DolphinDotNet.Build/bin/Debug/net8.0/DolphinDotNet.Build.dll"
probe_output = root / "tests/DolphinLaunchProbe/bin/Debug/net8.0"
dotnet = shutil.which("dotnet")
assert dotnet and driver.exists()


def call(*args, env=None, expected=0):
    result = subprocess.run([dotnet, str(driver), *map(str, args)], env=env,
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
    assert result.returncode == expected, result.stdout
    return result.stdout


with tempfile.TemporaryDirectory(prefix="dnd workflow & ") as temp:
    work = Path(temp)
    probe_dir = work / "tools with spaces"
    shutil.copytree(probe_output, probe_dir)
    probe = probe_dir / ("DolphinLaunchProbe.exe" if os.name == "nt" else "DolphinLaunchProbe")
    assert probe.exists()
    dol = work / "Pong & sample.dol"
    dol.write_bytes(bytes(256))  # The launcher test does not execute console code.
    config = work / "run config.json"
    user = work / "user settings"
    capture = work / "capture.json"
    env = {**os.environ, "DND_PROBE_OUTPUT": str(capture)}
    env.pop("DOLPHIN_PATH", None)
    config.write_text(json.dumps({"Dol": str(dol), "Dolphin": str(probe), "UserDirectory": str(user)}))
    call("run", "--config", config, env=env)
    assert json.loads(capture.read_text())["Arguments"] == ["--user", str(user), "--exec", str(dol)]
    ini = user / "Config/Dolphin.ini"
    assert "SIDevice1 = 6" in ini.read_text()
    ini.write_text("[Core]\nSIDevice1 = 0\n")
    call("run", "--config", config, env={**env, "DND_PROBE_EXIT": "17"}, expected=17)
    assert "SIDevice1 = 0" in ini.read_text()  # Never overwrite personal settings.
    capture.unlink()
    output = call("run", "--config", config, "--dolphin", work / "missing", env=env, expected=1)
    assert "DND001" in output and not capture.exists()
    config.write_text(json.dumps({"Dol": str(dol), "Dolphin": "", "UserDirectory": str(user)}))
    call("run", "--config", config, env={**env, "DOLPHIN_PATH": str(probe)})
    # Missing output must never invoke the emulator, even if a profile exists.
    capture.unlink()
    dol.unlink()
    output = call("run", "--config", config, env=env, expected=1)
    assert "Build the project" in output and not capture.exists()
    dol.write_bytes(bytes(256))
    if os.name != "nt":
        # Graceful console cancellation kills only the child process we launched.
        process = subprocess.Popen([dotnet, str(driver), "run", "--config", str(config)],
                                   env={**env, "DOLPHIN_PATH": str(probe), "DND_PROBE_WAIT": "true"},
                                   stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        try:
            deadline = time.monotonic() + 10
            pid = None
            while time.monotonic() < deadline:
                try:
                    pid = json.loads(capture.read_text())["Pid"]
                    break
                except (FileNotFoundError, json.JSONDecodeError):
                    time.sleep(.05)
            assert pid is not None, "Probe did not launch"
            process.send_signal(signal.SIGINT)
            process.wait(timeout=10)
            try:
                os.kill(pid, 0)
                raise AssertionError("Dolphin child survived cancellation")
            except ProcessLookupError:
                pass
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()
    output = call("doctor", "--backend", "Invalid", expected=1)
    assert "Auto, Native or Docker" in output
    # Preflight failure removes stale artifacts rather than publishing old code.
    assembly = work / "input.dll"
    assembly.write_bytes(b"unused")
    elf = work / "Pong.elf"
    elf.write_bytes(b"old output")
    debug_map = work / "Pong.dnd-debug.json"
    debug_map.write_text('{"stale": true}')
    output = call("build", "--assembly", assembly, "--compiler", assembly,
                  "--native-directory", work / "obj/native", "--dol", dol, "--elf", elf,
                  "--run-config", config, "--backend", "Invalid", expected=1)
    assert not dol.exists() and not elf.exists() and not config.exists() and not debug_map.exists()
    output = call("build", "--assembly", assembly, "--compiler", assembly,
                  "--native-directory", work / "fresh/native", "--dol", work / "fresh/Pong.dol",
                  "--elf", work / "fresh/Pong.elf", "--run-config", work / "fresh/run.json",
                  "--backend", "Invalid", expected=1)
    assert "DndBuildBackend must be" in output
    # Real driver + fake compiler/container: read-only Git metadata must be
    # removed, and failed cleanup must retain ownership for the next attempt.
    tool_dir = work / "fake toolchain"
    shutil.copytree(probe_output, tool_dir)
    tool_app = tool_dir / ("DolphinLaunchProbe.exe" if os.name == "nt" else "DolphinLaunchProbe")
    docker = tool_dir / ("docker.exe" if os.name == "nt" else "docker")
    shutil.copy2(tool_app, docker)
    if os.name == "nt":
        (tool_dir / "docker").write_text("#!/bin/sh\nexit 99\n")
        # Even an earlier PATH directory containing only a shell script is ignored.
        shims = work / "shell shims"
        shims.mkdir()
        (shims / "docker").write_text("#!/bin/sh\nexit 99\n")
    else:
        shims = tool_dir
    tool_env = {**env, "DND_PROBE_TOOLCHAIN": "true",
                "PATH": os.pathsep.join((str(shims), str(tool_dir), os.environ.get("PATH", "")))}
    native = work / "obj/native"
    native.mkdir(parents=True)
    marker = native / ".dnd-build-directory"
    marker.write_text("DolphinDotNet generated build directory\n")
    git_metadata = native / "dependency/.git/objects/info"
    git_metadata.mkdir(parents=True)
    chain = git_metadata / "commit-graph-chain"
    chain.write_text("old Git metadata")
    chain.chmod(stat.S_IREAD)
    if os.name != "nt":
        outside = work / "outside native"
        outside.mkdir()
        (outside / "keep.txt").write_text("external")
        (native / "linked dependency").symlink_to(outside, target_is_directory=True)
    build_args = ("build", "--assembly", assembly, "--compiler", tool_dir / "DolphinLaunchProbe.dll",
                  "--native-directory", native, "--dol", dol, "--elf", elf,
                  "--run-config", config, "--backend", "Docker", "--debug-symbols", "true")
    call(*build_args, env=tool_env)
    assert not git_metadata.exists() and marker.exists()
    if os.name != "nt":
        assert (outside / "keep.txt").read_text() == "external"
    assert dol.exists() and elf.exists() and debug_map.exists()
    assert (work / "Pong.dnd-addresses.json").exists()

    locked = native / "locked"
    locked.mkdir()
    locked_file = locked / "commit-graph-chain"
    locked_file.write_text("locked metadata")
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        create = ctypes.windll.kernel32.CreateFileW
        create.argtypes = (wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                           wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE)
        create.restype = wintypes.HANDLE
        close = ctypes.windll.kernel32.CloseHandle
        close.argtypes = (wintypes.HANDLE,)
        handle = create(str(locked_file), 0x80000000, 1, None, 3, 0, None)
        assert handle != wintypes.HANDLE(-1).value
        try:
            call(*build_args, env=tool_env, expected=1)
            assert marker.exists() and locked_file.exists()
        finally:
            close(handle)
    elif os.geteuid() != 0:
        locked.chmod(0o500)
        try:
            call(*build_args, env=tool_env, expected=1)
            assert marker.exists() and locked_file.exists()
        finally:
            locked.chmod(0o700)
    call(*build_args, env=tool_env)
    assert marker.exists() and not locked.exists(), "Cleanup must be retryable"
    # Preserve the guard against deleting arbitrary, unowned directories.
    marker.unlink()
    sentinel = native / "keep.txt"
    sentinel.write_text("not owned")
    output = call(*build_args, env=tool_env, expected=1)
    assert "unowned native work directory" in output and sentinel.exists()
    # Explicit SDK imports also work outside the repository and with spaces/&.
    project = work / "app"
    project.mkdir()
    sdk = root / "sdk/DolphinDotNet.Sdk/Sdk"
    (project / "Program.cs").write_text("return 0;\n")
    (project / "Test.csproj").write_text(
        '<Project><Import Project="' + escape(str(sdk / "Sdk.props"), {'"': '&quot;'}) + '" />'
        '<Import Project="' + escape(str(sdk / "Sdk.targets"), {'"': '&quot;'}) + '" />'
        '<Target Name="TestCompilerDiagnostic"><ItemGroup><DiagnosticArgs Include="' +
        escape(str(root / 'tools/DolphinDotNet.Compiler/bin/Debug/net8.0/DolphinDotNet.Compiler.dll'), {'"': '&quot;'}) +
        ';--aot;missing.dll;output.c" /></ItemGroup><DndRunTool Tool="dotnet" Arguments="@(DiagnosticArgs)" /></Target></Project>')
    # Debugger targets retain named locals; Release's normal optimization policy
    # must remain intact even when an old configuration has debug flags set.
    for configuration, expected_optimize in (("Debug", "false"), ("Release", "true")):
        result = subprocess.run([dotnet, "msbuild", str(project / "Test.csproj"),
                                 "-getProperty:Optimize", "-p:Configuration=" + configuration,
                                 "-p:DndDebugSymbols=true"], text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=30)
        assert result.returncode == 0 and result.stdout.strip() == expected_optimize, result.stdout
    # A normal IDE Debug build emits the Rider maps without extra CLI flags;
    # explicit opt-out and Release defaults remain available.
    for configuration, override, expected_symbols in (
            ("Debug", None, "true"), ("Release", None, "false"), ("Debug", "false", "false")):
        command = [dotnet, "msbuild", str(project / "Test.csproj"),
                   "-getProperty:DndDebugSymbols", "-p:Configuration=" + configuration]
        if override is not None:
            command.append("-p:DndDebugSymbols=" + override)
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT, timeout=30)
        assert result.returncode == 0 and result.stdout.strip() == expected_symbols, result.stdout
    result = subprocess.run([dotnet, "build", str(project / "Test.csproj"), "-p:DndBuildBackend=Invalid"],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
    assert result.returncode != 0 and "DndBuildBackend must be" in result.stdout, result.stdout
    assert "DND001" in result.stdout, result.stdout
    # A packaged compiler from a Windows checkout may contain CRLF scripts.
    # Bundle generation must make them runnable by Bash inside Linux Docker,
    # including for existing checkouts that predate the .gitattributes rule.
    packaged_compiler = work / "compiler with CRLF"
    shutil.copytree(root / "tools/DolphinDotNet.Compiler/bin/Debug/net8.0", packaged_compiler)
    shell = packaged_compiler / "native/scripts/build_exi_overlay.sh"
    shell_lf = shell.read_text().replace('\r\n', '\n')
    shell.write_bytes(shell_lf.replace('\n', '\r\n').encode())
    assert b'\r\n' in shell.read_bytes()
    bundle = work / "CRLF bundle"
    result = subprocess.run([dotnet, str(packaged_compiler / "DolphinDotNet.Compiler.dll"),
                             "--bundle", str(project / "bin/Debug/net8.0/Test.dll"), str(bundle)],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
    assert result.returncode == 0, result.stdout
    emitted_shell = (bundle / "scripts/build_exi_overlay.sh").read_bytes()
    assert emitted_shell == shell_lf.encode() and b'\r' not in emitted_shell
    if os.name != "nt":
        header = b'\n'.join(emitted_shell.split(b'\n')[:2]).decode()
        result = subprocess.run(['bash', '-c', header], capture_output=True, text=True, timeout=10)
        assert result.returncode == 0, result.stderr
    result = subprocess.run([dotnet, "msbuild", str(project / "Test.csproj"), "-t:TestCompilerDiagnostic"],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
    assert result.returncode != 0 and "DND002" in result.stdout and "missing.dll" in result.stdout, result.stdout
    if os.name == "nt":
        # Visual Studio uses full-framework MSBuild, including the task's
        # quoted-Arguments fallback (dotnet MSBuild uses ArgumentList).
        vswhere = Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
        found = subprocess.check_output([str(vswhere), "-latest", "-requires", "Microsoft.Component.MSBuild",
                                         "-find", r"MSBuild\**\Bin\MSBuild.exe"], text=True).splitlines()
        assert found, "Visual Studio MSBuild not found on Windows CI runner"
        result = subprocess.run([found[0], str(project / "Test.csproj"), "-restore", "-p:DndBuildBackend=Invalid"],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
        assert result.returncode != 0 and "DND001" in result.stdout and "DndBuildBackend must be" in result.stdout, result.stdout
    result = subprocess.run([dotnet, "msbuild", str(project / "Test.csproj"), "-t:BuildDol",
                             "-p:DesignTimeBuild=true", "-p:DndBuildBackend=Invalid"],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
    assert result.returncode == 0, result.stdout

print("IDE workflow: launch paths, exit codes, cancellation, settings, stale artifacts, MSBuild diagnostics and design-time guard passed")
