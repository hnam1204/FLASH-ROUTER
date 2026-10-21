namespace FlashAI.Modules.Connections.Contracts;

public enum BrowserSessionMode
{
    Temporary,
    KeepBrowserSession
}

public sealed class EphemeralCredentialContext : IDisposable
{
    private string? _account;
    private string? _password;
    private string? _totpSeed;

    public EphemeralCredentialContext(string account, string password, string? totpSeed)
    {
        _account = account;
        _password = password;
        _totpSeed = totpSeed;
    }

    public string? Account => _account;
    public string? Password => _password;
    public string? TotpSeed => _totpSeed;
    public bool IsCleared => _account is null && _password is null && _totpSeed is null;

    public void Clear()
    {
        _account = null;
        _password = null;
        _totpSeed = null;
    }

    public void Dispose() => Clear();
}

public sealed record EphemeralBrowserAuthRequest(
    EphemeralCredentialContext Credential,
    BrowserSessionMode SessionMode,
    Guid AccountId = default);

public sealed record ConsoleProviderOption(
    string Id,
    string DisplayName,
    string Description,
    bool IsSupported,
    Uri OfficialStartUri,
    ConsoleProviderCapability Capability = ConsoleProviderCapability.Unsupported);

public enum ConsoleProviderCapability
{
    VerifiedWebAdapter,
    UnverifiedWeb,
    ApiOnly,
    Unsupported
}

public interface IConsoleProviderRegistry
{
    IReadOnlyList<ConsoleProviderOption> List();

    bool TryGet(string providerId, out ConsoleProviderOption? provider);
}

public interface IEphemeralBrowserAuthOrchestrator
{
    Task<BrowserAuthResult> AutoLoginAsync(
        EphemeralBrowserAuthRequest request,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface ICleanBrowserReloginService
{
    Task<BrowserAuthResult> ExecuteAsync(
        Guid accountId,
        IProgress<BrowserAuthProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IEphemeralBrowserProfileFactory
{
    Task<EphemeralBrowserProfilePreparationResult> PrepareAsync(
        BrowserSessionMode sessionMode,
        CancellationToken cancellationToken = default);
}

public sealed class EphemeralBrowserProfilePreparationResult : IAsyncDisposable
{
    private Func<ValueTask>? _cleanup;

    public EphemeralBrowserProfilePreparationResult(
        bool isSuccess,
        string status,
        string message,
        BrowserProfileSessionContext? context = null,
        Func<ValueTask>? cleanup = null)
    {
        IsSuccess = isSuccess;
        Status = status;
        Message = message;
        Context = context;
        _cleanup = cleanup;
    }

    public bool IsSuccess { get; }
    public string Status { get; }
    public string Message { get; }
    public BrowserProfileSessionContext? Context { get; }

    /// <summary>
    /// Transfers the generated profile directory to its durable account binding.
    /// Once promoted, attempt cleanup must not delete the account's saved session.
    /// </summary>
    public void TransferOwnership() => _cleanup = null;

    public async ValueTask DisposeAsync()
    {
        if (_cleanup is not null)
        {
            await _cleanup();
        }
    }
}

public enum ConsoleEventSeverity
{
    Info,
    Pass,
    Warning,
    Error
}

public sealed record AutoLoginConsoleEvent(
    long Sequence,
    Guid AttemptId,
    DateTimeOffset OccurredAtUtc,
    ConsoleEventSeverity Severity,
    string Stage,
    string Message,
    string? EventCode = null,
    long? DurationMs = null,
    bool IsTerminal = false);
