using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Application;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using FlashAI.Modules.Connections.Features.OAuthCallback.Security;
using FlashAI.Modules.Connections.Features.RefreshConnection.Application;
using FlashAI.Modules.Connections.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace FlashAI.IntegrationTests;

public sealed class Be03OAuthTests
{
    [Fact]
    public async Task OfflineAuthorizationUsesFreshStateNoncePkceAndPersistsOnlyVaultTokenReferences()
    {
        using var fixture = OAuthFixture.Create();
        var start = await fixture.Service.StartAsync(Guid.NewGuid());
        Assert.True(start.IsSuccess);
        var request = start.Value!;
        Assert.Equal(OpenAiOAuthOptions.DynamicClientId, request.ClientId);
        Assert.Contains("127.0.0.1", request.RedirectUri, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", request.AuthorizationUri, StringComparison.Ordinal);
        Assert.DoesNotContain("code_verifier", request.AuthorizationUri, StringComparison.Ordinal);
        Assert.Contains("agent_name_hint=FLASH%20ROUTER", request.AuthorizationUri, StringComparison.Ordinal);

        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        var result = await fixture.Service.HandleCallbackAsync(
            request.AttemptId,
            new OAuthCallbackInput("synthetic-code", QueryParameter(request.AuthorizationUri, "state"), null, null, "oaiapp_issued", null));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.ConnectionId);
        Assert.True(result.ChatGptPlanUsageEnabled);
        var access = await fixture.Vault.ReadAsync(OAuthTokenVaultStore.GetAccessKey(result.ConnectionId!.Value));
        Assert.Equal("synthetic-access", Encoding.UTF8.GetString(access!.Value.Span));
        var snapshot = await fixture.Store.GetAsync(result.ConnectionId.Value);
        Assert.Equal("subject-a", snapshot!.ProviderSubject);
        Assert.DoesNotContain("synthetic-access", JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallbackRejectsStateErrorsReplayAndExpiredAttempts()
    {
        using var fixture = OAuthFixture.Create();
        var first = await fixture.Service.StartAsync(Guid.NewGuid());
        var request = first.Value!;
        var state = QueryParameter(request.AuthorizationUri, "state");
        var mismatch = await fixture.Service.HandleCallbackAsync(request.AttemptId, new OAuthCallbackInput("code", "wrong", null, null, "oaiapp_issued", null));
        Assert.Equal("STATE_MISMATCH", mismatch.ErrorCode);

        var replay = await fixture.Service.HandleCallbackAsync(request.AttemptId, new OAuthCallbackInput("code", state, null, null, "oaiapp_issued", null));
        Assert.Equal("OAUTH_ATTEMPT_NOT_FOUND", replay.ErrorCode);

        var expired = await fixture.Service.StartAsync(Guid.NewGuid());
        fixture.Clock.UtcNowValue = fixture.Clock.UtcNow.AddMinutes(6);
        var expiredResult = await fixture.Service.HandleCallbackAsync(
            expired.Value!.AttemptId,
            new OAuthCallbackInput("code", QueryParameter(expired.Value.AuthorizationUri, "state"), null, null, "oaiapp_issued", null));
        Assert.Equal("OAUTH_ATTEMPT_EXPIRED", expiredResult.ErrorCode);
    }

    [Fact]
    public async Task CallbackRejectsInvalidIdTokenIssuerAudienceExpiryNonceAndSignature()
    {
        using var fixture = OAuthFixture.Create();
        var request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        var state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");

        fixture.TokenClient.IdToken = fixture.CreateToken(issuer: "https://evil.example");
        Assert.Equal("INVALID_ID_TOKEN_ISSUER", (await fixture.Service.HandleCallbackAsync(request.AttemptId, Callback(state))).ErrorCode);

        request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        fixture.TokenClient.IdToken = fixture.CreateToken(audience: "wrong-client");
        Assert.Equal("INVALID_ID_TOKEN_AUDIENCE", (await fixture.Service.HandleCallbackAsync(request.AttemptId, Callback(state))).ErrorCode);

        request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        fixture.TokenClient.IdToken = fixture.CreateToken(expiry: fixture.Clock.UtcNow.AddMinutes(-1));
        Assert.Equal("EXPIRED_ID_TOKEN", (await fixture.Service.HandleCallbackAsync(request.AttemptId, Callback(state))).ErrorCode);

        request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = "wrong-nonce";
        fixture.TokenClient.IdToken = fixture.CreateToken(nonce: "wrong-nonce");
        Assert.Equal("NONCE_MISMATCH", (await fixture.Service.HandleCallbackAsync(request.AttemptId, Callback(state))).ErrorCode);

        request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        fixture.TokenClient.IdToken = fixture.CreateToken(tamperSignature: true);
        Assert.Equal("INVALID_ID_TOKEN_SIGNATURE", (await fixture.Service.HandleCallbackAsync(request.AttemptId, Callback(state))).ErrorCode);
    }

    [Fact]
    public async Task CallbackRequiresIssuedClientIdForRegistrationAndRejectsAccountSubjectMismatch()
    {
        using var fixture = OAuthFixture.Create();
        var request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        var state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        var missingClient = await fixture.Service.HandleCallbackAsync(
            request.AttemptId,
            new OAuthCallbackInput("code", state, null, null, null, null));
        Assert.Equal("MISSING_ISSUED_CLIENT_ID", missingClient.ErrorCode);

        request = (await fixture.Service.StartAsync(Guid.NewGuid(), "oaiapp_saved", Guid.NewGuid(), "subject-a")).Value!;
        state = QueryParameter(request.AuthorizationUri, "state");
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        fixture.TokenClient.IdToken = fixture.CreateToken(subject: "subject-b", audience: "oaiapp_saved");
        var mismatch = await fixture.Service.HandleCallbackAsync(
            request.AttemptId,
            new OAuthCallbackInput("code", state, null, null, null, null));
        Assert.Equal("PROVIDER_SUBJECT_MISMATCH", mismatch.ErrorCode);
    }

    [Fact]
    public async Task MissingIdentityScopeDisablesOrRejectsAccordingToContract()
    {
        using var fixture = OAuthFixture.Create();
        var request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        fixture.TokenClient.GrantedScopes = new HashSet<string>(["openid", "profile"], StringComparer.Ordinal);
        var missing = await fixture.Service.HandleCallbackAsync(
            request.AttemptId,
            Callback(QueryParameter(request.AuthorizationUri, "state")));
        Assert.Equal("MISSING_GRANTED_SCOPE", missing.ErrorCode);

        request = (await fixture.Service.StartAsync(Guid.NewGuid())).Value!;
        fixture.TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
        fixture.TokenClient.GrantedScopes = new HashSet<string>(["openid", "profile", "email"], StringComparer.Ordinal);
        var identityOnly = await fixture.Service.HandleCallbackAsync(
            request.AttemptId,
            Callback(QueryParameter(request.AuthorizationUri, "state")));
        Assert.True(identityOnly.IsSuccess);
        Assert.False(identityOnly.ChatGptPlanUsageEnabled);
    }

    [Fact]
    public async Task RefreshRotationIsSerializedAndInvalidGrantRequiresReauthorization()
    {
        using var fixture = OAuthFixture.Create();
        var accountId = Guid.NewGuid();
        var connection = await fixture.AuthorizeAsync(accountId);
        fixture.TokenClient.Delay = TimeSpan.FromMilliseconds(40);
        var refreshes = await Task.WhenAll(
            fixture.SessionService.RefreshAsync(connection),
            fixture.SessionService.RefreshAsync(connection));
        Assert.All(refreshes, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, fixture.TokenClient.MaximumConcurrentCalls);

        var latest = await fixture.Vault.ReadAsync(OAuthTokenVaultStore.GetRefreshKey(connection));
        Assert.Equal("rotated-refresh-2", Encoding.UTF8.GetString(latest!.Value.Span));

        fixture.TokenClient.RejectWithInvalidGrant = true;
        var failed = await fixture.SessionService.RefreshAsync(connection);
        Assert.Equal("REAUTHORIZATION_REQUIRED", failed.ErrorCode);
        var snapshot = await fixture.Store.GetAsync(connection);
        Assert.Equal(ConnectionStatus.NeedsReauthorization, snapshot!.Status);
    }

    [Fact]
    public async Task DisconnectClearsVaultTokensAndKeepsRemoteRevocationTruthful()
    {
        using var fixture = OAuthFixture.Create();
        var accountId = Guid.NewGuid();
        var connection = await fixture.AuthorizeAsync(accountId);
        var result = await fixture.SessionService.DisconnectAsync(connection);
        Assert.True(result.IsSuccess);
        Assert.Null(await fixture.Vault.ReadAsync(OAuthTokenVaultStore.GetAccessKey(connection)));
        Assert.Equal(ConnectionStatus.Disconnected, (await fixture.Store.GetAsync(connection))!.Status);

        var second = await fixture.AuthorizeAsync(accountId);
        fixture.TokenClient.RevokeConfirmed = false;
        var unconfirmed = await fixture.SessionService.DisconnectAsync(second);
        Assert.Equal("REVOCATION_UNCONFIRMED", unconfirmed.ErrorCode);
        Assert.Null(await fixture.Vault.ReadAsync(OAuthTokenVaultStore.GetRefreshKey(second)));
    }

    [Fact]
    public async Task EligibilityAndLoopbackGatesPreventLiveFallbacks()
    {
        using var fixture = OAuthFixture.Create(eligibilityConfirmed: false);
        var blocked = await fixture.Service.StartAsync(Guid.NewGuid());
        Assert.Equal("OAUTH_ELIGIBILITY_BLOCKED", blocked.ErrorCode);

        using var invalidFixture = OAuthFixture.Create(endpointFactory: new InvalidEndpointFactory());
        var invalid = await invalidFixture.Service.StartAsync(Guid.NewGuid());
        Assert.Equal("INVALID_LOOPBACK_REDIRECT", invalid.ErrorCode);

        var preview = new PreviewConnectionUiPort();
        var status = await preview.GetStatusAsync(Guid.NewGuid());
        Assert.Equal("NotConfigured", status.State);
        Assert.Null(status.AuthorizedIdentity);
    }

    [Fact]
    public async Task ExistingSqliteDatabasePassesIntegrityCheckAndContainsOAuthMetadataSchema()
    {
        var databasePath = Path.Combine(WorkspaceRoot(), ".local", "data", "flashrouter.db");
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();

        await using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await integrityCommand.ExecuteScalarAsync());

        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('OAuthRegistrations', 'ConnectionSessions', 'TokenVaultReferences', 'ConnectionChecks');";
        Assert.Equal(4L, (long)(await tableCommand.ExecuteScalarAsync())!);
    }

    private static OAuthCallbackInput Callback(string state)
        => new("synthetic-code", state, null, null, "oaiapp_issued", null);

    private static string WorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the FLASH ROUTER solution root.");
    }

