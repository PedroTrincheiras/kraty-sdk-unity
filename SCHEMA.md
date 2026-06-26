# Kraty Unity SDK — public surface (v0.4.0)

Canonical method + type listing for `app.kraty.sdk`. Update this file in
the same commit as any signature change so a single grep tells you
exactly what's available.

All methods sit on the `Kraty` facade unless noted otherwise. Every
async method takes an optional trailing `CancellationToken ct = default`.

## `Kraty` facade

```csharp
Kraty(KratyClientOptions opts)
void Dispose()

string? ActiveExternalPlayerId { get; }
Task<(string ExternalPlayerId, string Secret)> EnsureIdentityAsync(CancellationToken ct = default)
Task LogoutAsync(CancellationToken ct = default)
Task SignInAsync(string externalPlayerId, string secret, CancellationToken ct = default)

// Static identity helpers
static Task<StoredIdentity?> ReadStoredIdentityAsync(ISecretStore secretStore, CancellationToken ct = default)
static Task RestoreStoredIdentityAsync(KratyClient client, StoredIdentity identity, ISecretStore secretStore, CancellationToken ct = default)
static Task ClearStoredIdentityAsync(ISecretStore secretStore, CancellationToken ct = default)
static Task<Kraty> ConnectAsPlayerAsync(KratyClientOptions opts, string? externalPlayerId, ISecretStore secretStore, bool autoRotateOnConflict = false, CancellationToken ct = default)
```

## `kraty.Events` — `EventsClient`

```csharp
Task<List<EventListing>> ListForPlayerAsync(string? @as = null, CancellationToken ct = default)
Task<StartAttemptResponse> StartAsync(string eventKey, Dictionary<string, object?>? playerContext = null, string? idempotencyKey = null, string? @as = null, CancellationToken ct = default)
Task<ProgressResult> ProgressAsync(string eventKey, string attemptId, ProgressInput input, string? @as = null, CancellationToken ct = default)
```

## `kraty.Leaderboards` — `LeaderboardsClient`

The dashboard-configured cross-event boards (weekly / monthly /
all-time, optionally segmented). Addressed by stable game-scoped
**key**.

```csharp
Task<Leaderboard>        ReadAsync(string key, LeaderboardReadOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardPeriods> ListPeriodsAsync(string key, int? limit = null, CancellationToken ct = default)
```

`LeaderboardReadOptions`:
- `int? Limit` — 1–200, default 50 server-side
- `string? Segment` — bucket value for segmented boards (REQUIRED on segmented boards)
- `string? Period` — `"current"` (default) or an ISO timestamp from `LeaderboardPeriod.PeriodStartedAt`
- `bool IncludeSelf` — when true, response includes `self: { rank, score }` (live periods only)
- `string? ExternalId` — required when `IncludeSelf` is true; lazily resolved from the active player otherwise

## `kraty.EventLeaderboards` — `EventLeaderboardsClient`

The auto-generated per-event-window leaderboard. Addressed by the
**UUID** returned in `Events.StartAsync(...)`'s
`attempt.leaderboardId`. Includes Server-Sent Events live streaming.

```csharp
Task<EventLeaderboard>          ReadAsync(string leaderboardId, EventLeaderboardReadOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardStream>         LiveAsync(string leaderboardId, CancellationToken ct = default)
LiveLeaderboardSubscription     Subscribe(string leaderboardId, Action<LeaderboardStreamEvent> onEvent, SubscribeOptions? opts = null)
```

`EventLeaderboardReadOptions`:
- `int? Limit` — 1–200, default 50 server-side
- `bool IncludeSelf`
- `string? ExternalId`

`SubscribeOptions`:
- `int PollIntervalMs` — default 15_000; 0 disables polling (SSE-only)
- `Action<Exception>? OnError`

`LiveLeaderboardSubscription` exposes `Task CancelAsync()` /
`void Dispose()`. Callbacks fire on the HTTP background thread —
marshal to Unity's main thread before touching `UnityEngine` APIs.

## `kraty.Grants` — `GrantsClient`

