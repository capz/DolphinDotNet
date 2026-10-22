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
enabled after review/merge. Authentication uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
and GitHub OIDC; no long-lived API key secret is required.

Before the first publication:

1. In GitHub repository **Settings → Environments**, create/configure
   `nuget-preview`. Set required reviewers and restrict deployment branches to
   `main`. Add the environment **variable** `NUGET_USER` with your nuget.org
   profile username (not your email address). This is not a secret.
2. Sign in to nuget.org, open your username menu → **Trusted Publishing**, and
   add a GitHub policy with these exact identifiers:

   | Policy field | Value |
   | --- | --- |
   | Repository owner | `capz` |
   | Repository | `DolphinDotNet` |
   | Workflow file | `nuget-preview.yml` (filename only) |
   | Environment | `nuget-preview` |

   Select the individual/organization owner that will own the packages. Scope
   the policy to `DolphinDotNet.*`, allowing new packages for the initial release
   and new versions for later releases. The `NUGET_USER` account must be
   authorized for that owner and package scope; a GitHub username alone does
   not establish NuGet ownership.
3. Merge the workflow changes. In **Actions → NuGet preview → Run workflow**,
   select `main`, enable `publish`, and approve the environment deployment when
   requested. Do this only when you intend to publish the preview.

Only the publish job has `id-token: write`. It remains gated on successful
consumer and Dolphin smoke jobs, manual publication intent, the `main` branch,
and the `nuget-preview` environment. After downloading the exact verified
artifact, `NuGet/login@v1` exchanges the GitHub OIDC token for a short-lived NuGet
credential immediately before pushing. The normal `dotnet nuget push --api-key`
argument receives this temporary credential from the action output; it does not
read a stored API-key secret. Never print tokens or add them to artifacts.

Default dispatches and PR runs only build/test and do not request a publishing
token. If an old `NUGET_API_KEY` secret exists solely for this workflow, remove
it after adopting trusted publishing. Diagnose policy failures by checking the
NuGet username, policy owner/scope, workflow filename and environment name.
Actual token exchange must be verified in an authorized publication run; do not
publish packages merely to test authentication.

A partial upload should be investigated; do not overwrite versions or silently
skip conflicts. Retry only missing packages after reviewing what nuget.org
accepted, or issue a new preview version.

For a stable release, update the default version, test locally with
`python3 eng/pack.py --version X.Y.Z`, validate the consumers and release matrix,
and review package contents/licenses before an explicitly authorized publication.
The preview workflow intentionally does not publish stable versions. Package
owners, trusted-publishing policies, protected-environment settings, NuGet ingestion, and actual
publication cannot be proven by local packaging tests.
