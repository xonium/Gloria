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
    - Welcome (empty state): folder picker, file picker, drag and drop, and a CS2 replays folder lookup. The sidebar is hidden until something is imported.
    - Import: the same drop zone (`Controls/DemoDropZone`), the saved demo folders with the CS2 replays lookup and a rescan, and the import queue. The sidebar's import card opens it.
    - Player: candidates ordered by how many demos they appear in, or a pasted SteamID64.
    - Overview.
    - Matches.
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
7. **Numbers follow the Windows locale by default.** On sv-SE they showed as "63 %" and "0,19 s". The UI is in English, so `Display.UseCulture()` now sets an invariant culture with "63%" for every thread at startup.
8. **MAUI `Preferences` live in a per-app store, not next to the database,** so `GLORIA_DATA_DIR` didn't isolate them. Saved folders and the current profile are now in the `Settings` table (`AppSettings`). `LegacyPreferences` moves old values over once, for the default data folder only.
9. **Unpackaged, MAUI's per-app storage is `%LocalAppData%\Gloria\Gloria`, inside the Velopack install folder.** `Setup.exe` cleared it on install, and uninstall deletes it. Don't use `Preferences` or `FileSystem.AppDataDirectory` for anything that must last; use the database or `DatabaseLocation.DataDirectory`.

## Running against scratch data

```powershell
$env:GLORIA_DATA_DIR = "C:\temp\gloria-scratch"     # own database, crash.log
$env:GLORIA_START_ROUTE = "encounter?id=6092"       # optional: open a screen directly
$env:GLORIA_CAPTURE = "C:\temp\encounter.png"       # optional: save a PNG once it has rendered
$env:GLORIA_CAPTURE_SHELL = "1"                     # optional: include the sidebar
$env:GLORIA_CAPTURE_SCROLL = "700"                  # optional: scroll the page down first, in pixels
src\Gloria\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Gloria.exe
```

## Not done, or different from the design

- **Recoil:** done since the spike.
  - The replay draws a ring where the crosshair was on the tick before each shot, and a diamond where the bullet went (crosshair plus recoil), joined by a dashed line. The shots table gives both offsets from the body.
  - Random spread isn't in the demo, so it isn't modeled, and the screen says so.
  - Traces stored before this (`TraceCodec` format 1) keep the old drawing until the demo is re-analyzed. Format 2 adds the bullet direction, about 14 bytes a tick instead of 10.
