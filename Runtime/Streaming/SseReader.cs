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
    /// Server-Sent Events plumbing shared by every Kraty stream
    /// (leaderboards, inventory): opening the long-lived request, mapping a
    /// non-2xx response onto the SDK's error types, and parsing the
    /// <c>event:</c> / <c>data:</c> line protocol.
    ///
    /// <para>
    /// Each stream keeps its own strongly-typed event class and its own
    /// handle; only the transport lives here, so adding a stream is a
    /// factory plus a DTO rather than another copy of the parser.
    /// </para>
    /// </summary>
    internal static class SseReader
    {
        /// <summary>
        /// Opens the stream and returns the live response. Throws
        /// <see cref="KratyNetworkError"/> when the connection fails and
        /// <see cref="KratyApiError"/> for a non-2xx status (the body is
        /// drained and parsed first so the caller sees the real error code).
        /// <paramref name="label"/> only shapes error messages.
        /// </summary>
        public static async Task<HttpResponseMessage> OpenAsync(
            HttpClient http,
            string url,
            string authHeader,
            string? playerSecret,
            string label,
            CancellationToken cancellationToken
        )
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("authorization", authHeader);
            req.Headers.TryAddWithoutValidation("accept", "text/event-stream");
            if (!string.IsNullOrEmpty(playerSecret))
            {
                req.Headers.TryAddWithoutValidation("x-player-secret", playerSecret);
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new KratyNetworkError($"{label} connect failed: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var bodyText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                response.Dispose();
                string? code = null;
                string? message = null;
                Dictionary<string, object?>? details = null;
                try
                {
                    var root = JToken.Parse(bodyText);
                    if (root.Type == JTokenType.Object && root["error"] is JObject err)
                    {
                        code = (string?)err["code"];
                        message = (string?)err["message"];
                        var d = err["details"];
                        if (d != null)
                        {
                            details = new Dictionary<string, object?> { ["raw"] = d.ToString(Formatting.None) };
                        }
                    }
                }
                catch { /* not JSON, fall through */ }
                throw new KratyApiError(
                    (int)response.StatusCode,
                    code ?? $"http_{(int)response.StatusCode}",
                    message ?? bodyText,
                    details
                );
            }

            return response;
        }

        /// <summary>
        /// Reads the response body until the server closes it or
        /// <paramref name="cts"/> fires, invoking
        /// <paramref name="onEvent"/> once per complete SSE event.
        /// Parse failures and read failures go to <paramref name="onError"/>;
        /// a cancellation is never reported as an error.
        /// </summary>
        public static async Task PumpAsync(
            HttpResponseMessage response,
            CancellationTokenSource cts,
            Action<string, Dictionary<string, JToken>> onEvent,
            Action<Exception> onError
        )
        {
            try
            {
                using var bodyStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(bodyStream, Encoding.UTF8);
                string currentEvent = "message";
                var dataBuffer = new StringBuilder();

                void Emit()
                {
                    if (dataBuffer.Length == 0)
                    {
                        currentEvent = "message";
                        return;
                    }
                    var raw = dataBuffer.ToString();
                    try
                    {
                        var root = JToken.Parse(raw);
                        var dict = new Dictionary<string, JToken>();
                        if (root is JObject obj)
                        {
                            foreach (var prop in obj.Properties())
                            {
                                dict[prop.Name] = prop.Value;
                            }
                        }
                        else
                        {
                            dict["value"] = root;
                        }
                        onEvent(currentEvent, dict);
                    }
                    catch (Exception parseErr)
                    {
                        onError(parseErr);
                    }
                    dataBuffer.Clear();
                    currentEvent = "message";
                }

                // ReadLineAsync doesn't take a CT on netstandard2.1, so
                // poll the reader and bail when the linked token
                // fires by disposing the stream out from under it.
                cts.Token.Register(() =>
                {
                    try { bodyStream.Dispose(); } catch { /* swallow */ }
                });

                while (!cts.Token.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                    catch (Exception readErr)
                    {
                        if (!cts.Token.IsCancellationRequested) onError(readErr);
                        break;
                    }
                    if (line == null)
                    {
                        // Server closed the stream; flush any
                        // pending event, then exit cleanly.
                        Emit();
                        break;
                    }
                    if (line.Length == 0)
                    {
                        // Blank line terminates an event.
                        Emit();
                        continue;
                    }
                    if (line[0] == ':')
                    {
                        // Comment / heartbeat: ignore.
                        continue;
                    }
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;
                    var field = line.Substring(0, colonIdx);
                    var value = line.Substring(colonIdx + 1);
                    // Spec: a single leading space in the value is
                    // optional and should be stripped.
                    if (value.Length > 0 && value[0] == ' ') value = value.Substring(1);
                    switch (field)
                    {
                        case "event":
                            currentEvent = value;
                            break;
                        case "data":
                            if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                            dataBuffer.Append(value);
                            break;
                        // SSE also defines `id` and `retry`, which we
                        // don't use.
                        default:
                            break;
                    }
                }
            }
            catch (Exception loopErr)
            {
                if (!cts.Token.IsCancellationRequested) onError(loopErr);
            }
        }
    }
}
