using FlashAI.Foundation.Contracts;
using FlashAI.Modules.Authenticator.Contracts;

namespace FlashAI.Modules.Authenticator.Application;

public sealed class PreviewAuthenticatorUiPort : IAuthenticatorUiPort
{
    private bool _isLocked = false;
    private readonly List<TotpCredentialVm> _credentials = [];

    public PreviewAuthenticatorUiPort()
    {
        _credentials.Add(new TotpCredentialVm(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("11111111-1111-1111-1111-111111111101"),
            "OpenAI (DEMO)",
            "account01@example.com",
            "JBSWY3••••••••••••XP",
            6,
            30,
            true
        ));
    }

    public Task<bool> IsVaultLockedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isLocked);
    }

    public Task<OperationResultVm> UnlockVaultAsync(string passphrase, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            return Task.FromResult(OperationResultVm.Failure("INVALID_PASSPHRASE", "Vui lòng nhập mật khẩu mở khóa."));
        }

        _isLocked = false;
        return Task.FromResult(OperationResultVm.Success("Đã mở khóa két bảo mật xem trước."));
    }

    public Task<OperationResultVm> LockVaultAsync(CancellationToken cancellationToken = default)
    {
        _isLocked = true;
        return Task.FromResult(OperationResultVm.Success("Đã khóa két bảo mật."));
    }

    public Task<IReadOnlyList<TotpCredentialVm>> GetCredentialsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (_isLocked)
        {
            return Task.FromResult<IReadOnlyList<TotpCredentialVm>>([]);
        }

        var items = _credentials.Where(c => c.AccountId == accountId).ToList();
        return Task.FromResult<IReadOnlyList<TotpCredentialVm>>(items);
    }

    public Task<TotpCodeVm?> GetCurrentCodeAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        if (_isLocked)
        {
            return Task.FromResult<TotpCodeVm?>(null);
        }

        var cred = _credentials.FirstOrDefault(c => c.CredentialId == credentialId);
        if (cred == null) return Task.FromResult<TotpCodeVm?>(null);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var remaining = 30 - (int)(now % 30);
        // Deterministic demo 6-digit code for demonstration
        var step = now / 30;
        var codeNum = (int)((Math.Abs(step.GetHashCode() ^ credentialId.GetHashCode()) % 900000) + 100000);

        return Task.FromResult<TotpCodeVm?>(new TotpCodeVm(codeNum.ToString(), remaining, true));
    }

    public Task<OperationResultVm> AddCredentialAsync(Guid accountId, string issuer, string label, string maskedOrDemoSeed, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Task.FromResult(OperationResultVm.Failure("INVALID_LABEL", "Nhãn khóa không được để trống."));
        }

        _credentials.Add(new TotpCredentialVm(
            Guid.NewGuid(),
            accountId,
            string.IsNullOrWhiteSpace(issuer) ? "Khóa mới (DEMO)" : issuer,
            label,
            "DEMO••••••••••••KEY",
            6,
            30,
            true
        ));

        return Task.FromResult(OperationResultVm.Success("Đã thêm khóa 2FA mẫu vào bộ nhớ xem trước."));
    }

    public Task<OperationResultVm> RemoveCredentialAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        _credentials.RemoveAll(c => c.CredentialId == credentialId);
        return Task.FromResult(OperationResultVm.Success("Đã xóa khóa 2FA mẫu."));
    }
}
