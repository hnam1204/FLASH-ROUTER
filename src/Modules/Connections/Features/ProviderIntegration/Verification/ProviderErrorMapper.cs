using System.Net;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Features.ProviderIntegration.Registry;

namespace FlashAI.Modules.Connections.Features.ProviderIntegration.Verification;

public sealed class ProviderErrorMapper : IProviderErrorMapper
{
    private readonly IClock _clock;

    public ProviderErrorMapper(IClock clock)
    {
        _clock = clock;
    }

    public ProviderOperationResult Map(
        ProviderConnectionContext connection,
        ProviderCapability? capability,
        Exception exception,
        DateTimeOffset observedAtUtc,
        string correlationId)
    {
        if (exception is TaskCanceledException taskCanceled && !taskCanceled.CancellationToken.IsCancellationRequested)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.Timeout,
                "PROVIDER_TIMEOUT",
                "The provider did not respond before the timeout.",
                connection,
                observedAtUtc,
                correlationId,
                capability);
        }

        if (exception is OperationCanceledException)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.Cancelled,
                "OPERATION_CANCELLED",
                "The provider check was cancelled.",
                connection,
                observedAtUtc,
                correlationId,
                capability);
        }

        if (exception is SecretVaultException)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.ConfigurationError,
                "CREDENTIAL_VAULT_UNAVAILABLE",
                "The credential vault is unavailable; the provider was not contacted.",
                connection,
                observedAtUtc,
                correlationId,
                capability);
        }

        if (exception is ProviderConfigurationException)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.ConfigurationError,
                "PROVIDER_CONFIGURATION_INVALID",
                "The provider connection configuration is invalid.",
                connection,
                observedAtUtc,
                correlationId,
                capability);
        }

        if (exception is ProviderHttpException httpException)
        {
            var status = httpException.StatusCode switch
            {
                HttpStatusCode.Unauthorized => ProviderOperationStatus.Unauthorized,
                HttpStatusCode.Forbidden => ProviderOperationStatus.Forbidden,
                HttpStatusCode.TooManyRequests => ProviderOperationStatus.RateLimited,
                >= HttpStatusCode.InternalServerError => ProviderOperationStatus.ProviderUnavailable,
                _ => ProviderOperationStatus.InvalidResponse
            };
            var code = status switch
            {
                ProviderOperationStatus.Unauthorized => "PROVIDER_UNAUTHORIZED",
                ProviderOperationStatus.Forbidden => "PROVIDER_FORBIDDEN",
                ProviderOperationStatus.RateLimited => "PROVIDER_RATE_LIMITED",
                ProviderOperationStatus.ProviderUnavailable => "PROVIDER_UNAVAILABLE",
                _ => "PROVIDER_REQUEST_FAILED"
            };
            DateTimeOffset? retryAt = httpException.RetryAfter is null
                ? null
                : _clock.UtcNow.Add(httpException.RetryAfter.Value);
            return ProviderOperationResult.Failure(
                status,
                code,
                status == ProviderOperationStatus.Forbidden
                    ? "The credential does not have permission for this provider operation."
                    : "The provider operation could not be completed.",
                connection,
                observedAtUtc,
                correlationId,
                capability,
                retryAt);
        }

        if (exception is HttpRequestException)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.NetworkFailure,
                "PROVIDER_NETWORK_FAILURE",
                "The provider could not be reached; the last observation was preserved.",
                connection,
                observedAtUtc,
                correlationId,
                capability);
        }

        if (exception is TimeoutException)
        {
            return ProviderOperationResult.Failure(
                ProviderOperationStatus.Timeout,
                "PROVIDER_TIMEOUT",
                "The provider did not respond before the timeout.",
                connection,
                observedAtUtc,
                correlationId,
                capability);
        }

        return ProviderOperationResult.Failure(
            ProviderOperationStatus.InvalidResponse,
            "PROVIDER_OPERATION_FAILED",
            "The provider returned an unusable response.",
            connection,
            observedAtUtc,
            correlationId,
            capability);
    }
}

public sealed class ProviderHttpException : Exception
{
    public ProviderHttpException(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
        : base("The provider returned an HTTP error.")
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}
