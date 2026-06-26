# app.kraty.sdk (Unity / C#)

C# **client** SDK for the [Kraty](https://kraty.io) game-events
platform, targeting **Unity 2022 LTS+** and any modern .NET runtime.
Built on .NET Standard 2.1 with no Unity-engine dependencies in the
`Runtime/` tree — meaning the same package runs unmodified in standard
.NET (CLI tools, server-side bots, test suites) without an editor.

> 📖 **Full reference + examples:** <https://kraty.io/docs/sdks/unity>
>
> The docs site has the complete guide — install via UPM, sign-in
> patterns, every method, SSE streaming, error handling. This README
> is just the elevator pitch.

Auto-stamped idempotency keys on every write (preserved across
retries), exponential retry with jitter, sealed error codes, SSE
leaderboard streaming, adaptive polling helpers, and a one-call
per-player bootstrap.

> **This SDK is for game CLIENTS only.** It deliberately does NOT
> expose the `/server/v1` or `/admin/v1` surfaces. Server-side IAP
> fulfilment, manual grants, and admin tooling belong on your own
> backend with `@kraty/server-sdk` (Node). Embedding a
> `server_integration` key in a shipped Unity build is a security
> incident.

## Install

### Git URL (recommended)

In `Packages/manifest.json`:

```json
{
  "dependencies": {
    "app.kraty.sdk": "https://github.com/PedroTrincheiras/kraty-sdk-unity.git#v0.3.4"
  }
}
```

