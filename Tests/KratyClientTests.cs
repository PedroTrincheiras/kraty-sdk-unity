using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Kraty.Tests
{
    /// <summary>
    /// Mirror of the TypeScript SDK's <c>client.test.ts</c>: same 17
    /// scenarios, ported to xUnit + <see cref="FakeHandler"/>. Lock-in
    /// for the patterns that matter on the wire: bearer auth,
    /// idempotency-key auto-stamp + preservation, retry behaviors,
    /// lobby-forming envelope handling, and resource client URL
    /// construction.
    /// </summary>
    public sealed class KratyClientTests
    {
        private const string ApiKey = "pUUVdrM8.djr4-0Iv9h1JvVNSMZNDmSsSN7lSVq2F9dG6DG4A5uQ";
        private const string BaseUrl = "https://api.test.kraty.io";

        private static KratyClientOptions BaseOpts(FakeHandler handler, Func<string>? keyGen = null)
        {
            return new KratyClientOptions
            {
                ApiKey = ApiKey,
                BaseUrl = BaseUrl,
                HttpMessageHandler = handler,
                GenerateIdempotencyKey = keyGen,
                Retry = new RetryConfig
                {
                    Attempts = 3,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                    MaxDelay = TimeSpan.FromMilliseconds(5),
                    Jitter = 0,
                },
                Timeout = TimeSpan.FromSeconds(1),
            };
        }

        // ── request layer ────────────────────────────────────────────

        [Fact]
        public async Task SendsBearerAuthorization()
        {
            var handler = new FakeHandler().Push(200, "{\"ok\":true}");
            using var client = new KratyClient(BaseOpts(handler));
            await client.RequestAsync<Dictionary<string, object?>>(HttpMethod.Get, "/sdk/v1/ping");

            Assert.True(handler.Calls[0].Headers.ContainsKey("authorization"));
            Assert.Equal($"Bearer {ApiKey}", handler.Calls[0].Headers["authorization"]);
        }

        [Fact]
        public async Task StampsIdempotencyKeyOnPost()
        {
            var counter = 0;
            string Gen() => $"idem-{++counter}";
            var handler = new FakeHandler().Push(201, "{\"data\":{\"id\":\"x\"}}");
            using var client = new KratyClient(BaseOpts(handler, Gen));
            await client.RequestAsync<DataEnvelope<Dictionary<string, object?>>>(
                HttpMethod.Post, "/sdk/v1/foo", new Dictionary<string, object?> { ["x"] = 1 });

            var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(handler.Calls[0].Body!)!;
            Assert.Equal("idem-1", (string?)body["idempotencyKey"]);
            Assert.Equal(1, (int)body["x"]);
        }

        [Fact]
        public async Task DoesNotStampIdempotencyKeyOnGet()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":[]}");
            using var client = new KratyClient(BaseOpts(handler, () => "idem-WOULD-NOT-FIRE"));
            await client.RequestAsync<DataEnvelope<List<object>>>(HttpMethod.Get, "/sdk/v1/foo");
            Assert.Null(handler.Calls[0].Body);
        }

        [Fact]
        public async Task PreservesCallerSuppliedIdempotencyKey()
        {
            var counter = 0;
            string Gen() => $"idem-{++counter}";
            var handler = new FakeHandler().Push(201, "{\"data\":{}}");
            using var client = new KratyClient(BaseOpts(handler, Gen));
            await client.RequestAsync<DataEnvelope<Dictionary<string, object?>>>(
                HttpMethod.Post, "/sdk/v1/foo",
                new Dictionary<string, object?> { ["idempotencyKey"] = "caller-chose-me" }
            );
            var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(handler.Calls[0].Body!)!;
            Assert.Equal("caller-chose-me", (string?)body["idempotencyKey"]);
            Assert.Equal(0, counter); // auto-gen never fired
        }

        [Fact]
        public async Task ThrowsKratyApiErrorForNon2xxWithEnvelope()
        {
            var handler = new FakeHandler().Push(404,
                "{\"error\":{\"code\":\"not_found\",\"message\":\"leaderboard not found\"}}");
            using var client = new KratyClient(BaseOpts(handler));
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await client.RequestAsync<DataEnvelope<object>>(HttpMethod.Get, "/sdk/v1/event-leaderboards/missing"));
            Assert.Equal(404, err.Status);
            Assert.Equal("not_found", err.Code);
        }

        [Fact]
        public async Task RetriesOn503AndSucceedsOnSecondAttempt()
        {
            var handler = new FakeHandler()
                .Push(503)
                .Push(200, "{\"data\":{\"ok\":true}}");
            using var client = new KratyClient(BaseOpts(handler));
            var res = await client.RequestAsync<DataEnvelope<Dictionary<string, JToken>>>(
                HttpMethod.Get, "/sdk/v1/ping");
            Assert.NotNull(res.Data);
            Assert.True((bool)res.Data!["ok"]);
            Assert.Equal(2, handler.Calls.Count);
        }

        [Fact]
        public async Task PreservesSameIdempotencyKeyAcrossRetries()
        {
            var counter = 0;
            string Gen() => $"idem-{++counter}";
            var handler = new FakeHandler()
                .Push(503)
                .Push(503)
                .Push(201, "{\"data\":{}}");
            using var client = new KratyClient(BaseOpts(handler, Gen));
            await client.RequestAsync<DataEnvelope<object>>(
                HttpMethod.Post, "/sdk/v1/foo", new Dictionary<string, object?> { ["x"] = 1 });
            Assert.Equal(3, handler.Calls.Count);
            foreach (var call in handler.Calls)
            {
                var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(call.Body!)!;
                Assert.Equal("idem-1", (string?)body["idempotencyKey"]);
            }
            Assert.Equal(1, counter); // auto-gen fired once across all retries
        }

        [Fact]
        public async Task GivesUpAfterRetryAttemptsAndThrowsLastError()
        {
            var handler = new FakeHandler()
                .Push(503)
                .Push(503)
                .Push(503, "{\"error\":{\"code\":\"internal_error\",\"message\":\"still broken\"}}");
            using var client = new KratyClient(BaseOpts(handler));
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await client.RequestAsync<DataEnvelope<object>>(HttpMethod.Get, "/sdk/v1/ping"));
            Assert.Equal(503, err.Status);
            Assert.Equal(3, handler.Calls.Count);
        }

        [Fact]
        public async Task HonorsRetryAfterOn429()
        {
            var handler = new FakeHandler()
                .Push(429, headers: new Dictionary<string, string> { ["Retry-After"] = "0" })
                .Push(200, "{\"data\":{\"ok\":true}}");
            using var client = new KratyClient(BaseOpts(handler));
            var sw = Stopwatch.StartNew();
            await client.RequestAsync<DataEnvelope<Dictionary<string, JToken>>>(HttpMethod.Get, "/sdk/v1/ping");
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 500);
            Assert.Equal(2, handler.Calls.Count);
        }

        [Fact]
        public async Task DoesNotRetryOn4xxOtherThan408_425_429()
        {
            var handler = new FakeHandler().Push(404,
                "{\"error\":{\"code\":\"not_found\",\"message\":\"no such grant\"}}");
            using var client = new KratyClient(BaseOpts(handler));
            await Assert.ThrowsAsync<KratyApiError>(async () =>
                await client.RequestAsync<DataEnvelope<object>>(
                    HttpMethod.Post, "/sdk/v1/foo", new Dictionary<string, object?> { ["x"] = 1 }));
            Assert.Equal(1, handler.Calls.Count);
        }

        [Fact]
        public async Task WrapsNetworkCrashAsKratyNetworkErrorAfterExhaustingRetries()
        {
            var handler = new FakeHandler()
                .PushError(new HttpRequestException("connect ECONNREFUSED"))
                .PushError(new HttpRequestException("connect ECONNREFUSED"));
            var opts = BaseOpts(handler);
            opts.Retry = new RetryConfig { Attempts = 2, InitialDelay = TimeSpan.FromMilliseconds(1), MaxDelay = TimeSpan.FromMilliseconds(2), Jitter = 0 };
            using var client = new KratyClient(opts);
            await Assert.ThrowsAsync<KratyNetworkError>(async () =>
                await client.RequestAsync<DataEnvelope<object>>(HttpMethod.Get, "/sdk/v1/ping"));
        }

        [Fact]
        public async Task ExposesOnRequestForTelemetry()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":{}}");
            var events = new List<RequestInfo>();
            var opts = BaseOpts(handler);
            opts.OnRequest = info => events.Add(info);
            using var client = new KratyClient(opts);
            await client.RequestAsync<DataEnvelope<object>>(HttpMethod.Get, "/sdk/v1/ping");
            Assert.Single(events);
            Assert.Equal(200, events[0].Status);
            Assert.True(events[0].Ok);
        }

        // ── facade + resource clients ────────────────────────────────

        [Fact]
        public async Task FacadeWiresResourceClients()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"leaderboardId\":\"lb_1\",\"mode\":\"global\",\"finalized\":false,\"entries\":[],\"self\":null}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var board = await kraty.EventLeaderboards.ReadAsync("lb_1", new EventLeaderboardReadOptions { Limit = 10 });
            Assert.Equal("lb_1", board.LeaderboardId);
            Assert.Contains("/sdk/v1/event-leaderboards/lb_1?limit=10", handler.Calls[0].Url);
        }

        [Fact]
        public async Task LobbyFormingSurfacesAsKratyApiError()
        {
            var handler = new FakeHandler().Push(202,
                "{\"error\":{\"code\":\"lobby_forming\",\"message\":\"lobby '...' is filling (1/3)\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await kraty.Events.StartAsync("race", @as: "alice"));
            Assert.True(err.IsLobbyForming);
            Assert.Equal(202, err.Status);
        }

        [Fact]
        public async Task StartIncludesPlayerContextAndAutoStampsIdempotencyKey()
        {
            var counter = 0;
            string Gen() => $"idem-{++counter}";
            var handler = new FakeHandler().Push(201,
                "{\"data\":{\"attempt\":{\"id\":\"a1\",\"eventId\":\"e1\",\"eventWindowId\":\"w1\",\"leaderboardId\":\"lb1\",\"playerId\":\"p1\",\"startedAt\":\"2026-01-01T00:00:00Z\",\"endsAt\":\"2026-01-01T00:10:00Z\",\"completedAt\":null,\"metrics\":{},\"metricsRaw\":{},\"score\":0,\"status\":\"in_progress\"},\"leaderboardId\":\"lb1\",\"windowEndsAt\":\"2026-01-01T00:10:00Z\"}}");
            using var kraty = new Kraty(BaseOpts(handler, Gen));
            await kraty.Events.StartAsync("race", new Dictionary<string, object?>
            {
                ["country"] = "PT",
                ["level"] = 7,
            }, @as: "alice");
            var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(handler.Calls[0].Body!)!;
            Assert.True(body.ContainsKey("playerContext"));
            Assert.Equal("idem-1", (string?)body["idempotencyKey"]);
        }

        [Fact]
        public async Task ProgressSurfacesMilestonesFiredAlongsideTheAttempt()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{" +
                "\"attempt\":{\"id\":\"a1\",\"eventId\":\"e1\",\"eventWindowId\":\"w1\",\"leaderboardId\":\"lb1\",\"playerId\":\"p1\",\"startedAt\":\"2026-01-01T00:00:00Z\",\"endsAt\":\"2026-01-01T00:10:00Z\",\"completedAt\":null,\"metrics\":{\"kills\":15},\"metricsRaw\":{\"kills\":15},\"score\":15,\"status\":\"in_progress\"}," +
                "\"milestonesFired\":[{\"key\":\"kills_15\",\"grants\":[{\"id\":\"g1\",\"kind\":\"reward\",\"contents\":{\"entries\":[{\"type\":\"currency\",\"currencyKey\":\"gold\",\"amount\":50}]},\"sourceKind\":\"event_milestone\",\"sourceRefId\":\"a1\",\"parentGrantId\":null,\"status\":\"pending\",\"rolledAt\":\"2026-01-01T00:05:00Z\",\"claimedAt\":null,\"expiresAt\":null,\"createdAt\":\"2026-01-01T00:05:00Z\"}]}]" +
                "}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var update = await kraty.Events.ProgressAsync("race", "a1", new ProgressInput { Mode = "set", MetricValue = 15 }, @as: "alice");
            Assert.Equal("a1", update.Attempt.Id);
            Assert.Single(update.MilestonesFired);
            Assert.Equal("kills_15", update.MilestonesFired[0].Key);
            Assert.Single(update.MilestonesFired[0].Grants);
            Assert.Equal("reward", update.MilestonesFired[0].Grants[0].Kind);
            Assert.Equal("event_milestone", update.MilestonesFired[0].Grants[0].SourceKind);
        }

        [Fact]
        public async Task ProgressReturnsEmptyMilestonesFiredWhenNothingFires()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{" +
                "\"attempt\":{\"id\":\"a1\",\"eventId\":\"e1\",\"eventWindowId\":\"w1\",\"leaderboardId\":\"lb1\",\"playerId\":\"p1\",\"startedAt\":\"2026-01-01T00:00:00Z\",\"endsAt\":\"2026-01-01T00:10:00Z\",\"completedAt\":null,\"metrics\":{\"kills\":2},\"metricsRaw\":{\"kills\":2},\"score\":2,\"status\":\"in_progress\"}," +
                "\"milestonesFired\":[]" +
                "}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var update = await kraty.Events.ProgressAsync("race", "a1", new ProgressInput { Mode = "increment", MetricValue = 1 }, @as: "alice");
            Assert.Empty(update.MilestonesFired);
        }

        [Fact]
        public async Task ProgressToleratesMissingMilestonesFiredField()
        {
            // Older backend that doesn't include the field yet: the
            // C# default for the `List<MilestoneFired>` property keeps
            // the SDK safe to iterate without a null check.
            var handler = new FakeHandler().Push(200,
                "{\"data\":{" +
                "\"attempt\":{\"id\":\"a1\",\"eventId\":\"e1\",\"eventWindowId\":\"w1\",\"leaderboardId\":\"lb1\",\"playerId\":\"p1\",\"startedAt\":\"2026-01-01T00:00:00Z\",\"endsAt\":\"2026-01-01T00:10:00Z\",\"completedAt\":null,\"metrics\":{},\"metricsRaw\":{},\"score\":0,\"status\":\"in_progress\"}" +
                "}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var update = await kraty.Events.ProgressAsync("race", "a1", new ProgressInput { Mode = "set", MetricValue = 0 }, @as: "alice");
            Assert.NotNull(update.MilestonesFired);
            Assert.Empty(update.MilestonesFired);
        }

        [Fact]
        public async Task EventLeaderboardReadWithIncludeSelfBuildsQueryString()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"leaderboardId\":\"lb_self\",\"mode\":\"global\",\"finalized\":false,\"entries\":[],\"self\":{\"rank\":4,\"score\":90}}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var board = await kraty.EventLeaderboards.ReadAsync("lb_self", new EventLeaderboardReadOptions
            {
                Limit = 5,
                IncludeSelf = true,
                ExternalId = "alice",
            });
            Assert.NotNull(board.Self);
            Assert.Equal(4, board.Self!.Rank);
            Assert.Contains("limit=5", handler.Calls[0].Url);
            Assert.Contains("includeSelf=true", handler.Calls[0].Url);
            Assert.Contains("externalId=alice", handler.Calls[0].Url);
        }

        [Fact]
        public async Task EventLeaderboardReadWithIncludeSelfLazilyResolvesActivePlayer()
        {
            // Bare `new Kraty(...)` + IncludeSelf:true should lazily
            // register a player and forward THAT id as externalId, with no
            // need for the caller to thread it through.
            var handler = new FakeHandler()
                .Push(201, "{\"data\":{\"secret\":\"auto-secret\"}}")
                .Push(200, "{\"data\":{\"leaderboardId\":\"lb_x\",\"mode\":\"global\",\"finalized\":false,\"entries\":[],\"self\":null}}");
            using var kraty = new Kraty(BaseOpts(handler));
            await kraty.EventLeaderboards.ReadAsync("lb_x", new EventLeaderboardReadOptions { IncludeSelf = true });
            Assert.Equal(2, handler.Calls.Count);
            Assert.Matches(@"/sdk/v1/players/kp_[A-Za-z0-9_-]+/register$", handler.Calls[0].Url);
            Assert.Contains("includeSelf=true", handler.Calls[1].Url);
            Assert.Contains("externalId=kp_", handler.Calls[1].Url);
        }

        [Fact]
        public async Task LeaderboardReadHitsKeyedRouteAndDecodesShape()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{" +
                "\"key\":\"weekly_global\",\"leaderboardId\":\"slb_1\",\"scope\":\"global\"," +
                "\"resetCadence\":\"weekly\",\"scoreAggregation\":\"best\",\"segment\":null," +
                "\"period\":\"2026-06-22T00:00:00Z\"," +
                "\"entries\":[{\"participantId\":\"p1\",\"kind\":\"player\",\"name\":\"alice\",\"avatar\":null,\"score\":42,\"rank\":1,\"isSelf\":false}]," +
                "\"self\":null}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var board = await kraty.Leaderboards.ReadAsync("weekly_global", new LeaderboardReadOptions
            {
                Limit = 10,
            });
            Assert.Equal("weekly_global", board.Key);
            Assert.Equal("slb_1", board.LeaderboardId);
            Assert.Equal("weekly", board.ResetCadence);
            Assert.Single(board.Entries);
            Assert.Equal(1, board.Entries[0].Rank);
            Assert.Contains("/sdk/v1/leaderboards/weekly_global?limit=10", handler.Calls[0].Url);
        }

        [Fact]
        public async Task LeaderboardReadPassesSegmentPeriodIncludeSelf()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"key\":\"weekly_region\",\"leaderboardId\":\"slb_2\",\"scope\":null," +
                "\"resetCadence\":\"weekly\",\"scoreAggregation\":\"best\",\"segment\":\"eu\"," +
                "\"period\":\"2026-06-15T00:00:00Z\",\"entries\":[],\"self\":{\"rank\":7,\"score\":100}}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var board = await kraty.Leaderboards.ReadAsync("weekly_region", new LeaderboardReadOptions
            {
                Limit = 25,
                Segment = "eu",
                Period = "2026-06-15T00:00:00Z",
                IncludeSelf = true,
                ExternalId = "alice",
            });
            Assert.Equal("eu", board.Segment);
            Assert.NotNull(board.Self);
            Assert.Equal(7, board.Self!.Rank);
            var url = handler.Calls[0].Url;
            Assert.Contains("limit=25", url);
            Assert.Contains("segment=eu", url);
            Assert.Contains("period=2026-06-15T00%3A00%3A00Z", url);
            Assert.Contains("includeSelf=true", url);
            Assert.Contains("externalId=alice", url);
        }

        [Fact]
        public async Task LeaderboardListPeriodsDecodes()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"key\":\"weekly_global\",\"leaderboardId\":\"slb_1\"," +
                "\"currentPeriodStartedAt\":\"2026-06-22T00:00:00Z\"," +
                "\"periods\":[" +
                "{\"periodStartedAt\":\"2026-06-15T00:00:00Z\",\"periodEndedAt\":\"2026-06-22T00:00:00Z\"}," +
                "{\"periodStartedAt\":\"2026-06-08T00:00:00Z\",\"periodEndedAt\":\"2026-06-15T00:00:00Z\"}" +
                "]}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var resp = await kraty.Leaderboards.ListPeriodsAsync("weekly_global", limit: 5);
            Assert.Equal(2, resp.Periods.Count);
            Assert.Equal("2026-06-15T00:00:00Z", resp.Periods[0].PeriodStartedAt);
            Assert.Contains("/sdk/v1/leaderboards/weekly_global/periods?limit=5", handler.Calls[0].Url);
        }

        [Fact]
        public async Task LeaderboardJoinPostsToJoinRouteAndSurfacesJoined()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{" +
                "\"key\":\"weekly_region\",\"leaderboardId\":\"slb_2\",\"scope\":null," +
                "\"resetCadence\":\"weekly\",\"scoreAggregation\":\"best\",\"segment\":\"eu\"," +
                "\"period\":\"2026-06-22T00:00:00Z\"," +
                "\"entries\":[{\"participantId\":\"p1\",\"kind\":\"player\",\"name\":\"alice\",\"avatar\":null,\"score\":0,\"rank\":12,\"isSelf\":true}]," +
                "\"self\":{\"rank\":12,\"score\":0},\"joined\":true}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var board = await kraty.Leaderboards.JoinAsync("weekly_region", new LeaderboardJoinOptions
            {
                Segment = "eu",
                Limit = 10,
                ExternalId = "alice",
            });
            Assert.True(board.Joined);
            Assert.Equal("eu", board.Segment);
            Assert.Equal("POST", handler.Calls[0].Method);
            Assert.Contains("/sdk/v1/players/alice/leaderboards/weekly_region/join?limit=10", handler.Calls[0].Url);
            var body = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(handler.Calls[0].Body!)!;
            Assert.Equal("eu", (string?)body["segment"]);
        }

        [Fact]
        public async Task LeaderboardStandingsBuildsQueryStringAndDecodesSegments()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{" +
                "\"key\":\"weekly_global\",\"leaderboardId\":\"slb_1\",\"scope\":\"game\"," +
                "\"resetCadence\":\"weekly\",\"scoreAggregation\":\"best\",\"period\":\"2026-06-22T00:00:00Z\"," +
                "\"segments\":[{\"segment\":\"eu\",\"participated\":true,\"selfRank\":3," +
                "\"entries\":[{\"participantId\":\"p1\",\"kind\":\"player\",\"name\":\"alice\",\"avatar\":null,\"score\":42,\"rank\":3,\"isSelf\":true}]}]," +
                "\"segmentsTruncated\":false}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var standings = await kraty.Leaderboards.StandingsAsync("weekly_global", new StandingsReadOptions
            {
                Scope = "segment",
                Segment = "eu",
                Period = "2026-06-22T00:00:00Z",
                Limit = 25,
                MaxSegments = 5,
                ExternalId = "alice",
            });
            Assert.Equal("weekly_global", standings.Key);
            Assert.False(standings.SegmentsTruncated);
            Assert.Single(standings.Segments);
            Assert.Equal("eu", standings.Segments[0].Segment);
            Assert.True(standings.Segments[0].Participated);
            Assert.Equal(3, standings.Segments[0].SelfRank);
            var url = handler.Calls[0].Url;
            Assert.Contains("/sdk/v1/leaderboards/weekly_global/standings?", url);
            Assert.Contains("scope=segment", url);
            Assert.Contains("segment=eu", url);
            Assert.Contains("period=2026-06-22T00%3A00%3A00Z", url);
            Assert.Contains("limit=25", url);
            Assert.Contains("maxSegments=5", url);
            Assert.Contains("externalId=alice", url);
        }

        [Fact]
        public async Task LeaderboardStandingsDefaultsScopeToAll()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"key\":\"weekly_global\",\"leaderboardId\":\"slb_1\",\"scope\":\"game\"," +
                "\"resetCadence\":\"weekly\",\"scoreAggregation\":\"best\",\"period\":\"2026-06-22T00:00:00Z\"," +
                "\"segments\":[],\"segmentsTruncated\":false}}");
            using var kraty = new Kraty(BaseOpts(handler));
            await kraty.Leaderboards.StandingsAsync("weekly_global");
            Assert.Contains("scope=all", handler.Calls[0].Url);
        }

        [Fact]
        public async Task LeaderboardStandingsSelfSegmentLazilyResolvesActivePlayer()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":{\"secret\":\"auto-secret\"}}")
                .Push(200, "{\"data\":{\"key\":\"weekly_global\",\"leaderboardId\":\"slb_1\",\"scope\":\"game\"," +
                "\"resetCadence\":\"weekly\",\"scoreAggregation\":\"best\",\"period\":\"2026-06-22T00:00:00Z\"," +
                "\"segments\":[],\"segmentsTruncated\":false}}");
            using var kraty = new Kraty(BaseOpts(handler));
            await kraty.Leaderboards.StandingsAsync("weekly_global", new StandingsReadOptions { Scope = "self_segment" });
            Assert.Equal(2, handler.Calls.Count);
            Assert.Matches(@"/sdk/v1/players/kp_[A-Za-z0-9_-]+/register$", handler.Calls[0].Url);
            Assert.Contains("scope=self_segment", handler.Calls[1].Url);
            Assert.Contains("externalId=kp_", handler.Calls[1].Url);
        }

        [Fact]
        public async Task EventLeaderboardJoinPostsToJoinRouteAndSurfacesJoined()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"leaderboardId\":\"lb_1\",\"mode\":\"global\",\"finalized\":false," +
                "\"entries\":[{\"participantId\":\"p1\",\"kind\":\"player\",\"name\":\"alice\",\"avatar\":null,\"score\":0,\"rank\":5,\"isSelf\":true}]," +
                "\"self\":{\"rank\":5,\"score\":0},\"joined\":true}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var board = await kraty.EventLeaderboards.JoinAsync("lb_1", new EventLeaderboardJoinOptions
            {
                Limit = 10,
                ExternalId = "alice",
            });
            Assert.True(board.Joined);
            Assert.Equal("lb_1", board.LeaderboardId);
            Assert.Equal("POST", handler.Calls[0].Method);
            Assert.Contains("/sdk/v1/players/alice/event-leaderboards/lb_1/join?limit=10", handler.Calls[0].Url);
        }

        [Fact]
        public async Task EventLeaderboardJoinSurfacesConflictWhenFinalized()
        {
            var handler = new FakeHandler().Push(409,
                "{\"error\":{\"code\":\"conflict\",\"message\":\"event window already finalized\"}}");
            using var kraty = new Kraty(BaseOpts(handler));
            var err = await Assert.ThrowsAsync<KratyApiError>(async () =>
                await kraty.EventLeaderboards.JoinAsync("lb_1", new EventLeaderboardJoinOptions { ExternalId = "alice" }));
            Assert.Equal(409, err.Status);
            Assert.Equal("conflict", err.Code);
        }
    }
}
