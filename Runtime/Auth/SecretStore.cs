using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kraty
{
    /// <summary>
    /// Per-player secret persistence contract for
    /// <see cref="Kraty.ConnectAsPlayerAsync"/>. The SDK ships as
    /// netstandard2.1 so it can't depend on a platform-specific
    /// storage backend, so consumers implement this against whatever
    /// they ship:
    ///
    /// <list type="bullet">
    /// <item><description><b>Unity</b> → use the bundled
    /// <c>PlayerPrefsSecretStore</c>, or wrap your own secure
    /// keychain (Keychain on iOS, EncryptedSharedPreferences on
    /// Android via a plugin).</description></item>
    /// <item><description><b>Plain .NET</b> → wrap a config file or
    /// the platform secret store (DPAPI on Windows, libsecret on
    /// Linux).</description></item>
    /// <item><description><b>Tests</b> → use
    /// <see cref="InMemorySecretStore"/>.</description></item>
    /// </list>
    ///
    /// <para>
    /// Contract:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>ReadAsync</c> returns <c>null</c> (not throw)
    /// when no secret is stored. The SDK treats <c>null</c> and the
    /// empty string identically; both trigger a fresh
    /// <c>register</c>.</description></item>
    /// <item><description><c>WriteAsync</c> must be durable across app
    /// launches. The SDK calls it exactly once per successful
    /// register. Overwriting is fine; that's the rotation path.</description></item>
    /// <item><description><c>RemoveAsync</c> is called by your own logout
    /// flow; the SDK never calls it internally.</description></item>
    /// <item><description>Operations are awaited on the boot path, so
    /// implementations should be reasonably fast (under 50ms). Don't
    /// network-call from here.</description></item>
    /// <item><description>Key namespace is your problem: include
    /// <c>externalPlayerId</c> in the storage key so a device switching
    /// between two players doesn't mix secrets.</description></item>
    /// </list>
    /// </summary>
    public interface ISecretStore
    {
        /// <summary>
        /// Returns the stored secret for <paramref name="externalPlayerId"/>,
        /// or null if none is stored.
        /// </summary>
        Task<string?> ReadAsync(string externalPlayerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists <paramref name="secret"/> for <paramref name="externalPlayerId"/>.
        /// Overwrites any existing value (rotation).
        /// </summary>
        Task WriteAsync(string externalPlayerId, string secret, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the stored secret for <paramref name="externalPlayerId"/>.
        /// Used on logout or when the backend invalidates the secret.
        /// </summary>
        Task RemoveAsync(string externalPlayerId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the last <c>externalPlayerId</c> written via
        /// <see cref="WriteActiveExternalPlayerIdAsync"/>, or null when the
        /// store doesn't track an active id or none has been set.
        /// Default implementation returns null (no active-identity tracking).
        /// </summary>
        Task<string?> ReadActiveExternalPlayerIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        /// <summary>
        /// Persists <paramref name="externalPlayerId"/> as the device's
        /// "active player". Called automatically by
        /// <see cref="Kraty.ConnectAsPlayerAsync"/> on success. Default
        /// implementation is a no-op.
        /// </summary>
        Task WriteActiveExternalPlayerIdAsync(string externalPlayerId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <summary>
        /// Forgets the active-id marker without touching per-player
        /// secrets. Used on explicit logout / "switch user" flows.
        /// Default implementation is a no-op.
        /// </summary>
        Task ClearActiveExternalPlayerIdAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Result of <see cref="Kraty.ReadStoredIdentityAsync"/>: the
    /// last-active player and the secret needed to reconnect.
    /// </summary>
    public sealed class StoredIdentity
    {
        public string ExternalPlayerId { get; }
        public string Secret { get; }
        public StoredIdentity(string externalPlayerId, string secret)
        {
            ExternalPlayerId = externalPlayerId;
            Secret = secret;
        }
    }

    /// <summary>
    /// Volatile, process-local store. Intended for unit tests and
    /// short-lived bootstrap scripts. Production code SHOULD ship a
    /// persisted impl so a fresh launch doesn't trigger an avoidable
    /// <c>register?force=true</c> rotation.
    /// </summary>
    public sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new();
        private readonly object _gate = new();
        private string? _activeExternalPlayerId;

        public Task<string?> ReadAsync(string externalPlayerId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_secrets.TryGetValue(externalPlayerId, out var s) ? s : null);
            }
        }

        public Task WriteAsync(string externalPlayerId, string secret, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _secrets[externalPlayerId] = secret;
            }
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string externalPlayerId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _secrets.Remove(externalPlayerId);
                if (_activeExternalPlayerId == externalPlayerId)
                {
                    _activeExternalPlayerId = null;
                }
            }
            return Task.CompletedTask;
        }

        public Task<string?> ReadActiveExternalPlayerIdAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_activeExternalPlayerId);
            }
        }

        public Task WriteActiveExternalPlayerIdAsync(string externalPlayerId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _activeExternalPlayerId = externalPlayerId;
            }
            return Task.CompletedTask;
        }

        public Task ClearActiveExternalPlayerIdAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _activeExternalPlayerId = null;
            }
            return Task.CompletedTask;
        }
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>
    /// Unity-only <see cref="ISecretStore"/> backed by
    /// <c>UnityEngine.PlayerPrefs</c>. Reasonable default for prototypes;
    /// for shipped games on iOS / Android consider a Keychain /
    /// EncryptedSharedPreferences plugin instead; <c>PlayerPrefs</c>
    /// is unencrypted on disk and recoverable by anyone with physical
    /// device access.
    /// </summary>
    public sealed class PlayerPrefsSecretStore : ISecretStore
    {
        private readonly string _keyPrefix;

        // Captured at construction. Callers build this store on the Unity
        // main thread (Awake/Start), so this is the player-loop
        // SynchronizationContext. The SDK's identity resolver awaits its
        // network I/O with ConfigureAwait(false), so the continuations that
        // call into this store land on thread-pool threads. PlayerPrefs is
        // main-thread-only and throws off it ("can only be called from the
        // main thread"), so every access is marshalled back onto this
        // context. Null only if constructed off the player loop (e.g. a
        // background thread or a bare unit test), in which case we run
        // inline and trust the caller's threading.
        private readonly SynchronizationContext? _mainThread;

        public PlayerPrefsSecretStore(string keyPrefix = "kraty.playerSecret.")
        {
            _keyPrefix = keyPrefix;
            _mainThread = SynchronizationContext.Current;
        }

        // Sentinel suffix for the active-player marker. The trailing
        // `__active__` segment can't collide with a real externalPlayerId
        // because we restrict that to lowercase alphanumerics + a few
        // safe characters at the backend layer.
        private string ActiveKey => _keyPrefix + "__active__";

        // Run a PlayerPrefs access on the captured main-thread context. If
        // we have no context, or we're already on it, run inline to avoid a
        // pointless Post + frame of latency.
        private Task<T> OnMainThread<T>(Func<T> func)
        {
            if (_mainThread == null || _mainThread == SynchronizationContext.Current)
            {
                return Task.FromResult(func());
            }
            var tcs = new TaskCompletionSource<T>();
            _mainThread.Post(_ =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception e) { tcs.SetException(e); }
            }, null);
            return tcs.Task;
        }

        private Task OnMainThread(Action action) =>
            OnMainThread<object?>(() => { action(); return null; });

        public Task<string?> ReadAsync(string externalPlayerId, CancellationToken cancellationToken = default) =>
            OnMainThread<string?>(() =>
            {
                var key = _keyPrefix + externalPlayerId;
                var v = UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetString(key) : null;
                return string.IsNullOrEmpty(v) ? null : v;
            });

        public Task WriteAsync(string externalPlayerId, string secret, CancellationToken cancellationToken = default) =>
            OnMainThread(() =>
            {
                UnityEngine.PlayerPrefs.SetString(_keyPrefix + externalPlayerId, secret);
                UnityEngine.PlayerPrefs.Save();
            });

        public Task RemoveAsync(string externalPlayerId, CancellationToken cancellationToken = default) =>
            OnMainThread(() =>
            {
                UnityEngine.PlayerPrefs.DeleteKey(_keyPrefix + externalPlayerId);
                // If we just deleted the secret of the currently-active
                // player, also zap the active marker so a stored-identity
                // lookup doesn't return a usable id with a missing secret.
                if (UnityEngine.PlayerPrefs.HasKey(ActiveKey) &&
                    UnityEngine.PlayerPrefs.GetString(ActiveKey) == externalPlayerId)
                {
                    UnityEngine.PlayerPrefs.DeleteKey(ActiveKey);
                }
                UnityEngine.PlayerPrefs.Save();
            });

        public Task<string?> ReadActiveExternalPlayerIdAsync(CancellationToken cancellationToken = default) =>
            OnMainThread<string?>(() =>
            {
                var v = UnityEngine.PlayerPrefs.HasKey(ActiveKey)
                    ? UnityEngine.PlayerPrefs.GetString(ActiveKey)
                    : null;
                return string.IsNullOrEmpty(v) ? null : v;
            });

        public Task WriteActiveExternalPlayerIdAsync(string externalPlayerId, CancellationToken cancellationToken = default) =>
            OnMainThread(() =>
            {
                UnityEngine.PlayerPrefs.SetString(ActiveKey, externalPlayerId);
                UnityEngine.PlayerPrefs.Save();
            });

        public Task ClearActiveExternalPlayerIdAsync(CancellationToken cancellationToken = default) =>
            OnMainThread(() =>
            {
                UnityEngine.PlayerPrefs.DeleteKey(ActiveKey);
                UnityEngine.PlayerPrefs.Save();
            });
    }
#endif
}
