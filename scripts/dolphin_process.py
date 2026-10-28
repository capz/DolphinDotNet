"""Keep the X server alive until Dolphin finishes its GPU/video teardown."""
import os
from pathlib import Path
import signal
import subprocess


def xvfb_command(binary, arguments, pid_file):
    # exec preserves the recorded PID. All paths are positional shell arguments,
    # so neither spaces nor shell metacharacters are interpreted as commands.
    return ["xvfb-run", "-a", "sh", "-c",
            'echo $$ > "$1"; shift; exec "$@"',
            "dnd-dolphin", str(pid_file), str(binary), *map(str, arguments)]


def stop_dolphin(process, pid_file, timeout=30):
    if process.poll() is not None:
        return
    try:
        pid = int(Path(pid_file).read_text().strip())
        if pid <= 1 or pid == process.pid or os.getpgid(pid) != process.pid:
            raise ValueError("Dolphin PID is outside the capture process group")
        # Signalling xvfb-run's entire group also kills Xvfb. In-flight GL
        # readbacks then lose their display while the final AVI frame is being
        # drained. Let xvfb-run reap Dolphin and shut down Xvfb normally.
        try:
            os.kill(pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        process.wait(timeout=timeout)
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()
        # A forced termination cannot provide trustworthy final-frame evidence.
        raise RuntimeError("Dolphin did not finish graceful capture shutdown") from error