- **Replay detail:** done since the spike.
  - **Zero-hit kills:** these turned out to be an attribution bug. `player_hurt` names an M4A1-S hit `m4a1` and a USP-S hit `hkp2000`, so those hits never matched their shot. Segmenter version 3 fixes it; every gun hurt in the first demo now has a shot (86 of 646 didn't).
  - **Trace format 3:** adds a `TraceScene`, about 33 bytes a tick instead of 14. The table shows both players' health, the last hit, and each player's weapon with an icon. The timeline lists utility and damage taken, and names the kill weapon.
  - **Map:** a panel left of the replay draws the radar, both paths, view cones and shot positions. Two-level maps switch radar by the player's altitude.
  - **Agent models:** pre-rendered sprites from the Sketchfab GLBs (`tools/Gloria.Sprites`), drawn to scale and turned to the opponent's facing. That's 144 frames, 3.9 MB.
    - Both models load facing the camera, in an A-pose. The CT's `tools_preview` clip gives a rifle-ready stance. The T copies the CT's torso and arms by limb direction, with a side line (shoulders, index to pinky) for the twist. Hand-set arm angles didn't read as well.
    - The crouch bends the legs, and the tool re-grounds the feet. Bone axes differ per rig: the CT's legs are mirrored, so left and right need opposite signs.
  - **MAUI:** `MauiImage TintColor` doesn't recolor PNGs, so the weapon icons are recolored in `fetch-assets.ps1`. In the app project, `IImage` is ambiguous between `Microsoft.Maui.IImage` and `Microsoft.Maui.Graphics.IImage`; alias the graphics one.
- **Corrections:** done since the spike.
  - The encounter screen has Yes/No per category. Answers override the model in every screen and feed the user's model, which retrains after 10 new answers or on "Retrain now".
  - See `docs/ml-classification.md`, "Feedback loop".
  - The "What happened" chips now follow the flags for all three questions. Before, the overshoot/undershoot chip followed the measured path, so it could disagree with the flags.
- **Outcome model:** done since the spike. The encounter screen has a "What cost the duel" card.
  - It shows the chance to win and the chance the first shot hits, with the factors that lowered them. Each factor is tagged as aim or situation.
  - `DuelOutcomes` runs the model on the stored metrics (`EncounterRecord.ToFeatures`), so nothing is re-parsed.
  - Some factors read oddly, e.g. "reached the body 0.00 s after sighting" listed as a cost. They are what the trees learned, not a display bug.
- **Progress:** done since the spike.
  - Per-demo dots with a rolling median of 5 demos (`Rolling.Median`). Each card's number is the latest median.
  - A by-weapon table, grouped by the weapon fired first.
  - "What cost your lost duels": the outcome model runs over every lost duel. It counts how often each factor was the largest cost overall, and the largest aim factor (situation factors left out).
  - A period selector (last 20, last 50, all).
- **Overview:** period selector (last 10, 20, 50 or all demos), compared with the window before. Periods count demos, not days, because file dates are unreliable. Both choices are saved in `Settings`.
- **Matches:** filters for result, map and main habit, with Clear and a "no matches" message. They are not saved.
- **The first round was missing** from every demo until parser version 5, so sides seemed to switch after round 11 and pistol-round encounters were lost.
  - Cause: `RoundTracker.MatchStart` cleared the round in progress, but the match start is announced during the first live round.
  - Re-analysis adds one round per demo and about 6% more encounters.
  - Corrections are matched without the round number, so they survive the renumbering.
- **Profile switcher:** a plain picker rather than the designed menu.
- **Folder watching:** startup rescan only; there's no file system watcher.
- **Installed build, tested 2026-11-15:** `pack.ps1` 0.2.0, silent `Setup.exe`, then the Start-menu launcher.
  - It imported all 23 demos in 50 s, classified with the shipped base model.
  - It found 0.2.1 in a local release folder (`GLORIA_UPDATE_DIR`), downloaded the 226 kB delta, and restarted into it with the data intact.
  - `Update.exe --uninstall` removed the install and its Start menu entry.
- **Updates:** an installed copy checks at startup (`UpdateNotifier`) and downloads an update right away.
  - The sidebar footer shows the progress, then "Version X is ready" with a Restart button. If an import is running, the button asks first.
  - A "Check for updates" link checks by hand, and the Welcome screen shares the same state. Development builds show "Development build" and don't check.
  - Tested installed, 0.3.0 → 0.3.1: found and downloaded at startup with no clicks, restarted, then reported "up to date".
  - A missing local release folder counts as "no updates", not a failure.
- **Not exercised by script:** the folder and file pickers, drag and drop, and the CS2 replays lookup. The pickers open system dialogs that UI Automation can't drive from here, so these need a manual click-through.
- **Played-at date:** the demo records no wall-clock time.
  - `DemoDate` reads `matchtime` from the `.dem.info` file that Valve matchmaking downloads include.
  - Otherwise it takes the earlier of the file's creation and modified times. Demos downloaded together still share a date, and the match screen marks it "(file date)".
  - "Change date" on the match screen sets it by hand (`DemoDates`). The date is kept by content hash in `PlayedAtOverrides`, so re-analysis keeps it, and "Use the automatic date" removes it.

## Suggested next steps

1. Click through the pickers, drag and drop and the CS2 replays lookup by hand, ideally on a clean machine.
