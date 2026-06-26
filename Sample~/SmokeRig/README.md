# SDK Smoke Rig

A one-file `MonoBehaviour` panel that exercises every public surface of
the Kraty Unity SDK against your configured backend. Use it to catch
regressions in **shipped player builds** before pushing to your studio
— the editor unit tests don't run the IL2CPP / Mono code-paths that
have historically bitten this package (System.Text.Json stripping,
missing meta files, PlayerPrefs-off-thread).

## Setup

1. **Import the sample.** Package Manager → Kraty SDK → Samples → "SDK
   Smoke Rig" → Import. Unity copies it into
   `Assets/Samples/Kraty SDK/<version>/SDK Smoke Rig/`.
2. **Add to a scene.** Create an empty `GameObject` (any scene, any
   project) and add the `KratySdkSmokeRig` component.
3. **Fill the inspector.**
   - `Api Key` — a `client_sdk` API key (use a **test-env** key for
     staging; the rig is fine against prod too but writes count for
     real).
   - `Base Url` — default `https://api.staging.kraty.io`.
   - `Event Key` — optional; leave blank and click `List` to
     auto-pick the first active event.
   - `Shared Leaderboard Key` — e.g. `weekly_global`. Required for
     the shared-leaderboard buttons.
   - `Shared Leaderboard Segment` — required only for segmented
     boards.
   - `Lobby Id` — optional, only for exercising `Lobbies.ReadAsync`.
4. **Hit Play.** The on-screen panel renders via IMGUI on top of
   whatever scene is loaded. Click `Run ALL` to execute the standard
   sequence, or use individual buttons to exercise one surface.

## What it covers

| Section | Methods exercised |
| --- | --- |
| Identity | `EnsureIdentityAsync`, `LogoutAsync` |
| Events | `Events.ListForPlayerAsync`, `StartAsync`, `ProgressAsync` |
| Leaderboards | `Leaderboards.ReadAsync`, `ReadSharedAsync`, `ListSharedPeriodsAsync`, `Subscribe` |
| Grants | `Grants.ListPendingAsync`, `CollectAllAsync` |
| Lobbies | `Lobbies.ReadAsync` |
| Inventory + Wallet | `Inventory.ListAsync`, `Wallet.ListAsync` |

## Pre-ship checklist

For each SDK release:

1. Import the sample into a Unity 2022 LTS project.
2. Run in the editor against staging — every button green.
3. Build a Player (any target — Android / iOS / standalone is fine;
   IL2CPP is the one that matters). Run the same buttons.
4. Verify no `MissingMethodException`, no `TypeLoadException`, no
   off-thread `PlayerPrefs` errors in the Player log.

If step 3 surfaces a stripping issue that step 2 didn't, that's the
class of bug the smoke rig exists to catch.
