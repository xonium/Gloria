# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

The projects, the Velopack startup hook, update source, database location and migrations are in place. The analysis pipeline works end to end on real demos:
- per-tick parsing
- encounter segmentation
- aim metrics
- features
- outcome labels
- ML training via `tools/Gloria.Trainer`

A base model is shipped in `src/Gloria/models/base/`. A first UI slice runs on real data: Import (drop zone, demo folders, queue), Player (profiles, with search), Overview, Matches, Match, Encounter (with replay), Progress and Compare (against a bundled pro or a tracked friend). Findings and the gaps against the design are in `docs/ui-spike.md`. The Encounter replay shows where each bullet went (crosshair plus recoil), both players' health, the last hit's damage, weapon icons, the opponent's agent model (pre-rendered sprites) and a radar map of where the duel happened. User corrections work and retrain the user's model in the background. Progress has rolling medians, a by-weapon table and lost-duel causes; Overview and Progress have period selectors and Matches has filters.

## Commands

Run from the repo root. `global.json` pins the SDK and opts `dotnet test` into Microsoft.Testing.Platform (xUnit v3), so test filters use xUnit's MTP options, not VSTest's `--filter`.

- Build: `dotnet build Gloria.slnx`
- Test everything: `dotnet test`
- Skip tests that parse the real demos: `dotnet test -- --filter-not-trait "Category=Demo"`. Those tests also skip on their own when `_demos/` has no `.dem` files.
- Run a single test class or method: `dotnet test --project tests/Gloria.Core.Tests --filter-class "Gloria.Core.Tests.Metrics.AimMathTests"`, or `--filter-method "*YawDelta*"`
- Run the app (unpackaged): `dotnet build src/Gloria` and then start `src/Gloria/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Gloria.exe`, or press F5 in Visual Studio
- Add a migration: `dotnet tool restore`, then `dotnet ef migrations add <Name> -p src/Gloria.Data -s src/Gloria.Data -o Migrations`. `DesignTimeDbContextFactory` lets `dotnet ef` run without the MAUI app.
- Train on `_demos/`: `dotnet run --project tools/Gloria.Trainer -c Release -- all` (extract, train, report into `artifacts/training/`), then `-- publish` to copy the bundle into `src/Gloria/models/base/` and commit it. Re-run after changing the analysis; bump the stage's `Version` so cached features are re-extracted.
- Reference players (the pros on the Compare page): `dotnet run --project tools/Gloria.Trainer -c Release -- reference --demos _demos/donk --player donk666` (a SteamID64 or a name; `--name` and `--description` set what the app shows). It imports the demos into a database in memory and writes that player's encounters, without traces, to `src/Gloria/Resources/Raw/references/<name>.json` plus `index.json`. Commit them. Re-run after changing the analysis; the app still shows an older one, with a note. Keep pro demos in a subfolder of `_demos/`, so `extract` doesn't train on them.
- Game art: `./build/fetch-assets.ps1` downloads weapon icons (recolored for the dark theme) into `src/Gloria/Resources/Images/Weapons/` and radars plus `maps.json` into `src/Gloria/Resources/Raw/maps/`. Commit the results. The art is Valve's and is bundled by choice.
- App icon: `./build/make-icon.ps1` crops the black margin off `_assets/icon/gloria_icon.png` (git-ignored) into `src/Gloria/Resources/AppIcon/appicon.png` (the `MauiIcon`, which MAUI uses for the window) and `gloria.ico` beside it (the exe's `ApplicationIcon`, and Setup.exe via `pack.ps1`), plus `Resources/Images/logo.png` for the sidebar and welcome page. Commit the results.
- Agent sprites: rendered and committed in `src/Gloria/Resources/Raw/agents/`. To re-render, put the two Sketchfab models in `_assets/models/ct.glb` and `t.glb` (git-ignored; Sketchfab needs a login or an API token, see `tools/Gloria.Sprites/README.md`), then `dotnet run --project tools/Gloria.Sprites`. It renders with three.js in the installed Edge and writes contact sheets to `artifacts/sprites/`. The poses are in `tools/Gloria.Sprites/agents.json`: the CT uses its `tools_preview` clip, the T copies the CT's whole stance, standing and crouched (`matchFrom` with `"team": "ct"`, by limb direction, since the rigs' bone axes differ), and the CT's crouch bends its legs. The tool prints the bone names.
- README GIFs: `./build/make-gifs.ps1` (`-EncounterId N` picks the replayed duel, `-Scenes` a subset). It needs `ffmpeg` on PATH. Each scene starts the app with `GLORIA_RECORD` and `GLORIA_RECORD_SCENE` (`Services/DevRecorder.cs`), which steps the screen frame by frame, saves PNGs into `artifacts/gifs/<scene>/` and quits; ffmpeg writes `docs/media/<scene>.gif`. It uses your real database and current profile, so the names in it show. Commit the GIFs.
- Package: `./build/pack.ps1 -Version 0.1.0`. This publishes to `artifacts/publish/` and runs `vpk pack` into `releases/`, producing `Gloria-win-Setup.exe`. Keep the `Velopack` NuGet version equal to the `vpk` version in `.config/dotnet-tools.json`.
- Release: push a `v<semver>` tag (`git tag v0.2.0 && git push origin v0.2.0`). `.github/workflows/release.yml` tests, downloads the previous release (for deltas), runs `build/pack.ps1` and publishes with `vpk upload github`. A tag with a `-suffix` becomes a pre-release, which installed copies ignore.
- Test in-app updates without GitHub: install with Setup.exe, then pack a higher version and point `GLORIA_UPDATE_DIR` at `releases/` (as a user environment variable, e.g. `setx`, so the Start menu shortcut sees it). The installed app checks at startup and offers a restart in the sidebar (`Updates/UpdateNotifier`). Setup and uninstall clear `%LocalAppData%\Gloria`, so back up anything there first.
- To click through the UI from a script, use Windows UI Automation (`System.Windows.Automation` in `powershell.exe`). Buttons and pickers are found by their `SemanticProperties.Description`.
- Run the app against a scratch database: set `GLORIA_DATA_DIR`. Settings (saved folders, current profile) live in the database, so a scratch folder starts clean. `GLORIA_IMPORT` (files or folders, `;`-separated) imports demos before anything else. To check a screen from a script, also set `GLORIA_START_ROUTE` (e.g. `encounter?id=12`) and `GLORIA_CAPTURE` (a .png path); `GLORIA_CAPTURE_SCROLL` scrolls down first. See `Services/DevHooks.cs`. Desktop screenshot tools capture WinUI content as black, so use this instead.