Tags live at
[github.com/PedroTrincheiras/kraty-sdk-unity/releases](https://github.com/PedroTrincheiras/kraty-sdk-unity/releases);
each release is a flat-rooted mirror of this package that Unity
Package Manager can pull directly.

### Local disk

For monorepo / fork workflows: Window → Package Manager → `+` →
Add package from disk → pick `packages/sdk-unity/package.json`.

## Quickstart

```csharp
using Kraty;

// One line — the SDK lazily mints (or restores) an externalPlayerId
// and X-Player-Secret on the first player-scoped call and persists
// them via PlayerPrefs.
var kraty = new Kraty(new KratyClientOptions { ApiKey = "<your-client-sdk-key>" });

// What can this player start right now? No id plumbing.
var events = await kraty.Events.ListForPlayerAsync();

// Start an attempt. Atomically debits any entry cost; on failure
// throws KratyApiError with IsInsufficientEntryCost == true.
var start = await kraty.Events.StartAsync(events[0].EventKey);

// Push progress, see milestones fire.
var update = await kraty.Events.ProgressAsync(
    events[0].EventKey, start.Attempt.Id,
    new ProgressInput { Mode = "increment", MetricValue = 1 }
);
foreach (var fired in update.MilestonesFired)
    Debug.Log($"milestone {fired.Key} → {fired.Grants.Count} grants");

// Burn down the pending grants queue in one call.
await kraty.Grants.CollectAllAsync();

// Need the resolved id? It's on the facade.
Debug.Log($"playing as {kraty.ActiveExternalPlayerId}");
```

## Surface

Seven resource clients on the `Kraty` facade:

- `kraty.Events` — list / start / progress
- `kraty.Leaderboards` — snapshot read + live SSE stream (`LiveAsync`); `ReadSharedAsync(key)` / `ListSharedPeriodsAsync(key)` for configurable cross-event boards
- `kraty.Grants` — pending / claim / open / `CollectAllAsync`
- `kraty.Lobbies` — read (with `BotSlots` projection for smooth fill UI)
- `kraty.Inventory` — list / consume (platform-managed games)
- `kraty.Wallet` — list / debit (platform-managed games)
- `kraty.Players` — `RegisterAsync` / rotate (rarely needed; lazy bootstrap handles registration)

Identity surface on the facade:

- `kraty.ActiveExternalPlayerId` — id of the currently signed-in player (null until first resolve).
- `await kraty.EnsureIdentityAsync()` — eagerly resolve / register if you want the id available before the first request.
- `await kraty.SignInAsync(externalPlayerId, secret)` — install an explicit identity (e.g. from your own auth backend).
- `await kraty.LogoutAsync()` — wipe persisted id + secret. The next player-scoped call lazily registers a new player.

Static helpers:

- `Kraty.ConnectAsPlayerAsync(options, externalId, secretStore, ...)` — explicit bootstrap for the case where you want the register I/O to happen at a specific moment.
- `Kraty.ReadStoredIdentityAsync`, `Kraty.RestoreStoredIdentityAsync`, `Kraty.ClearStoredIdentityAsync` — direct store helpers for boot-time flows.
- `GrantPolling.PollPendingAsync(...)` — adaptive interval
- `LobbyPolling.UntilActiveAsync(...)` — fixed-interval with timeout

Auth contract:

- `ISecretStore` — async interface for persisting the per-player secret. Ships with `InMemorySecretStore` (tests + non-Unity) and `PlayerPrefsSecretStore` (Unity, wraps `UnityEngine.PlayerPrefs`).
- When `SecretStore` is omitted on `KratyClientOptions`, the SDK picks `PlayerPrefsSecretStore` on Unity and `InMemorySecretStore` everywhere else.
- For shipped games, consider replacing `PlayerPrefsSecretStore` with a Keychain / EncryptedSharedPreferences plugin — PlayerPrefs is unencrypted on disk.

## Retry + idempotency

Every `POST` / `PUT` / `PATCH` is automatically stamped with an
`idempotencyKey` (`Guid.NewGuid()` by default) before the first
attempt. Retries reuse the same key, so the server's idempotency
check dedupes a network-replayed call.

Retry config is tunable per client:

```csharp
new KratyClientOptions
{
    ApiKey = "...",
    Retry = new RetryConfig
    {
        Attempts = 5,
        InitialDelay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(10),
        Jitter = 0.25,
    },
}
```

`Retry-After` headers (used by the platform's 429 responses) are
honored. Retries fire on `408` / `425` / `429` / `5xx` and on
`HttpRequestException` / network failures.

## Error handling

Non-2xx responses throw `KratyApiError` with a typed `Code` (one of
`KratyErrorCode.*`). Network failures throw `KratyNetworkError`.

```csharp
try
{
    await kraty.Events.StartAsync("race");
}
catch (KratyApiError err) when (err.IsLobbyForming)
{
    var lobby = await LobbyPolling.UntilActiveAsync(kraty.Lobbies, lobbyId);
}
catch (KratyApiError err) when (err.IsInsufficientEntryCost)
{
    ShowOutOfResourceDialog(err.Message);
}
catch (KratyApiError err) when (err.IsPlayerSecretInvalid)
{
    await ReregisterFlow();
}
catch (KratyApiError err)
{
    switch (err.Code)
    {
        case KratyErrorCode.NoActiveWindow:        break;
        case KratyErrorCode.MaxAttemptsReached:    break;
        // ...
    }
}
```

Typed properties: `IsLobbyForming`, `IsInsufficientEntryCost`,
`IsPlayerSecretInvalid`, `IsPlayerAlreadyRegistered`,
`IsEntryRequirementFailed`. Full code list: [error reference](../../apps/portal/content/docs/errors.mdx).

## Development

The package ships with a `.csproj` so `dotnet` can build + test the
Runtime sources without Unity. Tests use xUnit and a fake
`HttpMessageHandler` (no real network IO).

```bash
# From the package root:
dotnet build Kraty.SDK.csproj
dotnet test Kraty.SDK.sln
```

Or from the repo root:

```bash
dotnet test packages/sdk-unity/Kraty.SDK.sln
```

Build outputs land in `tools/build/sdk-unity*/` so they don't
pollute Unity's view of the package.

## File layout

```
packages/sdk-unity/
  package.json              # Unity UPM manifest
  CHANGELOG.md              # release history (mirrored to upm branch)
  Kraty.SDK.sln             # .NET solution (plain dotnet build/test)
  Kraty.SDK.csproj          # netstandard2.1 library, ships to Unity
  Runtime/                  # what Unity imports
    Kraty.Runtime.asmdef    # Unity assembly definition
    AssemblyInfo.cs         # InternalsVisibleTo for the test project
    KratyClient.cs          # core HTTP client + retry / idempotency
    Kraty.cs                # facade (Kraty class + ConnectAsPlayerAsync)
    Errors/                 # KratyApiError, KratyNetworkError, codes
    Types/                  # public DTOs (Attempt, Grant, EntryCost, …)
    Resources/              # EventsClient, GrantsClient, InventoryClient, …
    Auth/                   # ISecretStore + impls
    Streaming/              # LeaderboardStream (SSE)
  Tests/                    # net10.0 xUnit tests (NOT imported by Unity)
```

Unity ignores everything outside `Runtime/`, so the `.csproj` +
`Tests/` tree is invisible inside the editor.

## Publishing (maintainers)

Releases are pushed from the private monorepo into
[PedroTrincheiras/kraty-sdk-unity](https://github.com/PedroTrincheiras/kraty-sdk-unity)
via `scripts/sync-public-sdks.sh`:

```bash
# 1. Bump packages/client/sdk-unity/package.json `version`.
# 2. Update packages/client/sdk-unity/CHANGELOG.md.
# 3. Update the install snippet in apps/portal/content/docs/sdks/unity.mdx.
# 4. Commit + push the monorepo.
scripts/sync-public-sdks.sh client-unity v0.3.4
```

The script copies the package contents into the public repo,
commits, pushes, tags, and creates the GitHub release —
idempotent: re-running for the same version is a no-op.

Consumers update by bumping the ref in their `manifest.json`:

```json
"app.kraty.sdk": "https://github.com/PedroTrincheiras/kraty-sdk-unity.git#v0.3.4"
```
