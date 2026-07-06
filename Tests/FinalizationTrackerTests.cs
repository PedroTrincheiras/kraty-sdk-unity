using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Kraty.Tests
{
    /// <summary>
    /// Mirror of the TypeScript SDK's <c>finalization.test.ts</c> — the
    /// finalization catch-up (docs/05b): the single-writer invariant, the
    /// SSE + catch-up dedupe, the persisted session-vs-window reason, and
    /// dismiss/clearReported.
    /// </summary>
    public sealed class FinalizationTrackerTests
    {
        private static MembershipRef Ref =>
            MembershipRef.EventBoard("lb-1", "daily");

        private static (FinalizationTracker tracker, List<FinalizationResult> fired) Make(
            bool finalized = false,
            string? reason = null,
            SelfEntry? self = null)
        {
            var store = new InMemoryMembershipStore();
            Task<EventBoardStatus?> ReadBoard(string _) =>
                Task.FromResult<EventBoardStatus?>(
                    new EventBoardStatus(finalized, reason, self ?? new SelfEntry(3, 42)));
            var tracker = new FinalizationTracker(
                store,
                getActivePlayerId: () => Task.FromResult<string?>("p1"),
                readEventBoard: ReadBoard);
            var fired = new List<FinalizationResult>();
            tracker.OnFinalized(r => fired.Add(r));
            return (tracker, fired);
        }

        [Fact]
        public async Task Track_IsIdempotentUpsert()
        {
            var store = new InMemoryMembershipStore();
            var tracker = new FinalizationTracker(
                store, () => Task.FromResult<string?>("p1"),
                _ => Task.FromResult<EventBoardStatus?>(null));
            await tracker.TrackAsync(Ref);
            await tracker.TrackAsync(Ref);
            var entries = await store.LoadAsync("p1");
            Assert.Single(entries);
            Assert.Equal(TrackedMembership.StatusActive, entries[0].Status);
        }

        [Fact]
        public async Task SsePath_WritesRegistryAndFiresOnce()
        {
            var (tracker, fired) = Make();
            await tracker.TrackAsync(Ref);
            await tracker.OnStreamFinalizedAsync("lb-1", new Dictionary<string, JToken>
            {
                ["reason"] = FinalizationReason.SessionTerminated,
            });
            Assert.Single(fired);
            Assert.Equal(FinalizationReason.SessionTerminated, fired[0].Reason);
        }

        [Fact]
        public async Task CatchUp_DoesNotReFireWhatSseResolved()
        {
            var (tracker, fired) = Make(finalized: true, reason: FinalizationReason.WindowClosed);
            await tracker.TrackAsync(Ref);
            await tracker.OnStreamFinalizedAsync("lb-1", new Dictionary<string, JToken>
            {
                ["reason"] = FinalizationReason.WindowClosed,
            });
            Assert.Single(fired);
            var newly = await tracker.CheckFinalizationsAsync();
            Assert.Empty(newly);
            Assert.Single(fired);
        }

        [Fact]
        public async Task CatchUp_ThreadsPersistedReason()
        {
            var session = Make(finalized: true, reason: FinalizationReason.SessionTerminated);
            await session.tracker.TrackAsync(Ref);
            var s = await session.tracker.CheckFinalizationsAsync();
            Assert.Equal(FinalizationReason.SessionTerminated, s[0].Reason);

            var window = Make(finalized: true, reason: FinalizationReason.WindowClosed);
            await window.tracker.TrackAsync(Ref);
            var w = await window.tracker.CheckFinalizationsAsync();
            Assert.Equal(FinalizationReason.WindowClosed, w[0].Reason);
        }

        [Fact]
        public async Task CatchUp_FallsBackToFinalizedWithoutReason()
        {
            var (tracker, _) = Make(finalized: true, reason: null);
            await tracker.TrackAsync(Ref);
            var outResults = await tracker.CheckFinalizationsAsync();
            Assert.Equal(FinalizationReason.Finalized, outResults[0].Reason);
        }

        [Fact]
        public async Task CatchUp_IgnoresActiveBoardsAndDedupes()
        {
            var (tracker, fired) = Make(finalized: true, self: new SelfEntry(2, 99));
            await tracker.TrackAsync(Ref);
            var first = await tracker.CheckFinalizationsAsync();
            Assert.Single(first);
            Assert.Equal(2, first[0].Self!.Rank);
            Assert.Equal("daily", first[0].EventKey);
            var second = await tracker.CheckFinalizationsAsync();
            Assert.Empty(second);
            Assert.Single(fired);
        }

        [Fact]
        public async Task Dismiss_RemovesMembership()
        {
            var (tracker, _) = Make(finalized: true);
            await tracker.TrackAsync(Ref);
            await tracker.DismissAsync(Ref);
            Assert.Empty(await tracker.CheckFinalizationsAsync());
        }

        [Fact]
        public async Task ClearReported_DropsDeliveredKeepsActive()
        {
            var (tracker, _) = Make(finalized: true);
            await tracker.TrackAsync(Ref);
            await tracker.TrackAsync(MembershipRef.EventBoard("lb-2"));
            await tracker.OnStreamFinalizedAsync("lb-1", new Dictionary<string, JToken>
            {
                ["reason"] = FinalizationReason.WindowClosed,
            });
            var removed = await tracker.ClearReportedAsync();
            Assert.Equal(1, removed);
        }

        [Fact]
        public async Task ConcurrentSseAndCheck_ResolveExactlyOnce()
        {
            var (tracker, fired) = Make(finalized: true);
            await tracker.TrackAsync(Ref);
            await Task.WhenAll(
                tracker.OnStreamFinalizedAsync("lb-1", new Dictionary<string, JToken>
                {
                    ["reason"] = FinalizationReason.SessionTerminated,
                }),
                tracker.CheckFinalizationsAsync());
            Assert.Single(fired);
        }
    }
}
