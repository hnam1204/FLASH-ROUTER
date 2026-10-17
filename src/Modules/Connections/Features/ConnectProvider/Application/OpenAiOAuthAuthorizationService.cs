using System.Collections.Concurrent;
using System.Text;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.OAuthCallback.Security;

namespace FlashAI.Modules.Connections.Features.ConnectProvider.Application;

public sealed class OpenAiOAuthAuthorizationService
{
    private readonly OpenAiOAuthOptions _options;
    private readonly IClock _clock;
    private readonly OpenAiHostIdentifierProvider _hostIdentifierProvider;
    private readonly ILoopbackCallbackEndpointFactory _endpointFactory;
    private readonly IOAuthTokenClient _tokenClient;
    private readonly OpenAiIdTokenValidator _idTokenValidator;
    private readonly OAuthTokenVaultStore _tokenVaultStore;
    private readonly IOAuthConnectionStore _connectionStore;
    private readonly ConcurrentDictionary<Guid, PendingAuthorization> _pending = new();

    public OpenAiOAuthAuthorizationService(
        OpenAiOAuthOptions options,
        IClock clock,
        OpenAiHostIdentifierProvider hostIdentifierProvider,
        ILoopbackCallbackEndpointFactory endpointFactory,
        IOAuthTokenClient tokenClient,
        OpenAiIdTokenValidator idTokenValidator,
        OAuthTokenVaultStore tokenVaultStore,
        IOAuthConnectionStore connectionStore)
    {
        _options = options;
        _clock = clock;
        _hostIdentifierProvider = hostIdentifierProvider;
        _endpointFactory = endpointFactory;
        _tokenClient = tokenClient;
        _idTokenValidator = idTokenValidator;
        _tokenVaultStore = tokenVaultStore;
        _connectionStore = connectionStore;
    }

    public async Task<Result<OAuthAuthorizationRequest>> StartAsync(
        Guid accountId,
        string? issuedClientId = null,
        Guid? registrationId = null,
        string? expectedProviderSubject = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.EligibilityConfirmed)
        {
            return Result<OAuthAuthorizationRequest>.Failure(
                "OAUTH_ELIGIBILITY_BLOCKED",
                "Official ChatGPT OAuth is not enabled for this application until eligibility is confirmed.");
        }

        if (accountId == Guid.Empty)
        {
            return Result<OAuthAuthorizationRequest>.Failure("ACCOUNT_REQUIRED", "An account is required to start authorization.");
        }

        var redirectUri = _endpointFactory.CreateRedirectUri();
        if (!LoopbackCallbackUri.IsValid(redirectUri))
        {
            return Result<OAuthAuthorizationRequest>.Failure(
                "INVALID_LOOPBACK_REDIRECT",
                "The OAuth callback must use the exact 127.0.0.1 loopback path.");
        }

        var hostIdentifier = await _hostIdentifierProvider.GetOrCreateAsync(cancellationToken);
        var state = OAuthSecurityPrimitives.CreateRandomBase64Url();
        var nonce = OAuthSecurityPrimitives.CreateRandomBase64Url();
        var verifier = OAuthSecurityPrimitives.CreateRandomBase64Url(48);
        var clientId = string.IsNullOrWhiteSpace(issuedClientId)
            ? OpenAiOAuthOptions.DynamicClientId
            : issuedClientId;
        var attemptId = Guid.NewGuid();
        var expiresAtUtc = _clock.UtcNow.Add(_options.AuthorizationAttemptLifetime);
        var pending = new PendingAuthorization(
            attemptId,
            accountId,
            registrationId ?? Guid.NewGuid(),
            expectedProviderSubject,
            clientId,
            hostIdentifier,
            redirectUri,
            state,
            nonce,
            verifier,
            expiresAtUtc);

