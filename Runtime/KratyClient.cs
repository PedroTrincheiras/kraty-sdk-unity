using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Kraty
{
    /// <summary>
    /// Options the consumer passes to <c>new KratyClient(...)</c>.
    /// </summary>
    public sealed class KratyClientOptions
    {
        /// <summary>API key in the <c>{prefix}.{secret}</c> form returned by the portal.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Per-player secret. When set, the client attaches
        /// <c>X-Player-Secret: &lt;value&gt;</c> to every request. Required
        /// by all player-scoped routes (events.start, events.progress,
        /// grants.*, inventory.*, wallet.*) — without it those return
        /// 401 <c>player_secret_invalid</c>.
        ///
        /// <para>
        /// Leave unset to let the SDK auto-register on the first
        /// player-scoped call. See <see cref="KratyClient.EnsureIdentityAsync"/>.
        /// </para>
        /// </summary>
        public string? PlayerSecret { get; set; }

        /// <summary>
        /// The <c>externalPlayerId</c> this SDK instance is
        /// authenticated as. When set, player-scoped methods default
        /// to this id and skip auto-register on first call. Leave
        /// unset for self-serve signups — the SDK then auto-generates
        /// a UUID on first use and persists it in
        /// <see cref="SecretStore"/>.
        /// </summary>
        public string? ActiveExternalPlayerId { get; set; }

        /// <summary>
        /// Persistence backend for the player secret + active id. When
        /// omitted, the SDK picks a sensible default:
        ///
        /// <list type="bullet">
        ///   <item><description>In a Unity runtime → <c>PlayerPrefsSecretStore</c>.</description></item>
        ///   <item><description>Plain .NET (CLI, tests) → <see cref="InMemorySecretStore"/>. Wire your own (DPAPI, libsecret, file-on-disk) if you need durability.</description></item>
        /// </list>
        ///
        /// Most game clients never touch this — the default does the
        /// right thing on every platform the SDK ships on.
        /// </summary>
        public ISecretStore? SecretStore { get; set; }

        /// <summary>Override only for testing / staging. Production clients always hit the default.</summary>
        public string BaseUrl { get; set; } = "https://api.kraty.io";

        /// <summary>Per-request timeout. Defaults to 10s.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Retry configuration. Defaults to 4 attempts with exponential backoff starting at 100ms.</summary>
        public RetryConfig Retry { get; set; } = new();

        /// <summary>
        /// Optional HttpMessageHandler — tests inject mocks; production
        /// can layer DelegatingHandler for logging / observability.
        /// </summary>
        public HttpMessageHandler? HttpMessageHandler { get; set; }

        /// <summary>
        /// Idempotency-key generator. Defaults to <see cref="Guid.NewGuid"/>.
        /// </summary>
        public Func<string>? GenerateIdempotencyKey { get; set; }

        /// <summary>Fires after every HTTP attempt. Useful for telemetry.</summary>
        public Action<RequestInfo>? OnRequest { get; set; }

        /// <summary>
        /// Returns a shallow copy of this options object with
        /// <see cref="PlayerSecret"/> replaced.
        /// </summary>
        public KratyClientOptions WithPlayerSecret(string? playerSecret)
        {
            return new KratyClientOptions
            {
                ApiKey = ApiKey,
                PlayerSecret = playerSecret,
                ActiveExternalPlayerId = ActiveExternalPlayerId,
                SecretStore = SecretStore,
                BaseUrl = BaseUrl,
                Timeout = Timeout,
                Retry = Retry,
                HttpMessageHandler = HttpMessageHandler,
                GenerateIdempotencyKey = GenerateIdempotencyKey,
                OnRequest = OnRequest,
            };
        }

        /// <summary>
        /// Returns a shallow copy with <see cref="ActiveExternalPlayerId"/>
        /// replaced.
        /// </summary>
        public KratyClientOptions WithActiveExternalPlayerId(string? id)
        {
            return new KratyClientOptions
            {
                ApiKey = ApiKey,
                PlayerSecret = PlayerSecret,
                ActiveExternalPlayerId = id,
                SecretStore = SecretStore,
                BaseUrl = BaseUrl,
                Timeout = Timeout,
                Retry = Retry,
                HttpMessageHandler = HttpMessageHandler,
                GenerateIdempotencyKey = GenerateIdempotencyKey,
                OnRequest = OnRequest,
            };
        }
    }

    public sealed class RetryConfig
    {
        /// <summary>TOTAL number of HTTP calls (1 = no retry).</summary>
        public int Attempts { get; set; } = 4;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);
        /// <summary>Jitter factor (0-1). Default 0.2.</summary>
        public double Jitter { get; set; } = 0.2;
    }

    public sealed class RequestInfo
    {
        public string Method { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public string? IdempotencyKey { get; set; }
        public TimeSpan Duration { get; set; }
        public int? Status { get; set; }
        public bool Ok { get; set; }
    }

    /// <summary>
    /// HTTP client for the Kraty <c>/sdk/v1</c> surface. Bearer auth,
    /// optional per-player <c>X-Player-Secret</c> header,
    /// auto-idempotency-key stamping on POST/PUT/PATCH (re-used across
    /// retries so the server's idempotency check dedupes a replay),
    /// exponential backoff + jitter on 408/425/429/5xx + network
    /// failures, special-cases the 202 + lobby_forming response shape.
    ///
    /// Resource clients (<see cref="EventsClient"/>,
    /// <see cref="LeaderboardsClient"/>, ...) compose over an instance;
    /// the convenience facade <see cref="Kraty"/> wires them all up.
    /// </summary>
    public sealed class KratyClient : IDisposable
    {
        private const string SdkName = "app.kraty.sdk";
        private const string SdkVersion = "0.0.1";
        private const string SdkUserAgent = SdkName + "/" + SdkVersion;

        private static readonly JsonSerializerSettings JsonOptions = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        private static readonly HashSet<HttpMethod> IdempotentMethods = new()
        {
            HttpMethod.Post,
            HttpMethod.Put,
            new HttpMethod("PATCH"),
        };

        private static readonly HashSet<int> RetryableStatuses = new() { 408, 425, 429, 500, 502, 503, 504 };

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly RetryConfig _retry;
        private readonly Func<string> _generateIdempotencyKey;
        private readonly Action<RequestInfo>? _onRequest;
        private readonly Random _jitterRng = new();
        private readonly string _baseUrl;
        private readonly string _authHeader;
        // Identity is mutable on purpose — the lazy EnsureIdentityAsync()
        // call may register or restore a player after construction and
        // mutate these in place so subsequent calls skip the round-trip.
        private string? _playerSecret;
        private string? _activeExternalPlayerId;
        private readonly ISecretStore _secretStore;
        private readonly object _identityGate = new();
        private Task<(string ExternalPlayerId, string Secret)>? _identityInit;

        public KratyClient(KratyClientOptions opts)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (string.IsNullOrEmpty(opts.ApiKey))
                throw new ArgumentException("KratyClient: ApiKey is required", nameof(opts));

            _baseUrl = opts.BaseUrl.TrimEnd('/');
            _authHeader = $"Bearer {opts.ApiKey}";
            _playerSecret = string.IsNullOrEmpty(opts.PlayerSecret) ? null : opts.PlayerSecret;
            _activeExternalPlayerId = string.IsNullOrEmpty(opts.ActiveExternalPlayerId)
                ? null
                : opts.ActiveExternalPlayerId;
            _secretStore = opts.SecretStore ?? DefaultSecretStore();

            _http = opts.HttpMessageHandler != null
                ? new HttpClient(opts.HttpMessageHandler, disposeHandler: false)
                : new HttpClient();
            _ownsHttp = true;
            _http.BaseAddress = new Uri(_baseUrl, UriKind.Absolute);
            _http.Timeout = opts.Timeout;

            _retry = opts.Retry;
            _generateIdempotencyKey = opts.GenerateIdempotencyKey ?? (() => Guid.NewGuid().ToString());
            _onRequest = opts.OnRequest;
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ── Accessors for the SSE leaderboard stream ─────────────────
        // These let LeaderboardStream re-use the configured auth/base
        // url without us having to widen the public API of the client.

        internal string BaseUrlForStreaming => _baseUrl;
        internal string AuthHeaderForStreaming => _authHeader;
        internal string? PlayerSecretForStreaming => _playerSecret;
        internal HttpClient HttpForStreaming => _http;
        internal JsonSerializerSettings JsonSerializerSettings => JsonOptions;
        internal ISecretStore SecretStore => _secretStore;

        /// <summary>
        /// The active player this client is authenticated as. Resource
        /// methods fall back to this id when the caller omits the
        /// <c>@as</c> argument. Returns null until
        /// <see cref="EnsureIdentityAsync"/> has run at least once.
        /// </summary>
        public string? ActiveExternalPlayerId => _activeExternalPlayerId;

        /// <summary>
        /// The current player secret, exposed for advanced cases (e.g.
        /// re-creating a client with the same identity). Null until
        /// identity is resolved.
        /// </summary>
        public string? PlayerSecret => _playerSecret;

        /// <summary>
        /// Resolve the active player, registering a fresh one if none
        /// exists. Called transparently by every player-scoped resource
        /// method — game code rarely needs to invoke this directly.
        ///
        /// <para>
        /// Resolution order:
        /// </para>
        /// <list type="number">
        ///   <item><description>Constructor <c>ActiveExternalPlayerId</c> + <c>PlayerSecret</c> if both supplied — explicit, no I/O.</description></item>
        ///   <item><description>Persisted active id in the SecretStore + matching persisted secret — restore.</description></item>
        ///   <item><description>Fresh signup — generate a <c>kp_&lt;guid&gt;</c> id, POST <c>/sdk/v1/players/:id/register</c>, persist + install.</description></item>
        /// </list>
        ///
        /// <para>
        /// Concurrent first-touch calls share one inflight task so we
        /// don't double-register.
        /// </para>
        /// </summary>
        public Task<(string ExternalPlayerId, string Secret)> EnsureIdentityAsync(CancellationToken cancellationToken = default)
        {
            if (_activeExternalPlayerId != null && _playerSecret != null)
            {
                return Task.FromResult((_activeExternalPlayerId, _playerSecret));
            }
            lock (_identityGate)
            {
                if (_activeExternalPlayerId != null && _playerSecret != null)
                {
                    return Task.FromResult((_activeExternalPlayerId, _playerSecret));
                }
                // An inflight resolve dedupes concurrent first-touch.
                // A completed cached task is stale (e.g. after LogoutAsync
                // wiped state) — re-run the resolver.
                if (_identityInit != null && !_identityInit.IsCompleted) return _identityInit;
                _identityInit = ResolveIdentityAsync(cancellationToken);
                return _identityInit;
            }
        }

        private async Task<(string ExternalPlayerId, string Secret)> ResolveIdentityAsync(CancellationToken cancellationToken)
        {
            var explicitActive = _activeExternalPlayerId;
            var storedActive = await _secretStore.ReadActiveExternalPlayerIdAsync(cancellationToken).ConfigureAwait(false);
            var candidate = explicitActive ?? storedActive;
            if (!string.IsNullOrEmpty(candidate))
            {
                var secret = await _secretStore.ReadAsync(candidate!, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(secret))
                {
                    _activeExternalPlayerId = candidate;
                    _playerSecret = secret;
                    await _secretStore.WriteActiveExternalPlayerIdAsync(candidate!, cancellationToken).ConfigureAwait(false);
                    return (candidate!, secret!);
                }
            }

            var newId = !string.IsNullOrEmpty(explicitActive)
                ? explicitActive!
                : GenerateExternalPlayerId();
            var env = await RequestAsync<DataEnvelope<PlayerRegistration>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(newId)}/register",
                body: new Dictionary<string, object?>(),
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
            var freshSecret = env.Data?.Secret ?? string.Empty;
            await _secretStore.WriteAsync(newId, freshSecret, cancellationToken).ConfigureAwait(false);
            await _secretStore.WriteActiveExternalPlayerIdAsync(newId, cancellationToken).ConfigureAwait(false);
            _activeExternalPlayerId = newId;
            _playerSecret = freshSecret;
            return (newId, freshSecret);
        }

        /// <summary>
        /// Forget the persisted identity (secret + active id). The next
        /// player-scoped call triggers a fresh
        /// <see cref="EnsureIdentityAsync"/> and either resumes from a
        /// different stored id or registers a new player.
        /// </summary>
        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            var id = _activeExternalPlayerId;
            if (!string.IsNullOrEmpty(id))
            {
                try { await _secretStore.RemoveAsync(id!, cancellationToken).ConfigureAwait(false); }
                catch { /* best-effort */ }
            }
            try { await _secretStore.ClearActiveExternalPlayerIdAsync(cancellationToken).ConfigureAwait(false); }
            catch { /* best-effort */ }
            _activeExternalPlayerId = null;
            _playerSecret = null;
        }

        /// <summary>
        /// Install an explicit identity on this client (and persist it
        /// via the SecretStore so subsequent launches resume to it).
        /// Use when a player signs in via your own auth on a new device
        /// and you've fetched their secret out of your own backend.
        /// </summary>
        public async Task SignInAsync(string externalPlayerId, string secret, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(externalPlayerId)) throw new ArgumentException("externalPlayerId is required", nameof(externalPlayerId));
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("secret is required", nameof(secret));
            await _secretStore.WriteAsync(externalPlayerId, secret, cancellationToken).ConfigureAwait(false);
            await _secretStore.WriteActiveExternalPlayerIdAsync(externalPlayerId, cancellationToken).ConfigureAwait(false);
            _activeExternalPlayerId = externalPlayerId;
            _playerSecret = secret;
        }

        /// <summary>
        /// Resolves the player id for a resource call: explicit
        /// <paramref name="as"/> wins; falls back to a lazy
        /// <see cref="EnsureIdentityAsync"/>. Internal helper for the
        /// resource clients.
        /// </summary>
        internal async Task<string> ResolvePlayerIdAsync(string? @as, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(@as)) return @as!;
            var identity = await EnsureIdentityAsync(cancellationToken).ConfigureAwait(false);
            return identity.ExternalPlayerId;
        }

        /// <summary>
        /// Low-level: fire a JSON request. Resource clients call this.
        /// </summary>
        public async Task<T> RequestAsync<T>(
            HttpMethod method,
            string path,
            object? body = null,
            CancellationToken cancellationToken = default
        ) where T : class
        {
            var url = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
            var idempotencyKey = ResolveIdempotencyKey(method, body);
            var requestBody = AttachIdempotencyKey(body, idempotencyKey);

            Exception? lastErr = null;
            for (int attempt = 1; attempt <= _retry.Attempts; attempt++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var res = await FireOnceAsync(method, url, requestBody, cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    _onRequest?.Invoke(new RequestInfo
                    {
                        Method = method.Method,
                        Url = url,
                        Attempt = attempt,
                        IdempotencyKey = idempotencyKey,
                        Duration = sw.Elapsed,
                        Status = (int)res.StatusCode,
                        Ok = res.IsSuccessStatusCode,
                    });

                    if (res.IsSuccessStatusCode)
                    {
                        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var maybeErr = TryParseErrorEnvelope(text);
                        if (maybeErr != null)
                            throw new KratyApiError((int)res.StatusCode, maybeErr.Code, maybeErr.Message, maybeErr.Details);

                        if (string.IsNullOrEmpty(text)) return default!;
                        return JsonConvert.DeserializeObject<T>(text, JsonOptions)!;
                    }

                    var apiErr = await AsApiErrorAsync(res).ConfigureAwait(false);
                    if (RetryableStatuses.Contains((int)res.StatusCode) && attempt < _retry.Attempts)
                    {
                        await SleepBackoffAsync(attempt, res, cancellationToken).ConfigureAwait(false);
                        lastErr = apiErr;
                        continue;
                    }
                    throw apiErr;
                }
                catch (KratyApiError)
                {
                    throw;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException oce && oce.CancellationToken == cancellationToken))
                {
                    sw.Stop();
                    _onRequest?.Invoke(new RequestInfo
                    {
                        Method = method.Method,
                        Url = url,
                        Attempt = attempt,
                        IdempotencyKey = idempotencyKey,
                        Duration = sw.Elapsed,
                        Ok = false,
                    });
                    var wrapped = new KratyNetworkError(ex.Message, ex);
                    if (attempt < _retry.Attempts)
                    {
                        await SleepBackoffAsync(attempt, null, cancellationToken).ConfigureAwait(false);
                        lastErr = wrapped;
                        continue;
                    }
                    throw wrapped;
                }
            }
            throw lastErr ?? new KratyNetworkError("exhausted retries");
        }

        private async Task<HttpResponseMessage> FireOnceAsync(
            HttpMethod method,
            string url,
            object? body,
            CancellationToken cancellationToken
        )
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.TryAddWithoutValidation("authorization", _authHeader);
            req.Headers.TryAddWithoutValidation("accept", "application/json");
            req.Headers.TryAddWithoutValidation("x-kraty-sdk", SdkUserAgent);
            var secret = _playerSecret;
            if (!string.IsNullOrEmpty(secret))
            {
                req.Headers.TryAddWithoutValidation("x-player-secret", secret);
            }
            if (body != null)
            {
                var payload = JsonConvert.SerializeObject(body, JsonOptions);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }

        private string? ResolveIdempotencyKey(HttpMethod method, object? body)
        {
            if (!IdempotentMethods.Contains(method)) return null;
            if (body is IDictionary<string, object?> dict
                && dict.TryGetValue("idempotencyKey", out var v)
                && v is string s
                && !string.IsNullOrEmpty(s))
            {
                return s;
            }
            if (body is ProgressInput pi && !string.IsNullOrEmpty(pi.IdempotencyKey)) return pi.IdempotencyKey;
            if (body is ConsumeItemInput ci && !string.IsNullOrEmpty(ci.IdempotencyKey)) return ci.IdempotencyKey;
            if (body is DebitWalletInput di && !string.IsNullOrEmpty(di.IdempotencyKey)) return di.IdempotencyKey;
            return _generateIdempotencyKey();
        }

        private object? AttachIdempotencyKey(object? body, string? key)
        {
            if (key == null) return body;
            if (body == null) return new Dictionary<string, object?> { ["idempotencyKey"] = key };

            if (body is IDictionary<string, object?> dict)
            {
                if (!dict.ContainsKey("idempotencyKey")) dict["idempotencyKey"] = key;
                return dict;
            }

            if (body is ProgressInput pi)
            {
                if (string.IsNullOrEmpty(pi.IdempotencyKey)) pi.IdempotencyKey = key;
                return pi;
            }
            if (body is ConsumeItemInput ci)
            {
                if (string.IsNullOrEmpty(ci.IdempotencyKey)) ci.IdempotencyKey = key;
                return ci;
            }
            if (body is DebitWalletInput di)
            {
                if (string.IsNullOrEmpty(di.IdempotencyKey)) di.IdempotencyKey = key;
                return di;
            }

            // Newtonsoft equivalent of System.Text.Json's
            // SerializeToNode: round-trip through JObject using the
            // configured settings (camelCase + ignore-null), then
            // attach the idempotency key if it isn't already there.
            var serializer = JsonSerializer.Create(JsonOptions);
            var node = JToken.FromObject(body, serializer);
            if (node is JObject obj && obj["idempotencyKey"] == null)
            {
                obj["idempotencyKey"] = key;
            }
            return node;
        }

        private async Task SleepBackoffAsync(int attempt, HttpResponseMessage? res, CancellationToken cancellationToken)
        {
            if (res?.Headers.RetryAfter is RetryConditionHeaderValue ra)
            {
                if (ra.Delta is TimeSpan d)
                {
                    var capped = d > _retry.MaxDelay ? _retry.MaxDelay : d;
                    await Task.Delay(capped, cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (ra.Date is DateTimeOffset dt)
                {
                    var d2 = dt - DateTimeOffset.UtcNow;
                    if (d2 < TimeSpan.Zero) d2 = TimeSpan.Zero;
                    if (d2 > _retry.MaxDelay) d2 = _retry.MaxDelay;
                    await Task.Delay(d2, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            var baseMs = _retry.InitialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            if (baseMs > _retry.MaxDelay.TotalMilliseconds) baseMs = _retry.MaxDelay.TotalMilliseconds;
            double jittered;
            lock (_jitterRng)
            {
                var rand = _jitterRng.NextDouble() * 2 - 1;
                jittered = baseMs * (1 + rand * _retry.Jitter);
            }
            if (jittered < 0) jittered = 0;
            await Task.Delay(TimeSpan.FromMilliseconds(jittered), cancellationToken).ConfigureAwait(false);
        }

        private static KratyErrorPayload? TryParseErrorEnvelope(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                var root = JToken.Parse(text);
                if (root.Type != JTokenType.Object) return null;
                var err = root["error"];
                if (err == null || err.Type != JTokenType.Object) return null;
                var code = (string?)err["code"];
                var msg = (string?)err["message"];
                if (string.IsNullOrEmpty(code)) return null;
                Dictionary<string, object?>? details = null;
                var d = err["details"];
                if (d != null)
                {
                    details = new Dictionary<string, object?> { ["raw"] = d.ToString(Formatting.None) };
                }
                return new KratyErrorPayload(code!, msg ?? string.Empty, details);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<KratyApiError> AsApiErrorAsync(HttpResponseMessage res)
        {
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var env = TryParseErrorEnvelope(text);
            if (env != null) return new KratyApiError((int)res.StatusCode, env.Code, env.Message, env.Details);
            return new KratyApiError(
                (int)res.StatusCode,
                KratyErrorCode.InternalError,
                $"non-2xx response without an error envelope (status={(int)res.StatusCode})"
            );
        }

        // Self-serve externalPlayerId for fresh signups. Prefixed so a
        // glance at the audit log distinguishes SDK-minted ids from
        // your own. UUID v4 under the hood — collision resistance is
        // good enough for the lifetime of a single device.
        private static string GenerateExternalPlayerId()
        {
            return $"kp_{Guid.NewGuid().ToString("N")}";
        }

        // Picks PlayerPrefsSecretStore on Unity, in-memory elsewhere.
        // Most game clients never need to override — the default does
        // the right thing per platform.
        private static ISecretStore DefaultSecretStore()
        {
#if UNITY_5_3_OR_NEWER
            return new PlayerPrefsSecretStore();
#else
            return new InMemorySecretStore();
#endif
        }
    }
}
