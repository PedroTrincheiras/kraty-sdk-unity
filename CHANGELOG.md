# Changelog

All notable changes to `app.kraty.sdk` (Kraty Unity SDK) live here.
Follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) +
[SemVer](https://semver.org/).

## [Unreleased]

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