        _pending[attemptId] = pending;
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("ext_agent_host_id", hostIdentifier),
            new("response_type", "code"),
            new("redirect_uri", redirectUri),
            new("scope", string.Join(' ', OfficialOAuthScopes.RequestedScopes.OrderBy(value => value, StringComparer.Ordinal))),
            new("resource", OpenAiOAuthOptions.Resource),
            new("state", state),
            new("nonce", nonce),
            new("code_challenge_method", "S256"),
            new("code_challenge", OAuthSecurityPrimitives.CreatePkceChallenge(verifier))
        };

        if (clientId == OpenAiOAuthOptions.DynamicClientId)
        {
            parameters.Add(new("agent_name_hint", _options.AgentName));
        }

        return Result<OAuthAuthorizationRequest>.Success(
            new OAuthAuthorizationRequest(
                attemptId,
                BuildAuthorizationUri(parameters),
                redirectUri,
                clientId,
                hostIdentifier,
                expiresAtUtc));
    }

    public async Task<OAuthConnectionResult> HandleCallbackAsync(
        Guid attemptId,
        OAuthCallbackInput callback,
        CancellationToken cancellationToken = default)
    {
        if (!_pending.TryRemove(attemptId, out var pending))
        {
            return OAuthConnectionResult.Failure("OAUTH_ATTEMPT_NOT_FOUND", "The OAuth authorization attempt is missing or already completed.");
        }

        if (_clock.UtcNow > pending.ExpiresAtUtc)
        {
            return OAuthConnectionResult.Failure("OAUTH_ATTEMPT_EXPIRED", "The OAuth authorization attempt expired.", ConnectionStatus.NeedsReauthorization);
        }

        if (string.IsNullOrWhiteSpace(callback.State)
            || !OAuthSecurityPrimitives.FixedTimeEquals(pending.State, callback.State))
        {
            return OAuthConnectionResult.Failure("STATE_MISMATCH", "The OAuth callback state is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            return OAuthConnectionResult.Failure(
                callback.Error == "access_denied" ? "ACCESS_DENIED" : "OAUTH_CALLBACK_ERROR",
                "The provider did not authorize this connection.",
                ConnectionStatus.AuthorizationRequired);
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            return OAuthConnectionResult.Failure("MISSING_AUTHORIZATION_CODE", "The OAuth callback did not contain an authorization code.");
        }

        var issuedClientId = pending.ClientId;
        if (pending.ClientId == OpenAiOAuthOptions.DynamicClientId)
        {
            if (string.IsNullOrWhiteSpace(callback.ClientId)
                || callback.ClientId == OpenAiOAuthOptions.DynamicClientId)
            {
                return OAuthConnectionResult.Failure("MISSING_ISSUED_CLIENT_ID", "The provider did not return an issued client ID for registration.");
            }

            issuedClientId = callback.ClientId;
        }
        else if (!string.IsNullOrWhiteSpace(callback.ClientId)
            && !string.Equals(callback.ClientId, pending.ClientId, StringComparison.Ordinal))
        {
            return OAuthConnectionResult.Failure("CLIENT_ID_MISMATCH", "The callback client ID does not match the selected registration.");
        }

        OAuthTokenSet tokenSet;
        try
        {
            tokenSet = await _tokenClient.ExchangeCodeAsync(
                new OAuthTokenExchangeRequest(
                    issuedClientId,
                    callback.Code,
                    pending.CodeVerifier,
                    pending.RedirectUri,
                    OpenAiOAuthOptions.Resource),
                cancellationToken);
        }
        catch (OAuthTokenClientException exception)
        {
            return OAuthConnectionResult.Failure(exception.ErrorCode, exception.SafeMessage, ConnectionStatus.Error);
        }

        VerifiedProviderIdentity identity;
        try
        {
            identity = await _idTokenValidator.ValidateAsync(
                tokenSet.IdToken ?? string.Empty,
                issuedClientId,
                pending.Nonce,
                cancellationToken);
        }
        catch (OAuthValidationException exception)
        {
            return OAuthConnectionResult.Failure(exception.ErrorCode, exception.Message);
        }

        if (pending.ExpectedProviderSubject is not null
            && !string.Equals(pending.ExpectedProviderSubject, identity.Subject, StringComparison.Ordinal))
        {
            return OAuthConnectionResult.Failure("PROVIDER_SUBJECT_MISMATCH", "The authorized identity does not match the selected account.");
        }

        var missingIdentityScope = OfficialOAuthScopes.IdentityScopes
            .FirstOrDefault(scope => !tokenSet.GrantedScopes.Contains(scope));
        if (missingIdentityScope is not null)
        {
            return OAuthConnectionResult.Failure("MISSING_GRANTED_SCOPE", "The provider did not grant the required identity scopes.");
        }

        var connectionId = Guid.NewGuid();
        try
        {
            await _tokenVaultStore.StoreAsync(connectionId, tokenSet, cancellationToken);
            await _connectionStore.SaveAuthorizedAsync(
                pending.AccountId,
                connectionId,
                pending.RegistrationId,
                OpenAiOAuthOptions.ProviderId,
                identity.Subject,
                issuedClientId,
                pending.HostIdentifier,
                _options.AgentName,
                tokenSet.GrantedScopes,
                _clock.UtcNow,
                _clock.UtcNow.AddSeconds(tokenSet.ExpiresIn),
                cancellationToken);
        }
        catch (SecretVaultException exception)
        {
            return OAuthConnectionResult.Failure(exception.ErrorCode, exception.Message);
        }
        catch (OAuthConnectionStoreException exception)
        {
            try
            {
                await _tokenVaultStore.ClearAsync(connectionId, CancellationToken.None);
            }
            catch (SecretVaultException)
            {
                // Keep the safe persistence error; ciphertext remains protected by the vault.
            }

            return OAuthConnectionResult.Failure(exception.ErrorCode, exception.SafeMessage);
        }

        return new OAuthConnectionResult(
            true,
            connectionId,
            ConnectionStatus.Authorized,
            tokenSet.GrantedScopes.Contains(OfficialOAuthScopes.ChatGptPlanUse),
            "OAuth authorization completed offline against the configured contract.",
            null);
    }

    public Task CancelAsync(Guid attemptId)
    {
        _pending.TryRemove(attemptId, out _);
        return Task.CompletedTask;
    }

    private static string BuildAuthorizationUri(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var query = string.Join(
            '&',
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        return $"{OpenAiOAuthOptions.AuthorizationEndpoint}?{query}";
    }

    private sealed record PendingAuthorization(
        Guid AttemptId,
        Guid AccountId,
        Guid RegistrationId,
        string? ExpectedProviderSubject,
        string ClientId,
        string HostIdentifier,
        string RedirectUri,
        string State,
        string Nonce,
        string CodeVerifier,
        DateTimeOffset ExpiresAtUtc);
}

public sealed class OAuthTokenClientException : Exception
{
    public OAuthTokenClientException(string errorCode, string safeMessage)
        : base(safeMessage)
    {
        ErrorCode = errorCode;
        SafeMessage = safeMessage;
    }

    public string ErrorCode { get; }
    public string SafeMessage { get; }
}

public sealed class OAuthConnectionStoreException : Exception
{
    public OAuthConnectionStoreException(string errorCode, string safeMessage)
        : base(safeMessage)
    {
        ErrorCode = errorCode;
        SafeMessage = safeMessage;
    }

    public string ErrorCode { get; }
    public string SafeMessage { get; }
}
