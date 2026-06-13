using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kraty
{
    /// <summary>
    /// Public response types for the <c>/sdk/v1</c> surface, mirroring
    /// the OpenAPI spec at <c>apps/backend/openapi.json</c>. Plain DTOs
    /// — System.Text.Json deserializes into them via the
    /// <see cref="JsonPropertyNameAttribute"/> hints.
    /// </summary>

    public sealed class Attempt
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonPropertyName("eventWindowId")] public string EventWindowId { get; set; } = string.Empty;
        [JsonPropertyName("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonPropertyName("startedAt")] public string StartedAt { get; set; } = string.Empty;
        [JsonPropertyName("endsAt")] public string EndsAt { get; set; } = string.Empty;
        [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
        [JsonPropertyName("metrics")] public Dictionary<string, double> Metrics { get; set; } = new();
        [JsonPropertyName("metricsRaw")] public Dictionary<string, double> MetricsRaw { get; set; } = new();
        [JsonPropertyName("score")] public double Score { get; set; }
        /// <summary>One of <c>in_progress</c>, <c>completed</c>, <c>expired</c>, <c>force_completed</c>.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    }

    public sealed class StartAttemptResponse
    {
        [JsonPropertyName("attempt")] public Attempt Attempt { get; set; } = new();
        [JsonPropertyName("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonPropertyName("windowEndsAt")] public string WindowEndsAt { get; set; } = string.Empty;
    }

    public sealed class ProgressResult
    {
        [JsonPropertyName("attempt")] public Attempt Attempt { get; set; } = new();

        /// <summary>
        /// Milestones whose threshold was crossed by THIS progress
        /// call. Always present (empty list when nothing fired) — never
        /// null, so callers can iterate without a null check. Each
        /// entry's <c>Grants</c> array carries the concrete payout
        /// rows in the same shape <c>GET /grants/pending</c> returns.
        /// </summary>
        [JsonPropertyName("milestonesFired")] public List<MilestoneFired> MilestonesFired { get; set; } = new();
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
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("grants")] public List<Grant> Grants { get; set; } = new();
    }

    /// <summary>
    /// One slot in an event's <c>entryCost.currencies</c> list — paid
    /// from the player's wallet on attempt start.
    /// </summary>
    public sealed class EntryCostCurrency
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("amount")] public int Amount { get; set; }
    }

    /// <summary>
    /// One slot in an event's <c>entryCost.items</c> list — consumed
    /// from inventory on attempt start.
    /// </summary>
    public sealed class EntryCostItem
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
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
        [JsonPropertyName("currencies")] public List<EntryCostCurrency> Currencies { get; set; } = new();
        [JsonPropertyName("items")] public List<EntryCostItem> Items { get; set; } = new();

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
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        /// <summary>Set when <see cref="Type"/> is <c>currency</c>.</summary>
        [JsonPropertyName("currencyKey")] public string? CurrencyKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>currency</c>.</summary>
        [JsonPropertyName("amount")] public double? Amount { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>item</c>.</summary>
        [JsonPropertyName("itemKey")] public string? ItemKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>crate</c>.</summary>
        [JsonPropertyName("crateItemKey")] public string? CrateItemKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>item</c> or <c>crate</c>.</summary>
        [JsonPropertyName("quantity")] public int? Quantity { get; set; }
        /// <summary>Optional per-item parameters (used by <c>item</c> entries).</summary>
        [JsonPropertyName("parameters")] public Dictionary<string, System.Text.Json.JsonElement>? Parameters { get; set; }
    }

    /// <summary>
    /// Inline preview of a reward bundle's contents. Surfaced on
    /// <see cref="EventListing.RewardPolicy"/> so the studio can
    /// render "you'll win X" without resolving bundle IDs against a
    /// separate endpoint.
    /// </summary>
    public sealed class RewardBundlePreview
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonPropertyName("name")] public System.Text.Json.JsonElement Name { get; set; }
        [JsonPropertyName("entries")] public List<RewardEntryPreview> Entries { get; set; } = new();
    }

    /// <summary>
    /// One milestone reward — fires when the player crosses
    /// <see cref="Threshold"/> on <see cref="MetricKey"/> during a
    /// single attempt. Use this to render "next milestone: 5 rabbits
    /// → 200 cash + 5 bullets" in your UI.
    /// </summary>
    public sealed class MilestoneRewardPreview
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("metricKey")] public string MetricKey { get; set; } = string.Empty;
        [JsonPropertyName("threshold")] public double Threshold { get; set; }
        [JsonPropertyName("entries")] public List<RewardEntryPreview> Entries { get; set; } = new();
    }

    /// <summary>One tier in a <c>rank_scaled</c> reward policy.</summary>
    public sealed class RewardPolicyTier
    {
        [JsonPropertyName("fromRank")] public int FromRank { get; set; }
        [JsonPropertyName("toRank")] public int ToRank { get; set; }
        [JsonPropertyName("bundle")] public RewardBundlePreview? Bundle { get; set; }
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
        [JsonPropertyName("type")] public string Type { get; set; } = "none";
        /// <summary>Set when <see cref="Type"/> is <c>fixed_bundle</c>.</summary>
        [JsonPropertyName("bundle")] public RewardBundlePreview? Bundle { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>rank_scaled</c>.</summary>
        [JsonPropertyName("tiers")] public List<RewardPolicyTier>? Tiers { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>shared_pool</c>.</summary>
        [JsonPropertyName("pool")] public double? Pool { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>shared_pool</c>.</summary>
        [JsonPropertyName("currencyKey")] public string? CurrencyKey { get; set; }
    }

    public sealed class EventListing
    {
        [JsonPropertyName("eventKey")] public string EventKey { get; set; } = string.Empty;
        /// <summary>
        /// LocalizedString — string or object, see backend docs/02 § English-only v1.
        /// Kept as <see cref="System.Text.Json.JsonElement"/> so the consumer can
        /// inspect the shape without the SDK locking it down prematurely.
        /// </summary>
        [JsonPropertyName("name")] public System.Text.Json.JsonElement Name { get; set; }
        [JsonPropertyName("windowId")] public string WindowId { get; set; } = string.Empty;
        [JsonPropertyName("startsAt")] public string StartsAt { get; set; } = string.Empty;
        [JsonPropertyName("endsAt")] public string EndsAt { get; set; } = string.Empty;
        [JsonPropertyName("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonPropertyName("currentAttemptId")] public string? CurrentAttemptId { get; set; }

        /// <summary>
        /// <c>single_metric</c> / <c>multi_metric</c> (and any future
        /// event-type registry entries). Lets the SDK consumer pick
        /// the right UI without a hard-coded per-event catalog.
        /// </summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "single_metric";

        /// <summary>
        /// <c>global</c> / <c>global_segmented</c> / <c>grouped</c> /
        /// <c>lobby_matched</c>. Combined with <see cref="LeaderboardId"/>
        /// tells the consumer whether to expect <c>lobby_forming</c>
        /// on <see cref="EventsClient.StartAsync"/>.
        /// </summary>
        [JsonPropertyName("leaderboardMode")] public string LeaderboardMode { get; set; } = "global";

        /// <summary>
        /// Free-form metric definitions from the event row (key,
        /// target, cap, scoreWeight, …) — same shape the server stores.
        /// </summary>
        [JsonPropertyName("metrics")] public List<Dictionary<string, System.Text.Json.JsonElement>> Metrics { get; set; } = new();

        /// <summary>Player-condition tree — null when there's no join gate.</summary>
        [JsonPropertyName("entryRequirement")] public Dictionary<string, System.Text.Json.JsonElement>? EntryRequirement { get; set; }

        /// <summary>
        /// Cost paid on attempt start. <c>null</c> (and an empty
        /// <see cref="EntryCost"/>) both mean "free to enter".
        /// </summary>
        [JsonPropertyName("entryCost")] public EntryCost? EntryCost { get; set; }

        /// <summary>
        /// Studio-defined free-form blob. Event-level metadata merged
        /// with the active window's metadata (window keys win). Use
        /// for UI hints — banner image keys, theme colors,
        /// special-event copy.
        /// </summary>
        [JsonPropertyName("metadata")] public Dictionary<string, System.Text.Json.JsonElement> Metadata { get; set; } = new();

        /// <summary>Mid-attempt milestone rewards. Empty list when none configured.</summary>
        [JsonPropertyName("milestoneRewards")] public List<MilestoneRewardPreview> MilestoneRewards { get; set; } = new();

        /// <summary>Reward policy summary with inline bundle previews.</summary>
        [JsonPropertyName("rewardPolicy")] public RewardPolicySummary? RewardPolicy { get; set; }

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
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonPropertyName("name")] public System.Text.Json.JsonElement Name { get; set; }
        [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>LocalizedString.</summary>
        [JsonPropertyName("description")] public System.Text.Json.JsonElement Description { get; set; }
        [JsonPropertyName("itemTypeKey")] public string ItemTypeKey { get; set; } = string.Empty;
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
        [JsonPropertyName("rarity")] public string? Rarity { get; set; }
        [JsonPropertyName("attributes")] public Dictionary<string, System.Text.Json.JsonElement> Attributes { get; set; } = new();
    }

    /// <summary>
    /// One currency row as exposed to game clients. <see cref="Kind"/>
    /// distinguishes spendable currencies (<c>currency</c>) from
    /// progression resources (<c>progression</c>).
    /// </summary>
    public sealed class CatalogCurrency
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonPropertyName("name")] public System.Text.Json.JsonElement Name { get; set; }
        [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>LocalizedString.</summary>
        [JsonPropertyName("description")] public System.Text.Json.JsonElement Description { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; } = "currency";
    }

    public sealed class Catalog
    {
        [JsonPropertyName("items")] public List<CatalogItem> Items { get; set; } = new();
        [JsonPropertyName("currencies")] public List<CatalogCurrency> Currencies { get; set; } = new();
    }

    public sealed class LeaderboardEntry
    {
        [JsonPropertyName("participantId")] public string ParticipantId { get; set; } = string.Empty;
        /// <summary>One of <c>player</c>, <c>bot</c>.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; set; }
        [JsonPropertyName("score")] public double Score { get; set; }
        [JsonPropertyName("rank")] public int Rank { get; set; }
    }

    public sealed class LeaderboardSelf
    {
        [JsonPropertyName("rank")] public int Rank { get; set; }
        [JsonPropertyName("score")] public double Score { get; set; }
    }

    public sealed class Leaderboard
    {
        [JsonPropertyName("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonPropertyName("mode")] public string Mode { get; set; } = string.Empty;
        [JsonPropertyName("finalized")] public bool Finalized { get; set; }
        [JsonPropertyName("entries")] public List<LeaderboardEntry> Entries { get; set; } = new();
        [JsonPropertyName("self")] public LeaderboardSelf? Self { get; set; }
    }

    public sealed class Grant
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        /// <summary>One of <c>reward</c>, <c>crate</c>.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("contents")] public Dictionary<string, System.Text.Json.JsonElement> Contents { get; set; } = new();
        [JsonPropertyName("sourceKind")] public string SourceKind { get; set; } = string.Empty;
        [JsonPropertyName("sourceRefId")] public string? SourceRefId { get; set; }
        [JsonPropertyName("parentGrantId")] public string? ParentGrantId { get; set; }
        /// <summary>One of <c>pending</c>, <c>claimed</c>, <c>expired</c>.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("rolledAt")] public string? RolledAt { get; set; }
        [JsonPropertyName("claimedAt")] public string? ClaimedAt { get; set; }
        [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    }

    public sealed class OpenCrateResponse
    {
        [JsonPropertyName("crate")] public Grant Crate { get; set; } = new();
        [JsonPropertyName("contents")] public Grant Contents { get; set; } = new();
    }

    public sealed class Lobby
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonPropertyName("eventWindowId")] public string EventWindowId { get; set; } = string.Empty;
        [JsonPropertyName("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonPropertyName("mode")] public string Mode { get; set; } = string.Empty;
        /// <summary>One of <c>forming</c>, <c>active</c>, <c>closed</c>.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("capacity")] public int Capacity { get; set; }
        [JsonPropertyName("fillBy")] public string? FillBy { get; set; }
        [JsonPropertyName("participantCount")] public int ParticipantCount { get; set; }

        /// <summary>
        /// Projected bot count at read time — derived server-side from
        /// the lobby's age and the matchmaking drip interval. Grows
        /// monotonically while the lobby is <c>forming</c>. UI typically
        /// renders <c>ParticipantCount + BotSlots</c> filled cells out
        /// of <c>Capacity</c> for a smooth fill animation.
        /// </summary>
        [JsonPropertyName("botSlots")] public int BotSlots { get; set; }

        [JsonPropertyName("startedAt")] public string? StartedAt { get; set; }
        [JsonPropertyName("endsAt")] public string? EndsAt { get; set; }

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
        [JsonPropertyName("mode")] public string Mode { get; set; } = "set";
        [JsonPropertyName("metricValue")] public double? MetricValue { get; set; }
        [JsonPropertyName("metrics")] public Dictionary<string, double>? Metrics { get; set; }
        [JsonPropertyName("occurredAt")] public string? OccurredAt { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="LeaderboardsClient.ReadAsync"/>.
    /// </summary>
    public sealed class LeaderboardReadOptions
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
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonPropertyName("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonPropertyName("secret")] public string Secret { get; set; } = string.Empty;
        [JsonPropertyName("secretPrefix")] public string? SecretPrefix { get; set; }
        [JsonPropertyName("registeredAt")] public string? RegisteredAt { get; set; }
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
        [JsonPropertyName("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("metadata")] public Dictionary<string, System.Text.Json.JsonElement> Metadata { get; set; } = new();
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// One row in the player's wallet. Wallets are kind-agnostic: a
    /// <c>gold</c> currency entry sits beside a <c>trophies</c>
    /// progression entry with the same shape. The owning currency's
    /// <c>kind</c> lives on the catalog row, not here.
    /// </summary>
    public sealed class PlayerWalletHolding
    {
        [JsonPropertyName("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonPropertyName("balance")] public int Balance { get; set; }
        [JsonPropertyName("metadata")] public Dictionary<string, System.Text.Json.JsonElement> Metadata { get; set; } = new();
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
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
        [JsonPropertyName("quantity")] public int Quantity { get; set; }

        /// <summary>
        /// Free-form tag persisted on the ledger row only. Surfaces in
        /// the admin audit screen.
        /// </summary>
        [JsonPropertyName("reason")] public string? Reason { get; set; }

        /// <summary>Optional override — leave null to let the client auto-stamp one.</summary>
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Result of <see cref="InventoryClient.ConsumeAsync"/>.
    /// <see cref="Applied"/> distinguishes a fresh mutation from an
    /// idempotent replay (the server returns the prior state when the
    /// same key arrives twice).
    /// </summary>
    public sealed class ConsumeItemResult
    {
        [JsonPropertyName("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("applied")] public bool Applied { get; set; }
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
        [JsonPropertyName("amount")] public int Amount { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public sealed class DebitWalletResult
    {
        [JsonPropertyName("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonPropertyName("balance")] public int Balance { get; set; }
        [JsonPropertyName("applied")] public bool Applied { get; set; }
    }

    /// <summary>
    /// Inventory list response wrapper — the backend nests the list
    /// inside <c>{ "data": { "items": [...] } }</c>.
    /// </summary>
    internal sealed class InventoryListEnvelope
    {
        [JsonPropertyName("items")] public List<PlayerItemHolding>? Items { get; set; }
    }

    /// <summary>
    /// Wallet list response wrapper — the backend nests the list
    /// inside <c>{ "data": { "wallet": [...] } }</c>.
    /// </summary>
    internal sealed class WalletListEnvelope
    {
        [JsonPropertyName("wallet")] public List<PlayerWalletHolding>? Wallet { get; set; }
    }

    /// <summary>
    /// Generic envelope: every successful SDK response is shaped as
    /// <c>{ "data": T }</c>. The client unwraps automatically.
    /// </summary>
    internal sealed class DataEnvelope<T>
    {
        [JsonPropertyName("data")] public T? Data { get; set; }
    }
}
