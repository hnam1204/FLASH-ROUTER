namespace FlashAI.Modules.Connections.Contracts;

public sealed record ChatGptCredentialInput(
    string Email,
    string Password,
    string? TotpSeed,
    bool OpenBrowserAfterSave);

public sealed record StoredChatGptCredential(
    string Email,
    string Password,
    string? TotpSeed);

public sealed record CredentialConnectionResult(
    bool IsSuccess,
    string Status,
    string Message,
    Guid? ConnectionId = null)
{
    public static CredentialConnectionResult Failure(string status, string message)
        => new(false, status, message);

    public static CredentialConnectionResult Success(string status, string message, Guid connectionId)
        => new(true, status, message, connectionId);
}

public interface IAccountCredentialUiPort
{
    Task<CredentialConnectionResult> SaveAsync(
        Guid accountId,
        ChatGptCredentialInput input,
        CancellationToken cancellationToken = default);

    Task<CredentialConnectionResult> ReplaceAsync(
        Guid accountId,
        ChatGptCredentialInput input,
        CancellationToken cancellationToken = default);
}

public sealed record AccountCopyResult(
    bool IsSuccess,
    string Status,
    string Message,
    string? ClipboardText = null,
    bool ContainsSecret = false)
{
    public static AccountCopyResult Failure(string status, string message)
        => new(false, status, message);

    public static AccountCopyResult Success(string status, string message, string clipboardText, bool containsSecret)
        => new(true, status, message, clipboardText, containsSecret);
}

public interface IAccountCopyUiPort
{
    Task<AccountCopyResult> CopyBasicAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<AccountCopyResult> CopySavedCredentialsAsync(
        Guid accountId,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

public sealed record OfficialLoginStartResult(bool IsSuccess, string Status, string Message)
{
    public static OfficialLoginStartResult Failure(string status, string message) => new(false, status, message);
    public static OfficialLoginStartResult Success(string status, string message) => new(true, status, message);
}

public interface IOfficialLoginUiPort
{
    Task<OfficialLoginStartResult> StartAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
