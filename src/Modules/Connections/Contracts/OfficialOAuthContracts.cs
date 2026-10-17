using FlashAI.Modules.Connections.Domain;

namespace FlashAI.Modules.Connections.Contracts;

public static class OfficialOAuthScopes
{
    public const string OpenId = "openid";
    public const string Profile = "profile";
    public const string Email = "email";
    public const string OfflineAccess = "offline_access";
    public const string ResourceInvoke = "resource.invoke";
    public const string ChatGptPlanUse = "chatgpt.tokens.use.direct";

    public static readonly IReadOnlySet<string> IdentityScopes =
        new HashSet<string>([OpenId, Profile, Email], StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> RequestedScopes =
        new HashSet<string>(
            [OpenId, Profile, Email, OfflineAccess, ResourceInvoke, ChatGptPlanUse],
            StringComparer.Ordinal);
}

public sealed record OAuthAuthorizationRequest(
    Guid AttemptId,
    string AuthorizationUri,
    string RedirectUri,
    string ClientId,
    string HostIdentifier,
    DateTimeOffset ExpiresAtUtc);

public sealed record OAuthCallbackInput(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription,
    string? ClientId,
    string? Scope);

public sealed record OAuthConnectionResult(
    bool IsSuccess,
    Guid? ConnectionId,
    ConnectionStatus Status,
    bool ChatGptPlanUsageEnabled,
    string? SafeMessage,
    string? ErrorCode)
{
    public static OAuthConnectionResult Failure(string code, string message, ConnectionStatus status = ConnectionStatus.Error)
        => new(false, null, status, false, message, code);
}

public sealed record ConnectionStatusDetails(
    Guid AccountId,
    Guid? ConnectionId,
    string Provider,
    string State,
    string StateLabel,
    string? AuthorizedIdentity,
    DateTimeOffset? AuthorizedAt,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastRefreshAt,
    IReadOnlyList<string> GrantedScopes,
    string? SafeErrorSummary,
    bool ChatGptPlanUsageEnabled,
    bool IsBackendIntegrated);

public sealed record OAuthTokenSet(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    string TokenType,
    int ExpiresIn,
    IReadOnlySet<string> GrantedScopes);

public sealed record VerifiedProviderIdentity(
    string Issuer,
    string Subject,
    string? Email,
    string? Name);

public sealed record OAuthTokenExchangeRequest(
    string ClientId,
    string Code,
    string CodeVerifier,
    string RedirectUri,
    string Resource);

public interface IOAuthTokenClient
{
    Task<OAuthTokenSet> ExchangeCodeAsync(OAuthTokenExchangeRequest request, CancellationToken cancellationToken = default);

    Task<OAuthTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default);
}

public interface IOpenAiJwksResolver
{
    Task<IReadOnlyList<OpenAiJsonWebKey>> GetKeysAsync(CancellationToken cancellationToken = default);
}

public sealed record OpenAiJsonWebKey(
    string KeyId,
    string KeyType,
    string Modulus,
    string Exponent,
    string? Algorithm = "RS256");

public interface IOAuthConnectionStore
{
    Task SaveAuthorizedAsync(
        Guid accountId,
        Guid connectionId,
        Guid registrationId,
        string providerId,
        string providerSubject,
        string issuedClientId,
        string hostIdentifier,
        string agentName,
        IReadOnlySet<string> grantedScopes,
        DateTimeOffset authorizedAtUtc,
        DateTimeOffset accessTokenExpiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<OAuthConnectionSnapshot?> GetAsync(Guid connectionId, CancellationToken cancellationToken = default);

    Task<OAuthConnectionSnapshot?> GetForAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OAuthConnectionSnapshot>> ListForAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task MarkStatusAsync(Guid connectionId, ConnectionStatus status, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default);

    Task MarkRefreshedAsync(Guid connectionId, DateTimeOffset refreshedAtUtc, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default);
}

public sealed record OAuthConnectionSnapshot(
    Guid AccountId,
    Guid ConnectionId,
    Guid RegistrationId,
    string ProviderId,
    string ProviderSubject,
    string IssuedClientId,
    string HostIdentifier,
    IReadOnlySet<string> GrantedScopes,
    ConnectionStatus Status,
    DateTimeOffset AuthorizedAtUtc,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset? LastRefreshAtUtc,
    DateTimeOffset? LastVerifiedAtUtc);

public interface IOfficialOAuthEligibility
{
    bool IsConfirmedForThisApplication { get; }

    string StatusMessage { get; }
}
