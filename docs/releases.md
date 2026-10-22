# Package releases

`eng/Package.props` holds the default development version. Core, GameCube, SDK
and Templates share the version; GameCube declares its Core dependency through
its project reference. The template package embeds matching SDK/GameCube pins.
Breaking API or SDK changes require a major version increase after 1.0; additive
changes increment minor, fixes increment patch. Preview builds use
`0.1.0-preview.<workflow-run>.<attempt>`; release versions are immutable.

The **NuGet preview** workflow builds packages and tests an isolated external
consumer on pull requests affecting distribution or its inputs. It also builds
external Debug Pong through Docker. Existing build/native/sample and IDE CI
workflows remain in place. Inspect their results as well as package verification.
Run the Dolphin smoke harness against an external build on an emulator-capable
host before promoting a release. Verify Visual Studio on Windows, Rider on its
supported hosts, the Native devkitPPC path, and hardware separately; a Linux
Docker check does not substitute for these.

Publishing is opt-in: manually dispatch the workflow on `main` with `publish`
enabled after review/merge. Configure the protected `nuget-preview` GitHub
environment with required reviewers and a scoped `NUGET_API_KEY` secret through
repository settings. Never paste release credentials into chat or commit them.
The publish job downloads the exact verified artifact, then pushes each package
to nuget.org. Default dispatches and PR runs only build/test; do not enable upload
to test packaging. A partial upload should be investigated; do not overwrite
versions or silently skip conflicts. Retry only missing packages after reviewing
what nuget.org accepted, or issue a new preview version.

For a stable release, update the default version, test locally with
`python3 eng/pack.py --version X.Y.Z`, validate the consumers and release matrix,
and review package contents/licenses before an explicitly authorized publication.
The preview workflow intentionally does not publish stable versions. Package
owners, API keys, protected-environment settings, NuGet ingestion, and actual
publication cannot be proven by local packaging tests.
