using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kraty.Tests
{
    /// <summary>
    /// HttpMessageHandler that returns a queue of pre-baked responses
    /// and records every request. Mirrors the `fakeFetch` helper in the
    /// TypeScript SDK tests.
    /// </summary>
    internal sealed class FakeHandler : HttpMessageHandler
    {
        public sealed class Call
        {
            public string Method { get; init; } = string.Empty;
            public string Url { get; init; } = string.Empty;
            public Dictionary<string, string> Headers { get; init; } = new();
            public string? Body { get; init; }
        }

        public List<Call> Calls { get; } = new();
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        public FakeHandler Push(int status, string? body = null, Dictionary<string, string>? headers = null)
        {
            Responses.Enqueue(() =>
            {
                var msg = new HttpResponseMessage((HttpStatusCode)status);
                if (body != null)
                {
                    msg.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                }
                if (headers != null)
                {
                    foreach (var (k, v) in headers)
                    {
                        msg.Headers.TryAddWithoutValidation(k, v);
                    }
                }
                return msg;
            });
            return this;
        }

        public FakeHandler PushError(Exception ex)
        {
            Responses.Enqueue(() => throw ex);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var bodyText = req.Content != null ? await req.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
            var headers = new Dictionary<string, string>();
            foreach (var h in req.Headers)
            {
                headers[h.Key.ToLowerInvariant()] = string.Join(",", h.Value);
            }
            if (req.Content?.Headers != null)
            {
                foreach (var h in req.Content.Headers)
                {
                    headers[h.Key.ToLowerInvariant()] = string.Join(",", h.Value);
                }
            }
            Calls.Add(new Call
            {
                Method = req.Method.Method,
                Url = req.RequestUri?.ToString() ?? string.Empty,
                Headers = headers,
                Body = bodyText,
            });
            if (Responses.Count == 0) throw new InvalidOperationException($"FakeHandler: out of responses (request #{Calls.Count})");
            var factory = Responses.Dequeue();
            return factory();
        }
    }
}
