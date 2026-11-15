# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

Scaffolded. The projects, the Velopack startup hook, update source, database location and migrations, and the DemoFile parser (events and players only) are in place. Encounter segmentation, per-tick state collection, aim metrics and aggregation are interfaces or stubs.

## Commands

Run from the repo root. `global.json` pins the SDK and opts `dotnet test` into Microsoft.Testing.Platform (xUnit v3), so test filters use xUnit's MTP options, not VSTest's `--filter`.

- Build: `dotnet build Gloria.slnx`
- Test everything: `dotnet test`
- Skip tests that parse the real demos: `dotnet test -- --filter-not-trait "Category=Demo"`. Those tests also skip on their own when `_demos/` has no `.dem` files.
- Run a single test class or method: `dotnet test --project tests/Gloria.Core.Tests --filter-class "Gloria.Core.Tests.Metrics.AimMathTests"`, or `--filter-method "*YawDelta*"`
- Run the app (unpackaged): `dotnet build src/Gloria` and then start `src/Gloria/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Gloria.exe`, or press F5 in Visual Studio
- Add a migration: `dotnet tool restore`, then `dotnet ef migrations add <Name> -p src/Gloria.Data -s src/Gloria.Data -o Migrations`. `DesignTimeDbContextFactory` lets `dotnet ef` run without the MAUI app.
- Package: `./build/pack.ps1 -Version 0.1.0`. This publishes to `artifacts/publish/` and runs `vpk pack` into `releases/`, producing `Gloria-win-Setup.exe`. Keep the `Velopack` NuGet version equal to the `vpk` version in `.config/dotnet-tools.json`.
- Test in-app updates: install with Setup.exe, then pack a higher version and copy `releases/` to `%LocalAppData%\Gloria.Releases`, or set `GLORIA_UPDATE_DIR` to point at it.

## Solution layout (actual)

- `src/Gloria.Core`: domain layers as namespaces: `Ingestion` (`IDemoParser`, `DemoFileDemoParser`, `DemoHasher`, normalized models), `Visibility`, `Encounters`, `Metrics` (`AimMath`), `Weapons`, `Aggregation`. It has no MAUI reference.
- `src/Gloria.Data`: EF Core SQLite: `GloriaDbContext`, `Migrations/`, `DatabaseLocation` (`%LocalAppData%\Gloria.Data\gloria.db`), and `DatabaseInitializer.Migrate`, which runs at startup.
- `src/Gloria`: the MAUI app (exe `Gloria.exe`, Velopack packId `Gloria`). `Platforms/Windows/Program.cs` holds the custom `Main` with the Velopack hook. `Updates/UpdateSourceFactory` is the single switch point for the update source.
- `tests/Gloria.Core.Tests`, `tests/Gloria.Data.Tests`: xUnit v3. Data tests use in-memory SQLite. `Fixtures/DemoFiles` finds `_demos/` and skips tests when it is empty.
- Package versions are managed centrally in `Directory.Packages.props`.

## Tech stack (decided)

- **.NET with a MAUI UI, Windows only.** This machine has the .NET 10 SDK and the `maui-windows` workload. Target only the Windows TFM (`net10.0-windows10.0.xxxxx`). Drop the Android/iOS/MacCatalyst targets from the MAUI template so builds don't need those toolchains.
- **Installer and updates: Velopack**, not MSIX, MSI or the Store. MSIX was rejected because it can't be installed without a trusted code-signing certificate.
  - **Build settings:** the app is built **unpackaged** (`<WindowsPackageType>None</WindowsPackageType>`) and self-contained (`<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` plus a self-contained `dotnet publish`).
  - **Packaging:** `vpk pack` (the Velopack CLI, a .NET global tool) turns the publish output into a `Setup.exe`, which installs per-user to `%LocalAppData%\<packId>\` with no admin rights needed. Always pack `dotnet publish` output, never `bin/`.
  - **Signing:** builds are unsigned for now. SmartScreen warns once, when a user runs `Setup.exe`; self-downloaded updates don't trigger it. A signing certificate can be added to `vpk pack` later.
  - **Startup hook:** `VelopackApp.Build().Run()` must be the first thing that runs. MAUI on Windows generates its own `Main`, so define `DISABLE_XAML_GENERATED_MAIN` in the Windows project and write a custom `Program.Main` that calls Velopack before starting the WinUI app.
  - **Update source, phase 1:** no GitHub repo exists yet. Packaging produces `Setup.exe` locally, and in-app updates can be tested against a local release folder.
  - **Update source, phase 2:** GitHub Releases. The app uses Velopack's `GithubSource`, and releases are published with `vpk upload github`. The releases repo must be public, since no token is shipped in the app; a separate public releases-only repo is fine. Keep the update source behind one setting or class so switching it is a single change.
- **Embedded database: SQLite**, through `Microsoft.Data.Sqlite` or EF Core's SQLite provider. The SQLite native library ships with the NuGet package, so the app needs no separate database install.
  - **Location:** create the file at first run in a per-user folder **outside the Velopack install directory**, e.g. `%LocalAppData%\Gloria.Data\`. Velopack replaces the app folder on every update, so a database stored there would be lost.
  - **Migrations:** schema changes go through migrations that run at startup, because installed copies keep their old database across updates.
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
