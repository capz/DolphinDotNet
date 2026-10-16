"""Cross-platform integration tests for the real driver and MSBuild SDK.

The probe records launch arguments; actual DOL compilation/boot stays in the
GameCube CI job. No installed Dolphin, Docker daemon or console SDK is required.
"""
import json
import os
from pathlib import Path
import shutil
import signal
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
    output = call("build", "--assembly", assembly, "--compiler", assembly,
                  "--native-directory", work / "obj/native", "--dol", dol, "--elf", elf,
                  "--run-config", config, "--backend", "Invalid", expected=1)
    assert not dol.exists() and not elf.exists() and not config.exists()
    output = call("build", "--assembly", assembly, "--compiler", assembly,
                  "--native-directory", work / "fresh/native", "--dol", work / "fresh/Pong.dol",
                  "--elf", work / "fresh/Pong.elf", "--run-config", work / "fresh/run.json",
                  "--backend", "Invalid", expected=1)
    assert "DndBuildBackend must be" in output
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
    result = subprocess.run([dotnet, "build", str(project / "Test.csproj"), "-p:DndBuildBackend=Invalid"],
                            text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=180)
    assert result.returncode != 0 and "DndBuildBackend must be" in result.stdout, result.stdout
    assert "DND001" in result.stdout, result.stdout
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
