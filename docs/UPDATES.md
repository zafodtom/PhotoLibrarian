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

On confirmation it downloads the installer to a temporary folder, displays progress, verifies its size and digest, then requests final confirmation before starting Inno Setup. Windows may show an elevation or SmartScreen prompt; no Authenticode certificate is bundled. Existing installation identity remains unchanged. Album directories and photo metadata are never part of the app installation.

The update flow is **not fully silent** and does not automatically restore the currently open album after installation. The user follows the standard Inno installer. No periodic checks, automatic installation or background network operations are performed.

## Troubleshooting and testing

- A release cannot be found: verify it is public, published, not a draft or prerelease, and that the installer asset is present.
- SHA-256 missing or mismatched: the updater rejects the release. Never bypass integrity checks.
- Build errors: inspect the GitHub Actions job logs; no stable release should be published from a failed build.
- Upgrade test: install a prior version first, then publish a higher stable version and test the in-app update end to end.
- App and brand: PhotoLibrarian is the product; Trimenor is the development brand. Neither name implies Windows code signing.

The Inno Setup `AppId`, default installation path and `.pla` association are preserved so that upgrades can replace older versions instead of installing a second product.
