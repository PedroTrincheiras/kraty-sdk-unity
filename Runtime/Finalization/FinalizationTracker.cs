#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Kraty
{
    // Client finalization catch-up; see docs/05b. Ported from the TS SDK
    // reference (packages/client/sdk-typescript/src/finalization.ts).
    //
    // CORE INVARIANT: a finalization is recorded through EXACTLY ONE writer,
    // ResolveFinalizedAsync. Both the live SSE `finalized` event AND
    // CheckFinalizationsAsync route through it, so the SSE path persists
    // Status=finalized + ReportedAt to the registry (not just fires the
    // callback). Whichever path arrives first wins; the other no-ops on
    // ReportedAt. That is what makes delivery exactly-once across live + catch-up.

    /// <summary>The kind of board a membership refers to. Reference these
    /// constants instead of hardcoding the wire strings,
    /// e.g. <c>MembershipKind.EventLeaderboard</c>, never <c>"event_leaderboard"</c>.</summary>
    public static class MembershipKind
    {
        /// <summary>A per-event (or per-session) board a player is placed on.</summary>
        public const string EventLeaderboard = "event_leaderboard";
        /// <summary>A recurring leaderboard (catch-up deferred; see docs/05b).</summary>
        public const string Leaderboard = "leaderboard";
    }

    /// <summary>Why a board finalized. The precise reasons come from the live SSE
    /// stream; a catch-up read reports <see cref="Finalized"/> only when the
    /// backend couldn't supply one.</summary>
    public static class FinalizationReason
    {
        /// <summary>A session/lobby inside an event terminated early.</summary>
        public const string SessionTerminated = "session_terminated";
        /// <summary>The event window closed.</summary>
        public const string WindowClosed = "window_closed";
        /// <summary>A recurring leaderboard rolled to a new period.</summary>
        public const string PeriodRolled = "period_rolled";
        /// <summary>Ended, but the precise cause is unknown.</summary>
        public const string Finalized = "finalized";
    }

    /// <summary>Whether a final standing belongs to a real player or a bot.</summary>
    public static class StandingKind
    {
        public const string Player = "player";
        public const string Bot = "bot";
    }

    /// <summary>A tracked board reference: either a per-event board (UUID) or a
    /// configurable leaderboard (key + period).</summary>
    public sealed class MembershipRef
    {
        [JsonProperty("kind")] public string Kind { get; set; } = MembershipKind.EventLeaderboard;
        [JsonProperty("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonProperty("eventKey")] public string? EventKey { get; set; }
        [JsonProperty("key")] public string? Key { get; set; }
        [JsonProperty("period")] public string? Period { get; set; }

        public static MembershipRef EventLeaderboard(string leaderboardId, string? eventKey = null) =>
            new() { Kind = MembershipKind.EventLeaderboard, LeaderboardId = leaderboardId, EventKey = eventKey };

        public bool SameAs(MembershipRef o)
        {
            if (Kind != o.Kind) return false;
            if (Kind == MembershipKind.EventLeaderboard) return LeaderboardId == o.LeaderboardId;
            if (Kind == MembershipKind.Leaderboard) return Key == o.Key && Period == o.Period;
            return false;
        }
    }

    public sealed class TrackedMembership
    {
        public const string StatusActive = "active";
        public const string StatusFinalized = "finalized";

        [JsonProperty("ref")] public MembershipRef Ref { get; set; } = new();
        [JsonProperty("status")] public string Status { get; set; } = StatusActive;
        [JsonProperty("joinedAt")] public string JoinedAt { get; set; } = string.Empty;
        [JsonProperty("reportedAt")] public string? ReportedAt { get; set; }
        [JsonProperty("label")] public string? Label { get; set; }
    }

    public sealed class FinalStanding
    {
        [JsonProperty("participantId")] public string ParticipantId { get; set; } = string.Empty;
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("kind")] public string Kind { get; set; } = StandingKind.Player;
        /// <summary>Avatar reference for this row, or null (bots / unset).
        /// Populated when the standings came from a board read (the normal
        /// path); may be null on the rare live-broadcast fallback.</summary>
        [JsonProperty("avatar")] public string? Avatar { get; set; }
        /// <summary>True for the row belonging to the player who received this
        /// result — use it to highlight "you" without matching ids yourself.
        /// Server-resolved on the board read; false on the live-broadcast
        /// fallback.</summary>
        [JsonProperty("isSelf")] public bool IsSelf { get; set; }
    }

    public sealed class SelfEntry
    {
        public int Rank { get; }
        public double Score { get; }
        public SelfEntry(int rank, double score) { Rank = rank; Score = score; }
    }

    public sealed class FinalizationResult
    {
        public MembershipRef Ref { get; set; } = new();
        /// <summary>One of the <see cref="FinalizationReason"/> constants.</summary>
        public string Reason { get; set; } = FinalizationReason.Finalized;
        public SelfEntry? Self { get; set; }
        public IReadOnlyList<FinalStanding>? Standings { get; set; }
        public string? EventKey { get; set; }
    }

    /// <summary>Board-status probe result the tracker asks the client for.
    /// <c>Reason</c> is one of the <see cref="FinalizationReason"/>
    /// constants, or null when the backend didn't supply one.</summary>
    public sealed class EventLeaderboardStatus
    {
        public bool Finalized { get; }
        public string? Reason { get; }
        public SelfEntry? Self { get; }
        /// <summary>The board's final rows (server-resolved: avatar + isSelf per
        /// row), so a finalization can be rendered without a second fetch. Null
        /// when the read couldn't return them.</summary>
        public IReadOnlyList<FinalStanding>? Standings { get; }
        public EventLeaderboardStatus(bool finalized, string? reason, SelfEntry? self, IReadOnlyList<FinalStanding>? standings = null)
        {
            Finalized = finalized;
            Reason = reason;
            Self = self;
            Standings = standings;
        }
    }

    /// <summary>Persisted membership registry, keyed per active player.</summary>
    public interface IMembershipStore
    {
        Task<List<TrackedMembership>> LoadAsync(string playerId, CancellationToken ct = default);
        Task SaveAsync(string playerId, List<TrackedMembership> entries, CancellationToken ct = default);
    }

    /// <summary>Volatile, process-local. Catch-up won't survive a restart.</summary>
    public sealed class InMemoryMembershipStore : IMembershipStore
    {
        private readonly Dictionary<string, string> _byPlayer = new();
        private readonly object _gate = new();

        public Task<List<TrackedMembership>> LoadAsync(string playerId, CancellationToken ct = default)
        {
            lock (_gate)
            {
                var json = _byPlayer.TryGetValue(playerId, out var v) ? v : null;
                return Task.FromResult(Deserialize(json));
            }
        }

        public Task SaveAsync(string playerId, List<TrackedMembership> entries, CancellationToken ct = default)
        {
            lock (_gate) { _byPlayer[playerId] = JsonConvert.SerializeObject(entries); }
            return Task.CompletedTask;
        }

        internal static List<TrackedMembership> Deserialize(string? json) =>
            string.IsNullOrEmpty(json)
                ? new List<TrackedMembership>()
                : JsonConvert.DeserializeObject<List<TrackedMembership>>(json!) ?? new List<TrackedMembership>();
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>Unity store backed by PlayerPrefs (main-thread marshalled).</summary>
    public sealed class PlayerPrefsMembershipStore : IMembershipStore
    {
        private readonly string _keyPrefix;
        private readonly SynchronizationContext? _mainThread;

        public PlayerPrefsMembershipStore(string keyPrefix = "kraty.memberships.")
        {
            _keyPrefix = keyPrefix;
            _mainThread = SynchronizationContext.Current;
        }

        private Task<T> OnMainThread<T>(Func<T> func)
        {
            if (_mainThread == null || _mainThread == SynchronizationContext.Current)
                return Task.FromResult(func());
            var tcs = new TaskCompletionSource<T>();
            _mainThread.Post(_ =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception e) { tcs.SetException(e); }
            }, null);
            return tcs.Task;
        }

        public Task<List<TrackedMembership>> LoadAsync(string playerId, CancellationToken ct = default) =>
            OnMainThread(() =>
            {
                var key = _keyPrefix + playerId;
                var json = UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetString(key) : null;
                return InMemoryMembershipStore.Deserialize(json);
            });

        public Task SaveAsync(string playerId, List<TrackedMembership> entries, CancellationToken ct = default) =>
            OnMainThread<object?>(() =>
            {
                UnityEngine.PlayerPrefs.SetString(_keyPrefix + playerId, JsonConvert.SerializeObject(entries));
                UnityEngine.PlayerPrefs.Save();
                return null;
            });
    }
#endif

    public sealed class FinalizationTracker
    {
        private readonly IMembershipStore _store;
        private readonly Func<Task<string?>> _getActivePlayerId;
        private readonly Func<string, Task<EventLeaderboardStatus?>> _readEventLeaderboard;
        private readonly TimeSpan _pruneAfter;
        private readonly List<Action<FinalizationResult>> _listeners = new();
        // Serializes all registry read-modify-write so a live SSE event and a
        // CheckFinalizationsAsync poll can't both pass the ReportedAt guard.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public FinalizationTracker(
            IMembershipStore store,
            Func<Task<string?>> getActivePlayerId,
            Func<string, Task<EventLeaderboardStatus?>> readEventLeaderboard,
            TimeSpan? pruneAfter = null)
        {
            _store = store;
            _getActivePlayerId = getActivePlayerId;
            _readEventLeaderboard = readEventLeaderboard;
            _pruneAfter = pruneAfter ?? TimeSpan.FromDays(7);
        }

        /// <summary>Register a finalization handler. Returns an unsubscribe action.</summary>
        public Action OnFinalized(Action<FinalizationResult> cb)
        {
            lock (_listeners) { _listeners.Add(cb); }
            return () => { lock (_listeners) { _listeners.Remove(cb); } };
        }

        /// <summary>Record that the player joined a board (idempotent upsert).</summary>
        public async Task TrackAsync(MembershipRef @ref, string? label = null)
        {
            var playerId = await _getActivePlayerId().ConfigureAwait(false);
            if (playerId == null) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var entries = Prune(await _store.LoadAsync(playerId).ConfigureAwait(false));
                if (!entries.Any(e => e.Ref.SameAs(@ref)))
                {
                    entries.Add(new TrackedMembership
                    {
                        Ref = @ref,
                        Status = TrackedMembership.StatusActive,
                        JoinedAt = DateTime.UtcNow.ToString("o"),
                        Label = label,
                    });
                }
                await _store.SaveAsync(playerId, entries).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        // The SINGLE writer. Persists status + reportedAt THEN fires the
        // callback. Returns true iff this call resolved the entry. Assumes the
        // caller does NOT hold _gate.
        private async Task<bool> ResolveFinalizedAsync(string playerId, MembershipRef @ref, FinalizationResult result)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var entries = await _store.LoadAsync(playerId).ConfigureAwait(false);
                var entry = entries.FirstOrDefault(e => e.Ref.SameAs(@ref));
                if (entry == null || entry.ReportedAt != null) return false;
                entry.Status = TrackedMembership.StatusFinalized;
                entry.ReportedAt = DateTime.UtcNow.ToString("o");
                await _store.SaveAsync(playerId, entries).ConfigureAwait(false); // persist BEFORE firing
                Emit(result);
                return true;
            }
            finally { _gate.Release(); }
        }

        /// <summary>Live SSE path: a `finalized` event arrived. Routes through the
        /// same writer as catch-up; persists to the registry AND fires.</summary>
        public async Task OnStreamFinalizedAsync(string leaderboardId, IDictionary<string, Newtonsoft.Json.Linq.JToken>? data)
        {
            var playerId = await _getActivePlayerId().ConfigureAwait(false);
            if (playerId == null) return;
            var reasonRaw = data != null && data.TryGetValue("reason", out var r) ? r?.ToString() : null;
            var reason = reasonRaw == FinalizationReason.SessionTerminated || reasonRaw == FinalizationReason.WindowClosed
                ? reasonRaw
                : FinalizationReason.Finalized;
            var @ref = MembershipRef.EventLeaderboard(leaderboardId);
            // The live `finalized` frame is a board-wide BROADCAST, so it can't
            // carry this viewer's `self` or per-row `isSelf`. Enrich with one
            // per-player board read so the result carries server-resolved
            // standings (avatar + isSelf) and the caller's self entry. Falls
            // back to the broadcast standings (no isSelf) if the read is
            // unavailable — delivery still happens either way.
            EventLeaderboardStatus? read;
            try { read = await _readEventLeaderboard(leaderboardId).ConfigureAwait(false); }
            catch { read = null; }
            var standings = read?.Standings ?? ExtractBroadcastStandings(data);
            await ResolveFinalizedAsync(playerId, @ref, new FinalizationResult
            {
                Ref = @ref,
                Reason = reason, // keep the precise SSE reason over the read's
                Self = read?.Self,
                Standings = standings,
            }).ConfigureAwait(false);
        }

        // Parse the board-wide `standings` array off the live broadcast frame,
        // used only as a fallback when the per-player board read is unavailable.
        // Rows from here have no per-viewer `isSelf`. Returns null when absent
        // or unparseable.
        private static IReadOnlyList<FinalStanding>? ExtractBroadcastStandings(IDictionary<string, Newtonsoft.Json.Linq.JToken>? data)
        {
            if (data == null || !data.TryGetValue("standings", out var token) || token == null) return null;
            try { return token.ToObject<List<FinalStanding>>(); }
            catch { return null; }
        }

        /// <summary>Catch-up path: poll still-active tracked boards and report any
        /// that finalized while away (through the same writer). Returns the new ones.</summary>
        public async Task<List<FinalizationResult>> CheckFinalizationsAsync()
        {
            var playerId = await _getActivePlayerId().ConfigureAwait(false);
            var outResults = new List<FinalizationResult>();
            if (playerId == null) return outResults;
            List<TrackedMembership> active;
            await _gate.WaitAsync().ConfigureAwait(false);
            try { active = (await _store.LoadAsync(playerId).ConfigureAwait(false)).Where(e => e.Status == TrackedMembership.StatusActive).ToList(); }
            finally { _gate.Release(); }

            foreach (var e in active)
            {
                if (e.Ref.Kind != MembershipKind.EventLeaderboard || e.Ref.LeaderboardId == null) continue; // leaderboard: see docs/05b (deferred)
                var status = await _readEventLeaderboard(e.Ref.LeaderboardId).ConfigureAwait(false);
                if (status == null || !status.Finalized) continue;
                var result = new FinalizationResult
                {
                    Ref = e.Ref,
                    // The board now persists WHY it finalized, so a catch-up read can
                    // tell a terminated session from a closed window. Finalized is
                    // only the fallback if the backend didn't supply a reason.
                    Reason = status.Reason ?? FinalizationReason.Finalized,
                    Self = status.Self,
                    Standings = status.Standings,
                    EventKey = e.Ref.EventKey,
                };
                if (await ResolveFinalizedAsync(playerId, e.Ref, result).ConfigureAwait(false))
                    outResults.Add(result);
            }
            return outResults;
        }

        /// <summary>Acknowledge a handled finalization: drop it from the registry.</summary>
        public async Task DismissAsync(MembershipRef @ref)
        {
            var playerId = await _getActivePlayerId().ConfigureAwait(false);
            if (playerId == null) return;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var entries = await _store.LoadAsync(playerId).ConfigureAwait(false);
                var kept = entries.Where(e => !e.Ref.SameAs(@ref)).ToList();
                if (kept.Count != entries.Count) await _store.SaveAsync(playerId, kept).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>Bulk-drop every already-reported membership. Returns the count.</summary>
        public async Task<int> ClearReportedAsync()
        {
            var playerId = await _getActivePlayerId().ConfigureAwait(false);
            if (playerId == null) return 0;
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var entries = await _store.LoadAsync(playerId).ConfigureAwait(false);
                var kept = entries.Where(e => e.ReportedAt == null).ToList();
                if (kept.Count != entries.Count) await _store.SaveAsync(playerId, kept).ConfigureAwait(false);
                return entries.Count - kept.Count;
            }
            finally { _gate.Release(); }
        }

        private List<TrackedMembership> Prune(List<TrackedMembership> entries)
        {
            var cutoff = DateTime.UtcNow - _pruneAfter;
            return entries.Where(e =>
            {
                var stamp = e.ReportedAt ?? e.JoinedAt;
                return !DateTime.TryParse(stamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts) || ts >= cutoff;
            }).ToList();
        }

        private void Emit(FinalizationResult result)
        {
            Action<FinalizationResult>[] snapshot;
            lock (_listeners) { snapshot = _listeners.ToArray(); }
            foreach (var l in snapshot)
            {
                try { l(result); }
                catch { /* a listener throwing must not break the writer */ }
            }
        }
    }
}
