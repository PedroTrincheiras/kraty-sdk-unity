using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kraty
{
    /// <summary>
    /// Resource client for the <c>/sdk/v1/players/.../events/...</c>
    /// surface: list, start, progress.
    /// </summary>
    public sealed class EventsClient
    {
        private readonly KratyClient _client;

        public EventsClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:externalId/events</c> — events whose
        /// current window the active player can start now. Pass
        /// <paramref name="as"/> to address a different player
        /// (server-side tooling only).
        /// </summary>
        public async Task<List<EventListing>> ListForPlayerAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<List<EventListing>>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<EventListing>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/events/:e/start</c> — start an
        /// attempt for the active player. Atomically debits any
        /// configured entry cost; on insufficient resources throws
        /// <see cref="KratyApiError"/> with
        /// <c>IsInsufficientEntryCost == true</c> (the debit is
        /// rolled back — partial spends never persist).
        ///
        /// <para>
        /// If matchmaking is still forming, throws with
        /// <c>IsLobbyForming == true</c>; poll the lobby endpoint and
        /// retry start.
        /// </para>
        /// </summary>
        public async Task<StartAttemptResponse> StartAsync(
            string eventKey,
            IDictionary<string, object?>? playerContext = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (playerContext != null) body["playerContext"] = playerContext;
            var env = await _client.RequestAsync<DataEnvelope<StartAttemptResponse>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events/{Uri.EscapeDataString(eventKey)}/start",
                body: body,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new StartAttemptResponse();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/events/:e/attempts/:a/progress</c>
        /// — push a metric update for the active player. Mode
        /// <c>"set"</c> writes the value, <c>"increment"</c> adds.
        /// Completing the metric target ends the attempt and fires
        /// the reward pipeline server-side.
        /// </summary>
        public async Task<ProgressResult> ProgressAsync(
            string eventKey,
            string attemptId,
            ProgressInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<ProgressResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events/{Uri.EscapeDataString(eventKey)}/attempts/{Uri.EscapeDataString(attemptId)}/progress",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new ProgressResult();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/leaderboards/:id</c> — snapshot
    /// read + Server-Sent-Events live stream.
    /// </summary>
    public sealed class LeaderboardsClient
    {
        private readonly KratyClient _client;
        public LeaderboardsClient(KratyClient client) => _client = client;

        public async Task<Leaderboard> ReadAsync(
            string leaderboardId,
            LeaderboardReadOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new LeaderboardReadOptions();
            var qs = new List<string>();
            if (opts.Limit.HasValue) qs.Add($"limit={opts.Limit.Value}");
            if (opts.IncludeSelf)
            {
                // Lazily resolve the active player when the dev didn't
                // pass an explicit ExternalId — same contract as every
                // other player-scoped method on the SDK.
                var externalId = !string.IsNullOrEmpty(opts.ExternalId)
                    ? opts.ExternalId!
                    : (await _client.EnsureIdentityAsync(ct).ConfigureAwait(false)).ExternalPlayerId;
                qs.Add("includeSelf=true");
                qs.Add($"externalId={Uri.EscapeDataString(externalId)}");
            }
            var path = qs.Count == 0
                ? $"/sdk/v1/leaderboards/{Uri.EscapeDataString(leaderboardId)}"
                : $"/sdk/v1/leaderboards/{Uri.EscapeDataString(leaderboardId)}?{string.Join("&", qs)}";

            var env = await _client.RequestAsync<DataEnvelope<Leaderboard>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Leaderboard();
        }

        /// <summary>
        /// GET <c>/sdk/v1/leaderboards/:id/stream</c> — opens a
        /// Server-Sent Events subscription that pushes score updates
        /// in real time. Returns a <see cref="LeaderboardStream"/> handle
        /// the caller drives via its <c>OnEvent</c> / <c>OnError</c>
        /// callbacks + <see cref="LeaderboardStream.CancelAsync"/>.
        ///
        /// <para>
        /// Event kinds the server emits today: <c>ready</c>,
        /// <c>score_update</c>, <c>closed</c>. See
        /// <see cref="LeaderboardStreamEvent"/>.
        /// </para>
        ///
        /// <para>
        /// Does NOT auto-reconnect on transport drop — handle errors
        /// via the returned stream's <c>OnError</c> and re-call
        /// <see cref="LiveAsync"/> after a backoff if you want resumption.
        /// </para>
        ///
        /// <para>
        /// Low-level — prefer <see cref="Subscribe"/> for game UIs.
        /// </para>
        /// </summary>
        public Task<LeaderboardStream> LiveAsync(string leaderboardId, CancellationToken ct = default)
        {
            return LeaderboardStreamFactory.OpenAsync(
                _client.HttpForStreaming,
                _client.BaseUrlForStreaming,
                leaderboardId,
                _client.AuthHeaderForStreaming,
                _client.PlayerSecretForStreaming,
                ct
            );
        }

        /// <summary>
        /// High-level live leaderboard subscription. Composes:
        ///
        /// <list type="number">
        ///   <item><description>the SSE stream from <see cref="LiveAsync"/> (real-time push for any score updates the server has published), AND</description></item>
        ///   <item><description>a periodic background <see cref="ReadAsync"/> poll that nudges the server's lazy bot evaluator to advance bot scores, then dedupes the resulting deltas against the SSE feed.</description></item>
        /// </list>
        ///
        /// <para>
        /// Why both: bot scores climb on a schedule (per the event's
        /// bot definitions) even when no player action would otherwise
        /// trigger a server-side read. Without the background poll,
        /// idle UIs never see bots tick. The SSE stream then carries
        /// the resulting <c>score_update</c> events (the backend
        /// publishes deltas on every lazy eval) so multiple subscribers
        /// per leaderboard share one fan-out.
        /// </para>
        ///
        /// <para>
        /// The callback fires for every event from either source,
        /// deduplicated so the same (participantId, score) tuple
        /// doesn't surface twice. Callbacks fire on the HTTP
        /// background thread — marshal to Unity's main thread before
        /// touching <c>UnityEngine</c> APIs.
        /// </para>
        ///
        /// <para>
        /// Returns a handle whose <c>CancelAsync</c> / <c>Dispose</c>
        /// tear down both transports.
        /// </para>
        /// </summary>
        /// <param name="leaderboardId">The leaderboard to subscribe to.</param>
        /// <param name="onEvent">Fires for every event from SSE or poll. Deduped on score.</param>
        /// <param name="opts">Optional cadence + error callback.</param>
        public LiveLeaderboardSubscription Subscribe(
            string leaderboardId,
            Action<LeaderboardStreamEvent> onEvent,
            SubscribeOptions? opts = null
        )
        {
            return LiveLeaderboardSubscription.Open(
                this,
                leaderboardId,
                onEvent,
                opts ?? new SubscribeOptions()
            );
        }
    }

    /// <summary>
    /// Options for <see cref="LeaderboardsClient.Subscribe"/>.
    /// </summary>
    public sealed class SubscribeOptions
    {
        /// <summary>Background read cadence. Default 15000ms. Set to 0 to disable polling (SSE-only).</summary>
        public int PollIntervalMs { get; set; } = 15000;
        /// <summary>Optional — receives transport / parse errors. SSE errors are non-fatal; the poll keeps running.</summary>
        public Action<Exception>? OnError { get; set; }
    }

    /// <summary>
    /// Handle to a live leaderboard subscription opened via
    /// <see cref="LeaderboardsClient.Subscribe"/>. Tears down both the
    /// SSE stream and the background poll when cancelled / disposed.
    /// </summary>
    public sealed class LiveLeaderboardSubscription : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly LeaderboardsClient _leaderboards;
        private readonly string _leaderboardId;
        private readonly Action<LeaderboardStreamEvent> _onEvent;
        private readonly Action<Exception> _onError;
        private readonly int _pollIntervalMs;
        private readonly Dictionary<string, double> _lastSurfacedScore = new();
        private LeaderboardStream? _sseStream;
        private Task? _pollLoop;
        private Task? _sseLoop;
        private int _closed;

        private LiveLeaderboardSubscription(
            LeaderboardsClient leaderboards,
            string leaderboardId,
            Action<LeaderboardStreamEvent> onEvent,
            SubscribeOptions opts
        )
        {
            _leaderboards = leaderboards;
            _leaderboardId = leaderboardId;
            _onEvent = onEvent;
            _onError = opts.OnError ?? (_ => { });
            _pollIntervalMs = opts.PollIntervalMs;
        }

        internal static LiveLeaderboardSubscription Open(
            LeaderboardsClient leaderboards,
            string leaderboardId,
            Action<LeaderboardStreamEvent> onEvent,
            SubscribeOptions opts
        )
        {
            var sub = new LiveLeaderboardSubscription(leaderboards, leaderboardId, onEvent, opts);
            sub._sseLoop = Task.Run(sub.RunSseLoopAsync);
            if (opts.PollIntervalMs > 0)
            {
                sub._pollLoop = Task.Run(sub.RunPollLoopAsync);
            }
            return sub;
        }

        private void Surface(LeaderboardStreamEvent ev)
        {
            if (Volatile.Read(ref _closed) == 1) return;
            // Dedup score_update by (participantId, score). Other event
            // kinds (ready, closed, parse-error) always pass through.
            if (ev.Kind == "score_update" && ev.Data != null)
            {
                if (ev.Data.TryGetValue("participantId", out var pidTok) &&
                    ev.Data.TryGetValue("score", out var scoreTok))
                {
                    var pid = (string?)pidTok;
                    if (pid != null && scoreTok.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                    {
                        var score = (double)scoreTok;
                        lock (_lastSurfacedScore)
                        {
                            if (_lastSurfacedScore.TryGetValue(pid, out var prior) && prior == score)
                                return;
                            _lastSurfacedScore[pid] = score;
                        }
                    }
                }
            }
            try { _onEvent(ev); }
            catch (Exception cbErr) { try { _onError(cbErr); } catch { /* swallow */ } }
        }

        private async Task RunSseLoopAsync()
        {
            try
            {
                _sseStream = await _leaderboards.LiveAsync(_leaderboardId, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception err)
            {
                if (Volatile.Read(ref _closed) == 0) { try { _onError(err); } catch { /* swallow */ } }
                return;
            }
            _sseStream.OnEvent = Surface;
            _sseStream.OnError = err => {
                if (Volatile.Read(ref _closed) == 0) { try { _onError(err); } catch { /* swallow */ } }
            };
        }

        private async Task RunPollLoopAsync()
        {
            // Fire one read immediately so the first frame of the UI
            // lands with current scores instead of waiting a full interval.
            await PollOnceAsync().ConfigureAwait(false);
            while (!_cts.IsCancellationRequested)
            {
                try { await Task.Delay(_pollIntervalMs, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                await PollOnceAsync().ConfigureAwait(false);
            }
        }

        private async Task PollOnceAsync()
        {
            if (Volatile.Read(ref _closed) == 1) return;
            try
            {
                var lb = await _leaderboards.ReadAsync(_leaderboardId, null, _cts.Token).ConfigureAwait(false);
                foreach (var entry in lb.Entries)
                {
                    Surface(new LeaderboardStreamEvent("score_update", new Dictionary<string, Newtonsoft.Json.Linq.JToken>
                    {
                        ["leaderboardId"] = lb.LeaderboardId,
                        ["participantId"] = entry.ParticipantId,
                        ["score"] = entry.Score,
                        ["rank"] = entry.Rank,
                    }));
                }
            }
            catch (Exception err)
            {
                if (Volatile.Read(ref _closed) == 0) { try { _onError(err); } catch { /* swallow */ } }
            }
        }

        public async Task CancelAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try { _cts.Cancel(); } catch { /* swallow */ }
            if (_sseStream != null)
            {
                try { await _sseStream.CancelAsync().ConfigureAwait(false); } catch { /* swallow */ }
            }
            try { if (_pollLoop != null) await _pollLoop.ConfigureAwait(false); } catch { /* swallow */ }
            try { if (_sseLoop != null) await _sseLoop.ConfigureAwait(false); } catch { /* swallow */ }
            try { _cts.Dispose(); } catch { /* swallow */ }
        }

        public void Dispose()
        {
            _ = CancelAsync();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/{pending-grants,grants,crates}</c>.
    /// </summary>
    public sealed class GrantsClient
    {
        private readonly KratyClient _client;
        public GrantsClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/pending-grants</c> — empty list
        /// for unknown players (not a 404).
        /// </summary>
        public async Task<List<Grant>> ListPendingAsync(
            int? limit = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var path = limit.HasValue
                ? $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/pending-grants?limit={limit.Value}"
                : $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/pending-grants";
            var env = await _client.RequestAsync<DataEnvelope<List<Grant>>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<Grant>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/grants/:g/claim</c> for the
        /// active player. Idempotent — claiming an already-claimed
        /// grant returns the same row.
        /// </summary>
        public async Task<Grant> ClaimAsync(
            string grantId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<Grant>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/grants/{Uri.EscapeDataString(grantId)}/claim",
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Grant();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/crates/:g/open</c> for the
        /// active player. Idempotent on the crate id — replays
        /// return the previously-rolled contents grant.
        /// </summary>
        public async Task<OpenCrateResponse> OpenAsync(
            string grantId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<OpenCrateResponse>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/crates/{Uri.EscapeDataString(grantId)}/open",
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new OpenCrateResponse();
        }

        /// <summary>
        /// Burn down the pending-grants queue in one call: list
        /// everything pending, open every crate, claim every reward,
        /// return a summary. Built for the "round complete"
        /// reward-collection moment most games have.
        ///
        /// <para>
        /// Errors per-grant are caught and surfaced in
        /// <see cref="CollectAllResult.Failures"/> — one bad grant
        /// doesn't abort the whole sweep. The remaining grants still
        /// get processed. Callers can inspect failures + retry the
        /// individual ones later.
        /// </para>
        ///
        /// <para>
        /// Order is server-determined (whatever
        /// <see cref="ListPendingAsync"/> returns). Crates open BEFORE
        /// rewards are claimed — the rolled-contents grant a crate
        /// produces lands in the NEXT <see cref="ListPendingAsync"/>,
        /// so a recursive call would catch it; we deliberately don't
        /// recurse to keep the call bounded. Re-invoke after the first
        /// call if you want to drain crate-opened contents too.
        /// </para>
        /// </summary>
        public async Task<CollectAllResult> CollectAllAsync(
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var pending = await ListPendingAsync(@as: @as, ct: ct).ConfigureAwait(false);
            var opened = new List<OpenCrateResponse>();
            var claimed = new List<Grant>();
            var failures = new List<CollectAllFailure>();
            foreach (var g in pending)
            {
                try
                {
                    if (g.Kind == "crate")
                    {
                        opened.Add(await OpenAsync(g.Id, @as: @as, ct: ct).ConfigureAwait(false));
                    }
                    else
                    {
                        claimed.Add(await ClaimAsync(g.Id, @as: @as, ct: ct).ConfigureAwait(false));
                    }
                }
                catch (Exception err)
                {
                    failures.Add(new CollectAllFailure(g, err));
                }
            }
            return new CollectAllResult(pending.Count, opened, claimed, failures);
        }
    }

    /// <summary>
    /// One pending grant that <see cref="GrantsClient.CollectAllAsync"/>
    /// couldn't process. The other grants in the same sweep still went
    /// through — inspect <see cref="Error"/> and retry the individual
    /// operation.
    /// </summary>
    public sealed class CollectAllFailure
    {
        public Grant Grant { get; }
        public Exception Error { get; }
        public CollectAllFailure(Grant grant, Exception error)
        {
            Grant = grant;
            Error = error;
        }
    }

    /// <summary>
    /// Aggregate result of <see cref="GrantsClient.CollectAllAsync"/>.
    /// <see cref="Processed"/> is the total pending count at the time
    /// of the call; <see cref="Opened"/> + <see cref="Claimed"/> +
    /// <see cref="Failures"/> sum to that.
    /// </summary>
    public sealed class CollectAllResult
    {
        public int Processed { get; }
        public IReadOnlyList<OpenCrateResponse> Opened { get; }
        public IReadOnlyList<Grant> Claimed { get; }
        public IReadOnlyList<CollectAllFailure> Failures { get; }

        public CollectAllResult(
            int processed,
            IReadOnlyList<OpenCrateResponse> opened,
            IReadOnlyList<Grant> claimed,
            IReadOnlyList<CollectAllFailure> failures
        )
        {
            Processed = processed;
            Opened = opened;
            Claimed = claimed;
            Failures = failures;
        }

        public bool HasFailures => Failures.Count > 0;
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/inventory(/...)</c>.
    /// Only surfaces meaningful data for games whose
    /// <c>settings.inventoryManagement</c> is <c>'platform'</c> — under
    /// studio-managed mode the lists come back empty (the studio's own
    /// backend holds the canonical state). The SDK doesn't expose
    /// grant / admin-credit endpoints; those are server-API only.
    /// </summary>
    public sealed class InventoryClient
    {
        private readonly KratyClient _client;
        public InventoryClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/inventory</c> — every item the
        /// player currently holds (quantity > 0). Ordering is not
        /// guaranteed; sort client-side if you need it.
        /// </summary>
        public async Task<List<PlayerItemHolding>> ListAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<InventoryListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/inventory",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Items ?? new List<PlayerItemHolding>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/inventory/:itemKey/consume</c> —
        /// atomic decrement on the active player's inventory.
        /// Auto-stamped idempotency key unless you provide one.
        /// Throws <see cref="KratyApiError"/> with code <c>conflict</c>
        /// if the player doesn't have enough of the item.
        /// </summary>
        public async Task<ConsumeItemResult> ConsumeAsync(
            string itemKey,
            ConsumeItemInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<ConsumeItemResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/inventory/{Uri.EscapeDataString(itemKey)}/consume",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new ConsumeItemResult();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/wallet(/...)</c>.
    /// Mirrors <see cref="InventoryClient"/> for currencies +
    /// progression resources.
    /// </summary>
    public sealed class WalletClient
    {
        private readonly KratyClient _client;
        public WalletClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/wallet</c> — every economy entry
        /// the player has touched. Returns zero-balance rows alongside
        /// positive ones, so a wallet that's been emptied still
        /// surfaces. Filter client-side if you only want live balances.
        /// </summary>
        public async Task<List<PlayerWalletHolding>> ListAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<WalletListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/wallet",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Wallet ?? new List<PlayerWalletHolding>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/wallet/:economyKey/debit</c> —
        /// atomic decrement on the active player's wallet. 409 on
        /// insufficient balance. Credit is intentionally not exposed
        /// here — only the studio's backend can mint balance.
        /// </summary>
        public async Task<DebitWalletResult> DebitAsync(
            string economyKey,
            DebitWalletInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<DebitWalletResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/wallet/{Uri.EscapeDataString(economyKey)}/debit",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new DebitWalletResult();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/register</c> — the
    /// zero-trust bootstrap. Most apps never call this directly:
    /// <see cref="KratyClient.EnsureIdentityAsync"/> handles register
    /// lazily on the first player-scoped call. Reach for
    /// <see cref="RegisterAsync"/> only when you want to register a
    /// specific id you control (e.g. tied to your own auth).
    /// </summary>
    public sealed class PlayersClient
    {
        private readonly KratyClient _client;
        public PlayersClient(KratyClient client) => _client = client;

        /// <summary>
        /// POST <c>/sdk/v1/players/:externalId/register</c> — creates
        /// the player row if it doesn't exist + mints a per-player
        /// secret. Throws <see cref="KratyApiError"/> with
        /// <c>IsPlayerAlreadyRegistered == true</c> if the player has
        /// already claimed a secret.
        ///
        /// <para>
        /// Pass <paramref name="force"/>=true to ROTATE an existing
        /// secret. Only honoured by non-<c>live</c> API keys (dev /
        /// test / staging).
        /// </para>
        /// </summary>
        public async Task<PlayerRegistration> RegisterAsync(
            string externalPlayerId,
            bool force = false,
            CancellationToken ct = default
        )
        {
            var path = force
                ? $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/register?force=true"
                : $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/register";
            var env = await _client.RequestAsync<DataEnvelope<PlayerRegistration>>(
                HttpMethod.Post,
                path,
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new PlayerRegistration();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/catalog</c> — single-shot read
    /// of every item + currency configured for the calling game.
    /// Studios call this once at boot and cache locally; pairs with
    /// <see cref="EventsClient.ListForPlayerAsync"/> (which inlines
    /// reward-bundle previews) so a UI can render names, icons,
    /// rarities, and reward previews without keeping a parallel
    /// catalog in the client codebase.
    /// </summary>
    public sealed class CatalogClient
    {
        private readonly KratyClient _client;
        public CatalogClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/catalog</c> — items + currencies for the
        /// calling game. Game is derived from the API key.
        /// </summary>
        public async Task<Catalog> ReadAsync(CancellationToken ct = default)
        {
            var env = await _client.RequestAsync<DataEnvelope<Catalog>>(
                HttpMethod.Get,
                "/sdk/v1/catalog",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Catalog();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/lobbies/:id</c>. Used after
    /// <c>/start</c> returns <c>lobby_forming</c>: poll this until
    /// <see cref="Lobby.Status"/> transitions out of <c>forming</c>,
    /// then retry start.
    /// </summary>
    public sealed class LobbiesClient
    {
        private readonly KratyClient _client;
        public LobbiesClient(KratyClient client) => _client = client;

        public async Task<Lobby> ReadAsync(string lobbyId, CancellationToken ct = default)
        {
            var env = await _client.RequestAsync<DataEnvelope<Lobby>>(
                HttpMethod.Get,
                $"/sdk/v1/lobbies/{Uri.EscapeDataString(lobbyId)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Lobby();
        }
    }

    /// <summary>
    /// Adaptive polling for a player's pending grants. Grows the
    /// interval while the queue is empty; resets to the floor when
    /// grants land. Resolves when the cancellation token fires.
    /// </summary>
    public static class GrantPolling
    {
        public sealed class Options
        {
            public TimeSpan Start { get; set; } = TimeSpan.FromSeconds(2);
            public double Grow { get; set; } = 1.5;
            public TimeSpan Max { get; set; } = TimeSpan.FromSeconds(30);
            /// <summary>Fires for every batch (including empty ones).</summary>
            public Action<List<Grant>>? OnBatch { get; set; }
        }

        public static async Task PollPendingAsync(
            GrantsClient grants,
            Options? opts = null,
            string? @as = null,
            CancellationToken cancellationToken = default
        )
        {
            opts ??= new Options();
            var interval = opts.Start;
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await grants.ListPendingAsync(@as: @as, ct: cancellationToken).ConfigureAwait(false);
                opts.OnBatch?.Invoke(batch);
                if (batch.Count > 0)
                {
                    interval = opts.Start;
                }
                else
                {
                    var grown = TimeSpan.FromMilliseconds(interval.TotalMilliseconds * opts.Grow);
                    interval = grown > opts.Max ? opts.Max : grown;
                }
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public static class LobbyPolling
    {
        public sealed class Options
        {
            public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
            public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Polls a lobby until it transitions out of <c>forming</c>.
        /// Throws <see cref="TimeoutException"/> if the deadline
        /// elapses, <see cref="OperationCanceledException"/> if the
        /// token is cancelled.
        /// </summary>
        public static async Task<Lobby> UntilActiveAsync(
            LobbiesClient lobbies,
            string lobbyId,
            Options? opts = null,
            CancellationToken cancellationToken = default
        )
        {
            opts ??= new Options();
            var deadline = DateTimeOffset.UtcNow + opts.Timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lobby = await lobbies.ReadAsync(lobbyId, ct: cancellationToken).ConfigureAwait(false);
                if (lobby.Status != "forming") return lobby;
                await Task.Delay(opts.Interval, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException($"UntilActiveAsync: lobby '{lobbyId}' did not leave 'forming' within {opts.Timeout}");
        }
    }
}
