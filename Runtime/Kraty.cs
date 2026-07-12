using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kraty
{
    /// <summary>
    /// Convenience facade: instantiate one <see cref="Kraty"/> instead
    /// of wiring <see cref="KratyClient"/> + each resource client by
    /// hand. All resource clients share the same underlying
    /// <see cref="KratyClient"/> so retry config, telemetry, and the
    /// HTTP connection pool are shared.
    ///
    /// <example>
    /// <code><![CDATA[
    /// using var kraty = new Kraty(new KratyClientOptions {
    ///     ApiKey = "<your-client-sdk-key>",
    /// });
    /// // No id plumbing; the SDK lazily registers a player on the
    /// // first player-scoped call and persists it via PlayerPrefs.
    /// var events = await kraty.Events.ListForPlayerAsync();
    /// ]]></code>
    /// </example>
    /// </summary>
    public sealed class Kraty : IDisposable
    {
        public KratyClient Client { get; }
        public EventsClient Events { get; }
        public LeaderboardsClient Leaderboards { get; }
        public EventLeaderboardsClient EventLeaderboards { get; }
        public GrantsClient Grants { get; }
        public LobbiesClient Lobbies { get; }
        public InventoryClient Inventory { get; }
        public WalletClient Wallet { get; }
        public PlayersClient Players { get; }
        public CatalogClient Catalog { get; }

        public Kraty(KratyClientOptions opts)
        {
            Client = new KratyClient(opts);
            Events = new EventsClient(Client);
            Leaderboards = new LeaderboardsClient(Client);
            EventLeaderboards = new EventLeaderboardsClient(Client);
            Grants = new GrantsClient(Client);
            Lobbies = new LobbiesClient(Client);
            Inventory = new InventoryClient(Client);
            Wallet = new WalletClient(Client);
            Players = new PlayersClient(Client);
            Catalog = new CatalogClient(Client);
        }

        public void Dispose() => Client.Dispose();

        /// <summary>
        /// The active player this SDK is currently representing. Null
        /// until the first player-scoped call resolves the identity
        /// (or <see cref="EnsureIdentityAsync"/> is awaited
        /// explicitly).
        /// </summary>
        public string? ActiveExternalPlayerId => Client.ActiveExternalPlayerId;

        /// <summary>
        /// Resolve the active player identity, registering a fresh one
        /// if no persisted identity exists. Most games don't need to
        /// call this, since any player-scoped method triggers it
        /// transparently. Reach for it when you want the id available
        /// before the first request.
        /// </summary>
        public Task<(string ExternalPlayerId, string Secret)> EnsureIdentityAsync(CancellationToken ct = default)
            => Client.EnsureIdentityAsync(ct);

        /// <summary>
        /// Forget the persisted identity. The next player-scoped call
        /// lazily registers a new player (or resumes a different id if
        /// the SecretStore holds one).
        /// </summary>
        public Task LogoutAsync(CancellationToken ct = default) => Client.LogoutAsync(ct);

        /// <summary>
        /// Install an explicit identity on this SDK and persist it.
        /// Use when your own auth gave you back a Kraty
        /// <c>externalPlayerId</c> + <c>secret</c>, e.g. on a new
        /// device after a server-side device-link flow.
        /// </summary>
        public Task SignInAsync(string externalPlayerId, string secret, CancellationToken ct = default)
            => Client.SignInAsync(externalPlayerId, secret, ct);

        /// <summary>
        /// Finalization catch-up (docs/05b). <see cref="OnFinalized"/> fires
        /// when a board the player is in ends: live over SSE while subscribed,
        /// OR via <see cref="CheckFinalizationsAsync"/> for boards that
        /// finalized while they were away (call it on app foreground /
        /// reconnect). Both paths deliver exactly once.
        /// <see cref="DismissAsync"/> / <see cref="ClearReportedAsync"/>
        /// acknowledge handled results so they leave storage.
        /// </summary>
        public Action OnFinalized(Action<FinalizationResult> cb) => Client.OnFinalized(cb);

        /// <summary>Poll tracked boards; report + return any that finalized while away.</summary>
        public Task<List<FinalizationResult>> CheckFinalizationsAsync() => Client.CheckFinalizationsAsync();

        /// <summary>Acknowledge a handled finalization, dropping it from the registry.</summary>
        public Task DismissAsync(MembershipRef @ref) => Client.DismissAsync(@ref);

        /// <summary>Bulk-drop every already-reported membership. Returns the count.</summary>
        public Task<int> ClearReportedAsync() => Client.ClearReportedAsync();

        /// <summary>
        /// Bootstrap a player-authenticated <see cref="Kraty"/> in one
        /// call. Reads the stored secret from
        /// <paramref name="secretStore"/>, registers if absent, retries
        /// with <c>force: true</c> on a <c>player_already_registered</c>
        /// 409 (dev/test envs only, since production keys reject
        /// <c>force</c> and the 409 surfaces to the caller), persists
        /// the secret, and returns a <see cref="Kraty"/> wired with the
        /// secret on every subsequent request.
        ///
        /// <para>
        /// Most apps don't need this; <c>new Kraty(new KratyClientOptions { ApiKey = ... })</c>
        /// is enough and lazily registers on the first call. Use this
        /// when you want the register I/O to happen at a specific
        /// moment (e.g. behind a loading screen).
        /// </para>
        /// </summary>
        public static async Task<Kraty> ConnectAsPlayerAsync(
            KratyClientOptions options,
            string externalPlayerId,
            ISecretStore secretStore,
            bool autoRotateOnConflict = true,
            CancellationToken cancellationToken = default
        )
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(externalPlayerId)) throw new ArgumentException("externalPlayerId is required", nameof(externalPlayerId));
            if (secretStore == null) throw new ArgumentNullException(nameof(secretStore));

            var stored = await secretStore.ReadAsync(externalPlayerId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stored))
            {
                await secretStore.WriteActiveExternalPlayerIdAsync(externalPlayerId, cancellationToken).ConfigureAwait(false);
                return new Kraty(options
                    .WithPlayerSecret(stored)
                    .WithActiveExternalPlayerId(externalPlayerId));
            }

            // First-launch / wiped-data path: register via a pre-register
            // client (no PlayerSecret) so we don't keep a long-lived
            // client around without a secret.
            var preRegister = new Kraty(options.WithPlayerSecret(null));
            try
            {
                PlayerRegistration reg;
                try
                {
                    reg = await preRegister.Players.RegisterAsync(externalPlayerId, force: false, ct: cancellationToken).ConfigureAwait(false);
                }
                catch (KratyApiError err) when (autoRotateOnConflict && err.IsPlayerAlreadyRegistered)
                {
                    reg = await preRegister.Players.RegisterAsync(externalPlayerId, force: true, ct: cancellationToken).ConfigureAwait(false);
                }
                await secretStore.WriteAsync(externalPlayerId, reg.Secret, cancellationToken).ConfigureAwait(false);
                await secretStore.WriteActiveExternalPlayerIdAsync(externalPlayerId, cancellationToken).ConfigureAwait(false);
                return new Kraty(options
                    .WithPlayerSecret(reg.Secret)
                    .WithActiveExternalPlayerId(externalPlayerId));
            }
            finally
            {
                preRegister.Dispose();
            }
        }

        /// <summary>
        /// Recover the active player identity from a
        /// <see cref="ISecretStore"/>. Returns null when the device has
        /// never connected or after a logout. Use on app boot to
        /// decide between "auto-resume" and "show onboarding".
        /// </summary>
        public static async Task<StoredIdentity?> ReadStoredIdentityAsync(
            ISecretStore secretStore,
            CancellationToken cancellationToken = default
        )
        {
            if (secretStore == null) throw new ArgumentNullException(nameof(secretStore));
            var externalPlayerId = await secretStore.ReadActiveExternalPlayerIdAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(externalPlayerId)) return null;
            var secret = await secretStore.ReadAsync(externalPlayerId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(secret)) return null;
            return new StoredIdentity(externalPlayerId, secret);
        }

        /// <summary>
        /// Writes <paramref name="identity"/> into
        /// <paramref name="secretStore"/> so a subsequent
        /// <see cref="ReadStoredIdentityAsync"/> returns it.
        /// </summary>
        public static async Task RestoreStoredIdentityAsync(
            ISecretStore secretStore,
            StoredIdentity identity,
            CancellationToken cancellationToken = default
        )
        {
            if (secretStore == null) throw new ArgumentNullException(nameof(secretStore));
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            await secretStore.WriteAsync(identity.ExternalPlayerId, identity.Secret, cancellationToken).ConfigureAwait(false);
            await secretStore.WriteActiveExternalPlayerIdAsync(identity.ExternalPlayerId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Forgets the device's active-player marker WITHOUT erasing
        /// per-player secrets. Use this on "log out" or "switch user".
        /// </summary>
        public static Task ClearStoredIdentityAsync(
            ISecretStore secretStore,
            CancellationToken cancellationToken = default
        )
        {
            if (secretStore == null) throw new ArgumentNullException(nameof(secretStore));
            return secretStore.ClearActiveExternalPlayerIdAsync(cancellationToken);
        }
    }
}
