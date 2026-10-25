# Plan: PKForge fork — daycare pace, PokeDaisy backups, fork releases

**Spec:** approved in chat on 2026-10-24 (summarised in "Spec" below; this file is the record).
**Branch:** `gabzi/daycare-pace-and-fork-release` in `~/Dev/GabziOSS/PKForge` (fork `gabzidevs/PKForge`, upstream `sofianeelhor/PKForge`, upstream push disabled).

## Spec

The user plays Pokémon Emerald in PokeDaisy and uses PKForge as a companion. They want a fork they maintain:

1. **Daycare pace.** The Poképark daycare gives a flat 500 EXP per hour of real time (`PokeparkDaycareService.ExpPerHour`), which levelled a starter from 5 to 13 in about 2 hours. Replace it with a pace measured in *levels*: each hour of stay gives a fixed share of the EXP the Pokémon needs for its next level, so the feel is the same at level 5 and level 50. A long stay crosses several levels, each at its own level's rate. Presets, picked in Blissey's daycare menu next to "Write back to saves":
   - `Off`: no EXP (the stay is a park memory only).
   - `Slow`: 1 level per 24 hours.
   - `Gentle` (**default**): 1 level per 8 hours.
   - `Original`: the old flat 500 EXP per hour.
   Level 100 caps everything; games whose daycare doesn't train (`ParkDaycareRules.Grows == false`) still gain nothing.
