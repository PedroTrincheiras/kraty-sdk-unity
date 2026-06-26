using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>
    /// Public response types for the <c>/sdk/v1</c> surface, mirroring
    /// the OpenAPI spec at <c>apps/backend/openapi.json</c>. Plain DTOs
    /// — Newtonsoft.Json deserializes into them via the
    /// <see cref="JsonPropertyAttribute"/> hints.
    ///
    /// LocalizedString-ish fields (<c>name</c>, <c>description</c>) and
    /// free-form metadata bags are typed as <c>JToken</c> /
    /// <c>Dictionary&lt;string, JToken&gt;</c> so the consumer can
    /// inspect the shape without the SDK locking it down prematurely.
    /// </summary>

    public sealed class Attempt
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonProperty("eventWindowId")] public string EventWindowId { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonProperty("startedAt")] public string StartedAt { get; set; } = string.Empty;
        [JsonProperty("endsAt")] public string EndsAt { get; set; } = string.Empty;
        [JsonProperty("completedAt")] public string? CompletedAt { get; set; }
        [JsonProperty("metrics")] public Dictionary<string, double> Metrics { get; set; } = new();
        [JsonProperty("metricsRaw")] public Dictionary<string, double> MetricsRaw { get; set; } = new();
        [JsonProperty("score")] public double Score { get; set; }
        /// <summary>One of <c>in_progress</c>, <c>completed</c>, <c>expired</c>, <c>force_completed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    }

    public sealed class StartAttemptResponse
    {
        [JsonProperty("attempt")] public Attempt Attempt { get; set; } = new();
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("windowEndsAt")] public string WindowEndsAt { get; set; } = string.Empty;
    }

    public sealed class ProgressResult
    {
        [JsonProperty("attempt")] public Attempt Attempt { get; set; } = new();

        /// <summary>
        /// Milestones whose threshold was crossed by THIS progress
        /// call. Always present (empty list when nothing fired) — never
        /// null, so callers can iterate without a null check. Each
        /// entry's <c>Grants</c> array carries the concrete payout
        /// rows in the same shape <c>GET /grants/pending</c> returns.
        /// </summary>
        [JsonProperty("milestonesFired")] public List<MilestoneFired> MilestonesFired { get; set; } = new();
    }

    /// <summary>
    /// A single milestone that fired during a <see cref="EventsClient.ProgressAsync"/>
    /// call. The <c>Key</c> is the designer-assigned id (use it to look
    /// up icon/copy/animation in your asset bundle). <c>Grants</c> is the
    /// concrete reward rows the engine wrote — render those directly or
    /// hand them to your existing grant-handling code.
    /// </summary>
    public sealed class MilestoneFired
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("grants")] public List<Grant> Grants { get; set; } = new();
    }

    /// <summary>
    /// One slot in an event's <c>entryCost.currencies</c> list — paid
    /// from the player's wallet on attempt start.
    /// </summary>
    public sealed class EntryCostCurrency
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("amount")] public int Amount { get; set; }
    }

    /// <summary>
    /// One slot in an event's <c>entryCost.items</c> list — consumed
    /// from inventory on attempt start.
    /// </summary>
    public sealed class EntryCostItem
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int Quantity { get; set; }
    }

    /// <summary>
    /// Transactional cost paid on <see cref="EventsClient.StartAsync"/>.
    /// Server atomically debits + creates the attempt in a single tx —
    /// partial debits never persist. Missing entry triggers a
    /// <see cref="KratyApiError"/> with code
    /// <c>insufficient_entry_cost</c>.
    /// </summary>
    public sealed class EntryCost
    {
        [JsonProperty("currencies")] public List<EntryCostCurrency> Currencies { get; set; } = new();
        [JsonProperty("items")] public List<EntryCostItem> Items { get; set; } = new();

        [JsonIgnore]
        public bool IsEmpty => (Currencies?.Count ?? 0) == 0 && (Items?.Count ?? 0) == 0;
    }

    /// <summary>
    /// One slot inside a reward bundle or milestone reward payload.
    /// Carries one of three shapes discriminated by <see cref="Type"/>:
    /// <c>currency</c>, <c>item</c>, or <c>crate</c>. Optional fields
    /// match whichever shape applies.
    /// </summary>
    public sealed class RewardEntryPreview
    {
        /// <summary>One of <c>currency</c> / <c>item</c> / <c>crate</c>.</summary>
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        /// <summary>Set when <see cref="Type"/> is <c>currency</c>.</summary>
        [JsonProperty("currencyKey")] public string? CurrencyKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>currency</c>.</summary>
        [JsonProperty("amount")] public double? Amount { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>item</c>.</summary>
        [JsonProperty("itemKey")] public string? ItemKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>crate</c>.</summary>
        [JsonProperty("crateItemKey")] public string? CrateItemKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>item</c> or <c>crate</c>.</summary>
        [JsonProperty("quantity")] public int? Quantity { get; set; }
        /// <summary>Optional per-item parameters (used by <c>item</c> entries).</summary>
        [JsonProperty("parameters")] public Dictionary<string, JToken>? Parameters { get; set; }
    }

    /// <summary>
    /// Inline preview of a reward bundle's contents. Surfaced on
    /// <see cref="EventListing.RewardPolicy"/> so the studio can
    /// render "you'll win X" without resolving bundle IDs against a
    /// separate endpoint.
    /// </summary>
    public sealed class RewardBundlePreview
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("entries")] public List<RewardEntryPreview> Entries { get; set; } = new();
    }

    /// <summary>
    /// One milestone reward — fires when the player crosses
    /// <see cref="Threshold"/> on <see cref="MetricKey"/> during a
    /// single attempt. Use this to render "next milestone: 5 rabbits
    /// → 200 cash + 5 bullets" in your UI.
    /// </summary>
    public sealed class MilestoneRewardPreview
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("metricKey")] public string MetricKey { get; set; } = string.Empty;
        [JsonProperty("threshold")] public double Threshold { get; set; }
        [JsonProperty("entries")] public List<RewardEntryPreview> Entries { get; set; } = new();
    }

    /// <summary>One tier in a <c>rank_scaled</c> reward policy.</summary>
    public sealed class RewardPolicyTier
    {
        [JsonProperty("fromRank")] public int FromRank { get; set; }
        [JsonProperty("toRank")] public int ToRank { get; set; }
        [JsonProperty("bundle")] public RewardBundlePreview? Bundle { get; set; }
    }

    /// <summary>
    /// Reward policy summary with inline bundle previews. Mirrors the
    /// four sealed policy types the backend supports:
    /// <list type="bullet">
    ///   <item><description><c>none</c> — event has no rewards.</description></item>
    ///   <item><description><c>fixed_bundle</c> — everyone who completes gets <see cref="Bundle"/>.</description></item>
    ///   <item><description><c>rank_scaled</c> — bundle picked by leaderboard rank; see <see cref="Tiers"/>.</description></item>
    ///   <item><description><c>shared_pool</c> — currency pool split among winners; see <see cref="Pool"/> and <see cref="CurrencyKey"/>.</description></item>
    /// </list>
    /// </summary>
    public sealed class RewardPolicySummary
    {
        [JsonProperty("type")] public string Type { get; set; } = "none";
        /// <summary>Set when <see cref="Type"/> is <c>fixed_bundle</c>.</summary>
        [JsonProperty("bundle")] public RewardBundlePreview? Bundle { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>rank_scaled</c>.</summary>
        [JsonProperty("tiers")] public List<RewardPolicyTier>? Tiers { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>shared_pool</c>.</summary>
        [JsonProperty("pool")] public double? Pool { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>shared_pool</c>.</summary>
        [JsonProperty("currencyKey")] public string? CurrencyKey { get; set; }
    }

    public sealed class EventListing
    {
        [JsonProperty("eventKey")] public string EventKey { get; set; } = string.Empty;
        /// <summary>
        /// LocalizedString — string or object, see backend docs/02 § English-only v1.
        /// Kept as <see cref="JToken"/> so the consumer can
        /// inspect the shape without the SDK locking it down prematurely.
        /// </summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("windowId")] public string WindowId { get; set; } = string.Empty;
        [JsonProperty("startsAt")] public string StartsAt { get; set; } = string.Empty;
        [JsonProperty("endsAt")] public string EndsAt { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonProperty("currentAttemptId")] public string? CurrentAttemptId { get; set; }

        /// <summary>
        /// <c>single_metric</c> / <c>multi_metric</c> (and any future
        /// event-type registry entries). Lets the SDK consumer pick
        /// the right UI without a hard-coded per-event catalog.
        /// </summary>
        [JsonProperty("type")] public string Type { get; set; } = "single_metric";

        /// <summary>
        /// <c>global</c> / <c>global_segmented</c> / <c>grouped</c> /
        /// <c>lobby_matched</c>. Combined with <see cref="LeaderboardId"/>
        /// tells the consumer whether to expect <c>lobby_forming</c>
        /// on <see cref="EventsClient.StartAsync"/>.
        /// </summary>
        [JsonProperty("leaderboardMode")] public string LeaderboardMode { get; set; } = "global";

        /// <summary>
        /// Free-form metric definitions from the event row (key,
        /// target, cap, scoreWeight, …) — same shape the server stores.
        /// </summary>
        [JsonProperty("metrics")] public List<Dictionary<string, JToken>> Metrics { get; set; } = new();

        /// <summary>Player-condition tree — null when there's no join gate.</summary>
        [JsonProperty("entryRequirement")] public Dictionary<string, JToken>? EntryRequirement { get; set; }

        /// <summary>
        /// Cost paid on attempt start. <c>null</c> (and an empty
        /// <see cref="EntryCost"/>) both mean "free to enter".
        /// </summary>
        [JsonProperty("entryCost")] public EntryCost? EntryCost { get; set; }

        /// <summary>
        /// Studio-defined free-form blob. Event-level metadata merged
        /// with the active window's metadata (window keys win). Use
        /// for UI hints — banner image keys, theme colors,
        /// special-event copy.
        /// </summary>
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();

        /// <summary>Mid-attempt milestone rewards. Empty list when none configured.</summary>
        [JsonProperty("milestoneRewards")] public List<MilestoneRewardPreview> MilestoneRewards { get; set; } = new();

        /// <summary>Reward policy summary with inline bundle previews.</summary>
        [JsonProperty("rewardPolicy")] public RewardPolicySummary? RewardPolicy { get; set; }

        /// <summary>
        /// True when <see cref="LeaderboardMode"/> is <c>lobby_matched</c>.
        /// Convenience flag for switching into a lobby-waiting view.
        /// </summary>
        [JsonIgnore]
        public bool IsLobbyMatched => LeaderboardMode == "lobby_matched";
    }

    /// <summary>
    /// One item row as exposed to game clients via the catalog
    /// endpoint. Display-relevant fields only — internal config and
    /// archival timestamps stay off the SDK wire format.
    /// </summary>
    public sealed class CatalogItem
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>LocalizedString.</summary>
        [JsonProperty("description")] public JToken? Description { get; set; }
        [JsonProperty("itemTypeKey")] public string ItemTypeKey { get; set; } = string.Empty;
        [JsonProperty("tags")] public List<string> Tags { get; set; } = new();
        [JsonProperty("rarity")] public string? Rarity { get; set; }
        [JsonProperty("attributes")] public Dictionary<string, JToken> Attributes { get; set; } = new();
    }

    /// <summary>
    /// One currency row as exposed to game clients. <see cref="Kind"/>
    /// distinguishes spendable currencies (<c>currency</c>) from
    /// progression resources (<c>progression</c>).
    /// </summary>
    public sealed class CatalogCurrency
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>LocalizedString.</summary>
        [JsonProperty("description")] public JToken? Description { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; } = "currency";
    }

    public sealed class Catalog
    {
        [JsonProperty("items")] public List<CatalogItem> Items { get; set; } = new();
        [JsonProperty("currencies")] public List<CatalogCurrency> Currencies { get; set; } = new();
    }

    public sealed class LeaderboardEntry
    {
        [JsonProperty("participantId")] public string ParticipantId { get; set; } = string.Empty;
        /// <summary>One of <c>player</c>, <c>bot</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("avatarUrl")] public string? AvatarUrl { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
        [JsonProperty("rank")] public int Rank { get; set; }
        /// <summary>
        /// <c>true</c> when this entry is the player calling the API
        /// (resolved from the <c>externalId</c> passed via
        /// <c>includeSelf</c>). Highlight rows off this rather than
        /// matching <c>ParticipantId</c> to the external id yourself —
        /// the server surfaces the internal player UUID, not the
        /// external one. Always <c>false</c> on entries without a
        /// self-context request, and on bot entries regardless.
        /// </summary>
        [JsonProperty("isSelf")] public bool IsSelf { get; set; }
    }

    public sealed class LeaderboardSelf
    {
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
    }

    /// <summary>
    /// Response from <see cref="LeaderboardsClient.ReadAsync"/> — a
    /// configurable, cross-event leaderboard addressed by its game-scoped
    /// <c>key</c> (e.g. <c>"weekly_global"</c>). This is what most games
    /// render; the auto-created per-event-window board is
    /// <see cref="EventLeaderboard"/>.
    /// </summary>
    public sealed class Leaderboard
    {
        /// <summary>The board's stable game-scoped key (e.g. <c>"weekly_global"</c>).</summary>
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>UUID of the leaderboard config row.</summary>
        [JsonProperty("sharedLeaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        /// <summary>One of <c>global</c>, <c>regional</c>, etc — set in the board's config.</summary>
        [JsonProperty("scope")] public string? Scope { get; set; }
        /// <summary>One of <c>daily</c>, <c>weekly</c>, <c>monthly</c>, <c>never</c>.</summary>
        [JsonProperty("resetCadence")] public string? ResetCadence { get; set; }
        /// <summary>How concurrent scores combine: <c>best</c>, <c>sum</c>, <c>last</c>.</summary>
        [JsonProperty("scoreAggregation")] public string? ScoreAggregation { get; set; }
        /// <summary>Resolved segment bucket for segmented boards; <c>null</c> on unsegmented boards.</summary>
        [JsonProperty("segment")] public string? Segment { get; set; }
        /// <summary>ISO timestamp of the period this read snapshot is from. <c>"current"</c> reads return the live period.</summary>
        [JsonProperty("period")] public string Period { get; set; } = string.Empty;
        [JsonProperty("entries")] public List<LeaderboardEntry> Entries { get; set; } = new();
        [JsonProperty("self")] public LeaderboardSelf? Self { get; set; }
    }

    /// <summary>
    /// One snapshot period on a leaderboard. Returned by
    /// <see cref="LeaderboardsClient.ListPeriodsAsync"/>; pass
    /// <see cref="PeriodStartedAt"/> as <c>opts.Period</c> on
    /// <see cref="LeaderboardsClient.ReadAsync"/> to read that period's
    /// snapshot.
    /// </summary>
    public sealed class LeaderboardPeriod
    {
        [JsonProperty("periodStartedAt")] public string PeriodStartedAt { get; set; } = string.Empty;
        [JsonProperty("periodEndedAt")] public string PeriodEndedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from <see cref="LeaderboardsClient.ListPeriodsAsync"/>: the
    /// board's identity plus a newest-first list of finalized periods.
    /// Useful for "last week's top 10" UI selectors.
    /// </summary>
    public sealed class LeaderboardPeriods
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("sharedLeaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("currentPeriodStartedAt")] public string CurrentPeriodStartedAt { get; set; } = string.Empty;
        [JsonProperty("periods")] public List<LeaderboardPeriod> Periods { get; set; } = new();
    }

    /// <summary>
    /// Response from <see cref="EventLeaderboardsClient.ReadAsync"/> — the
    /// auto-generated per-event-window leaderboard, addressed by the UUID
    /// returned in <c>Events.StartAsync(...)</c>'s
    /// <c>attempt.LeaderboardId</c>. For most game UI you want
    /// <see cref="Leaderboard"/> (the dashboard-configured board) instead.
    /// </summary>
    public sealed class EventLeaderboard
    {
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("mode")] public string Mode { get; set; } = string.Empty;
        [JsonProperty("finalized")] public bool Finalized { get; set; }
        [JsonProperty("entries")] public List<LeaderboardEntry> Entries { get; set; } = new();
        [JsonProperty("self")] public LeaderboardSelf? Self { get; set; }
    }

    public sealed class Grant
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        /// <summary>One of <c>reward</c>, <c>crate</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("contents")] public Dictionary<string, JToken> Contents { get; set; } = new();
        [JsonProperty("sourceKind")] public string SourceKind { get; set; } = string.Empty;
        [JsonProperty("sourceRefId")] public string? SourceRefId { get; set; }
        [JsonProperty("parentGrantId")] public string? ParentGrantId { get; set; }
        /// <summary>One of <c>pending</c>, <c>claimed</c>, <c>expired</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("rolledAt")] public string? RolledAt { get; set; }
        [JsonProperty("claimedAt")] public string? ClaimedAt { get; set; }
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    }

    public sealed class OpenCrateResponse
    {
        [JsonProperty("crate")] public Grant Crate { get; set; } = new();
        [JsonProperty("contents")] public Grant Contents { get; set; } = new();
    }

    public sealed class Lobby
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonProperty("eventWindowId")] public string EventWindowId { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; } = string.Empty;
        /// <summary>One of <c>forming</c>, <c>active</c>, <c>closed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("capacity")] public int Capacity { get; set; }
        [JsonProperty("fillBy")] public string? FillBy { get; set; }
        [JsonProperty("participantCount")] public int ParticipantCount { get; set; }

        /// <summary>
        /// Projected bot count at read time — derived server-side from
        /// the lobby's age and the matchmaking drip interval. Grows
        /// monotonically while the lobby is <c>forming</c>. UI typically
        /// renders <c>ParticipantCount + BotSlots</c> filled cells out
        /// of <c>Capacity</c> for a smooth fill animation.
        /// </summary>
        [JsonProperty("botSlots")] public int BotSlots { get; set; }

        [JsonProperty("startedAt")] public string? StartedAt { get; set; }
        [JsonProperty("endsAt")] public string? EndsAt { get; set; }

        /// <summary>
        /// Total filled cells (humans + projected bots). Capped at
        /// capacity in case server / client clocks ever disagree
        /// mid-drip.
        /// </summary>
        [JsonIgnore]
        public int FilledSlots
        {
            get
            {
                var total = ParticipantCount + BotSlots;
                return total > Capacity ? Capacity : total;
            }
        }
    }

    /// <summary>
    /// Input for <see cref="EventsClient.ProgressAsync"/>. Supply either
    /// <see cref="MetricValue"/> (single-metric events) or
    /// <see cref="Metrics"/> (multi-metric). <see cref="IdempotencyKey"/>
    /// is auto-generated by the client if you leave it null.
    /// </summary>
    public sealed class ProgressInput
    {
        /// <summary><c>set</c> writes the value as the new metric; <c>increment</c> adds.</summary>
        [JsonProperty("mode")] public string Mode { get; set; } = "set";
        [JsonProperty("metricValue")] public double? MetricValue { get; set; }
        [JsonProperty("metrics")] public Dictionary<string, double>? Metrics { get; set; }
        [JsonProperty("occurredAt")] public string? OccurredAt { get; set; }
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="LeaderboardsClient.ReadAsync"/>.
    /// </summary>
    public sealed class LeaderboardReadOptions
    {
        /// <summary>1–200, default 50 server-side.</summary>
        public int? Limit { get; set; }
        /// <summary>
        /// Bucket value for segmented boards. <b>Required</b> when the board
        /// is segmented (the server returns <c>400 validation_failed</c>
        /// otherwise). Pass the same value your SDK supplied in
        /// <c>playerContext[segmentation.key]</c> when starting the
        /// contributing attempt.
        /// </summary>
        public string? Segment { get; set; }
        /// <summary>
        /// <c>"current"</c> (default) reads the live period off Redis; an ISO
        /// timestamp (typically one of
        /// <see cref="LeaderboardPeriod.PeriodStartedAt"/>) reads the
        /// snapshotted final ranks for that period.
        /// </summary>
        public string? Period { get; set; }
        /// <summary>When true, response includes <c>self: { rank, score }</c> (live periods only).</summary>
        public bool IncludeSelf { get; set; }
        /// <summary>Required when <see cref="IncludeSelf"/> is true.</summary>
        public string? ExternalId { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="EventLeaderboardsClient.ReadAsync"/>.
    /// </summary>
    public sealed class EventLeaderboardReadOptions
    {
        /// <summary>1–200, default 50 server-side.</summary>
        public int? Limit { get; set; }
        /// <summary>When true, response includes <c>self: { rank, score }</c>.</summary>
        public bool IncludeSelf { get; set; }
        /// <summary>Required when <see cref="IncludeSelf"/> is true.</summary>
        public string? ExternalId { get; set; }
    }

    /// <summary>
    /// Result of <c>players.register()</c>. The plaintext
    /// <see cref="Secret"/> is only ever surfaced HERE — store it
    /// locally on the device immediately (e.g. <c>PlayerPrefs</c> via
    /// <c>PlayerPrefsSecretStore</c>). A subsequent call to
    /// <see cref="PlayersClient.RegisterAsync"/> for the same player
    /// returns 409 <c>player_already_registered</c>; lost-secret
    /// recovery is a studio-side admin flow, not a client capability.
    /// </summary>
    public sealed class PlayerRegistration
    {
        [JsonProperty("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("secret")] public string Secret { get; set; } = string.Empty;
        [JsonProperty("secretPrefix")] public string? SecretPrefix { get; set; }
        [JsonProperty("registeredAt")] public string? RegisteredAt { get; set; }
    }

    /// <summary>
    /// One row in the player's platform-managed inventory. Returned by
    /// <c>GET /sdk/v1/players/:externalId/inventory</c>. The item's
    /// display name and other catalog metadata live on the <c>items</c>
    /// table — the SDK only carries the per-player quantity + free-form
    /// metadata stamped on deposits (e.g. a granted potion's roll
    /// details).
    /// </summary>
    public sealed class PlayerItemHolding
    {
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int Quantity { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// One row in the player's wallet. Wallets are kind-agnostic: a
    /// <c>gold</c> currency entry sits beside a <c>trophies</c>
    /// progression entry with the same shape. The owning currency's
    /// <c>kind</c> lives on the catalog row, not here.
    /// </summary>
    public sealed class PlayerWalletHolding
    {
        [JsonProperty("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonProperty("balance")] public int Balance { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Input for <see cref="InventoryClient.ConsumeAsync"/>. The server
    /// requires <see cref="IdempotencyKey"/> for consume — the SDK
    /// auto-generates one if you leave it null, matching the
    /// auto-stamping behavior on every other POST endpoint.
    /// </summary>
    public sealed class ConsumeItemInput
    {
        /// <summary>
        /// Positive integer — the SDK does NOT enforce; the server
        /// validates and returns 400 on zero / negative.
        /// </summary>
        [JsonProperty("quantity")] public int Quantity { get; set; }

        /// <summary>
        /// Free-form tag persisted on the ledger row only. Surfaces in
        /// the admin audit screen.
        /// </summary>
        [JsonProperty("reason")] public string? Reason { get; set; }

        /// <summary>Optional override — leave null to let the client auto-stamp one.</summary>
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Result of <see cref="InventoryClient.ConsumeAsync"/>.
    /// <see cref="Applied"/> distinguishes a fresh mutation from an
    /// idempotent replay (the server returns the prior state when the
    /// same key arrives twice).
    /// </summary>
    public sealed class ConsumeItemResult
    {
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int Quantity { get; set; }
        [JsonProperty("applied")] public bool Applied { get; set; }
    }

    /// <summary>
    /// Input for <see cref="WalletClient.DebitAsync"/>. Same idempotency
    /// story as <see cref="ConsumeItemInput"/>. Credits are intentionally
    /// NOT in the SDK — only the studio's backend
    /// (<c>/server/v1/...</c>) can mint balance, so a client SDK that
    /// exposed <c>credit</c> would invite money printing.
    /// </summary>
    public sealed class DebitWalletInput
    {
        [JsonProperty("amount")] public int Amount { get; set; }
        [JsonProperty("reason")] public string? Reason { get; set; }
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public sealed class DebitWalletResult
    {
        [JsonProperty("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonProperty("balance")] public int Balance { get; set; }
        [JsonProperty("applied")] public bool Applied { get; set; }
    }

    /// <summary>
    /// Inventory list response wrapper — the backend nests the list
    /// inside <c>{ "data": { "items": [...] } }</c>.
    /// </summary>
    internal sealed class InventoryListEnvelope
    {
        [JsonProperty("items")] public List<PlayerItemHolding>? Items { get; set; }
    }

    /// <summary>
    /// Wallet list response wrapper — the backend nests the list
    /// inside <c>{ "data": { "wallet": [...] } }</c>.
    /// </summary>
    internal sealed class WalletListEnvelope
    {
        [JsonProperty("wallet")] public List<PlayerWalletHolding>? Wallet { get; set; }
    }

    /// <summary>
    /// Generic envelope: every successful SDK response is shaped as
    /// <c>{ "data": T }</c>. The client unwraps automatically.
    /// </summary>
    internal sealed class DataEnvelope<T>
    {
        [JsonProperty("data")] public T? Data { get; set; }
    }
}
