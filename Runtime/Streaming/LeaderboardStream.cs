using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>
    /// One event emitted by the leaderboard SSE stream.
    /// <see cref="Kind"/> is the SSE <c>event:</c> line, typically:
    /// <list type="bullet">
    /// <item><description><c>ready</c>: handshake, sent once after the
    /// subscription is wired. Safe to start posting progress as soon
    /// as this lands without missing the resulting update.</description></item>
    /// <item><description><c>score_update</c>: a participant's score / rank
    /// changed; payload carries the new <c>rank</c> + <c>score</c> for
    /// the affected entry.</description></item>
    /// <item><description><c>finalized</c>: the board ended (a session/lobby
    /// terminated, or the event window closed). <see cref="Data"/> carries
    /// <c>reason</c> (<c>session_terminated</c> | <c>window_closed</c>) and
    /// <c>standings</c> (a list of <c>{participantId, rank, score, name, kind}</c>).
    /// Find the caller by <c>participantId</c> to show their placement, then
    /// stop expecting <c>score_update</c>s.</description></item>
    /// <item><description><c>closed</c>: server is finalizing or closing.
    /// After this the stream completes and <see cref="LeaderboardStream.CancelAsync"/>
    /// is a no-op.</description></item>
    /// </list>
    /// <see cref="Data"/> is the parsed <c>data:</c> JSON line.
    /// </summary>
    public sealed class LeaderboardStreamEvent
    {
        public string Kind { get; }
        public Dictionary<string, JToken> Data { get; }

        public LeaderboardStreamEvent(string kind, Dictionary<string, JToken> data)
        {
            Kind = kind;
            Data = data;
        }
    }

    /// <summary>
    /// Handle to an active SSE subscription. Hook <see cref="OnEvent"/>
    /// / <see cref="OnError"/> before the connection is opened (or
    /// immediately after, since events buffer until the consumer's
    /// SynchronizationContext drains), call <see cref="CancelAsync"/>
    /// to stop.
    ///
    /// <para>
    /// Callbacks fire on the HTTP background thread. In Unity, marshal
    /// to the main thread before touching <c>UnityEngine</c> APIs:
    /// </para>
    /// <code>
    /// stream.OnEvent = ev => mainThreadDispatcher.Enqueue(() => Repaint(ev));
    /// </code>
    ///
    /// <para>
    /// The SDK does NOT auto-reconnect on transport drop; surface
    /// errors via <see cref="OnError"/> and re-invoke
    /// <see cref="EventLeaderboardsClient.LiveAsync"/> after a backoff
    /// if you want resumption.
    /// </para>
    /// </summary>
    public sealed class LeaderboardStream : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _readLoop;
        private readonly HttpResponseMessage _response;
        private int _disposed;

        public Action<LeaderboardStreamEvent>? OnEvent { get; set; }
        public Action<Exception>? OnError { get; set; }

        internal LeaderboardStream(HttpResponseMessage response, CancellationTokenSource cts, Func<LeaderboardStream, Task> startReadLoop)
        {
            _response = response;
            _cts = cts;
            // Start the read loop on the threadpool; it runs until the
            // server closes the stream OR the consumer cancels.
            _readLoop = Task.Run(() => startReadLoop(this));
        }

        /// <summary>
        /// Cancels the subscription + closes the HTTP socket. Idempotent:
        /// safe to call after the server emits <c>closed</c> or after
        /// <see cref="Dispose"/>.
        /// </summary>
        public async Task CancelAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _cts.Cancel(); } catch { /* swallow */ }
            try { await _readLoop.ConfigureAwait(false); } catch { /* swallow */ }
            try { _response.Dispose(); } catch { /* swallow */ }
            try { _cts.Dispose(); } catch { /* swallow */ }
        }

        public void Dispose()
        {
            _ = CancelAsync();
        }

        internal void EmitEvent(LeaderboardStreamEvent ev)
        {
            try { OnEvent?.Invoke(ev); }
            catch (Exception cbErr) { try { OnError?.Invoke(cbErr); } catch { /* swallow */ } }
        }

        internal void EmitError(Exception err)
        {
            try { OnError?.Invoke(err); } catch { /* swallow */ }
        }
    }

    /// <summary>
    /// Opens an SSE subscription to a leaderboard. Returns a
    /// <see cref="LeaderboardStream"/> handle the caller drives via its
    /// <c>OnEvent</c> / <c>OnError</c> callbacks. Does NOT
    /// auto-reconnect.
    ///
    /// <para>
    /// Implementation: opens a long-lived <c>HttpClient.SendAsync</c>
    /// with <c>ResponseHeadersRead</c> + a manual line-by-line parser.
    /// <c>\n\n</c> separates events; <c>event:</c> / <c>data:</c> are
    /// the keys we read. Comment lines starting with <c>:</c> are
    /// heartbeats, ignored.
    /// </para>
    /// </summary>
    internal static class LeaderboardStreamFactory
    {
        public static async Task<LeaderboardStream> OpenAsync(
            HttpClient http,
            string baseUrl,
            string leaderboardId,
            string authHeader,
            string? playerSecret,
            CancellationToken cancellationToken
        )
        {
            var url = $"{baseUrl}/sdk/v1/event-leaderboards/{Uri.EscapeDataString(leaderboardId)}/stream";
            var response = await SseReader.OpenAsync(
                http, url, authHeader, playerSecret, "leaderboard stream", cancellationToken
            ).ConfigureAwait(false);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return new LeaderboardStream(response, cts, (self) =>
                SseReader.PumpAsync(
                    response,
                    cts,
                    (kind, data) => self.EmitEvent(new LeaderboardStreamEvent(kind, data)),
                    self.EmitError
                )
            );
        }
    }
}