2. **Skip PokeDaisy's backups.** PokeDaisy writes up to 10 dated copies of each save to a `pokedaisy-backups` subfolder beside the save (`<game>.backup-<yyyyMMdd-HHmmss>.<ext>`). PKForge's Android scanner recurses into it and shows each backup as a separate game, which duplicates Pokémon in the Living Dex. `pokedaisy-backups` must never be scanned, for every emulator kind, case-insensitively.
3. **Installable fork releases.** The fork installs **beside** the official app: its own application ID, its own name, and update checks against the fork's releases. GitHub Actions builds a signed APK for every `v*` tag and attaches it to a GitHub Release, signed with the fork's own permanent key (generated separately by the controller; the user adds the secrets). The README gets an Obtainium install link.
4. **Session memory.** The repo carries `docs/fork/` notes (why the fork exists, what changed, the user's decisions and the PokeDaisy modularity roadmap) and a `CLAUDE.md` pointing at them, so a fresh session can pick up.

**Non-goals:** no upstream PRs for now (forks only); no change to sprite-pack downloads (they keep using upstream release assets); no PokeDaisy work in this plan.

## Global Constraints

- Tests: `mise x dotnet@10 -- dotnet test tests/PKForge.Engine.Tests/PKForge.Engine.Tests.csproj -c Release` and the same for `tests/PKForge.Domain.Tests/PKForge.Domain.Tests.csproj`. Baseline before this plan: Engine 966 passed, Domain 860 passed, 0 failed. `dotnet` is only available through `mise x dotnet@10 -- …`.
- `src/PKForge.App` (MAUI Android) cannot be built locally (no MAUI workload / Android SDK). Keep App changes thin and push logic into `PKForge.Engine` / `PKForge.Infrastructure`, where it is tested. App code is verified by the CI build.
- `TreatWarningsAsErrors` is on. Match the surrounding code's style: terse XML doc comments, records, expression bodies, British-neutral plain English in UI strings.
- New application ID: `io.github.gabzidevs.pkforge` (diagnostic build: `io.github.gabzidevs.pkforge.debug`). App title: `PKForge Fork`.
- Fork version: `ApplicationDisplayVersion` `3.1.1-fork.1`, `ApplicationVersion` `312`. Release tags look like `v3.1.1-fork.1`.
- Fork repo URL: `https://github.com/gabzidevs/PKForge`.
- Commit each task separately on the branch. Never push.

## Task 1: Daycare pace in the engine

Files: `src/PKForge.Engine/ParkDaycareService.cs`, `tests/PKForge.Engine.Tests/ParkDaycareServiceTests.cs`.

Add, in `PKForge.Engine`:

- `public enum DaycarePace { Off, Slow, Gentle, Original }`.
- `public static uint ExpForStay(PKM pk, TimeSpan stay, DaycarePace pace)` on `ParkDaycareService`, plus a `(byte[] data, string? format, TimeSpan stay, DaycarePace pace)` overload returning 0 when the bytes can't be parsed.
  - Negative stays count as zero.
  - `Off` → 0. `Original` → `stay.TotalHours * 500`, as today (keep the existing `uint.MaxValue / 4` clamp).
  - `Slow` (24 h per level) and `Gentle` (8 h per level): walk forward from the Pokémon's current EXP. At level L the rate is `(EXP(L+1) − EXP(L)) / hoursPerLevel` per hour, using `Experience.GetEXP(level, pk.PersonalInfo.EXPGrowth)`. Spend the remaining time level by level until it runs out or level 100 is reached; return the total EXP (floor). A Pokémon partway through a level finishes that level at that level's rate.
  - Return 0 for eggs, for level 100, and when `RulesFor(pk).Grows` is false.
- Leave `Preview`, `GrowSlot`, `GrowBytes` as they are; callers pass the EXP from `ExpForStay`.

Tests (write first, watch them fail), using the existing `Make` helper with a Gen 3 Pokémon (e.g. species 255 Torchic, Medium Slow growth):

- `Gentle` for exactly 8 h from the exact start of level 5 → `Preview` reaches exactly level 6.
- `Gentle` for 16 h from the start of level 5 → level 7.
- `Gentle` for 4 h from the start of level 5 → EXP equals half of `EXP(6) − EXP(5)` (floor), still level 5.
- `Slow` for 24 h → +1 level. `Off` for 100 h → 0. `Original` for 2 h → 1000.
- Level 100 → 0 for every pace. Negative stay → 0.
- A Gen 8 Pokémon (no daycare EXP) → 0 for `Gentle`.

To start "exactly at the start of a level", set `pk.EXP = Experience.GetEXP(level, growth)` on the made Pokémon.

## Task 2: Daycare pace in the app

Files: `src/PKForge.App/Services/PokeparkService.cs` (`PokeparkSettings`), `src/PKForge.App/Services/PokeparkDaycareService.cs`, `src/PKForge.App/Views/PokeparkPage.cs`.

- `PokeparkSettings` gains `public DaycarePace DaycarePace { get; init; } = DaycarePace.Gentle;` with a one-line doc comment. Existing saved settings without the field must load as `Gentle` (check how settings are serialised and that a missing property takes the initialiser).
- `PokeparkDaycareService`: remove `ExpPerHour` and the private `Exp(TimeSpan)`. `Progress` and `PickUpAsync`/`GrowAsync` compute EXP with `ParkDaycareService.ExpForStay(source.Data, source.Format, time, settings.DaycarePace)` (the source is already resolved there). The card preview and the pick-up must use the same number.
- The check-in line for growing games currently says "It will grow a little for every hour it stays." Make it reflect the pace: Off → "Daycare pace is Off: it won't gain EXP here."; Slow → "It will grow about a level a day."; Gentle → "It will grow about a level every 8 hours."; Original → "It will grow a little for every hour it stays."
- `PokeparkPage` Blissey menu (around the "Write back to saves" option, ~line 711–770): add an option `Daycare pace: <Pace>` with a Detail line describing the current pace in the same words as above, which cycles Off → Slow → Gentle → Original → Off and saves via `_park.SaveSettings(_park.Settings with { DaycarePace = next })`, following exactly how the write-back toggle is wired.

No unit tests exist for the App project; keep logic out of it. The CI build is the check. Run both test suites anyway to prove nothing else moved.

## Task 3: Never scan `pokedaisy-backups`

Files: `src/PKForge.Infrastructure/EmulatorSaveHeuristics.cs`, `src/PKForge.App/Platforms/Android/AndroidEmulatorScanner.cs`, `tests/PKForge.Domain.Tests/EmulatorSaveHeuristicsTests.cs`.

- Move the scanner's `PrunedDirectories` set and `IsPruned(string folder, EmulatorKind kind)` into `EmulatorSaveHeuristics` as `public static bool IsPrunedDirectory(string folder, EmulatorKind kind)`, unchanged in behaviour, and add `"pokedaisy-backups"` to the set. Keep the existing comments explaining the set and the `roms` exception.
- `AndroidEmulatorScanner.FindFilesRecursive` calls `EmulatorSaveHeuristics.IsPrunedDirectory`; delete the scanner's private copies.
- Tests first: `pokedaisy-backups` (and `PokeDaisy-Backups`) is pruned for every `EmulatorKind` value; `states` is pruned; `roms` is pruned for `RetroArch` only; `saves` is not pruned.

## Task 4: Fork identity and release pipeline

Files: `src/PKForge.App/PKForge.App.csproj`, `src/PKForge.App/Services/AppUpdateService.cs`, `src/PKForge.App/Views/AboutPopup.cs`, `.github/workflows/build.yml`, `.gitignore`, plus anything else a grep for `org.pkforge` turns up under `src/`.

- csproj: the application IDs, title and versions from Global Constraints.
- `AppUpdateService`: `LatestUrl` → `https://api.github.com/repos/gabzidevs/PKForge/releases/latest`; User-Agent URL → the fork URL; the three `org.pkforge.app.*` intent action/extra names → `io.github.gabzidevs.pkforge.*` (so the two installed apps never answer each other's broadcasts). Read how the update check compares the release tag with the installed version and make sure `v3.1.1-fork.1`-style tags compare correctly (newer fork tags must win; the installed version must not see itself as an update). If parsing can't handle the suffix, fix the parser and cover it with a test in `PKForge.Domain`/`Engine` if the logic is reachable there; otherwise keep the change minimal and explain in the report.
- `AboutPopup`: keep the upstream credit line; add a line `Fork: github.com/gabzidevs/PKForge`.
- `build.yml`:
  - Keep the same secret names (`PKFORGE_ANDROID_KEYSTORE_BASE64`, `PKFORGE_ANDROID_KEYSTORE_PASSWORD`, `PKFORGE_ANDROID_KEY_ALIAS`, `PKFORGE_ANDROID_KEY_PASSWORD`); the fork sets its own values.
  - Replace the hard-coded upstream certificate SHA-256 with the repository variable `vars.PKFORGE_RELEASE_CERT_SHA256`; fail the tag build with a clear message if it is empty.
  - Name the release asset `PKForge-Fork-${GITHUB_REF_NAME}.apk` (one APK per release, so Obtainium picks it up without filters).
  - Also run the workflow on pushes to `gabzi/**` branches (tests + unsigned debug APK artifact), so App changes are compile-checked before merging.
- `.gitignore`: add `.superpowers/`.

## Task 5: README install section and fork docs

Files: `README.md`, `docs/fork/README.md`, `docs/fork/ROADMAP.md`, `CLAUDE.md` (new).

- `README.md`: a short "This fork" section at the very top (above the upstream content, which stays): what the fork changes (daycare pace, skips PokeDaisy backups, installs beside the official app), a download link to the fork's latest release, and an Obtainium button/link: `https://apps.obtainium.imranr.dev/redirect?r=obtainium://add/https://github.com/gabzidevs/PKForge`. Note that the fork starts with empty app data (Bank, Poképark) because it installs beside the official app.
- `docs/fork/README.md`: why the fork exists (the two problems, with the 5→13 in ~2 h example and the duplicated Living Dex), what changed and where in the code, how releases and signing work (secrets + `PKFORGE_RELEASE_CERT_SHA256` variable, keep a backup of the keystore), how to sync upstream (`git fetch upstream && git merge upstream/main`; upstream push is disabled).
- `docs/fork/ROADMAP.md`: the brainstorm so far, as decisions and open ideas:
  - Principle: "organic, not cheating": companion features make the game more convenient and alive; rewards are earned by playing, scaled to what Emerald itself gives (Pickup lists, Berry Master, daily limits), gated by story progress; events (e.g. Eon Ticket) arrive in story order.
  - Target: PokeDaisy is the hub; fork PokeDaisy (GPLv3, embeds upstream mGBA + melonDS as submodules with one small melonDS patch) and add features as modules in the fork behind a small host interface (read saves, read live memory via its `MemoryReader`, write on pack-up, add companion screens), clean enough to become separate-app plugins later. PKForge stays a design reference, not a code dependency (PKHeX.Core is C#; the organic features don't need it).
  - Order: (1) PokeDaisy fork + module host, (2) Bank + transfers (both directions allowed, a deliberate break from the real games), (3) Camp, (4) Poképark, (5) Cobblemon bridge, (6) modernized Emerald hack (pokeemerald-expansion) as a separate later project.
  - Camp: set up from the companion on the current route (any time, including during play); party gains friendship; Pickup-style finds from Emerald's own lists, badge-gated, daily-capped; route Pokémon visit (shows what lives there); leaving the route pauses the game and asks Pack up / Stay a while; pack-up is when rewards are written. Secret Bases can be developed later.
  - Setup: Redmi Note 15 Pro 5G (HyperOS 3, Android 16) with a Nova Lite controller; the game on the Mac via AirSync's native desktop mode (a scrcpy virtual display), touch screen + companion on the phone; telescopic controller later.
  - RetroArch option: RetroArch's Android build gained network commands (`READ_CORE_MEMORY`, UDP 55355) on 2026-03-25 (after 1.22.2, nightly only), so a companion could read a RetroArch game; PokeDaisy's action features (Fly, item use) call game code in-process and wouldn't work that way.
  - Forks only; upstream contributions later.
- `CLAUDE.md`: a few lines saying this is the user's fork, pointing to `docs/fork/README.md` and `docs/fork/ROADMAP.md`, the test commands from Global Constraints, and "never push to upstream".
