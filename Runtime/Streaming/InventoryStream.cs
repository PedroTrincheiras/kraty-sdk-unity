using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>Who caused an inventory change.</summary>
    public static class InventoryEventOrigin
    {
        public const string Client = "client";
        public const string Server = "server";
        public const string Admin = "admin";
        public const string Engine = "engine";
    }

    /// <summary>
    /// One event off the inventory stream. <see cref="Kind"/> is the SSE
    /// <c>event:</c> line:
    /// <list type="bullet">
    /// <item><description><c>ready</c>: handshake, once the subscription is wired.</description></item>
    /// <item><description><c>inventory_changed</c>: an item quantity moved.</description></item>
    /// <item><description><c>wallet_changed</c>: a currency / progression balance moved.</description></item>
    /// <item><description><c>grant_created</c>: a reward became claimable.</description></item>
    /// </list>
    /// The typed properties cover the common fields; read <see cref="Data"/>
    /// directly for anything a newer backend adds.
    /// </summary>
    public sealed class InventoryStreamEvent
    {
        public string Kind { get; }
        public Dictionary<string, JToken> Data { get; }

        public InventoryStreamEvent(string kind, Dictionary<string, JToken> data)
        {
            Kind = kind;
            Data = data;
        }

        /// <summary><c>client</c> / <c>server</c> / <c>admin</c> / <c>engine</c>; empty on <c>ready</c>.</summary>
        public string Origin => Str("origin");

        /// <summary>Set on <c>inventory_changed</c>.</summary>
        public string ItemKey => Str("itemKey");

        /// <summary>Set on <c>wallet_changed</c>.</summary>
        public string EconomyKey => Str("economyKey");

        /// <summary>Signed change; negative for a consume. 0 when not applicable.</summary>
        public int Delta => Int("delta");

        /// <summary>Quantity AFTER the change (<c>inventory_changed</c>).</summary>
        public int Quantity => Int("quantity");

        /// <summary>Balance AFTER the change (<c>wallet_changed</c>).</summary>
        public int Balance => Int("balance");

        /// <summary>Ledger reason (<c>grant_deposit</c>, <c>consume</c>, <c>admin_grant</c>, …).</summary>
        public string Reason => Str("reason");

        /// <summary>Set on <c>grant_created</c>; claim it with <c>Grants.CollectAllAsync()</c>.</summary>
        public string GrantId => Str("grantId");

        /// <summary>True when this event is the echo of a write this client made.</summary>
        public bool IsOwnWrite => Origin == InventoryEventOrigin.Client;

        private string Str(string key) =>
            Data.TryGetValue(key, out var v) && v.Type == JTokenType.String
                ? ((string?)v ?? string.Empty)
                : string.Empty;

        private int Int(string key) =>
            Data.TryGetValue(key, out var v) &&
            (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                ? (int)v
                : 0;
    }

    /// <summary>
    /// Handle to an active inventory subscription. Hook
    /// <see cref="OnEvent"/> / <see cref="OnError"/>, call
    /// <see cref="CancelAsync"/> to stop.
    ///
    /// <para>
    /// Callbacks fire on the HTTP background thread. In Unity, marshal to
    /// the main thread before touching <c>UnityEngine</c> APIs:
    /// </para>
    /// <code>
    /// stream.OnEvent = ev => mainThreadDispatcher.Enqueue(() => RefreshBackpack(ev));
    /// </code>
    ///
    /// <para>
    /// The SDK does NOT auto-reconnect on transport drop; surface errors
    /// via <see cref="OnError"/> and re-invoke
    /// <see cref="InventoryClient.LiveAsync"/> after a backoff if you want
    /// resumption.
    /// </para>
    /// </summary>
    public sealed class InventoryStream : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _readLoop;
        private readonly HttpResponseMessage _response;
        private int _disposed;

        public Action<InventoryStreamEvent>? OnEvent { get; set; }
        public Action<Exception>? OnError { get; set; }

        internal InventoryStream(HttpResponseMessage response, CancellationTokenSource cts, Func<InventoryStream, Task> startReadLoop)
        {
            _response = response;
            _cts = cts;
            // Start the read loop on the threadpool; it runs until the
            // server closes the stream OR the consumer cancels.
            _readLoop = Task.Run(() => startReadLoop(this));
        }

        /// <summary>
        /// Cancels the subscription + closes the HTTP socket. Idempotent.
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

        internal void EmitEvent(InventoryStreamEvent ev)
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
    /// Opens an SSE subscription to a player's inventory / wallet / grants.
    /// Does NOT auto-reconnect. Transport + parsing live in
    /// <see cref="SseReader"/>.
    /// </summary>
    internal static class InventoryStreamFactory
    {
        public static async Task<InventoryStream> OpenAsync(
            HttpClient http,
            string baseUrl,
            string externalPlayerId,
            string authHeader,
            string? playerSecret,
            CancellationToken cancellationToken
        )
        {
            var url = $"{baseUrl}/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/inventory/stream";
            var response = await SseReader.OpenAsync(
                http, url, authHeader, playerSecret, "inventory stream", cancellationToken
            ).ConfigureAwait(false);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return new InventoryStream(response, cts, (self) =>
                SseReader.PumpAsync(
                    response,
                    cts,
                    (kind, data) => self.EmitEvent(new InventoryStreamEvent(kind, data)),
                    self.EmitError
                )
            );
        }
    }
}
