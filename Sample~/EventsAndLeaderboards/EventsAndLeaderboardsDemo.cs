// Kraty Unity SDK end-to-end demo: normal events + leaderboards.
//
// Drop this on a GameObject in a scene, set the API key (a TEST
// client-SDK key from your game's dashboard), press Play, and watch the
// Console. It walks the full player loop:
//
//   1. Connect with a client-SDK key and lazily register a player.
//   2. List the events available to that player.
//   3. Start an attempt, report progress, and read the resulting score.
//   4. JOIN a cross-event board (segmented, without submitting a score).
//   5. Submit a score DIRECTLY to a cross-event board.
//   6. Read the cross-event board (single segment).
//   7. Multi-segment STANDINGS read (all divisions, or just yours).
//
// Everything here is illustrative; copy the call shapes into your own
// systems; you would not normally do all of this in one MonoBehaviour.

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kraty;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kraty.Samples
{
    public sealed class EventsAndLeaderboardsDemo : MonoBehaviour
    {
        [Header("Auth")]
        [Tooltip("A TEST client-SDK API key (prefix.secret) from your game dashboard.")]
        [SerializeField] private string apiKey = "<your-test-client-sdk-key>";
        [Tooltip("Prod is the default; point at staging if you need to.")]
        [SerializeField] private string baseUrl = "https://api.kraty.io";

        [Header("Leaderboard to read")]
        [Tooltip("Dashboard key of a configurable leaderboard (e.g. Sniper Hunter's 'weekly_league').")]
        [SerializeField] private string leaderboardKey = "weekly_league";
        [Tooltip("Division to read when the board is segmented (e.g. 'Champion'). Leave blank for unsegmented boards.")]
        [SerializeField] private string division = "Champion";

        private async void Start()
        {
            try
            {
                await RunAsync();
            }
            catch (KratyApiError e)
            {
                // Every backend error surfaces as KratyApiError with a typed
                // code + message, so log it to see exactly what the API
                // rejected (e.g. a bad division value lists the valid ones).
                Debug.LogError($"[Kraty] API error {e.Status} {e.Code}: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Kraty] {e}");
            }
        }

        private async Task RunAsync()
        {
            // ── 1. Connect ────────────────────────────────────────────────
            // One Kraty instance owns the HttpClient + all resource clients.
            // No PlayerSecret/ActiveExternalPlayerId set → the SDK auto-
            // registers a fresh player on the first player-scoped call and
            // remembers the secret in its SecretStore.
            using var kraty = new Kraty.Kraty(new KratyClientOptions
            {
                ApiKey = apiKey,
                BaseUrl = baseUrl,
            });

            var identity = await kraty.EnsureIdentityAsync();
            Debug.Log($"[Kraty] player ready: {identity.ExternalPlayerId}");

            // ── 2. List the events open to this player ────────────────────
            var events = await kraty.Events.ListForPlayerAsync();
            Debug.Log($"[Kraty] {events.Count} event(s) available");
            foreach (var ev in events)
                Debug.Log($"  • {ev.EventKey}  (leaderboardId={ev.LeaderboardId})");

            // ── 3. (Optional) Play an event: start → progress → score ─────
            // Events are ONE way scores reach a leaderboard: an event that
            // `contributesTo` a board publishes the player's score into it.
            // Skipped when the game has no open events (then we score the
            // board directly in step 4).
            string liveBoardId = null;
            if (events.Count > 0)
            {
                var eventKey = events[0].EventKey;

                // `playerContext` carries values your boards may segment on
                // (e.g. region/platform for context-segmented boards).
                var start = await kraty.Events.StartAsync(
                    eventKey,
                    playerContext: new Dictionary<string, object?> { ["region"] = "EU" });
                var attemptId = start.Attempt.Id;
                liveBoardId = start.LeaderboardId;
                Debug.Log($"[Kraty] started '{eventKey}' attempt={attemptId} → leaderboard {start.LeaderboardId}");

                // `metricValue` is the single-metric convenience; it routes
                // into the event's primary metric. `increment` adds; `set`
                // overwrites. (ProgressInput.Metrics = {..} for multi-metric.)
                var progress = await kraty.Events.ProgressAsync(
                    eventKey, attemptId,
                    new ProgressInput { Mode = "increment", MetricValue = 1250 });
                Debug.Log($"[Kraty] progress → score={progress.Attempt.Score} status={progress.Attempt.Status}");
                foreach (var m in progress.MilestonesFired)
                    Debug.Log($"  🎁 milestone fired: {m.Key}");

                // The per-event leaderboard (by the id from `start`).
                var eventLeaderboard = await kraty.EventLeaderboards.ReadAsync(
                    start.LeaderboardId,
                    new EventLeaderboardReadOptions { Limit = 10, IncludeSelf = true });
                Debug.Log($"[Kraty] event leaderboard, top {eventLeaderboard.Entries.Count}:");
                PrintEntries(eventLeaderboard.Entries);
            }
            else
            {
                Debug.Log("[Kraty] no open events; going straight to direct leaderboard scoring.");
            }

            // ── 3b. LIVE updates + the `finalized` handler (sessions) ─────
            // Subscribe to the per-event board for low-latency updates. Beyond
            // `score_update`, the stream delivers a `finalized` event when the
            // board ENDS: either a session (lobby) hit its end trigger
            // (sudden-death: first to N, roster full, idle, …) or the whole
            // event window closed. On `finalized`, render the result screen and
            // stop expecting score updates.
            //
            // Callbacks fire on the HTTP background thread, so marshal to Unity's
            // main thread (e.g. a MainThreadDispatcher) before touching UI.
            // `Debug.Log` is fine to call from any thread.
            if (liveBoardId != null)
            {
                using var sub = kraty.EventLeaderboards.Subscribe(
                    liveBoardId,
                    ev =>
                    {
                        switch (ev.Kind)
                        {
                            case "score_update":
                                Debug.Log($"[Kraty] live score → {Val(ev, "participantId")} = {Val(ev, "score")}");
                                break;
                            case "finalized":
                                // reason: "session_terminated" (your lobby ended
                                // early) or "window_closed" (the whole event ended).
                                Debug.Log($"[Kraty] 🏁 board finalized ({Val(ev, "reason")}), final standings:");
                                if (ev.Data.TryGetValue("standings", out var s) && s is JArray rows)
                                {
                                    var shown = 0;
                                    foreach (var r in rows)
                                    {
                                        if (shown++ >= 5) break;
                                        Debug.Log($"    #{r["rank"]} {r["name"]}: {r["score"]} [{r["kind"]}]");
                                    }
                                    // Find YOUR placement by the participantId you
                                    // captured from `start`/reads to show "you placed Nth".
                                }
                                break;
                        }
                    },
                    new SubscribeOptions
                    {
                        PollIntervalMs = 15000, // 0 = SSE-only
                        OnError = e => Debug.LogWarning($"[Kraty] stream: {e.Message}"),
                    });

                await Task.Delay(1500); // let a few live frames land during the demo
            }

            // ── 3c. What happened while you were AWAY ─────────────────────
            // The `finalized` event above is LIVE-ONLY: a disconnected player
            // misses it, and on return they're on a FRESH board (the next
            // session/window). So DON'T rely on the SSE for correctness; the
            // durable channel is GRANTS. Any reward from a finalized session or
            // window (per-session prize, main-board reward, promotion/relegation)
            // persists as a pending grant. Pull + collect them on app
            // foreground / reconnect to tell the player what they earned.
            var pending = await kraty.Grants.ListPendingAsync(limit: 50);
            if (pending.Count > 0)
            {
                Debug.Log($"[Kraty] {pending.Count} reward(s) earned since you last played:");
                foreach (var g in pending)
                    Debug.Log($"    • {g.Kind} (source={g.SourceKind}, ref={g.SourceRefId})");
                var collected = await kraty.Grants.CollectAllAsync();
                Debug.Log($"[Kraty] collected {collected.Claimed.Count} grant(s) + opened {collected.Opened.Count} crate(s).");
                // Placement from a board you missed: if you stored its leaderboardId,
                // read it back: a finalized board still serves its final standings:
                //   await kraty.EventLeaderboards.ReadAsync(storedBoardId, new(){ IncludeSelf = true });
            }

            // ── 3d. Finalization catch-up (durable, exactly-once) ─────────
            // The SDK does the "which of my boards ended while I was away?"
            // bookkeeping FOR you (docs/05b). Every Events.StartAsync auto-
            // tracks its board in a persisted registry. Register ONE handler
            // and it fires for BOTH paths: the live SSE `finalized` above AND
            // boards that ended while the app was closed, exactly once each.
            //
            // `result.Reason` distinguishes SessionTerminated (your lobby ended
            // early) from WindowClosed (the whole event ended) even on the
            // catch-up path, because the board persists why it finalized.
            var unsubscribe = kraty.OnFinalized(result =>
            {
                var placement = result.Self != null ? $"#{result.Self.Rank}" : "N/A";
                Debug.Log($"[Kraty] catch-up: board {result.Ref.LeaderboardId} ended "
                          + $"({result.Reason}), you placed {placement}. Show the result screen…");
                // Acknowledge so it never resurfaces and leaves local storage.
                _ = kraty.DismissAsync(result.Ref);
            });

            // Call this on app FOREGROUND / reconnect. Cheap when nothing ended.
            var newlyEnded = await kraty.CheckFinalizationsAsync();
            Debug.Log($"[Kraty] {newlyEnded.Count} board(s) finalized while away.");
            unsubscribe(); // drop the handler when leaving this screen

            // ── 4. JOIN a cross-event board (no score submitted) ──────────
            // Enrols the active player in the board's current period at score
            // 0, and returns the board view so you can render it immediately.
            // Use this when the player should APPEAR in the ranking as soon as
            // they open the leaderboard UI, even before they've played:
            //   • Weekly leagues where "starting at 0" is the intended UX.
            //   • Progression-segmented boards: join derives the caller's
            //     division from their progression balance server-side, so
            //     leave `Segment` null; the response carries the resolved
            //     segment for you to display.
            //   • Boards you want to render "empty but present" on first
            //     launch instead of a "not on the board yet" empty state.
            //
            // Idempotent: re-joining NEVER resets an existing score. On event
            // leaderboards, `join` throws 409 `conflict` once the window has
            // finalized (nothing to enroll into anymore).
            var joinOpts = new LeaderboardJoinOptions { Limit = 10 };
            // Context-segmented boards: pass the division VALUE. Progression-
            // segmented boards: omit it, and the server derives it from your balance.
            if (!string.IsNullOrEmpty(division)) joinOpts.Segment = division;
            var joined = await kraty.Leaderboards.JoinAsync(leaderboardKey, joinOpts);
            Debug.Log($"[Kraty] joined '{leaderboardKey}'"
                      + (string.IsNullOrEmpty(division) ? "" : $" / {division}")
                      + $", you are rank #{joined.Self?.Rank.ToString() ?? "-"} at score {joined.Self?.Score ?? 0}"
                      + $" (joined-response={joined.Joined})");

            // ── 5. Score a configurable leaderboard DIRECTLY ──────────────
            // The OTHER way onto a leaderboard, with no event required. Ideal for
            // boards fed straight from gameplay. Segmentation:
            //   • context board     → pass the division value as `Segment`.
            //   • progression board → OMIT it; the server picks the player's
            //                         division from their progression balance.
            // If the board is server-only (`acceptClientScores = false`) this
            // throws 403 `client_scoring_disabled`, so score it from your backend
            // with the server SDK instead (anti-cheat).
            try
            {
                var submitOpts = new LeaderboardSubmitOptions();
                if (!string.IsNullOrEmpty(division)) submitOpts.Segment = division;
                var result = await kraty.Leaderboards.SubmitScoreAsync(leaderboardKey, 4200, submitOpts);
                Debug.Log($"[Kraty] submitted score to '{leaderboardKey}' → rank #{result.Rank} score {result.Score}");
            }
            catch (KratyApiError e) when (e.Code == "client_scoring_disabled")
            {
                Debug.LogWarning("[Kraty] board is server-only, so submit from your backend (server SDK), not the client.");
            }
            catch (KratyApiError e) when (e.Code == "score_not_supported")
            {
                Debug.LogWarning("[Kraty] board ranks by a progression item, so its value comes from that item's balance, not direct scores.");
            }

            // ── 6. Read the configurable leaderboard (by dashboard key) ───
            // For a context board pass the division VALUE as `Segment`; for a
            // progression board you can OMIT it to get your OWN division.
            var opts = new LeaderboardReadOptions { Limit = 10, IncludeSelf = true };
            if (!string.IsNullOrEmpty(division)) opts.Segment = division;

            var board = await kraty.Leaderboards.ReadAsync(leaderboardKey, opts);
            Debug.Log($"[Kraty] leaderboard '{leaderboardKey}'"
                      + (string.IsNullOrEmpty(division) ? "" : $" / {division}")
                      + $", top {board.Entries.Count}:");
            PrintEntries(board.Entries);

            // ── 7. Multi-segment STANDINGS (versatile read) ───────────────
            // `ReadAsync` returns ONE segment. `StandingsAsync` returns one
            // block per segment picked by `Scope`:
            //   • "self_segment" → just the caller's home division.
            //   • "mine"         → every division the caller appears in.
            //   • "segment"      → the one named in `Segment`.
            //   • "all"          → every division for the period (nice for
            //                      the "ladder overview" screen).
            // Each block flags participation + selfRank, so the UI can render
            // "you are #3 in Champion, not present in Gold, …" without extra
            // reads. Set `Period` to an ISO timestamp from `ListPeriodsAsync`
            // for historical ladders; leave as "current" for the live period.
            var ladder = await kraty.Leaderboards.StandingsAsync(leaderboardKey,
                new StandingsReadOptions { Scope = "all", Limit = 5, MaxSegments = 8 });
            Debug.Log($"[Kraty] standings '{leaderboardKey}': {ladder.Segments.Count} segment(s)"
                      + (ladder.SegmentsTruncated ? " (more truncated)" : "") + ":");
            foreach (var seg in ladder.Segments)
            {
                Debug.Log($"  · {seg.Segment ?? "(unsegmented)"}: "
                          + $"{(seg.Participated ? $"you rank #{seg.SelfRank}" : "not on this ladder")}");
                PrintEntries(seg.Entries);
            }
        }

        // Reads a scalar field off a stream event's `data` payload as a string.
        private static string Val(LeaderboardStreamEvent ev, string key)
            => ev.Data.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

        private static void PrintEntries(List<LeaderboardEntry> entries)
        {
            foreach (var e in entries)
                Debug.Log($"  #{e.Rank,-3} {e.Name,-18} {e.Score,8}  [{e.Kind}]{(e.IsSelf ? " ← you" : "")}");
        }
    }
}
