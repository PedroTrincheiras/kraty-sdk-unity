// Kraty Unity SDK — end-to-end demo: normal events + leaderboards.
//
// Drop this on a GameObject in a scene, set the API key (a TEST
// client-SDK key from your game's dashboard), press Play, and watch the
// Console. It walks the full player loop:
//
//   1. Connect with a client-SDK key and lazily register a player.
//   2. List the events available to that player.
//   3. Start an attempt, report progress, and read the resulting score.
//   4. Read the per-event leaderboard (by the id from `start`).
//   5. Read a configurable cross-event leaderboard (by its dashboard key),
//      including a segmented "division" read.
//
// Everything here is illustrative — copy the call shapes into your own
// systems; you would not normally do all of this in one MonoBehaviour.

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kraty;
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
                // code + message — log it so you can see exactly what the API
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
            // Events are ONE way scores reach a leaderboard — an event that
            // `contributesTo` a board publishes the player's score into it.
            // Skipped when the game has no open events (then we score the
            // board directly in step 4).
            if (events.Count > 0)
            {
                var eventKey = events[0].EventKey;

                // `playerContext` carries values your boards may segment on
                // (e.g. region/platform for context-segmented boards).
                var start = await kraty.Events.StartAsync(
                    eventKey,
                    playerContext: new Dictionary<string, object?> { ["region"] = "EU" });
                var attemptId = start.Attempt.Id;
                Debug.Log($"[Kraty] started '{eventKey}' attempt={attemptId} → leaderboard {start.LeaderboardId}");

                // `metricValue` is the single-metric convenience — it routes
                // into the event's primary metric. `increment` adds; `set`
                // overwrites. (ProgressInput.Metrics = {..} for multi-metric.)
                var progress = await kraty.Events.ProgressAsync(
                    eventKey, attemptId,
                    new ProgressInput { Mode = "increment", MetricValue = 1250 });
                Debug.Log($"[Kraty] progress → score={progress.Attempt.Score} status={progress.Attempt.Status}");
                foreach (var m in progress.MilestonesFired)
                    Debug.Log($"  🎁 milestone fired: {m.Key}");

                // The per-event leaderboard (by the id from `start`).
                var eventBoard = await kraty.EventLeaderboards.ReadAsync(
                    start.LeaderboardId,
                    new EventLeaderboardReadOptions { Limit = 10, IncludeSelf = true });
                Debug.Log($"[Kraty] event leaderboard — top {eventBoard.Entries.Count}:");
                PrintEntries(eventBoard.Entries);
            }
            else
            {
                Debug.Log("[Kraty] no open events — going straight to direct leaderboard scoring.");
            }

            // ── 4. Score a configurable leaderboard DIRECTLY ──────────────
            // The OTHER way onto a leaderboard — no event required. Ideal for
            // boards fed straight from gameplay. Segmentation:
            //   • context board     → pass the division value as `Segment`.
            //   • progression board → OMIT it; the server picks the player's
            //                         division from their progression balance.
            // If the board is server-only (`acceptClientScores = false`) this
            // throws 403 `client_scoring_disabled` — score it from your backend
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
                Debug.LogWarning("[Kraty] board is server-only — submit from your backend (server SDK), not the client.");
            }
            catch (KratyApiError e) when (e.Code == "score_not_supported")
            {
                Debug.LogWarning("[Kraty] board ranks by a progression item — its value comes from that item's balance, not direct scores.");
            }

            // ── 5. Read the configurable leaderboard (by dashboard key) ───
            // For a context board pass the division VALUE as `Segment`; for a
            // progression board you can OMIT it to get your OWN division.
            var opts = new LeaderboardReadOptions { Limit = 10, IncludeSelf = true };
            if (!string.IsNullOrEmpty(division)) opts.Segment = division;

            var board = await kraty.Leaderboards.ReadAsync(leaderboardKey, opts);
            Debug.Log($"[Kraty] leaderboard '{leaderboardKey}'"
                      + (string.IsNullOrEmpty(division) ? "" : $" / {division}")
                      + $" — top {board.Entries.Count}:");
            PrintEntries(board.Entries);
        }

        private static void PrintEntries(List<LeaderboardEntry> entries)
        {
            foreach (var e in entries)
                Debug.Log($"  #{e.Rank,-3} {e.Name,-18} {e.Score,8}  [{e.Kind}]{(e.IsSelf ? " ← you" : "")}");
        }
    }
}