```csharp
Task<List<Grant>>       ListPendingAsync(int? limit = null, string? @as = null, CancellationToken ct = default)
Task<Grant>             ClaimAsync(string grantId, string? idempotencyKey = null, string? @as = null, CancellationToken ct = default)
Task<OpenCrateResponse> OpenAsync(string grantId, string? idempotencyKey = null, string? @as = null, CancellationToken ct = default)
Task<CollectAllResult>  CollectAllAsync(string? @as = null, CancellationToken ct = default)
```

`CollectAllResult` carries:
- `List<Grant> Opened` — crate grants whose contents were rolled
- `List<Grant> Claimed` — reward grants flipped to claimed
- `List<CollectAllFailure> Failures` — per-grant errors that didn't abort the sweep
- `bool HasFailures => Failures.Count > 0`

## `kraty.Lobbies` — `LobbiesClient`

```csharp
Task<Lobby> ReadAsync(string lobbyId, CancellationToken ct = default)
```

## `kraty.Inventory` — `InventoryClient`

```csharp
Task<List<PlayerItemHolding>> ListAsync(string? @as = null, CancellationToken ct = default)
Task<ConsumeItemResult>       ConsumeAsync(string itemKey, ConsumeItemInput input, string? @as = null, CancellationToken ct = default)
```

## `kraty.Wallet` — `WalletClient`

```csharp
Task<List<PlayerWalletHolding>> ListAsync(string? @as = null, CancellationToken ct = default)
Task<DebitWalletResult>         DebitAsync(string economyKey, DebitWalletInput input, string? @as = null, CancellationToken ct = default)
```

## `kraty.Players` — `PlayersClient`

```csharp
Task<PlayerRegistration> RegisterAsync(string externalPlayerId, bool force = false, CancellationToken ct = default)
```

## `kraty.Catalog` — `CatalogClient`

```csharp
Task<Catalog> GetAsync(CancellationToken ct = default)
```

## Polling helpers (static)

```csharp
GrantPolling.PollPendingAsync(GrantsClient grants, string? @as = null, GrantPollOptions? opts = null, CancellationToken ct = default)
LobbyPolling.UntilActiveAsync(LobbiesClient lobbies, string lobbyId, LobbyPollOptions? opts = null, CancellationToken ct = default)
```

## Secret stores

```csharp
ISecretStore                  // interface — implement for custom storage
InMemorySecretStore           // tests + non-Unity
PlayerPrefsSecretStore        // Unity, wraps UnityEngine.PlayerPrefs (default on Unity)
```

## DTOs (response shapes)

`Attempt`, `StartAttemptResponse`, `ProgressResult`, `MilestoneFired`,
`EventListing`, `EntryCost`, `EntryCostCurrency`, `EntryCostItem`,
`Leaderboard`, `EventLeaderboard`, `LeaderboardEntry`,
`LeaderboardSelf`, `LeaderboardPeriod`, `LeaderboardPeriods`,
`Grant`, `OpenCrateResponse`, `Lobby`, `ProgressInput`,
`PlayerRegistration`, `PlayerItemHolding`, `PlayerWalletHolding`,
`ConsumeItemInput`, `ConsumeItemResult`, `DebitWalletInput`,
`DebitWalletResult`, `Catalog`, `CatalogItem`, `CatalogCurrency`,
`RewardBundlePreview`, `RewardEntryPreview`, `RewardPolicySummary`,
`StoredIdentity`.

## Errors

```csharp
KratyApiError      // non-2xx response — has Code (KratyErrorCode), Status, Message, Body
KratyNetworkError  // transport / parse failure
```

`KratyApiError` boolean helpers:
`IsLobbyForming`, `IsInsufficientEntryCost`, `IsPlayerSecretInvalid`,
`IsPlayerAlreadyRegistered`, `IsEntryRequirementFailed`,
`IsNoLeaderboard`, `IsNoActiveWindow`, `IsMaxAttemptsReached`,
`IsAttemptExpired`, `IsAttemptCompleted`.

Full `KratyErrorCode` enum: see `Runtime/Errors/KratyErrorCode.cs`.
