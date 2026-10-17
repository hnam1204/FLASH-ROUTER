using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;
using FlashAI.Modules.Connections.Integration;

namespace FlashAI.Modules.Connections.Application;

public sealed class OfficialAuthorizationPort : IOfficialAuthorizationPort
{
    private readonly OpenAiOAuthAuthorizationService _service;

    public OfficialAuthorizationPort(OpenAiOAuthAuthorizationService service)
    {
        _service = service;
    }

    public Task<Result<OAuthAuthorizationRequest>> StartAsync(
        Guid accountId,
        string? issuedClientId = null,
        Guid? registrationId = null,
        string? expectedProviderSubject = null,
        CancellationToken cancellationToken = default)
        => _service.StartAsync(accountId, issuedClientId, registrationId, expectedProviderSubject, cancellationToken);

    public Task<OAuthConnectionResult> HandleCallbackAsync(
        Guid attemptId,
        OAuthCallbackInput callback,
        CancellationToken cancellationToken = default)
        => _service.HandleCallbackAsync(attemptId, callback, cancellationToken);
}
