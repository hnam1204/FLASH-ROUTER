using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Integration;

public interface IOfficialAuthorizationPort
{
    Task<Result<OAuthAuthorizationRequest>> StartAsync(
        Guid accountId,
        string? issuedClientId = null,
        Guid? registrationId = null,
        string? expectedProviderSubject = null,
        CancellationToken cancellationToken = default);

    Task<OAuthConnectionResult> HandleCallbackAsync(
        Guid attemptId,
        OAuthCallbackInput callback,
        CancellationToken cancellationToken = default);
}
