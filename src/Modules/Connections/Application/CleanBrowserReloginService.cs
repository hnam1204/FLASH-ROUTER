using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class CleanBrowserReloginService : ICleanBrowserReloginService
{
    private readonly BrowserAuthCredentialReader _credentialReader;
    private readonly IEphemeralBrowserAuthOrchestrator _orchestrator;

    public CleanBrowserReloginService(
        BrowserAuthCredentialReader credentialReader,
        IEphemeralBrowserAuthOrchestrator orchestrator)
    {
        _credentialReader = credentialReader;
        _orchestrator = orchestrator;
    }

    public async Task<BrowserAuthResult> ExecuteAsync(
        Guid accountId,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var credentialResult = await _credentialReader.ReadAsync(accountId, cancellationToken);
        if (!credentialResult.IsSuccess || credentialResult.Credential is null)
        {
            return BrowserAuthResult.Failure(
                BrowserAuthState.Failed,
                credentialResult.Status,
                credentialResult.Message);
        }

        using var credential = new EphemeralCredentialContext(
            credentialResult.Credential.Email,
            credentialResult.Credential.Password,
            credentialResult.Credential.TotpSeed);
        var request = new EphemeralBrowserAuthRequest(
            credential,
            BrowserSessionMode.KeepBrowserSession,
            accountId);

        return await _orchestrator.AutoLoginAsync(request, progress, cancellationToken);
    }
}
