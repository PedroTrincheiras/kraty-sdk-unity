using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kraty;
using UnityEngine;

namespace Kraty.Sample.SmokeRig
{
    /// <summary>
    /// Drop on any GameObject in any scene, fill in the inspector fields,
    /// hit Play. The on-screen panel exercises every public surface of the
    /// SDK against the configured backend so you can verify a build (Editor
    /// or shipped player) end-to-end before pushing to your studio.
    ///
    /// <para>
    /// Why a runtime MonoBehaviour and not editor tests: most regressions
    /// that have bitten this SDK in prod were IL2CPP-only (stripped
    /// reflection in <c>System.Text.Json</c>, missing meta files breaking
    /// asmdef refs, <c>PlayerPrefs</c> off the main thread). Editor edit-mode
    /// tests don't run through that path. A button you click in a real
    /// Player build does.
    /// </para>
    ///
    /// <para>
    /// The panel is IMGUI on purpose: no scene, no prefab, no UI Toolkit
    /// dependency. The whole thing is one file.
    /// </para>
    /// </summary>
    public sealed class KratySdkSmokeRig : MonoBehaviour
    {
        [Header("Backend target")]
        [Tooltip("Client SDK API key (test or live).")]
        [SerializeField] private string apiKey = "";
        [Tooltip("Backend base URL. Default: staging.")]
        [SerializeField] private string baseUrl = "https://api.staging.kraty.io";

        [Header("Optional: what to exercise")]
        [Tooltip("Event key to start / progress against. Leave blank to auto-pick the first event from ListForPlayerAsync.")]
        [SerializeField] private string eventKey = "";
        [Tooltip("Leaderboard key to read (e.g. weekly_global).")]
        [SerializeField] private string leaderboardKey = "";
        [Tooltip("Optional segment for segmented leaderboards.")]
        [SerializeField] private string leaderboardSegment = "";
        [Tooltip("Optional lobby id to exercise the lobbies endpoint against (paste from dashboard or your own backend).")]
        [SerializeField] private string lobbyId = "";

        private Kraty.Kraty _kraty;
        private readonly List<string> _log = new();
        private Vector2 _logScroll;
        private string _activePlayerId = "(not signed in)";
        private string _lastAttemptId;
        private string _lastLeaderboardId;
        private LiveLeaderboardSubscription _liveSub;
        private bool _busy;

        private void OnEnable()
        {
            // Capture the player-loop SynchronizationContext implicitly by
            // constructing the Kraty facade on Start, so every async call below
            // is `await`-ed without ConfigureAwait, so continuations land back
            // here. PlayerPrefs reads/writes happen on the main thread.
            _kraty = new Kraty.Kraty(new KratyClientOptions
            {
                ApiKey = string.IsNullOrEmpty(apiKey) ? "(missing-key)" : apiKey,
                BaseUrl = baseUrl,
            });
            AppendLog($"rig armed: base={baseUrl}, key={(string.IsNullOrEmpty(apiKey) ? "MISSING" : Mask(apiKey))}");
        }

        private void OnDisable()
        {
            _liveSub?.Dispose();
            _kraty?.Dispose();
        }

