using System;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kraty.Tests
{
    /// <summary>
    /// Covers the per-player auth + new resource clients added for
    /// parity with the Flutter SDK: X-Player-Secret header injection,
    /// PlayersClient.RegisterAsync, ConnectAsPlayerAsync (happy path
    /// + already-registered rotation), Inventory + Wallet + CollectAll.
    /// </summary>
    public sealed class PlayerAuthAndResourcesTests
    {
        private const string ApiKey = "pUUVdrM8.djr4-0Iv9h1JvVNSMZNDmSsSN7lSVq2F9dG6DG4A5uQ";
        private const string BaseUrl = "https://api.test.kraty.io";

        private static KratyClientOptions BaseOpts(FakeHandler handler, string? playerSecret = null)
        {
            return new KratyClientOptions
            {
                ApiKey = ApiKey,
                BaseUrl = BaseUrl,
                HttpMessageHandler = handler,
                PlayerSecret = playerSecret,
                Retry = new RetryConfig
                {
                    Attempts = 2,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                    MaxDelay = TimeSpan.FromMilliseconds(5),
                    Jitter = 0,
                },
                Timeout = TimeSpan.FromSeconds(1),
            };
        }

        // ── X-Player-Secret header injection ─────────────────────────

        [Fact]
        public async Task AttachesPlayerSecretHeaderWhenConfigured()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":[]}");
            using var client = new KratyClient(BaseOpts(handler, playerSecret: "abc123"));
            await client.RequestAsync<DataEnvelope<List<Grant>>>(
                HttpMethod.Get, "/sdk/v1/players/alice/pending-grants");
            Assert.True(handler.Calls[0].Headers.ContainsKey("x-player-secret"));
            Assert.Equal("abc123", handler.Calls[0].Headers["x-player-secret"]);
        }

        [Fact]
        public async Task DoesNotAttachPlayerSecretHeaderWhenNotConfigured()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":[]}");
            using var client = new KratyClient(BaseOpts(handler));
            await client.RequestAsync<DataEnvelope<List<Grant>>>(
                HttpMethod.Get, "/sdk/v1/players/alice/pending-grants");
            Assert.False(handler.Calls[0].Headers.ContainsKey("x-player-secret"));
        }

        [Fact]
        public void WithPlayerSecretReturnsCopyWithoutMutatingOriginal()
        {
            var orig = new KratyClientOptions { ApiKey = ApiKey, PlayerSecret = "first" };
            var copy = orig.WithPlayerSecret("second");
            Assert.Equal("first", orig.PlayerSecret);
            Assert.Equal("second", copy.PlayerSecret);
            Assert.Equal(orig.ApiKey, copy.ApiKey);
            // Mutating one should not bleed into the other.
            copy.PlayerSecret = "third";
            Assert.Equal("first", orig.PlayerSecret);
        }

        // ── PlayersClient ────────────────────────────────────────────

        [Fact]
        public async Task RegisterReturnsTheMintedSecret()
        {
            var handler = new FakeHandler().Push(201,
                "{\"data\":{\"playerId\":\"p1\",\"externalPlayerId\":\"alice\",\"secret\":\"s3cret\",\"secretPrefix\":\"abcd\",\"registeredAt\":\"2026-01-01T00:00:00Z\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var reg = await kraty.Players.RegisterAsync("alice");
            Assert.Equal("s3cret", reg.Secret);
            Assert.Equal("alice", reg.ExternalPlayerId);
            Assert.Contains("/sdk/v1/players/alice/register", handler.Calls[0].Url);
            Assert.DoesNotContain("force=true", handler.Calls[0].Url);
        }

        [Fact]
        public async Task RegisterWithForceAppendsQueryParam()
        {
            var handler = new FakeHandler().Push(201,
                "{\"data\":{\"playerId\":\"p1\",\"externalPlayerId\":\"alice\",\"secret\":\"new-secret\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            await kraty.Players.RegisterAsync("alice", force: true);
            Assert.Contains("?force=true", handler.Calls[0].Url);
        }

        [Fact]
        public async Task RegisterSurfacesAlreadyRegisteredAsTypedError()
        {
            var handler = new FakeHandler().Push(409,
                "{\"error\":{\"code\":\"player_already_registered\",\"message\":\"player alice already has a secret\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await kraty.Players.RegisterAsync("alice"));
            Assert.True(err.IsPlayerAlreadyRegistered);
        }

        // ── ConnectAsPlayerAsync ─────────────────────────────────────

        [Fact]
        public async Task ConnectAsPlayerSkipsRegisterWhenSecretAlreadyStored()
        {
            var handler = new FakeHandler();
            var store = new InMemorySecretStore();
            await store.WriteAsync("alice", "cached-secret");
            using var kraty = await Kraty.ConnectAsPlayerAsync(BaseOpts(handler), "alice", store);
            Assert.Empty(handler.Calls); // no HTTP — used the cached secret
            // Confirm the secret was wired through.
            handler.Push(200, "{\"data\":[]}");
            await kraty.Grants.ListPendingAsync(@as: "alice");
            Assert.Equal("cached-secret", handler.Calls[0].Headers["x-player-secret"]);
        }

        [Fact]
        public async Task ConnectAsPlayerRegistersAndPersistsOnEmptyStore()
        {
            var handler = new FakeHandler().Push(201,
                "{\"data\":{\"playerId\":\"p1\",\"externalPlayerId\":\"bob\",\"secret\":\"fresh-secret\"}}");
            var store = new InMemorySecretStore();
            using var kraty = await Kraty.ConnectAsPlayerAsync(BaseOpts(handler), "bob", store);
            Assert.Single(handler.Calls);
            Assert.Contains("/sdk/v1/players/bob/register", handler.Calls[0].Url);
            // Store now has it for next launch.
            Assert.Equal("fresh-secret", await store.ReadAsync("bob"));
        }

        [Fact]
        public async Task ConnectAsPlayerAutoRotatesOnAlreadyRegisteredByDefault()
        {
            var handler = new FakeHandler()
                .Push(409, "{\"error\":{\"code\":\"player_already_registered\",\"message\":\"already there\"}}")
                .Push(201, "{\"data\":{\"playerId\":\"p1\",\"externalPlayerId\":\"carol\",\"secret\":\"rotated-secret\"}}");
            var store = new InMemorySecretStore();
            using var kraty = await Kraty.ConnectAsPlayerAsync(BaseOpts(handler), "carol", store);
            Assert.Equal(2, handler.Calls.Count);
            Assert.DoesNotContain("force=true", handler.Calls[0].Url);
            Assert.Contains("force=true", handler.Calls[1].Url);
            Assert.Equal("rotated-secret", await store.ReadAsync("carol"));
        }

        [Fact]
        public async Task ConnectAsPlayerSurfacesConflictWhenAutoRotateDisabled()
        {
            var handler = new FakeHandler()
                .Push(409, "{\"error\":{\"code\":\"player_already_registered\",\"message\":\"already there\"}}");
            var store = new InMemorySecretStore();
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await Kraty.ConnectAsPlayerAsync(BaseOpts(handler), "dave", store, autoRotateOnConflict: false));
            Assert.True(err.IsPlayerAlreadyRegistered);
            Assert.Null(await store.ReadAsync("dave"));
        }

        // ── Stored-identity helpers (Kraty.{Read,Restore,Clear}StoredIdentityAsync)

        [Fact]
        public async Task ReadStoredIdentityReturnsNullWhenNoActiveMarker()
        {
            var store = new InMemorySecretStore();
            // Secret is present but no active marker — the helper must
            // still return null so the game shows onboarding instead of
            // a half-broken auto-resume.
            await store.WriteAsync("alice", "secret-1");
            Assert.Null(await Kraty.ReadStoredIdentityAsync(store));
        }

        [Fact]
        public async Task RestoreStoredIdentityRoundtripsThroughRead()
        {
            var store = new InMemorySecretStore();
            await Kraty.RestoreStoredIdentityAsync(store, new StoredIdentity("alice", "secret-abc"));
            var stored = await Kraty.ReadStoredIdentityAsync(store);
            Assert.NotNull(stored);
            Assert.Equal("alice", stored!.ExternalPlayerId);
            Assert.Equal("secret-abc", stored.Secret);
        }

        [Fact]
        public async Task ClearStoredIdentityForgetsMarkerButKeepsSecret()
        {
            var store = new InMemorySecretStore();
            await Kraty.RestoreStoredIdentityAsync(store, new StoredIdentity("alice", "sec"));
            Assert.NotNull(await Kraty.ReadStoredIdentityAsync(store));
            await Kraty.ClearStoredIdentityAsync(store);
            Assert.Null(await Kraty.ReadStoredIdentityAsync(store));
            // Secret survives — if the user signs back in we don't
            // need to re-register.
            Assert.Equal("sec", await store.ReadAsync("alice"));
        }

        [Fact]
        public async Task ConnectAsPlayerWritesActiveMarker()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":{\"playerId\":\"plr-1\",\"externalPlayerId\":\"eve\",\"secret\":\"fresh-secret\",\"createdAt\":\"2026-01-01\"}}");
            var store = new InMemorySecretStore();
            using var kraty = await Kraty.ConnectAsPlayerAsync(BaseOpts(handler), "eve", store);
            // Both the per-player secret and the active marker should
            // be set so the next boot can auto-resume without an id.
            Assert.Equal("fresh-secret", await store.ReadAsync("eve"));
            var resumed = await Kraty.ReadStoredIdentityAsync(store);
            Assert.NotNull(resumed);
            Assert.Equal("eve", resumed!.ExternalPlayerId);
        }

        // ── InventoryClient ──────────────────────────────────────────

        [Fact]
        public async Task InventoryListUnwrapsItemsArray()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"items\":[" +
                "{\"itemKey\":\"potion_hp\",\"quantity\":3,\"metadata\":{},\"createdAt\":\"2026-01-01\",\"updatedAt\":\"2026-01-01\"}," +
                "{\"itemKey\":\"sword_iron\",\"quantity\":1,\"metadata\":{},\"createdAt\":\"2026-01-01\",\"updatedAt\":\"2026-01-01\"}" +
                "]}}");
            using var kraty = new Kraty(BaseOpts(handler, "ps"));
            var items = await kraty.Inventory.ListAsync(@as: "alice");
            Assert.Equal(2, items.Count);
            Assert.Equal("potion_hp", items[0].ItemKey);
            Assert.Equal(3, items[0].Quantity);
        }

        [Fact]
        public async Task InventoryConsumeAutoStampsIdempotencyKey()
        {
            var counter = 0;
            string Gen() => $"idem-{++counter}";
            var opts = BaseOpts(handler: new FakeHandler().Push(200,
                "{\"data\":{\"itemKey\":\"potion_hp\",\"quantity\":2,\"applied\":true}}"));
            opts.GenerateIdempotencyKey = Gen;
            opts.PlayerSecret = "ps";
            using var kraty = new Kraty(opts);
            var handler = (FakeHandler)opts.HttpMessageHandler!;
            var res = await kraty.Inventory.ConsumeAsync("potion_hp", new ConsumeItemInput { Quantity = 1 }, @as: "alice");
            Assert.True(res.Applied);
            Assert.Equal(2, res.Quantity);
            var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(handler.Calls[0].Body!)!;
            Assert.Equal(1, (int)body["quantity"]);
            Assert.Equal("idem-1", (string?)body["idempotencyKey"]);
        }

        // ── WalletClient ─────────────────────────────────────────────

        [Fact]
        public async Task WalletListUnwrapsWalletArray()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"wallet\":[" +
                "{\"economyKey\":\"gold\",\"balance\":150,\"metadata\":{},\"createdAt\":\"2026-01-01\",\"updatedAt\":\"2026-01-01\"}" +
                "]}}");
            using var kraty = new Kraty(BaseOpts(handler, "ps"));
            var wallet = await kraty.Wallet.ListAsync(@as: "alice");
            Assert.Single(wallet);
            Assert.Equal("gold", wallet[0].EconomyKey);
            Assert.Equal(150, wallet[0].Balance);
        }

        [Fact]
        public async Task WalletDebitPassesAmountAndAutoStampsKey()
        {
            var counter = 0;
            string Gen() => $"idem-{++counter}";
            var opts = BaseOpts(handler: new FakeHandler().Push(200,
                "{\"data\":{\"economyKey\":\"gold\",\"balance\":50,\"applied\":true}}"));
            opts.GenerateIdempotencyKey = Gen;
            opts.PlayerSecret = "ps";
            using var kraty = new Kraty(opts);
            var handler = (FakeHandler)opts.HttpMessageHandler!;
            var res = await kraty.Wallet.DebitAsync("gold", new DebitWalletInput { Amount = 100 }, @as: "alice");
            Assert.Equal(50, res.Balance);
            var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(handler.Calls[0].Body!)!;
            Assert.Equal(100, (int)body["amount"]);
            Assert.Equal("idem-1", (string?)body["idempotencyKey"]);
        }

        // ── GrantsClient.CollectAllAsync ─────────────────────────────

        [Fact]
        public async Task CollectAllOpensCratesAndClaimsRewards()
        {
            var handler = new FakeHandler()
                // listPending
                .Push(200, "{\"data\":[" +
                    "{\"id\":\"g1\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"pending\",\"createdAt\":\"2026-01-01\"}," +
                    "{\"id\":\"g2\",\"kind\":\"crate\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"pending\",\"createdAt\":\"2026-01-01\"}," +
                    "{\"id\":\"g3\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"pending\",\"createdAt\":\"2026-01-01\"}" +
                    "]}")
                // open g2 (crates first per pending order)
                .Push(200, "{\"data\":{\"crate\":{\"id\":\"g2\",\"kind\":\"crate\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"claimed\",\"createdAt\":\"2026-01-01\"},\"contents\":{\"id\":\"g2c\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"crate\",\"status\":\"pending\",\"createdAt\":\"2026-01-01\"}}}")
                // claim g1
                .Push(200, "{\"data\":{\"id\":\"g1\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"claimed\",\"createdAt\":\"2026-01-01\"}}")
                // claim g3
                .Push(200, "{\"data\":{\"id\":\"g3\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"claimed\",\"createdAt\":\"2026-01-01\"}}");
            using var kraty = new Kraty(BaseOpts(handler, "ps"));
            var result = await kraty.Grants.CollectAllAsync(@as: "alice");
            Assert.Equal(3, result.Processed);
            Assert.Single(result.Opened);
            Assert.Equal(2, result.Claimed.Count);
            Assert.False(result.HasFailures);
        }

        [Fact]
        public async Task CollectAllCapturesPerGrantFailuresWithoutAborting()
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":[" +
                    "{\"id\":\"g1\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"pending\",\"createdAt\":\"2026-01-01\"}," +
                    "{\"id\":\"g2\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"pending\",\"createdAt\":\"2026-01-01\"}" +
                    "]}")
                .Push(404, "{\"error\":{\"code\":\"not_found\",\"message\":\"g1 expired\"}}")
                .Push(200, "{\"data\":{\"id\":\"g2\",\"kind\":\"reward\",\"contents\":{},\"sourceKind\":\"event\",\"status\":\"claimed\",\"createdAt\":\"2026-01-01\"}}");
            using var kraty = new Kraty(BaseOpts(handler, "ps"));
            var result = await kraty.Grants.CollectAllAsync(@as: "alice");
            Assert.Equal(2, result.Processed);
            Assert.Single(result.Claimed);
            Assert.Single(result.Failures);
            Assert.Equal("g1", result.Failures[0].Grant.Id);
            Assert.IsType<KratyApiError>(result.Failures[0].Error);
        }

        // ── Typed error helpers ──────────────────────────────────────

        [Fact]
        public async Task TypedErrorHelpersFireForKnownCodes()
        {
            var handler = new FakeHandler().Push(402,
                "{\"error\":{\"code\":\"insufficient_entry_cost\",\"message\":\"need 50 gold\",\"details\":{\"resource\":\"gold\"}}}");
            using var kraty = new Kraty(BaseOpts(handler, "ps"));
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await kraty.Events.StartAsync("race", @as: "alice"));
            Assert.True(err.IsInsufficientEntryCost);
            Assert.False(err.IsLobbyForming);
            Assert.False(err.IsPlayerSecretInvalid);
            Assert.False(err.IsPlayerAlreadyRegistered);
            Assert.False(err.IsEntryRequirementFailed);
        }

        // ── EventListing + EntryCost wire shape ──────────────────────

        [Fact]
        public async Task EventListingDecodesEntryCostAndModeFields()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":[{" +
                "\"eventKey\":\"bounty_hunt\"," +
                "\"name\":\"Bounty Hunt\"," +
                "\"windowId\":\"w1\",\"startsAt\":\"2026-01-01\",\"endsAt\":\"2026-01-02\"," +
                "\"leaderboardId\":\"lb1\",\"currentAttemptId\":null," +
                "\"type\":\"single_metric\",\"leaderboardMode\":\"lobby_matched\"," +
                "\"metrics\":[{\"key\":\"score\",\"target\":1000}]," +
                "\"entryRequirement\":null," +
                "\"entryCost\":{\"currencies\":[{\"key\":\"gold\",\"amount\":50}],\"items\":[]}" +
                "}]}");
            using var kraty = new Kraty(BaseOpts(handler));
            var events = await kraty.Events.ListForPlayerAsync(@as: "alice");
            Assert.Single(events);
            Assert.Equal("bounty_hunt", events[0].EventKey);
            Assert.True(events[0].IsLobbyMatched);
            Assert.NotNull(events[0].EntryCost);
            Assert.False(events[0].EntryCost!.IsEmpty);
            Assert.Single(events[0].EntryCost!.Currencies);
            Assert.Equal("gold", events[0].EntryCost!.Currencies[0].Key);
            Assert.Equal(50, events[0].EntryCost!.Currencies[0].Amount);
        }

        [Fact]
        public async Task LobbyDecodesBotSlotsAndComputesFilledSlots()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"id\":\"lob1\",\"eventId\":\"e1\",\"eventWindowId\":\"w1\",\"leaderboardId\":\"lb1\"," +
                "\"mode\":\"lobby_matched\",\"status\":\"forming\",\"capacity\":4," +
                "\"fillBy\":\"2026-01-01T00:00:30Z\",\"participantCount\":1,\"botSlots\":2," +
                "\"startedAt\":null,\"endsAt\":null}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var lobby = await kraty.Lobbies.ReadAsync("lob1");
            Assert.Equal(1, lobby.ParticipantCount);
            Assert.Equal(2, lobby.BotSlots);
            Assert.Equal(3, lobby.FilledSlots);
        }

        [Fact]
        public async Task LobbyFilledSlotsCapsAtCapacity()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"id\":\"lob1\",\"eventId\":\"e1\",\"eventWindowId\":\"w1\",\"leaderboardId\":\"lb1\"," +
                "\"mode\":\"lobby_matched\",\"status\":\"active\",\"capacity\":4," +
                "\"participantCount\":3,\"botSlots\":5}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var lobby = await kraty.Lobbies.ReadAsync("lob1");
            Assert.Equal(4, lobby.FilledSlots);
        }

        // ── InMemorySecretStore ──────────────────────────────────────

        [Fact]
        public async Task InMemorySecretStoreReadsWritesAndRemoves()
        {
            var store = new InMemorySecretStore();
            Assert.Null(await store.ReadAsync("alice"));
            await store.WriteAsync("alice", "s1");
            Assert.Equal("s1", await store.ReadAsync("alice"));
            await store.WriteAsync("alice", "s2");
            Assert.Equal("s2", await store.ReadAsync("alice"));
            await store.RemoveAsync("alice");
            Assert.Null(await store.ReadAsync("alice"));
        }

        // ── Lazy-identity ergonomics ─────────────────────────────────

        [Fact]
        public async Task GrantsListPendingUsesActivePlayerWhenCalledBare()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":[]}");
            var opts = BaseOpts(handler, playerSecret: "ps");
            opts.ActiveExternalPlayerId = "alice";
            using var kraty = new Kraty(opts);
            await kraty.Grants.ListPendingAsync();
            Assert.Contains("/players/alice/pending-grants", handler.Calls[0].Url);
        }

        [Fact]
        public async Task AutoRegistersAFreshPlayerOnFirstBareCall()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":{\"secret\":\"auto-secret\"}}")
                .Push(200, "{\"data\":[]}");
            using var kraty = new Kraty(BaseOpts(handler));
            await kraty.Grants.ListPendingAsync();
            Assert.Matches(@"/sdk/v1/players/kp_[A-Za-z0-9_-]+/register$", handler.Calls[0].Url);
            Assert.Matches(@"/sdk/v1/players/kp_[A-Za-z0-9_-]+/pending-grants$", handler.Calls[1].Url);
            Assert.NotNull(kraty.ActiveExternalPlayerId);
            Assert.StartsWith("kp_", kraty.ActiveExternalPlayerId);
            // Second bare call reuses the freshly minted id — no extra
            // register fires.
            handler.Push(200, "{\"data\":[]}");
            await kraty.Grants.ListPendingAsync();
            Assert.Equal(3, handler.Calls.Count);
            Assert.Contains("/pending-grants", handler.Calls[2].Url);
            // X-Player-Secret rides along on the second call.
            Assert.Equal("auto-secret", handler.Calls[2].Headers["x-player-secret"]);
        }

        [Fact]
        public async Task AsOverrideOverridesActivePlayerId()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":[]}");
            var opts = BaseOpts(handler, playerSecret: "ps");
            opts.ActiveExternalPlayerId = "alice";
            using var kraty = new Kraty(opts);
            await kraty.Grants.ListPendingAsync(@as: "bob");
            Assert.Contains("/players/bob/pending-grants", handler.Calls[0].Url);
        }

        [Fact]
        public async Task EnsureIdentityAsyncSurfacesTheResolvedIdentity()
        {
            var handler = new FakeHandler().Push(201, "{\"data\":{\"secret\":\"auto-secret\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var identity = await kraty.EnsureIdentityAsync();
            Assert.StartsWith("kp_", identity.ExternalPlayerId);
            Assert.Equal("auto-secret", identity.Secret);
            Assert.Equal(identity.ExternalPlayerId, kraty.ActiveExternalPlayerId);
        }

        [Fact]
        public async Task EnsureIdentityAsyncDedupesConcurrentFirstTouch()
        {
            // Two simultaneous calls share one inflight register — no
            // double-POST against /register.
            var handler = new FakeHandler().Push(201, "{\"data\":{\"secret\":\"auto-secret\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var t1 = kraty.EnsureIdentityAsync();
            var t2 = kraty.EnsureIdentityAsync();
            var r1 = await t1;
            var r2 = await t2;
            Assert.Single(handler.Calls);
            Assert.Equal(r1.ExternalPlayerId, r2.ExternalPlayerId);
        }

        [Fact]
        public async Task SignInAsyncInstallsAndPersistsExplicitIdentity()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":[]}");
            var store = new InMemorySecretStore();
            var opts = BaseOpts(handler);
            opts.SecretStore = store;
            using var kraty = new Kraty(opts);
            await kraty.SignInAsync("eve", "fixed-secret");
            Assert.Equal("eve", kraty.ActiveExternalPlayerId);
            Assert.Equal("fixed-secret", await store.ReadAsync("eve"));
            Assert.Equal("eve", await store.ReadActiveExternalPlayerIdAsync());
            // Subsequent player-scoped calls use the installed identity
            // without another register round-trip.
            await kraty.Grants.ListPendingAsync();
            Assert.Contains("/players/eve/pending-grants", handler.Calls[0].Url);
            Assert.Equal("fixed-secret", handler.Calls[0].Headers["x-player-secret"]);
        }

        [Fact]
        public async Task LogoutAsyncWipesActiveIdAndSecret()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":{\"secret\":\"auto-secret\"}}")
                .Push(200, "{\"data\":[]}")
                .Push(201, "{\"data\":{\"secret\":\"second-secret\"}}")
                .Push(200, "{\"data\":[]}");
            var store = new InMemorySecretStore();
            var opts = BaseOpts(handler);
            opts.SecretStore = store;
            using var kraty = new Kraty(opts);
            await kraty.Grants.ListPendingAsync();
            var firstId = kraty.ActiveExternalPlayerId;
            Assert.NotNull(firstId);

            await kraty.LogoutAsync();
            Assert.Null(kraty.ActiveExternalPlayerId);
            Assert.Null(await store.ReadAsync(firstId!));
            Assert.Null(await store.ReadActiveExternalPlayerIdAsync());

            // Next bare call lazily registers a NEW player.
            await kraty.Grants.ListPendingAsync();
            Assert.Equal(4, handler.Calls.Count);
            Assert.NotNull(kraty.ActiveExternalPlayerId);
            Assert.NotEqual(firstId, kraty.ActiveExternalPlayerId);
        }

        [Fact]
        public async Task EnsureIdentityAsyncResumesFromPersistedSecretStore()
        {
            var handler = new FakeHandler();
            var store = new InMemorySecretStore();
            await store.WriteAsync("alice", "cached-secret");
            await store.WriteActiveExternalPlayerIdAsync("alice");
            var opts = BaseOpts(handler);
            opts.SecretStore = store;
            using var kraty = new Kraty(opts);
            var identity = await kraty.EnsureIdentityAsync();
            Assert.Equal("alice", identity.ExternalPlayerId);
            Assert.Equal("cached-secret", identity.Secret);
            // No HTTP fired — the store already had everything.
            Assert.Empty(handler.Calls);
        }
    }
}
