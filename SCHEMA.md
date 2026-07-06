# Kraty Unity SDK — public surface (v0.6.0)

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

// Finalization catch-up (docs/05b). Fires exactly once per board across the
// live SSE `finalized` event AND boards that ended while the player was away.
Action OnFinalized(Action<FinalizationResult> cb)         // returns unsubscribe
Task<List<FinalizationResult>> CheckFinalizationsAsync()  // call on app foreground/reconnect
Task DismissAsync(MembershipRef ref)                      // ack one handled result
Task<int> ClearReportedAsync()                            // bulk-drop delivered entries

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
**key**. Wire endpoints:

- `GET /sdk/v1/leaderboards/:key`
- `GET /sdk/v1/leaderboards/:key/standings`
- `GET /sdk/v1/leaderboards/:key/periods`
- `POST /sdk/v1/players/:externalId/leaderboards/:key/join`
- `POST /sdk/v1/players/:externalId/leaderboards/:key/score`

```csharp
Task<Leaderboard>            ReadAsync(string key, LeaderboardReadOptions? opts = null, CancellationToken ct = default)
Task<Leaderboard>            JoinAsync(string key, LeaderboardJoinOptions? opts = null, CancellationToken ct = default)
Task<BoardStandings>         StandingsAsync(string key, StandingsReadOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardScoreResult> SubmitScoreAsync(string key, double value, LeaderboardSubmitOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardPeriods>     ListPeriodsAsync(string key, int? limit = null, CancellationToken ct = default)
```

`LeaderboardReadOptions`:
- `int? Limit` — 1–200, default 50 server-side
- `string? Segment` — bucket value; required only for `context` segmentation. Leave null for `progression`-segmented boards (server derives the caller's division); unsegmented boards ignore it
- `string? Period` — `"current"` (default) or an ISO timestamp from `LeaderboardPeriod.PeriodStartedAt`
- `bool IncludeSelf` — when true, response includes `self: { rank, score }` (live periods only)
- `string? ExternalId` — required when `IncludeSelf` is true; lazily resolved from the active player otherwise

`JoinAsync` — add the active player to the board at score 0 without submitting a score, and return the current standings. Idempotent (never resets an existing score). Response `Joined = true`. `LeaderboardJoinOptions`:
- `int? Limit` — 1–200, default 50 server-side
- `string? Segment` — bucket value for `context` boards; leave null for `progression` boards (server derives the division from the caller's balance)
- `string? ExternalId` — address a different player (server-side tooling only); lazily resolved otherwise

`StandingsAsync` — multi-segment read. Returns one `StandingsSegment` block per segment picked by `StandingsReadOptions.Scope`. `StandingsReadOptions`:
- `string? Scope` — `"self_segment"`, `"mine"`, `"segment"`, `"all"` (default `"all"`)
- `string? Segment` — required when `Scope == "segment"` on a segmented board
- `string? Period` — `"current"` (default) or an ISO timestamp from `ListPeriodsAsync`
- `string? ExternalId` — flags `isSelf` / `selfRank`; auto-resolved for `self_segment` / `mine`
- `int? Limit` — per-segment top-N (1..200, default 50)
- `int? MaxSegments` — cap on returned segment blocks (1..100, default 20)

`BoardStandings`: `key`, `sharedLeaderboardId`, `scope`, `resetCadence`, `scoreAggregation`, `period`, `List<StandingsSegment> Segments`, `bool SegmentsTruncated`.
`StandingsSegment`: `string? Segment`, `bool Participated`, `int? SelfRank`, `List<LeaderboardEntry> Entries`.

`SubmitScoreAsync` — submit a score for the active player directly to the board, outside an event attempt. Errors: `client_scoring_disabled` (403, board is server-only), `score_not_supported` (400, progression-ranked board), `not_found` (404), `validation_failed` (400). `LeaderboardSubmitOptions`:
- `string? Segment` — required only for `context` segmentation; null for `progression` boards (server derives the division); unsegmented boards ignore it
- `string? IdempotencyKey` — auto-stamped when null
- `string? ExternalId` — address a different player (server-side tooling only); lazily resolved otherwise

`LeaderboardScoreResult`:
- `string LeaderboardId`
- `double Score`
- `int? Rank`

## `kraty.EventLeaderboards` — `EventLeaderboardsClient`

The auto-generated per-event-window leaderboard. Addressed by the
**UUID** returned in `Events.StartAsync(...)`'s
`attempt.leaderboardId`. Includes Server-Sent Events live streaming.
Wire endpoints:

- `GET /sdk/v1/event-leaderboards/:id`
- `GET /sdk/v1/event-leaderboards/:id/stream`

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

`EventLeaderboard` read response includes `bool Finalized` and, when finalized, `string? FinalizedReason` (`session_terminated` \| `window_closed`) — powers the finalization catch-up's session-vs-window distinction. `FinalizationResult` = `{ MembershipRef Ref; string Reason; SelfEntry? Self; IReadOnlyList<FinalStanding>? Standings; string? EventKey }`; `Reason` uses the `FinalizationReason` consts (`SessionTerminated` \| `WindowClosed` \| `PeriodRolled` \| `Finalized`). Registry persistence is injectable via `KratyClientOptions.MembershipStore` (`PlayerPrefsMembershipStore` in Unity, `InMemoryMembershipStore` otherwise).

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
