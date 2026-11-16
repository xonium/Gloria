# Gloria.Sprites

Renders the agent models into the sprites the Encounter replay draws for the opponent. The output goes to `src/Gloria/Resources/Raw/agents/`:
- one PNG per team (`ct`, `t`), pose (`stand`, `crouch`) and 10° of yaw
- `agents.json`, which tells the replay how to place each frame to scale

Until those files exist, the replay draws a plain body.

## Importing the models

The models are not in git: `_assets/` is ignored. They are free to download under CC BY 4.0, but Sketchfab requires a signed-in account. Its download API returns 401 without one.

| Team | File | Model | Author |
|---|---|---|---|
| CT | `_assets/models/ct.glb` | [SAS / CS2 Agent Model Blue](https://sketchfab.com/3d-models/sas-cs2-agent-model-blue-7f18aaccd0ee4694a36646101a12339e) | gettan (@kill6lucius) |
| T | `_assets/models/t.glb` | [cs2 terrorist rigged](https://sketchfab.com/3d-models/cs2-terrorist-rigged-5213ffe8520d40daadd42cad3fa0e510) | roham12418 |

Get them one of two ways:
- **By hand:** sign in, choose Download, then the glTF Binary (`.glb`) format, and save the files under the names above.
- **With an API token:** take the token from sketchfab.com, Settings, Password & API, and store it as a user environment variable (`setx SKETCHFAB_TOKEN <token>`). Then request `https://api.sketchfab.com/v3/models/<uid>/download` with the header `Authorization: Token <token>`. The response holds a short-lived `glb.url` to download from.

CC BY requires credit. Keep the authors and links above with the sprites. The models are ripped from CS2, so the character art itself is Valve's. Bundling it is a deliberate choice (see `CLAUDE.md`).

## Rendering

```
dotnet run --project tools/Gloria.Sprites
```

The tool drives the installed Microsoft Edge through Playwright, with `--channel msedge`, so no browser download is needed. three.js loads from jsDelivr.

Each run prints the model's bone and animation clip names. It also writes a contact sheet per team to `artifacts/sprites/`. On the contact sheet, check two things:
- **The 0° frame faces the camera.** If it doesn't, set `yawOffset`. A model that faces −Z needs 180.
- **The pose looks natural.** If the model loads in a T-pose, pick an animation clip, or set bone rotations for each pose.

Both settings go in `_assets/models/agents.json`. For example:

```json
{
  "ct": { "yawOffset": 180, "clip": "Idle", "clipTime": 0 },
  "t": { "stand": { "LeftArm": [0, 0, 60] }, "crouch": { "LeftUpLeg": [-80, 0, 0], "LeftLeg": [100, 0, 0] } }
}
```

- Bone rotations are degrees added to the rest pose.
- Without a `crouch` pose, the standing model is squashed to crouch height.
- Commit `src/Gloria/Resources/Raw/agents/` once the sheets look right.
