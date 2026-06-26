# Changelog

All notable changes to `app.kraty.sdk` (Kraty Unity SDK) live here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) +
[SemVer](https://semver.org/).

## [0.4.0] — 2026-06-26

### Changed (BREAKING)

- **Leaderboard naming reshaped.** The "shared" / "cross-event"
  configurable boards are now the primary `Leaderboards` surface,
  and the per-event-window boards moved under a new
  `EventLeaderboards` resource. The previous shape conflated the
  two and forced every game to know what "shared" meant.
  Migration:
  - `kraty.Leaderboards.ReadAsync(uuid, LeaderboardReadOptions)`
    → `kraty.EventLeaderboards.ReadAsync(uuid, EventLeaderboardReadOptions)`
  - `kraty.Leaderboards.LiveAsync(uuid)`
    → `kraty.EventLeaderboards.LiveAsync(uuid)`
  - `kraty.Leaderboards.Subscribe(uuid, …)`
    → `kraty.EventLeaderboards.Subscribe(uuid, …)`
  - `kraty.Leaderboards.ReadSharedAsync(key, SharedLeaderboardReadOptions)`
    → `kraty.Leaderboards.ReadAsync(key, LeaderboardReadOptions)`
  - `kraty.Leaderboards.ListSharedPeriodsAsync(key)`
    → `kraty.Leaderboards.ListPeriodsAsync(key)`
- **Types renamed in lockstep:**
  - `SharedLeaderboard` → `Leaderboard`
  - `SharedLeaderboardPeriod(s)` → `LeaderboardPeriod(s)`
  - `SharedLeaderboardReadOptions` → `LeaderboardReadOptions`
  - `Leaderboard` (old per-event shape) → `EventLeaderboard`
  - `LeaderboardReadOptions` (old per-event shape) → `EventLeaderboardReadOptions`
  - `Leaderboard.SharedLeaderboardId` JSON property still wires
    from the backend's `sharedLeaderboardId` field; the C# property
    is now just `LeaderboardId` (mirroring the new top-level type
    name).
- Backend URLs are unchanged (`/sdk/v1/leaderboards/:uuid` +
  `/sdk/v1/shared-leaderboards/:key`); this is purely an SDK-side
  rename.

## [0.3.4] — 2026-06-26

### Added

- **`SDK Smoke Rig` sample.** A single-file `MonoBehaviour` panel
  importable via Package Manager → Samples that exercises every
  public surface of the SDK (Identity / Events / Leaderboards
  per-event + shared / Grants / Lobbies / Inventory / Wallet)
  against a configured backend, with a `Run ALL` button for one-shot
  smoke-testing. IMGUI-only so no scene, prefab, or UI Toolkit
  dependency. The point is catching regressions that only manifest
  in shipped Player builds (IL2CPP stripping, off-thread
  PlayerPrefs, missing meta files) — bugs editor unit tests miss
  by definition. Each release should be sanity-checked through this
  rig in both Editor and a real Player build before publish.

## [0.3.3] — 2026-06-26

### Added

- **`Leaderboards.ReadSharedAsync(key, opts?, ct?)`** — snapshot read for
  configurable cross-event leaderboards addressed by their game-scoped
  key (e.g. `"weekly_global"`). Hits `GET /sdk/v1/shared-leaderboards/:key`.
  Use this for any board defined in the dashboard's Leaderboards page;
  reserve `ReadAsync` for the auto-created per-event-window boards
  addressed by UUID. Supports `Limit`, `Segment`, `Period`
  (`"current"` or an ISO timestamp from `ListSharedPeriodsAsync`),
  and `IncludeSelf` + `ExternalId`. Passing the key into the old
  `ReadAsync` previously crashed the server with a uuid-syntax 500;
  the backend now returns a clear 400 pointing at this method.
- **`Leaderboards.ListSharedPeriodsAsync(key, limit?, ct?)`** — newest-first
  list of finalized snapshot periods for a shared board. Pair with
  `ReadSharedAsync(key, opts.Period = period.PeriodStartedAt)` to render
  "last week's top 10" UI.
- New DTOs: `SharedLeaderboard`, `SharedLeaderboardPeriod`,
  `SharedLeaderboardPeriods`, `SharedLeaderboardReadOptions`.

## [0.3.2] — 2026-06-25

### Fixed

- **`PlayerPrefsSecretStore` is now main-thread-safe.** UnityEngine's
  `PlayerPrefs` throws when read or written off the player loop
  (`can only be called from the main thread`), and the SDK's
  identity resolver awaits its network I/O with
  `ConfigureAwait(false)` — the continuations that touched
  `PlayerPrefs` were landing on thread-pool threads in shipped
  builds and crashing the auth bootstrap. The store now captures
  the player-loop `SynchronizationContext` at construction and
  marshals every read/write/remove back onto it via a small
  `OnMainThread(...)` helper, returning a `Task` that completes
  when the main thread finishes the prefs op. Inline fast-path
  when the caller is already on the main thread keeps the cost
  to one allocation per access (the wrapper `Task`).

## [0.3.1] — 2026-06-16

### Fixed

- **Shipped `.meta` files for every package asset.** The package's
  `.gitignore` carried a blanket `*.meta` rule, so the published UPM
  package contained no meta files. Unity then generated fresh random
  GUIDs on each consumer's import, breaking the assembly-definition
  reference and emitting import errors on the SDK scripts. The
  Runtime scripts, folders, the asmdef, `package.json`, README and
  CHANGELOG now ship tracked metas with stable GUIDs.

## [0.3.0] — 2026-06-15

### Changed (BREAKING)

- **JSON layer ported from `System.Text.Json` to Newtonsoft.Json**
  (Json.NET) so the package actually works in Unity. Unity does not
  ship `System.Text.Json`, and `System.Text.Json`'s reflection path
  is stripped by IL2CPP on iOS / Android / WebGL — the previous SDK
  compiled in the editor but crashed on first deserialize in a
  shipped build.
- `package.json` now declares a dependency on
  `com.unity.nuget.newtonsoft-json` (3.2.1) — UPM auto-installs it
  on first import. The runtime asmdef references
  `Unity.Nuget.Newtonsoft.Json`.
- Public API types previously typed as `System.Text.Json.JsonElement`
  (the LocalizedString-ish `Name` / `Description` fields on
  `EventListing`, `CatalogItem`, `CatalogCurrency`,
  `RewardBundlePreview`; the free-form `Metadata` / `Attributes` /
  `Parameters` / `EntryRequirement` bags) now use
  `Newtonsoft.Json.Linq.JToken`. **Migration:** swap
  `value.GetString()` → `(string?)value`, `.GetInt32()` →
  `(int)value`, `.GetBoolean()` → `(bool)value`, or use
  `value.Value<T>()` / `value.ToObject<T>()` for richer
  conversions. The localized `Name` fields are now declared
  `JToken?` because the wire format may legitimately omit them.
- Internal `KratyClient.JsonSerializerOptions` getter renamed to
  `JsonSerializerSettings` and returns Newtonsoft's
  `JsonSerializerSettings` type — only consumed by the SSE stream
  helper, not in the public API surface.

### Added

- `ISecretStore` interface for per-player secret persistence, plus
  `InMemorySecretStore` (tests) and `PlayerPrefsSecretStore`
  (Unity-only, wraps `UnityEngine.PlayerPrefs`).
- `Kraty.ConnectAsPlayerAsync(...)` factory — one-call player
  bootstrap: reads the cached secret if present, otherwise calls
  `Players.RegisterAsync`, auto-rotates on 409 in dev/test envs,
  and returns a `Kraty` wired with `X-Player-Secret` on every
  request.
- `PlayersClient.RegisterAsync(externalId, force, ct)` —
  zero-trust per-player registration.
- `InventoryClient` — `ListAsync`, `ConsumeAsync` for
  platform-managed inventory.
- `WalletClient` — `ListAsync`, `DebitAsync` for platform-managed
  wallets (credit is server-API only).
- `GrantsClient.CollectAllAsync(externalId, ct)` — burn down the
  pending-grants queue in one call; per-grant failures are
  captured in `CollectAllResult.Failures` without aborting the
  sweep.
- `LeaderboardsClient.LiveAsync(id, ct)` — Server-Sent Events
  subscription with `OnEvent` / `OnError` callbacks and
  `CancelAsync`.
- `KratyClientOptions.PlayerSecret` + `WithPlayerSecret(...)` copy
  method — the SDK auto-attaches `X-Player-Secret` to every
  request when set.
- Typed error helpers on `KratyApiError`:
  `IsInsufficientEntryCost`, `IsPlayerSecretInvalid`,
  `IsPlayerAlreadyRegistered`, `IsEntryRequirementFailed` (in
  addition to the existing `IsLobbyForming`).
- DTOs: `EntryCost`, `EntryCostCurrency`, `EntryCostItem`,
  `PlayerRegistration`, `PlayerItemHolding`, `PlayerWalletHolding`,
  `ConsumeItemInput` / `Result`, `DebitWalletInput` / `Result`,
  plus `Lobby.BotSlots` + `Lobby.FilledSlots` for smooth lobby
  fill UI.
- 24 new xUnit tests covering player auth, register flows,
  inventory/wallet, CollectAll happy path and partial failure,
  EntryCost decoding, and Lobby bot-slot projection (41 tests
  total).

### Changed

- `EventListing` gained `Type`, `LeaderboardMode`, `Metrics`,
  `EntryRequirement`, `EntryCost`, and `IsLobbyMatched` —
  bringing the wire shape to parity with the backend's listing
  response.
- `KratyErrorCode` expanded from 19 to 25 constants; missing
  codes (`player_secret_invalid`, `player_already_registered`,
  `insufficient_entry_cost`, `entry_requirement_failed`,
  `invalid_entry_requirement`, `max_daily_attempts_reached`) are
  now first-class.

## [0.0.1]

- Initial scaffold: `KratyClient` with retry / idempotency,
  `EventsClient`, `LeaderboardsClient` (read), `GrantsClient`
  (list/claim/open), `LobbiesClient`, `GrantPolling`,
  `LobbyPolling`.