## Solution layout (actual)

- `src/Gloria.Core`: domain layers as namespaces. It has no MAUI reference.
  - `Ingestion`: `IDemoParser`, `DemoFileDemoParser`, the columnar `PlayerTimeline`, `DemoHasher`.
  - `Visibility`: spotted state, with a view-cone fallback.
  - `Encounters`: `EncounterSegmenter`, `ShotAttribution`, `DemoIndex`.
  - `Metrics`: `AimMetricsCalculator`, `AimMath`, `BodyModel`.
  - `Weapons`: speeds and classes for every gun.
  - `Classification`: the feature extractor and `OutcomeLabeler`.
  - `Analysis`: `DemoAnalyzer`, which runs the whole chain.
  - `Aggregation`: `Rolling.Median` for per-demo trends.
- `src/Gloria.Data`: EF Core SQLite: `GloriaDbContext`, `Migrations/`, `DatabaseLocation` (`%LocalAppData%\Gloria.Data\gloria.db`, or `GLORIA_DATA_DIR`), and `DatabaseInitializer.Migrate`, which runs at startup.
  - `Import`: `DemoImporter` analyzes a demo once, for every player in it, and stores rounds, encounters, classifications and a compact per-tick trace (`TraceCodec`, format 3: aim angles, plus a `TraceScene` with positions, yaw, health, crouch, held weapons, damage between the two and the deciding death). No parse cache: the `.dem` isn't needed afterwards.
  - `Queries`: `GloriaReadModel`, the UI's read side. Every query takes the viewed profile's SteamID. `Comparison` ranks the metrics of two players (`ComparisonSide`); duels won and reaction times depend on the opposition and aren't ranked.
  - `References`: `ReferencePlayer`, the bundled reference file format (encounter metrics, no traces).
  - `AppSettings`: key-value settings in the `Settings` table. Don't use MAUI `Preferences` or `FileSystem.AppDataDirectory`: unpackaged, they live in `%LocalAppData%\Gloria\Gloria`, inside the Velopack install folder, which `Setup.exe` and uninstall delete.
  - `DemoDates`: user-set played-at dates, kept by content hash so re-analysis doesn't lose them.
  - `Corrections`: `CorrectionStore` stores the user's verdicts, keyed so they survive re-analysis. It also applies them to `EncounterRecord.Flags`, builds the training set, and reclassifies stored encounters.
  - Profiles: one SteamID each (`ProfileRecord`). Demos are shared between profiles; switching profile only changes the SteamID filter.
