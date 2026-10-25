# PKForge Fork

Personal fork of [PKForge](https://github.com/sofianeelhor/PKForge) at https://github.com/gabzidevs/PKForge (upstream: `sofianeelhor/PKForge`). It exists to fix two things for the owner's Emerald setup (PokeDaisy on Android, PKForge as the companion). Where it's going next is in [ROADMAP.md](ROADMAP.md). The plan behind this first round of changes is [`docs/superpowers/plans/2026-10-24-fork-daycare-pace-and-release.md`](../superpowers/plans/2026-10-24-fork-daycare-pace-and-release.md).

## Why the fork exists

1. **The daycare was too fast.** The Poképark daycare gave a flat 500 EXP per hour of real time. A starter went from level 5 to 13 in about 2 hours, which made the daycare a cheat rather than something that fits the game.
2. **PokeDaisy backups showed up as games.** Per PokeDaisy's source (`SaveBackups.kt`, `KEEP = 10`), PokeDaisy writes up to 10 dated copies of each save into a `pokedaisy-backups` subfolder (`<game>.backup-<yyyyMMdd-HHmmss>.<ext>`). PKForge's scanner recursed into it and listed every backup as its own game, so the same Pokémon appeared several times in the Living Dex.

## What changed, and where

**Daycare pace** (Off / Slow / Gentle / Original)

- Engine: `src/PKForge.Engine/ParkDaycareService.cs`. `DaycarePace` is the enum; `ExpForStay` turns a stay into EXP. Original is the old flat 500 EXP an hour. Slow (24 h per level) and Gentle (8 h per level, the default) walk forward from the Pokémon's current EXP, level by level, each level at its own rate, until the time runs out or level 100. Off, eggs, level 100 and games whose daycare doesn't train give nothing.
- Setting: `PokeparkSettings.DaycarePace` in `src/PKForge.App/Services/PokeparkService.cs` (default `Gentle`, an undefined stored value falls back to it).
- Wiring: `src/PKForge.App/Services/PokeparkDaycareService.cs` (`ExpForStay` replaces the old `ExpPerHour`; `PaceNote` and `NextPace` for the menu).
- UI: a "Daycare pace" row in Blissey's menu, next to "Write back to saves", in `src/PKForge.App/Views/PokeparkPage.cs`. Tapping it cycles Off, Slow, Gentle, Original.
- Tests: `tests/PKForge.Engine.Tests/ParkDaycareServiceTests.cs`.

**`pokedaisy-backups` is never scanned**

- `EmulatorSaveHeuristics.IsPrunedDirectory` in `src/PKForge.Infrastructure/EmulatorSaveHeuristics.cs` now owns the pruned-folder list (moved out of `AndroidEmulatorScanner`), with `pokedaisy-backups` added. Only `pokedaisy-backups` applies to every emulator kind (`roms` is still pruned for RetroArch only); matching is case-insensitive.
- Tests: `tests/PKForge.Domain.Tests/EmulatorSaveHeuristicsTests.cs`.

**Menu crash fix (also in upstream)**

- `DsFolderButton` in `src/PKForge.App/Views/DsKit.cs`: a selected row whose label is too long scrolls it in an `async void` loop. Closing the menu mid-scroll left the loop running against the torn-down MAUI context, and the next `TranslateToAsync` threw `ObjectDisposedException: IServiceProvider`, killing the app. The row now stops the loop on `Unloaded`, the loop also ends once the label has no handler, and the disposal exception is caught. Not covered by tests (App views have no test project); found and checked on device through logcat.

**Fork identity**

- `src/PKForge.App/PKForge.App.csproj`: app ID `io.github.gabzidevs.pkforge` (diagnostic build `...pkforge.debug`, titled "PKForge Fork Diagnostics"), title "PKForge Fork", version `3.1.1-fork.1`, version code `312`.
- `AndroidManifest.xml` no longer hard-codes `android:label`; the build writes `ApplicationTitle` there.
- `AppUpdateService` checks `https://api.github.com/repos/gabzidevs/PKForge/releases/latest`. Its broadcast action/extra names use the fork's ID so the two installed apps never answer each other.
- `src/PKForge.Domain/AppUpdate.cs`: prerelease comparison now follows SemVer (`fork.10` is newer than `fork.9`); before, it compared as plain text. Tests are in `tests/PKForge.Domain.Tests/AppUpdateRulesTests.cs`.
- `AboutPopup` keeps the upstream credit and adds a line for the fork.

Sprite-pack downloads are unchanged and still use upstream's release assets.

## Releases and signing

CI is `.github/workflows/build.yml`.

- **Pushes to `main` and `gabzi/**`, and pull requests:** run both test suites and build a diagnostic APK (`-p:DiagnosticBuild=true`, signed with a throwaway debug key), kept as the `pkforge-apk` artifact for 3 days. This is how App changes get compile-checked, since the MAUI project can't be built locally. The diagnostic build can be sideloaded to try a branch: it embeds its assemblies (a plain Debug APK uses Fast Deployment and aborts on launch with "No assemblies found in .../.__override__") and installs as `io.github.gabzidevs.pkforge.debug`, beside the release, with its own data. Never sideload a build that uses the release app ID with a debug key: the signed release would then refuse to install over it.
  - Diagnostic builds are signed with a stable key, so each one updates the installed diagnostic app in place. It lives in the secret `PKFORGE_DIAGNOSTIC_KEYSTORE_BASE64` (PKCS12, alias `pkforge-fork-diagnostic`, passwords `android` like Android's own debug key; the keystore is the secret), and CI checks the APK against the variable `PKFORGE_DIAGNOSTIC_CERT_SHA256`. `scripts/fork/create-diagnostic-key.sh` makes both. It isn't backed up on purpose: if it's lost, rerun the script with `FORCE=1` and reinstall the diagnostic app once. Without the secret (pull requests from other forks), CI warns and signs with a throwaway key.
- **Tags `v*`** (for example `v3.1.1-fork.1`): run the tests, build a signed Release APK, check it, and publish a GitHub Release with the asset `PKForge-Fork-<tag>.apk` (one APK per release, so Obtainium needs no filters). The checks are:
  - The build fails if the repo variable `PKFORGE_RELEASE_CERT_SHA256` is empty, and again if the APK's signing certificate doesn't match it.
  - `aapt2` must report package `io.github.gabzidevs.pkforge` and label `PKForge Fork`. This check runs on every build, not only tags.

Signing setup, in the repo's Actions settings:

- Secrets: `PKFORGE_ANDROID_KEYSTORE_BASE64` (the PKCS12 keystore, base64), `PKFORGE_ANDROID_KEYSTORE_PASSWORD`, `PKFORGE_ANDROID_KEY_ALIAS`, `PKFORGE_ANDROID_KEY_PASSWORD`.
- Variable: `PKFORGE_RELEASE_CERT_SHA256`, the certificate's SHA-256 fingerprint exactly as `keytool -list -v` or `keytool -printcert` prints it: uppercase hex, colon-separated (`AA:BB:...`), 32 bytes. The workflow rejects any other form.

`scripts/fork/create-release-key.sh` does all of this in one go, and is meant to be run once, by the owner, in their own terminal: it makes the key with keytool (Java through mise), saves it to Bitwarden as a Login item named "PKForge Fork release key" (username = key alias, password = keystore password, URI = the repo, `cert_sha256` field; the `.p12` as a base64 block in the notes, which works without Premium, and as an attachment too when the account has Premium), then sets the four secrets and the variable with `gh`. It never prints a secret and refuses to run if the Bitwarden item or the keystore secret already exists. `DRY_RUN=1` makes a throwaway key and stops before Bitwarden and GitHub. `RESUME=1` makes no key: it takes the one already in Bitwarden, checks it opens with the saved password and matches its recorded fingerprint, and only sets the GitHub secrets (for a run that stopped after Bitwarden, or to restore CI).

Keep that backup. Android only installs an update signed with the same key as the installed app; if the key is lost, every installed copy has to be uninstalled (and its data lost) before a new key can be used. To restore, take the `.p12` and passwords from the Bitwarden item rather than making a new key.

To cut a release: bump `ApplicationDisplayVersion` and `ApplicationVersion` in `PKForge.App.csproj`, commit, then tag `v<display version>` and push the tag.

Status: the first signed release has not been built yet, so the tag workflow (signing, certificate check, aapt2 check, release upload) is untested end to end.

## Syncing with upstream

```sh
git config remote.upstream.tagOpt --no-tags   # once: keep upstream's v* tags out of this repo
git fetch upstream
git merge upstream/main
```

The upstream remote is fetch-only (its push URL is set to `DISABLED`). The `--no-tags` setting matters because a later `git push --tags` would otherwise push upstream's `v*` tags to the fork and trigger failing release builds. Expect conflicts in the files listed above, mainly `PKForge.App.csproj`, `AppUpdateService.cs`, `build.yml` and `PokeparkPage.cs`. Run both test suites after merging (commands in the repo's `CLAUDE.md`).