    private static string QueryParameter(string uri, string name)
    {
        var query = uri[(uri.IndexOf('?') + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == name)
            {
                return Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }
        }

        throw new InvalidOperationException($"Missing query parameter {name}.");
    }

    private sealed class OAuthFixture : IDisposable
    {
        private readonly RSA _signingKey;
        private readonly string _root;

        private OAuthFixture(
            string root,
            FileSecretVault vault,
            MutableClock clock,
            FakeTokenClient tokenClient,
            InMemoryOAuthConnectionStore store,
            OpenAiOAuthAuthorizationService service,
            OAuthConnectionSessionService sessionService,
            RSA signingKey)
        {
            _root = root;
            _signingKey = signingKey;
            Vault = vault;
            Clock = clock;
            TokenClient = tokenClient;
            Store = store;
            Service = service;
            SessionService = sessionService;
        }

        public FileSecretVault Vault { get; }
        public MutableClock Clock { get; }
        public FakeTokenClient TokenClient { get; }
        public InMemoryOAuthConnectionStore Store { get; }
        public OpenAiOAuthAuthorizationService Service { get; }
        public OAuthConnectionSessionService SessionService { get; }

        public static OAuthFixture Create(
            bool eligibilityConfirmed = true,
            ILoopbackCallbackEndpointFactory? endpointFactory = null)
        {
            var root = Path.Combine(WorkspaceRoot(), ".local", "test-data", "be03-oauth-" + Guid.NewGuid().ToString("N"));
            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(root, "keys")),
                builder => builder.SetApplicationName("FLASH ROUTER BE-03 TEST"));
            var vault = new FileSecretVault(Path.Combine(root, "vault"), provider);
            var clock = new MutableClock(DateTimeOffset.Parse("2026-10-15T10:00:00Z"));
            var tokenClient = new FakeTokenClient();
            var signingKey = RSA.Create(2048);
            var parameters = signingKey.ExportParameters(false);
            var jwks = new StaticJwksResolver([
                new OpenAiJsonWebKey(
                    "synthetic-key",
                    "RSA",
                    OAuthSecurityPrimitives.Base64UrlEncode(parameters.Modulus!),
                    OAuthSecurityPrimitives.Base64UrlEncode(parameters.Exponent!))]);
            var store = new InMemoryOAuthConnectionStore();
            var options = new OpenAiOAuthOptions("FLASH ROUTER", eligibilityConfirmed, TimeSpan.FromMinutes(5));
            var hostProvider = new OpenAiHostIdentifierProvider(vault);
            var tokenVault = new OAuthTokenVaultStore(vault);
            var validator = new OpenAiIdTokenValidator(jwks, clock);
            var service = new OpenAiOAuthAuthorizationService(
                options,
                clock,
                hostProvider,
                endpointFactory ?? new FixedLoopbackCallbackEndpointFactory(1455),
                tokenClient,
                validator,
                tokenVault,
                store);
            var sessionService = new OAuthConnectionSessionService(clock, tokenClient, tokenVault, store);
            var fixture = new OAuthFixture(root, vault, clock, tokenClient, store, service, sessionService, signingKey);
            tokenClient.Fixture = fixture;
            return fixture;
        }

        public async Task<Guid> AuthorizeAsync(Guid? accountId = null)
        {
            var request = (await Service.StartAsync(accountId ?? Guid.NewGuid())).Value!;
            TokenClient.Nonce = QueryParameter(request.AuthorizationUri, "nonce");
            TokenClient.IdToken = CreateToken();
            var result = await Service.HandleCallbackAsync(request.AttemptId, Callback(QueryParameter(request.AuthorizationUri, "state")));
            Assert.True(result.IsSuccess, result.SafeMessage);
            return result.ConnectionId!.Value;
        }

        public string CreateToken(
            string issuer = OpenAiOAuthOptions.Issuer,
            string audience = "oaiapp_issued",
            string? nonce = null,
            string subject = "subject-a",
            DateTimeOffset? expiry = null,
            bool tamperSignature = false)
        {
            var header = OAuthSecurityPrimitives.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"synthetic-key\",\"typ\":\"JWT\"}"));
            var payloadJson = JsonSerializer.Serialize(new
            {
                iss = issuer,
                aud = audience,
                exp = (expiry ?? Clock.UtcNow.AddMinutes(10)).ToUnixTimeSeconds(),
                nonce = nonce ?? TokenClient.Nonce,
                sub = subject,
                email = "synthetic@example.test",
                name = "Synthetic User"
            });
            var payload = OAuthSecurityPrimitives.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
            var input = Encoding.ASCII.GetBytes($"{header}.{payload}");
            var signature = _signingKey.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (tamperSignature)
            {
                signature[0] ^= 0x01;
            }

            return $"{header}.{payload}.{OAuthSecurityPrimitives.Base64UrlEncode(signature)}";
        }

        public void Dispose()
        {
            _signingKey.Dispose();
            Vault.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static string WorkspaceRoot()
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "FLASH ROUTER.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the FLASH ROUTER solution root.");
        }
    }

    private sealed class FakeTokenClient : IOAuthTokenClient
    {
        private int _activeRefreshes;
        private int _maximumConcurrentCalls;

        public OAuthFixture? Fixture { get; set; }
        public RSA? SigningKey { get; set; }
        public string Nonce { get; set; } = "nonce";
        public string? IdToken { get; set; }
        public IReadOnlySet<string> GrantedScopes { get; set; } = new HashSet<string>(OfficialOAuthScopes.RequestedScopes, StringComparer.Ordinal);
        public TimeSpan Delay { get; set; }
        public bool RejectExchange { get; set; }
        public bool RejectWithInvalidGrant { get; set; }
        public bool RevokeConfirmed { get; set; } = true;
        public int RefreshCalls { get; private set; }
        public int MaximumConcurrentCalls => _maximumConcurrentCalls;

        public Task<OAuthTokenSet> ExchangeCodeAsync(OAuthTokenExchangeRequest request, CancellationToken cancellationToken = default)
        {
            if (RejectExchange)
            {
                throw new OAuthTokenClientException("invalid_grant", "The authorization code was rejected.");
            }

            return Task.FromResult(new OAuthTokenSet(
                "synthetic-access",
                "synthetic-refresh",
                IdToken ?? Fixture!.CreateToken(nonce: Nonce, audience: request.ClientId),
                "Bearer",
                3600,
                GrantedScopes));
        }

        public async Task<OAuthTokenSet> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken = default)
        {
            if (RejectWithInvalidGrant)
            {
                throw new OAuthTokenClientException("invalid_grant", "The refresh token is invalid.");
            }

            RefreshCalls++;
            var active = Interlocked.Increment(ref _activeRefreshes);
            InterlockedMax(ref _maximumConcurrentCalls, active);
            try
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, cancellationToken);
                }

                return new OAuthTokenSet(
                    $"rotated-access-{RefreshCalls}",
                    $"rotated-refresh-{RefreshCalls}",
                    IdToken,
                    "Bearer",
                    3600,
                    GrantedScopes);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRefreshes);
            }
        }

        public Task<bool> RevokeAsync(string clientId, string refreshToken, CancellationToken cancellationToken = default)
            => Task.FromResult(RevokeConfirmed);

        private static void InterlockedMax(ref int location, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref location);
                if (current >= value || Interlocked.CompareExchange(ref location, value, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset utcNow)
        {
            UtcNowValue = utcNow;
        }

        public DateTimeOffset UtcNowValue { get; set; }
        public DateTimeOffset UtcNow => UtcNowValue;
    }

    private sealed class InvalidEndpointFactory : ILoopbackCallbackEndpointFactory
    {
        public string CreateRedirectUri() => "http://localhost:1455/auth/callback";
    }
}