- `src/Gloria.Core/Classification`: aim categories (`AimCategory`), `EncounterFeatures`, `RuleBasedAimLabeler`, and `IAimClassifier` with its rule-based fallback. It has no ML.NET reference.
- `src/Gloria.Ml`: ML.NET. See `docs/ml-classification.md`.
  - `AimModelTrainer` trains one FastTree binary model per category.
  - `AimModelStore` saves and loads bundle folders.
  - `MlAimClassifier` predicts in batches.
  - `AimClassifierFactory` picks the user model, then the base model, then the rules. `SwitchableAimClassifier` lets a retrained model replace it while the app runs. `UserModelFolder` keeps user models in versioned folders behind a `current` file.
  - `OutcomeModelTrainer` and `MlOutcomePredictor` handle FirstShotHit and WonDuel, labeled from the demos.
  - `CrossValidation` evaluates leave-one-demo-out.
  - `FeatureBinReport` produces the trainer report's statistics.
- `tools/Gloria.Trainer`: the dev-time console tool that turns `_demos/` into the base model and `artifacts/training/report.md`.
- `tools/Gloria.Sprites`: the dev-time tool that renders agent models into the replay's sprites (Playwright on the installed Edge).
- `src/Gloria`: the MAUI app (exe `Gloria.exe`, Velopack packId `Gloria`). `Platforms/Windows/Program.cs` holds the custom `Main` with the Velopack hook. `Updates/UpdateSourceFactory` is the single switch point for the update source.
  - Native XAML, dark theme in `Resources/Styles/Theme.xaml`. `AppShell` has a custom sidebar; detail routes (`match`, `encounter`) are created through DI by `ServiceRouteFactory`.
  - `Pages/` and `ViewModels/` (plain `ObservableObject`, no MVVM package). `Graphics/` holds the `GraphicsView` drawables: sparklines, trace charts, the replay and the map (`MapDrawable`, using `Core/Maps/MapProjection`).
  - `Services/`: `ImportQueue` (background import), `ProfileState` (current profile), `DemoSources` (pickers, drag and drop, CS2 replays folder), `DuelOutcomes` (the outcome model on a stored encounter), `UserModelTraining` (background retraining and promotion), `Display` (wording, colors, weapon names and icons, and the number culture), `GameArt` (loads radars and agent sprites from `Resources/Raw` once, outside `Draw`), `ClickableHover` (hand cursor and hover highlight on buttons, pickers and anything with a `TapGestureRecognizer`, applied to every view's handler, so new clickable elements need nothing extra).
  - Unhandled exceptions are written to `crash.log` in the data folder, failed imports to `import-errors.log`.
  - Compare page: the current profile against a bundled pro (`Services/ReferencePlayers`, classified with the app's current aim model so both sides are judged alike) or another tracked profile.
  - MAUI pitfalls hit so far: see `docs/ui-spike.md`. The main one: use `Grid` columns, not `FlexLayout` with a percentage basis, for cards with long text.
- `tests/Gloria.Core.Tests`, `tests/Gloria.Data.Tests`, `tests/Gloria.Ml.Tests`: xUnit v3. ML tests train on seeded synthetic features (`SyntheticEncounters`). Data tests use in-memory SQLite. `Fixtures/DemoFiles` finds `_demos/` and skips tests when it is empty.
- Package versions are managed centrally in `Directory.Packages.props`.

## Tech stack (decided)

- **.NET with a MAUI UI, Windows only.** This machine has the .NET 10 SDK and the `maui-windows` workload. Target only the Windows TFM (`net10.0-windows10.0.xxxxx`). Drop the Android/iOS/MacCatalyst targets from the MAUI template so builds don't need those toolchains.
- **Installer and updates: Velopack**, not MSIX, MSI or the Store. MSIX was rejected because it can't be installed without a trusted code-signing certificate.
  - **Build settings:** the app is built **unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`) and self-contained (`<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` plus a self-contained `dotnet publish`).
  - **Packaging:** `vpk pack` (the Velopack CLI, a .NET global tool) turns the publish output into a `Setup.exe`, which installs per-user to `%LocalAppData%\<packId>\` with no admin rights needed. Always pack `dotnet publish` output, never `bin/`.
  - **Signing:** builds are unsigned for now. SmartScreen warns once, when a user runs `Setup.exe`; self-downloaded updates don't trigger it. A signing certificate can be added to `vpk pack` later.
  - **Startup hook:** `VelopackApp.Build().Run()` must be the first thing that runs. MAUI on Windows generates its own `Main`, so define `DISABLE_XAML_GENERATED_MAIN` in the Windows project and write a custom `Program.Main` that calls Velopack before starting the WinUI app.
  - **Update source:** GitHub Releases on `xonium/Gloria` through Velopack's `GithubSource` (stable releases only), published by `.github/workflows/release.yml` with `vpk upload github`. The repo must stay public, since no token is shipped in the app; a separate public releases-only repo would also work. `GLORIA_UPDATE_DIR` swaps in a local release folder for testing. `Updates/UpdateSourceFactory` is the one place to change either.
- **Embedded database: SQLite**, through `Microsoft.Data.Sqlite` or EF Core's SQLite provider. The SQLite native library ships with the NuGet package, so the app needs no separate database install.
  - **Location:** create the file at first run in a per-user folder **outside the Velopack install directory**, e.g. `%LocalAppData%\Gloria.Data\`. Velopack replaces the app folder on every update, so a database stored there would be lost.
  - **Migrations:** schema changes go through migrations that run at startup, because installed copies keep their old database across updates.
- **Aim classification: ML.NET** (`Microsoft.ML` + `Microsoft.ML.FastTree`), one binary model per category.
  - Labels come from `RuleBasedAimLabeler` first. User corrections override them, with a higher example weight.
  - A base model ships with the app, and the user's retrained model lives in `%LocalAppData%\Gloria.Data\models\user\`.
  - ML.NET is not trim-safe, so don't enable trimming.
  - Bump `EncounterFeatures.SchemaVersion` whenever the features change; saved models with another version are refused.
  - Design and next steps: `docs/ml-classification.md`.
- **Demo parsing in .NET:** prefer a managed CS2 parser such as `DemoFile.Net` (`DemoFile.Game.Cs` package). That keeps the MSI free of native interop. Choosing `demoparser2` instead would mean shipping its native library and wrapping it.

## Solution layout (intended)

Each layer from the domain model below gets its own project, so analysis code can be tested without MAUI:

- A core class library holding ingestion, encounter segmentation, aim metrics and aggregation. It must not reference MAUI.
- A data project holding the SQLite schema and repositories. It stores demo metadata (file hash, map, date, players), parsed per-tick data or a cache of it, encounters, and computed metrics. Key demos by content hash, so re-importing a demo is detected and analysis can be re-run when the metric code changes.
- The MAUI app. It only calls the core and data projects, and it hosts the Velopack startup hook and update check.
- Test projects for the core library. Tests must not depend on the 200 MB+ demos in `_demos/`. Use small extracted fixtures, or skip tests when the demos are missing.

Parsing a demo takes seconds or more. Run imports off the UI thread, show progress, and support importing many demos at once.

## What the application does

Gloria analyzes Counter-Strike 2 demo files to show one player how they aim and how to improve. The user loads one or more demos and picks a player (by SteamID). The app breaks each match into rounds, and each round into **combat encounters** (engagements). It scores every encounter, then aggregates across many demos so the player can see whether they are getting better or worse over time.

The questions each encounter must answer:

1. **Overshoot / undershoot.** As the crosshair moves toward an enemy, does it go past the target and come back (overshoot), or stop short and need small corrections (undershoot)?
2. **Stopping before shooting (counter-strafing).** Was the player's velocity low enough for accurate fire when they shot? Moving shots are inaccurate in CS2.
3. **Haste.** After the enemy became visible, did the player fire too early, before their crosshair was on target and/or before they had stopped? Or was their reaction slow?

The UI shows rounds and their encounters, so the user can drill down from trends across demos, to a match, to a round, to a single encounter.

## Domain model (intended layering)

Keep these layers separate. Parsing is slow, and analysis heuristics will change often:

- **Demo ingestion**: parse `.dem` into normalized per-tick player state (position, velocity, view angles yaw/pitch, active weapon, alive/team) and events (`weapon_fire`, `player_hurt`, `player_death`, round start/end, spotted/visibility). Cache the parsed output so the analysis can be re-run without re-parsing the demo.
- **Encounter segmentation**: turn the tick stream into encounters between the analyzed player and an opponent. An encounter runs from first visibility/contact to a kill, death, disengage, or timeout.
- **Aim metrics** (per encounter): the angular error between the view direction and the direction to the enemy over time, overshoot/undershoot classification, velocity at each shot, time from first visibility to first shot, and time from first visibility to crosshair-on-target.
- **Aggregation**: per-round, per-match and cross-demo trends for the selected player.
- **UI**: consumes the aggregated and per-encounter results. It must not parse demos itself.

## Demo data facts

- Files in `_demos/` are CS2 Source 2 demos: the magic header is `PBDEMS2`, protobuf-based, roughly 200–270 MB each. Never read them into context or open them with text tools. Process them only through a demo parser.
- Use an existing CS2-capable parser rather than writing one. Examples are `demoparser2` (Rust core, with Python/JS bindings) and `demoinfocs-golang` v4+.
- Demos store state at a fixed tick rate, but CS2 uses sub-tick input, so a shot can fall between recorded ticks. Account for this when you line up shots with view angles and velocity.
- Recorded view angles do not equal the bullet direction: recoil and aim punch offset shots. Keep "where the crosshair was" separate from "where the shot went".
- The speed at which a player counts as "stopped" depends on the weapon (it is a fraction of that weapon's max movement speed). Take these thresholds from weapon data; don't hardcode one global value.
- The game's "spotted" state is not true line of sight. Treat visibility-based timings (reaction time, haste) as approximations, and keep the visibility source swappable.
- Keep `_demos/` out of version control. The files are too large.
- Measured on these demos with DemoFile.Net 0.45:
  - **Spotted state:**
    - Read `pawn.EntitySpottedState.SpottedByMask` on the target. Observer bit = `controller.EntityIndex - 1`.
    - It updates only about every 33 ticks, so visibility onsets are uncertain by up to ~0.5 s.
  - **Event timing:**
    - `player_hurt` lands on the same tick as its `weapon_fire`.
    - The view angles at the tick *before* the fire tick match where the shot went best.
  - **Weapon names:**
    - Fire events and item names carry the `weapon_` prefix; hurt and death events don't.
    - Normalize with `StaticWeaponData.Normalize`.
    - `player_hurt` names the base item, not the variant: an M4A1-S hit says `m4a1` and a USP-S hit `hkp2000`, while `weapon_fire` and `player_death` say `m4a1_silencer` / `usp_silencer`. Match fires to hurts with `StaticWeaponData.SameGun`. Before segmenter version 3 every M4A1-S and USP-S hit counted as a miss.
    - The pawn's active item name (`ActiveWeapon`) does name the variant, so `TraceDamage` takes the attacker's held weapon when it is the same gun.
  - **Damage:** `dmg_health` is not exact: an 18-damage hit can kill at 19 HP. Use the per-tick `Health` for health, and `dmg_health` only as the hit's reported damage.
  - **Maps:** radar pixel = ((x − pos_x) / scale, (pos_y − y) / scale) on the 1024 px radar. Nuke, Vertigo and Train have a lower level, below `lowerBelow` altitude in `maps.json`.
  - **API pitfall:** `parser.GameRules` throws until the game rules entity exists.
  - **First round:**
    - These demos start recording inside the first live round's freeze time, with no `round_start` for it.
    - `round_announce_match_start` lands on that round's freeze-end tick, so it marks the round as live rather than ending a warmup before it.
    - `RoundTracker` handles this; before parser version 5 the first round was dropped.
  - **Aim punch (recoil):**
    - Read `pawn.AimPunchServices.PredictableBaseAngle` and `PredictableBaseAngleVel`. They are networked only when a shot changes them, stamped at `PredictableBaseTick + PredictableBaseTickInterpAmount` in half ticks (divide by 128 for seconds; `CurrentGameTick` divides by 64).
    - Evolve them to the sample time with the game's decay (`Recoil.Evolve`: exponential 8, linear 18°/s, velocity 4.5). `DemoFileDemoParser` stores the evolved punch per tick.
    - **DemoFile.Net 0.45 decodes these `QAngle`s as 32-bit quantized angles**, so they read around 90° or 270°. They hold plain floats: recover them from the bits (`DemoFileDemoParser.FloatField`). `RecoilDemoTests` fails if a parser update changes this.
    - Bullet direction = view angles on the tick before the fire tick + 2 × punch (`Recoil.Scale`, CS2's weapon_recoil_scale). The fit on 1,923 hits in 5 demos gives k = 1.9 overall, and 1.75–2.15 at every position in a burst.
    - With recoil, the median error from the bullet to the victim's body falls from 0.77° to 0.47°, and hits on the body rise from 64% to 79%. For shots 6–9 of a spray the error falls from 3.60° to 0.54°.
    - Not the aim punch: `CameraServices.CsViewPunchAngle` is the camera kick. The scale it would need grows from 0 at the second shot to over 3 in long sprays. `UnpredictableBaseAngle` (probably punch from taking damage) made the fit worse and is ignored.
    - Random spread isn't in the demo, so the bullet direction is crosshair plus recoil only.
