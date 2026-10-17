using System.Diagnostics;
using System.Text.Json;
using FlashAI.Foundation.Hosting;

namespace FlashAI.Cli;

internal static class LauncherOwnershipVerifier
{
    public static bool IsVerified(LauncherRuntimeOptions options, ManagedHostControlResponse response)
    {
        if (response.ProcessId <= 0 || !Uri.TryCreate(response.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        if (!string.Equals(endpoint.AbsoluteUri, options.Endpoint.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(options.MetadataFile))
        {
            return false;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ManagedHostInstanceMetadata>(
                File.ReadAllText(options.MetadataFile),
                JsonDefaults.Web);
            if (metadata is null
                || metadata.ProcessId != response.ProcessId
                || !string.Equals(metadata.PipeName, options.PipeName, StringComparison.Ordinal)
                || !Uri.TryCreate(metadata.Endpoint, UriKind.Absolute, out var metadataEndpoint)
                || !string.Equals(metadataEndpoint.AbsoluteUri, options.Endpoint.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var process = Process.GetProcessById(response.ProcessId);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
