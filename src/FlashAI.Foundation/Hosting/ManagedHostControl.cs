using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Persistence;
using Microsoft.Extensions.Logging;

namespace FlashAI.Foundation.Hosting;

public static class ManagedHostControl
{
    public const string ReadyPath = "/health/ready";

    public static string GetPipeName(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var identity = $"{Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar)}|{Environment.UserName}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"FlashRouter-{Convert.ToHexString(digest)[..24]}";
    }

    public static string GetMutexName(string dataRoot)
        => $"Local\\FlashRouterHost-{GetPipeName(dataRoot)}";

    public static string GetSemaphoreName(string dataRoot)
        => $"Local\\FlashRouterHostSemaphore-{GetPipeName(dataRoot)}";
}

public sealed record ManagedHostControlRequest(string Command, string Secret);

public sealed record ManagedHostControlResponse(
    bool Accepted,
    string Status,
    string? ErrorCode,
    int ProcessId,
    string Endpoint,
    string Version,
    string HostPath,
    DateTimeOffset StartedAtUtc);

public sealed record ManagedHostInstanceMetadata(
    int ProcessId,
    string HostPath,
    string Endpoint,
    string Version,
    string PipeName,
    DateTimeOffset StartedAtUtc);

public sealed class ManagedHostControlHostedService : IHostedService, IAsyncDisposable
{
    private readonly LocalDataPaths _paths;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ManagedHostControlHostedService> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private Semaphore? _semaphore;
    private string? _secret;
    private ManagedHostInstanceMetadata? _metadata;
    private Task? _serverTask;

    public ManagedHostControlHostedService(
        LocalDataPaths paths,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime,
        ILogger<ManagedHostControlHostedService> logger)
    {
        _paths = paths;
        _configuration = configuration;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.Runtime);
        Directory.CreateDirectory(_paths.Logs);

        var semaphoreName = ManagedHostControl.GetSemaphoreName(_paths.Root);
        _semaphore = new Semaphore(1, 1, semaphoreName);
        if (!_semaphore.WaitOne(TimeSpan.Zero))
        {
            _semaphore.Dispose();
            _semaphore = null;
            throw new InvalidOperationException("Another FLASH ROUTER host already owns this data root.");
        }

        _secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(_paths.ControlSecretFile, _secret, new UTF8Encoding(false));

        _metadata = new ManagedHostInstanceMetadata(
            Environment.ProcessId,
            Path.GetFullPath(Environment.ProcessPath ?? string.Empty),
            ResolveEndpoint(),
            ResolveVersion(),
            ManagedHostControl.GetPipeName(_paths.Root),
            DateTimeOffset.UtcNow);
        File.WriteAllText(
            _paths.InstanceMetadataFile,
            JsonSerializer.Serialize(_metadata, JsonOptions),
            Encoding.UTF8);

        _serverTask = ServeAsync(_shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        if (_serverTask is not null)
        {
            await _serverTask.WaitAsync(cancellationToken);
        }

        CleanupRuntimeFiles();
        ReleaseSemaphore();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_serverTask is not null)
        {
            await _serverTask.ConfigureAwait(false);
        }

        CleanupRuntimeFiles();
        ReleaseSemaphore();
        _shutdown.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        var pipeName = ManagedHostControl.GetPipeName(_paths.Root);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                await HandleRequestAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                _logger.LogWarning("Managed host control channel was interrupted: {Message}", exception.Message);
            }
        }
    }

    private async Task HandleRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        var line = await reader.ReadLineAsync(cancellationToken);
        ManagedHostControlRequest? request = null;
        try
        {
            request = string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<ManagedHostControlRequest>(line, JsonOptions);
        }
        catch (JsonException)
        {
            // Return a generic protocol error without echoing request content.
        }

        if (request is null || !SecretsMatch(request.Secret))
        {
            await WriteResponseAsync(writer, "Unauthorized", "CONTROL_AUTH_FAILED", cancellationToken);
            return;
        }

        if (string.Equals(request.Command, "ping", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(writer, "Ready", null, cancellationToken);
            return;
        }

        if (string.Equals(request.Command, "stop", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(writer, "Stopping", null, cancellationToken);
            _lifetime.StopApplication();
            return;
        }

        await WriteResponseAsync(writer, "Rejected", "CONTROL_COMMAND_INVALID", cancellationToken);
    }

    private async Task WriteResponseAsync(
        StreamWriter writer,
        string status,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var metadata = _metadata ?? throw new InvalidOperationException("Managed host metadata is not initialized.");
        var response = new ManagedHostControlResponse(
            errorCode is null,
            status,
            errorCode,
            metadata.ProcessId,
            metadata.Endpoint,
            metadata.Version,
            metadata.HostPath,
            metadata.StartedAtUtc);
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions).AsMemory(), cancellationToken);
    }

    private bool SecretsMatch(string candidate)
    {
        if (_secret is null || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(_secret);
        var actual = Encoding.UTF8.GetBytes(candidate);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private string ResolveEndpoint()
    {
        var configured = _configuration["ASPNETCORE_URLS"] ?? _configuration["urls"];
        return configured?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "http://127.0.0.1:5270";
    }

    private static string ResolveVersion()
        => typeof(ManagedHostControlHostedService).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private void CleanupRuntimeFiles()
    {
        foreach (var path in new[] { _paths.ControlSecretFile, _paths.InstanceMetadataFile })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                _logger.LogWarning("Managed host runtime metadata could not be removed.");
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Managed host runtime metadata could not be removed.");
            }
        }
    }

    private void ReleaseSemaphore()
    {
        if (_semaphore is null)
        {
            return;
        }

        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // The semaphore may already have been released during a failed startup.
        }
        finally
        {
            _semaphore.Dispose();
            _semaphore = null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
