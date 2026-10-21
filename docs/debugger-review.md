# Debugger lifecycle and packaging review

Reviewed the shared TCP/USB Gecko RSP client, DAP attach and execution lifecycle,
source stepping, breakpoint ownership, stopped inspection, Rider DAP process
ownership, and Visual Studio packaging. The same .NET adapter serves emulator
and hardware targets; physical transport compatibility is a separate acceptance
requirement.

## Findings fixed

- **Malformed inspection replies left a usable session.** `InvalidDataException`
  is not an `IOException`. An invalid register/memory packet during a stopped
  request therefore bypassed fatal transport cleanup. The adapter now closes
  that connection and reports termination. The empty-register-after-ACK test
  reproduces the original missing termination. Well-framed target refusals still
  produce unavailable values, and invalid source mapping arguments remain
  request errors without terminating a healthy target.
- **Resume could write breakpoints after target exit.** When the instruction used
  to step off a user breakpoint returned `W00`, Resume still issued `Z0` writes.
  Restoration is now conditional on a live stop. The regression test checks that
  the exit-producing single step is the final RSP command.
- **Error cleanup could race with execution cleanup.** Request-side transport
  failure could clear `_client` before the execution catch dereferenced it.
  Execution now retains its own transport reference, termination notification is
  idempotent, and reattach waits for previous execution cleanup to finish. Normal
  target exit also closes the heartbeat and transport immediately.
- **Rider integration tests could be cached across adapter changes.** The test
  task depended on publishing the adapter but did not declare the resulting
  binaries as inputs. A local build republished the changed adapter while
  reporting `test UP-TO-DATE`. The adapter directory is now a test input.
- **IDE release assets were omitted.** Rider was built inside the main workflow,
  while VSIX had a separate workflow; neither package was attached to the sample
  release. Both now have reusable/manual workflows called by selected main-build
  jobs, and releases collect their same-run packages. Missing selected packages
  fail publication instead of silently producing an incomplete release.

## Remaining limits and acceptance

- Stack unwinding and values require matching ELF/source/address metadata and
  supported DWARF/location rules. Startup or unsupported native frames can still
  report a partial stack. No fallback guesses stack layouts or object fields.
- Source Step Over can run to a temporary return breakpoint. A callee that never
  returns requires Pause; instruction budgets do not bound code executed by a
  continue-to-return. This is intentional and remains interruptible.
- Breakpoint edits during execution become effective at the next stop. Pause
  applies queued edits. This is not immediate live breakpoint insertion.
- A failed/disconnected RSP stream cannot reliably clear target breakpoints or
  resume the CPU. Cleanup does not issue further commands on a corrupt stream
  and does not kill Dolphin. Reconnect/restart may be required after adapter or
  target failure.
- UDP debugger liveness requires native heartbeat support and a reachable host
  address. The native BBA GDB stack has no application UDP channel; USB Gecko
  disconnect and real BBA behavior still need physical-device validation.
- Interactive Visual Studio acceptance and platform-specific serial drivers
  remain hardware/UI checks. Host tests exercise fake TCP and a PTY serial relay;
  these do not establish real USB/EXI compatibility.

See [Rider setup](rider-debugging.md), [Visual Studio setup](visual-studio-debugging.md)
and [CI selection and independent packaging](ci-selection.md).
