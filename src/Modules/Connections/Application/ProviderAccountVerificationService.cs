using System.Net.Mail;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

/// <summary>
/// Compares identity observed in the account-owned browser UI with the selected
/// account. This service never reads private provider endpoints or session data.
/// </summary>
public sealed class ProviderAccountVerificationService : IProviderAccountVerificationService
{
    public async Task<ProviderIdentityVerificationResult> VerifyAsync(
        ProviderIdentityVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var observedAtUtc = DateTimeOffset.UtcNow;
        if (request.AccountId == Guid.Empty
            || request.AttemptId == Guid.Empty
            || request.BrowserProfileBindingId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return Result(
                request,
                IdentityVerificationStatus.Unverified,
                observedAtUtc,
                "Provider account verification context is incomplete.",
                "IDENTITY_CONTEXT_INCOMPLETE",
                null);
        }

        if (!request.ProviderId.Equals("openai-chatgpt", StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                request,
                IdentityVerificationStatus.Unsupported,
                observedAtUtc,
                "Provider identity observation is not enabled for this provider.",
                "IDENTITY_PROVIDER_UNSUPPORTED",
                null);
        }

        if (request.Session is not IBrowserIdentityObservationSession identitySession)
        {
            return Result(
                request,
                IdentityVerificationStatus.Unverified,
                observedAtUtc,
                "The managed session does not expose supported visible identity evidence.",
                "IDENTITY_OBSERVATION_UNSUPPORTED",
                null);
        }

        BrowserIdentityEvidence evidence;
        try
        {
            evidence = await identitySession.ReadIdentityEvidenceAsync(cancellationToken);
        }
        catch (BrowserAuthSessionException exception)
        {
            return Result(
                request,
                IdentityVerificationStatus.Unverified,
                observedAtUtc,
                exception.SafeMessage,
                exception.SafeCode,
                null);
        }

        if (!evidence.AuthenticatedShellDetected || !evidence.AccountMenuDetected)
        {
            return Result(
                request,
                IdentityVerificationStatus.Unverified,
                evidence.ObservedAtUtc,
                "The authenticated shell or account menu was not independently observed.",
                evidence.SafeReasonCode ?? "IDENTITY_EVIDENCE_INSUFFICIENT",
                evidence.ObservedEmail);
        }

        var observedEmail = NormalizeEmail(evidence.ObservedEmail);
        var intendedEmail = NormalizeEmail(request.IntendedAccountEmail);
        if (observedEmail is null || intendedEmail is null)
        {
            return Result(
                request,
                IdentityVerificationStatus.Unverified,
                evidence.ObservedAtUtc,
                "The account menu did not expose a comparable email address.",
                evidence.SafeReasonCode ?? "IDENTITY_EMAIL_UNAVAILABLE",
                observedEmail);
        }

        var status = string.Equals(observedEmail, intendedEmail, StringComparison.OrdinalIgnoreCase)
            ? IdentityVerificationStatus.IdentityVerified
            : IdentityVerificationStatus.Mismatch;
        var reason = status == IdentityVerificationStatus.IdentityVerified
            ? null
            : "IDENTITY_EMAIL_MISMATCH";

        return Result(
            request,
            status,
            evidence.ObservedAtUtc,
            evidence.EvidenceSource,
            reason,
            observedEmail);
    }

    private static ProviderIdentityVerificationResult Result(
        ProviderIdentityVerificationRequest request,
        IdentityVerificationStatus status,
        DateTimeOffset observedAtUtc,
        string evidenceSource,
        string? safeReasonCode,
        string? observedEmail)
        => new(
            request.AccountId,
            request.AttemptId,
            request.BrowserProfileBindingId,
            request.ProviderId,
            status,
            observedAtUtc,
            string.IsNullOrWhiteSpace(evidenceSource) ? "Visible provider UI" : evidenceSource,
            NormalizeEmail(observedEmail),
            safeReasonCode);

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        try
        {
            var parsed = new MailAddress(trimmed);
            return parsed.Address.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                ? parsed.Address.ToLowerInvariant()
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