        private void OnGUI()
        {
            const int W = 480;
            GUILayout.BeginArea(new Rect(10, 10, W, Screen.height - 20), GUI.skin.box);

            GUILayout.Label("<b>Kraty SDK: Smoke Rig</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
            GUILayout.Space(4);
            GUILayout.Label($"player: {_activePlayerId}");
            GUILayout.Label($"attempt: {_lastAttemptId ?? "n/a"}    leaderboard: {_lastLeaderboardId ?? "n/a"}    lobby: {(string.IsNullOrEmpty(lobbyId) ? "n/a" : lobbyId)}");
            GUILayout.Space(6);

            GUI.enabled = !_busy;

            GUILayout.Label("<b>Identity</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("EnsureIdentity")) Run(EnsureIdentity);
            if (GUILayout.Button("Logout")) Run(Logout);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Events</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("List")) Run(ListEvents);
            if (GUILayout.Button("Start")) Run(StartEvent);
            if (GUILayout.Button("Progress +10")) Run(() => ProgressEvent(10));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Leaderboards</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Read (event)")) Run(ReadPerEventLeaderboard);
            if (GUILayout.Button("Read")) Run(ReadLeaderboard);
            if (GUILayout.Button("List periods")) Run(ListPeriods);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_liveSub == null ? "Subscribe live" : "Cancel live")) Run(ToggleLiveSubscribe);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Grants</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("List pending")) Run(ListPendingGrants);
            if (GUILayout.Button("CollectAll")) Run(CollectAllGrants);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Lobbies</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Read last lobby")) Run(ReadLobby);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Inventory + Wallet</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Inventory")) Run(ListInventory);
            if (GUILayout.Button("Wallet")) Run(ListWallet);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            if (GUILayout.Button("<b>Run ALL</b>", new GUIStyle(GUI.skin.button) { richText = true, fontSize = 13 }, GUILayout.Height(28)))
            {
                Run(RunAll);
            }

            GUI.enabled = true;

            GUILayout.Space(6);
            GUILayout.Label("<b>Log</b>", new GUIStyle(GUI.skin.label) { richText = true });
            _logScroll = GUILayout.BeginScrollView(_logScroll, GUI.skin.box, GUILayout.ExpandHeight(true));
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                GUILayout.Label(_log[i], new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true });
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        // ── individual surface exercises ─────────────────────────────────

        private async Task EnsureIdentity(CancellationToken ct)
        {
            var id = await _kraty.EnsureIdentityAsync(ct);
            _activePlayerId = id.ExternalPlayerId;
            AppendLog($"<color=#7fff7f>identity:</color> {_activePlayerId}");
        }

        private async Task Logout(CancellationToken ct)
        {
            await _kraty.LogoutAsync(ct);
            _activePlayerId = "(signed out)";
            AppendLog("<color=#ffd17f>logged out + cleared stored secret</color>");
        }

        private async Task ListEvents(CancellationToken ct)
        {
            var events = await _kraty.Events.ListForPlayerAsync(ct: ct);
            AppendLog($"events: {events.Count} active");
            foreach (var e in events) AppendLog($"  • {e.EventKey}  (type={e.Type}, mode={e.LeaderboardMode ?? "n/a"})");
            if (string.IsNullOrEmpty(eventKey) && events.Count > 0)
            {
                eventKey = events[0].EventKey;
                AppendLog($"  → auto-picked {eventKey}");
            }
        }

        private async Task StartEvent(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(eventKey)) throw new InvalidOperationException("set eventKey or call ListEvents first");
            var start = await _kraty.Events.StartAsync(eventKey, ct: ct);
            _lastAttemptId = start.Attempt.Id;
            _lastLeaderboardId = !string.IsNullOrEmpty(start.Attempt.LeaderboardId)
                ? start.Attempt.LeaderboardId
                : start.LeaderboardId;
            AppendLog($"<color=#7fff7f>started:</color> attempt={_lastAttemptId} lb={_lastLeaderboardId}");
        }

        private async Task ProgressEvent(double amount, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(eventKey)) throw new InvalidOperationException("no eventKey");
            if (string.IsNullOrEmpty(_lastAttemptId)) throw new InvalidOperationException("call Start first");
            var update = await _kraty.Events.ProgressAsync(eventKey, _lastAttemptId,
                new ProgressInput { Mode = "increment", MetricValue = amount }, ct: ct);
            AppendLog($"progress → score={update.Attempt.Score} (milestones fired: {update.MilestonesFired.Count})");
        }

        private async Task ReadPerEventLeaderboard(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_lastLeaderboardId)) throw new InvalidOperationException("call Start first");
            var board = await _kraty.EventLeaderboards.ReadAsync(_lastLeaderboardId,
                new EventLeaderboardReadOptions { Limit = 10, IncludeSelf = true }, ct);
            AppendLog($"event leaderboard: {board.Entries.Count} rows (mode={board.Mode}, finalized={board.Finalized})");
            if (board.Self != null) AppendLog($"  you: #{board.Self.Rank} score={board.Self.Score}");
        }

        private async Task ReadLeaderboard(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(leaderboardKey)) throw new InvalidOperationException("set leaderboardKey");
            var board = await _kraty.Leaderboards.ReadAsync(leaderboardKey,
                new LeaderboardReadOptions
                {
                    Limit = 10,
                    IncludeSelf = true,
                    Segment = string.IsNullOrEmpty(leaderboardSegment) ? null : leaderboardSegment,
                }, ct);
            AppendLog($"leaderboard {board.Key}: {board.Entries.Count} rows (period={board.Period}, segment={board.Segment ?? "n/a"})");
            if (board.Self != null) AppendLog($"  you: #{board.Self.Rank} score={board.Self.Score}");
        }

        private async Task ListPeriods(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(leaderboardKey)) throw new InvalidOperationException("set leaderboardKey");
            var resp = await _kraty.Leaderboards.ListPeriodsAsync(leaderboardKey, ct: ct);
            AppendLog($"periods for {resp.Key}: {resp.Periods.Count} (current started {resp.CurrentPeriodStartedAt})");
            foreach (var p in resp.Periods) AppendLog($"  • {p.PeriodStartedAt} → {p.PeriodEndedAt}");
        }

        private Task ToggleLiveSubscribe(CancellationToken ct)
        {
            if (_liveSub != null)
            {
                _liveSub.Dispose();
                _liveSub = null;
                AppendLog("live subscription cancelled");
                return Task.CompletedTask;
            }
            if (string.IsNullOrEmpty(_lastLeaderboardId)) throw new InvalidOperationException("call Start first to get a leaderboardId");
            _liveSub = _kraty.EventLeaderboards.Subscribe(_lastLeaderboardId, ev =>
            {
                // Callbacks fire on the background thread, so keep the OnGUI
                // side lock-free by deferring to the next OnGUI tick via the log.
                AppendLogThreadSafe($"<color=#9fc9ff>live:</color> {ev.Kind}");
            }, new SubscribeOptions
            {
                PollIntervalMs = 15000,
                OnError = err => AppendLogThreadSafe($"<color=#ff9f9f>live err:</color> {err.GetType().Name}: {err.Message}"),
            });
            AppendLog("live subscription opened (SSE + 15s poll)");
            return Task.CompletedTask;
        }

        private async Task ListPendingGrants(CancellationToken ct)
        {
            var grants = await _kraty.Grants.ListPendingAsync(ct: ct);
            AppendLog($"pending grants: {grants.Count}");
            foreach (var g in grants) AppendLog($"  • {g.Id}  kind={g.Kind}  source={g.SourceKind}");
        }

        private async Task CollectAllGrants(CancellationToken ct)
        {
            var result = await _kraty.Grants.CollectAllAsync(ct: ct);
            AppendLog($"collected: opened={result.Opened.Count}, claimed={result.Claimed.Count}, failures={result.Failures.Count}");
            foreach (var f in result.Failures) AppendLog($"  ! {f.Grant.Id} → {f.Error}");
        }

        private async Task ReadLobby(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(lobbyId)) throw new InvalidOperationException("paste a lobby id into the inspector first");
            var lobby = await _kraty.Lobbies.ReadAsync(lobbyId, ct);
            AppendLog($"lobby {lobby.Id}: status={lobby.Status} filled={lobby.FilledSlots}/{lobby.Capacity} (bots={lobby.BotSlots})");
        }

        private async Task ListInventory(CancellationToken ct)
        {
            var items = await _kraty.Inventory.ListAsync(ct: ct);
            AppendLog($"inventory: {items.Count} item rows");
            foreach (var i in items) AppendLog($"  • {i.ItemKey} ×{i.Quantity}");
        }

        private async Task ListWallet(CancellationToken ct)
        {
            var wallets = await _kraty.Wallet.ListAsync(ct: ct);
            AppendLog($"wallet: {wallets.Count} currency rows");
            foreach (var w in wallets) AppendLog($"  • {w.EconomyKey} = {w.Balance}");
        }

        // ── orchestration ────────────────────────────────────────────────

        private async Task RunAll(CancellationToken ct)
        {
            // Order matters: later steps depend on the IDs earlier steps populate.
            await EnsureIdentity(ct);
            await ListEvents(ct);
            if (!string.IsNullOrEmpty(eventKey))
            {
                await StartEvent(ct);
                await ProgressEvent(10, ct);
                await ReadPerEventLeaderboard(ct);
            }
            if (!string.IsNullOrEmpty(leaderboardKey))
            {
                await ReadLeaderboard(ct);
                await ListPeriods(ct);
            }
            await ListPendingGrants(ct);
            await CollectAllGrants(ct);
            await ListInventory(ct);
            await ListWallet(ct);
            AppendLog("<color=#7fff7f><b>Run ALL: finished</b></color>");
        }

        // ── plumbing ─────────────────────────────────────────────────────

        private void Run(Func<CancellationToken, Task> step)
        {
            if (_busy) return;
            _busy = true;
            _ = RunWrapped(step);
        }

        private void Run(Func<Task> step) => Run(_ => step());

        private async Task RunWrapped(Func<CancellationToken, Task> step)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await step(cts.Token);
            }
            catch (KratyApiError err)
            {
                AppendLog($"<color=#ff9f9f>api error:</color> [{err.Status}] {err.Code}: {err.Message}");
            }
            catch (Exception err)
            {
                AppendLog($"<color=#ff9f9f>error:</color> {err.GetType().Name}: {err.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        private readonly object _logLock = new();
        private readonly Queue<string> _pendingLog = new();
        private void AppendLog(string line)
        {
            var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
            _log.Add(stamped);
            if (_log.Count > 200) _log.RemoveAt(0);
            Debug.Log($"[kraty-smoke] {line}");
        }
        private void AppendLogThreadSafe(string line)
        {
            lock (_logLock) _pendingLog.Enqueue(line);
        }
        private void Update()
        {
            if (_pendingLog.Count == 0) return;
            lock (_logLock)
            {
                while (_pendingLog.Count > 0) AppendLog(_pendingLog.Dequeue());
            }
        }

        private static string Mask(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= 8) return "****";
            return s.Substring(0, 4) + "…" + s.Substring(s.Length - 4);
        }
    }
}
