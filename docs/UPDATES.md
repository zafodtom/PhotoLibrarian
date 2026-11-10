# PhotoLibrarian — Windows installer and manual updates

## GitHub Actions build

Workflow: `.github/workflows/windows-installer-release.yml`.

Every push to `feature/trimenor-updates-ci` or `master` automatically builds the Windows application and test project, runs tests, publishes a self-contained Windows x64 application and creates an Inno Setup installer. Download `PhotoLibrarian-windows-x64` from that workflow run's **Artifacts** section for local validation.

The project `<Version>` property is the single release version source. For future releases, increment that value according to the [Trimenor SemVer rules](https://github.com/zafodtom/trimenor) and update AssemblyVersion/FileVersion and release notes in `docs/releases/X.Y.Z.md` accordingly. The installer and generated manifest derive their version from the project; do not publish two different artifacts under the same version.

### Publish a stable release

After build + tests + manual Windows installation and update tests:

1. GitHub → Actions → **PhotoLibrarian Windows Build and Release**.
2. **Run workflow** on the reviewed branch, selecting **publish_release**.
3. The job repeats the complete Windows build and checks. It creates a **draft** GitHub Release `vX.Y.Z`, attaches the installer, `SHA256SUMS.txt` and `photolibrarian-update.json`, then marks it as a stable latest release. Existing released versions are never overwritten.
4. Confirm that `https://github.com/zafodtom/PhotoLibrarian/releases/latest` shows the intended version.

GitHub Actions artifacts **are not update feeds**. Only a published public stable Release can be offered within PhotoLibrarian.

## Manual update in the app

Settings → **CHECK FOR UPDATES** contacts `https://api.github.com/repos/zafodtom/PhotoLibrarian/releases/latest` once, on user request only. The application offers only a stable `vMAJOR.MINOR.PATCH` version strictly newer than its installed version, and an x64 installer matching the release name. It validates the official release asset URL, maximum size (700 MiB), exact asset size and SHA-256 digest provided by GitHub.

On confirmation it downloads the installer to a temporary folder, displays progress, and verifies its size and digest. Confirmation is rendered in a **Trimenor-themed Settings overlay**, rather than a system-style ContentDialog. After the user's second confirmation, the running application copies its separately published `PhotoLibrarian.UpdateHelper.exe` into the temporary update directory, passes immutable verification parameters as process arguments, then exits.

The helper has its own dark Trimenor-style installation progress window. It waits up to 45 seconds for PhotoLibrarian to exit, independently re-verifies the installer SHA-256 and keeps the verified file open with a read-only lock while the installer runs. It invokes Inno Setup with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS`, waits for the exit code, and on success starts the updated PhotoLibrarian, reopening the same album when one was active. On failure it keeps the helper window open and records details in `%LOCALAPPDATA%\\PhotoLibrarian\\logs\\update.log`. If the installer returns code 3010, Windows must be restarted before using the updated application.

An administrator/UAC prompt **may still appear** because the current Inno Setup installer is per-machine. This security prompt is owned by Windows and must not be bypassed or imitated. Windows SmartScreen may also display a trust warning because the installer does not carry an Authenticode signature. Existing installation identity remains unchanged; album directories and photo metadata are not part of the installation. No periodic checks, automatic installation or background network operations are performed.

**Version policy:** This implementation is initially on the development branch at application version 1.1.1. Changes are not testable through the published 1.1.1 binary until a later version is explicitly prepared, built and released. Do not republish/overwrite v1.1.1 assets.

## Troubleshooting and testing

- A release cannot be found: verify it is public, published, not a draft or prerelease, and that the installer asset is present.
- SHA-256 missing or mismatched: the updater rejects the release. Never bypass integrity checks.
- Build errors: inspect the GitHub Actions job logs; no stable release should be published from a failed build.
- Upgrade test: install a prior version first, then publish a higher stable version and test the in-app update end to end.
- App and brand: PhotoLibrarian is the product; Trimenor is the development brand. Neither name implies Windows code signing.

The Inno Setup `AppId`, default installation path and `.pla` association are preserved so that upgrades can replace older versions instead of installing a second product.
