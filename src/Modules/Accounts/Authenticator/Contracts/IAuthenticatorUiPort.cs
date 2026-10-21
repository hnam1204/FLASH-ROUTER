using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Authenticator.Contracts;

public interface IAuthenticatorUiPort
{
    Task<bool> IsVaultLockedAsync(CancellationToken cancellationToken = default);

    Task<OperationResultVm> UnlockVaultAsync(string passphrase, CancellationToken cancellationToken = default);

    Task<OperationResultVm> LockVaultAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TotpCredentialVm>> GetCredentialsAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<TotpCodeVm?> GetCurrentCodeAsync(Guid credentialId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> AddCredentialAsync(Guid accountId, string issuer, string label, string maskedOrDemoSeed, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RemoveCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default);

    Task<OperationResultVm> RemoveAccountCredentialsAsync(Guid accountId, CancellationToken cancellationToken = default);
}
