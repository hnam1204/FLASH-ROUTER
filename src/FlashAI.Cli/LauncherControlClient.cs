using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FlashAI.Foundation.Hosting;

namespace FlashAI.Cli;

public sealed class LauncherControlClient
{
    public async Task<ManagedHostControlResponse?> SendAsync(
        LauncherRuntimeOptions options,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.SecretFile))
        {
            return null;
        }

        var secret = ReadControlSecret(options.SecretFile);
        if (secret is null)
        {
            return null;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                options.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutSource.Token);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            var request = JsonSerializer.Serialize(new ManagedHostControlRequest(command, secret), JsonDefaults.Web);
            await writer.WriteLineAsync(request);
            var response = await reader.ReadLineAsync(timeoutSource.Token);
            var parsed = string.IsNullOrWhiteSpace(response)
                ? null
                : JsonSerializer.Deserialize<ManagedHostControlResponse>(response, JsonDefaults.Web);
            return parsed is not null && LauncherOwnershipVerifier.IsVerified(options, parsed)
                ? parsed
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? ReadControlSecret(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8)
                .Trim()
                .TrimStart('\uFEFF');
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
