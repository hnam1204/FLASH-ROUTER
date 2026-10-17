namespace FlashAI.Modules.Connections.Domain;

public sealed class OAuthRegistrationRecord
{
    private OAuthRegistrationRecord()
    {
    }

    public OAuthRegistrationRecord(
        Guid id,
        Guid accountId,
        string providerId,
        string issuedClientId,
        string hostIdentifier,
        string agentName,
        string? providerSubject,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        AccountId = accountId;
        ProviderId = providerId;
        IssuedClientId = issuedClientId;
        HostIdentifier = hostIdentifier;
        AgentName = agentName;
        ProviderSubject = providerSubject;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public string IssuedClientId { get; private set; } = string.Empty;
    public string HostIdentifier { get; private set; } = string.Empty;
    public string AgentName { get; private set; } = string.Empty;
    public string? ProviderSubject { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}

public sealed class ConnectionSessionRecord
{
    private ConnectionSessionRecord()
    {
    }

    public ConnectionSessionRecord(
        Guid id,
        Guid accountId,
        string providerId,
        Guid registrationId,
        Guid connectionId,
        string providerSubject,
        string issuedClientId,
        string hostIdentifier,
        string grantedScopes,
        ConnectionStatus status,
        DateTimeOffset authorizedAtUtc,
        DateTimeOffset accessTokenExpiresAtUtc,
        DateTimeOffset? lastRefreshAtUtc,
        DateTimeOffset? lastVerifiedAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        AccountId = accountId;
        ProviderId = providerId;
        RegistrationId = registrationId;
        ConnectionId = connectionId;
        ProviderSubject = providerSubject;
        IssuedClientId = issuedClientId;
        HostIdentifier = hostIdentifier;
        GrantedScopes = grantedScopes;
        Status = status;
        AuthorizedAtUtc = authorizedAtUtc;
        AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc;
        LastRefreshAtUtc = lastRefreshAtUtc;
        LastVerifiedAtUtc = lastVerifiedAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public Guid RegistrationId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderSubject { get; private set; } = string.Empty;
    public string IssuedClientId { get; private set; } = string.Empty;
    public string HostIdentifier { get; private set; } = string.Empty;
    public string GrantedScopes { get; private set; } = string.Empty;
    public ConnectionStatus Status { get; private set; }
    public DateTimeOffset AuthorizedAtUtc { get; private set; }
    public DateTimeOffset AccessTokenExpiresAtUtc { get; private set; }
    public DateTimeOffset? LastRefreshAtUtc { get; private set; }
    public DateTimeOffset? LastVerifiedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void MarkRefreshed(DateTimeOffset refreshedAtUtc, DateTimeOffset expiresAtUtc)
    {
        LastRefreshAtUtc = refreshedAtUtc;
        AccessTokenExpiresAtUtc = expiresAtUtc;
        UpdatedAtUtc = refreshedAtUtc;
        Status = ConnectionStatus.Authorized;
    }

    public void MarkStatus(ConnectionStatus status, DateTimeOffset updatedAtUtc)
    {
        Status = status;
        UpdatedAtUtc = updatedAtUtc;
    }
}

public sealed class TokenVaultReferenceRecord
{
    private TokenVaultReferenceRecord()
    {
    }

    public TokenVaultReferenceRecord(
        Guid id,
        Guid connectionId,
        string accessTokenKey,
        string refreshTokenKey,
        string idTokenKey,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        ConnectionId = connectionId;
        AccessTokenKey = accessTokenKey;
        RefreshTokenKey = refreshTokenKey;
        IdTokenKey = idTokenKey;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string AccessTokenKey { get; private set; } = string.Empty;
    public string RefreshTokenKey { get; private set; } = string.Empty;
    public string IdTokenKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}

public sealed class ConnectionCheckRecord
{
    private ConnectionCheckRecord()
    {
    }

    public ConnectionCheckRecord(
        Guid id,
        Guid connectionId,
        string checkType,
        string source,
        string result,
        DateTimeOffset observedAtUtc)
    {
        Id = id;
        ConnectionId = connectionId;
        CheckType = checkType;
        Source = source;
        Result = result;
        ObservedAtUtc = observedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string CheckType { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string Result { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; private set; }
}
