# UI spike: native MAUI XAML, vertical slice

Goal: find out whether the design (canvas "Gloria UI — first pass") can be built in native MAUI XAML on real data, and what the storage model costs.

**Verdict: yes.** Every screen in the slice is standard MAUI controls plus `GraphicsView` drawables for the charts and the replay. Nothing needed a web view or a custom native control. Dense layouts take care (see the gotchas below), but they work.

## What was built

- **Storage:** migration `EncountersAndProfiles`, adding encounters, rounds, profiles and per-round sides.
  - Each demo is analyzed **once at import, for all ten players**: encounters, metrics, classification and a compact per-tick trace (`EncounterTraceBuilder`, `TraceCodec`).
  - Nothing parsed is cached, so the `.dem` file isn't needed afterwards.
- **Import:** `DemoImporter` hashes the file, skips it if already imported, then parses, analyzes, classifies and saves. `ImportQueue` runs one demo at a time in the background and reports progress on the UI thread. Folders are rescanned at startup; files already imported (same path and size) are skipped without hashing.
- **Read side:** `GloriaReadModel` holds all queries. Every query takes the viewed profile's SteamID.
- **App:**
  - Shell with a custom sidebar: profile switcher, navigation, import status and analysis version.
  - Screens:
    - Welcome (empty state): folder picker, file picker, drag and drop, and a CS2 replays folder lookup.
    - Folders & player: candidates ordered by how many demos they appear in, or a pasted SteamID64.
    - Overview.
    - Matches, with the import queue.
    - Match: round strip, encounters per round, habits, opponents.
    - Encounter:
      - A replay to scale with play, step and speed controls.
      - Error and speed charts with a playhead, plus a shots table.
      - Verdicts and classifier scores.
    - Progress: plain per-demo lines.

## Measurements (23 demos in `_demos/`, 4.6 GB)

| | |
|---|---|
| Import time | 49 s in total, about 2 s per demo, mostly parsing |
| Encounters stored | 6,438, about 280 per demo across all ten players |
| Traces | 6.5 MB, about 280 kB per demo (half floats, about 10 bytes a tick) |
| Database after VACUUM | 9.4 MB, about 0.4 MB per demo |

Storing every player's encounters and traces is cheap, so the "analyze once, no parse cache" decision holds. Adding a profile later needs no re-import.

## MAUI gotchas found

1. **`FlexLayout` with percentage `Basis` doesn't constrain label width.** Long text doesn't wrap and cards get clipped. Use `Grid` columns for side-by-side cards; keep `FlexLayout` for tiles and chips with short text.
2. **Shell route pages are created with `Activator`, not DI.** `Routing.RegisterRoute(route, typeof(Page))` fails for pages with constructor parameters. `ServiceRouteFactory<T>` in `AppShell.xaml.cs` resolves them from the container.
3. **Don't inject `AppShell` into `App`'s constructor.** Its XAML looks up `StaticResource`s before `App.InitializeComponent` has loaded them. Resolve it in `CreateWindow`.
4. **SQLite can't `ORDER BY` a `DateTimeOffset` in SQL.** Order on the client, or by `Id`.
5. **Unhandled UI exceptions crash with `0xc000027b` and no managed details.** `Platforms/Windows/App.xaml.cs` now writes them to `crash.log` in the data folder.
6. **Desktop screenshot tools capture WinUI content as solid black** (`PrintWindow`, `CopyFromScreen`). Use the in-app capture in `Services/DevHooks.cs` instead (below).
7. **Numbers follow the Windows locale.** On sv-SE they show as "63 %" and "0,19 s". Decide whether to keep that or format invariantly.
8. **`Preferences` (current profile, saved folders) live in MAUI's per-app store, not next to the database.** `GLORIA_DATA_DIR` doesn't isolate them. Move them into a settings table.

## Running against scratch data

```powershell
$env:GLORIA_DATA_DIR = "C:\temp\gloria-scratch"     # own database, crash.log
$env:GLORIA_START_ROUTE = "encounter?id=6092"       # optional: open a screen directly
$env:GLORIA_CAPTURE = "C:\temp\encounter.png"       # optional: save a PNG once it has rendered
$env:GLORIA_CAPTURE_SHELL = "1"                     # optional: include the sidebar
src\Gloria\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Gloria.exe
```

## Not done, or different from the design

- **Recoil:** done since the spike.
  - The replay draws a ring where the crosshair was on the tick before each shot, and a diamond where the bullet went (crosshair plus recoil), joined by a dashed line. The shots table gives both offsets from the body.
  - Random spread isn't in the demo, so it isn't modeled, and the screen says so.
  - Traces stored before this (`TraceCodec` format 1) keep the old drawing until the demo is re-analyzed. Format 2 adds the bullet direction, about 14 bytes a tick instead of 10.
- **Corrections** aren't stored. The encounter screen only explains them.
- **Outcome model:** "What cost the duel" isn't shown, because `MlOutcomePredictor` isn't registered.
- **Progress:** plain lines only. Rolling medians, the weapon-class table and duel costs are still to come.
- **Overview and Matches:** the period selector and the match filters are missing.
- **Profile switcher:** a plain picker rather than the designed menu.
- **Folder watching:** startup rescan only; there's no file system watcher.
- **Not exercised by script:** the folder and file pickers, drag and drop, and the CS2 replays lookup. These need a manual click-through.
- **Played-at date:** taken from the file's modified time, so demos copied together all show the same date.

## Suggested next steps

1. Move preferences into the database and decide on number formatting.
2. Store corrections per profile and wire them into retraining.
3. Finish Progress, and the Overview and Matches filters.
4. Click-test import on a clean machine, including an installed (Velopack) build.
