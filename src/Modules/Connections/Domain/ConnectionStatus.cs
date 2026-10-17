namespace FlashAI.Modules.Connections.Domain;

public enum ConnectionStatus
{
    NotConfigured,
    Pending,
    AuthorizationRequired,
    ProfileOnly,
    CredentialStored,
    Authorized,
    NeedsReauthorization,
    Unauthorized,
    RateLimited,
    VerificationUnavailable,
    NetworkError,
    Disconnected,
    Revoked,
    Error
}
