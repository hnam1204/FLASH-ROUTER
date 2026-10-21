using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Domain;

public sealed class ProviderCredentialReferenceRecord
{
    private ProviderCredentialReferenceRecord()
    {
    }

    public ProviderCredentialReferenceRecord(
        Guid id,
        Guid connectionId,
        string providerId,
        string credentialId,
        ProviderCredentialKind credentialKind,
        string vaultKey,
        string? scope,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string protectionScheme = CredentialProtectionMetadata.CurrentScheme,
        int protectionFormatVersion = CredentialProtectionMetadata.CurrentFormatVersion)
    {
        Id = id;
        ConnectionId = connectionId;
        ProviderId = providerId;
        CredentialId = credentialId;
        CredentialKind = credentialKind;
        VaultKey = vaultKey;
        Scope = scope;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        ProtectionScheme = protectionScheme;
        ProtectionFormatVersion = protectionFormatVersion;
    }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public string CredentialId { get; private set; } = string.Empty;
    public ProviderCredentialKind CredentialKind { get; private set; }
    public string VaultKey { get; private set; } = string.Empty;
    public string? Scope { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string ProtectionScheme { get; private set; } = CredentialProtectionMetadata.CurrentScheme;
    public int ProtectionFormatVersion { get; private set; } = CredentialProtectionMetadata.CurrentFormatVersion;
}

public static class CredentialProtectionMetadata
{
    public const string CurrentScheme = "AspNetCoreDataProtection-DPAPI-CurrentUser";
    public const int CurrentFormatVersion = 1;
    public const string CurrentScope = "WindowsCurrentUser";
}

public sealed class ProviderCapabilityDefinitionRecord
{
    private ProviderCapabilityDefinitionRecord()
    {
    }

    public ProviderCapabilityDefinitionRecord(
        Guid id,
        string providerId,
        ProviderCapability capability,
        ProviderCredentialKind requiredCredentialKind,
        string endpointReference,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        ProviderId = providerId;
        Capability = capability;
        RequiredCredentialKind = requiredCredentialKind;
        EndpointReference = endpointReference;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public ProviderCapability Capability { get; private set; }
    public ProviderCredentialKind RequiredCredentialKind { get; private set; }
    public string EndpointReference { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}

public sealed class ProviderCheckRunRecord
{
    private ProviderCheckRunRecord()
    {
    }

    public ProviderCheckRunRecord(
        Guid id,
        Guid connectionId,
        string providerId,
        ProviderOperationStatus status,
        string safeErrorCode,
        string safeUserMessage,
        string correlationId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc)
    {
        Id = id;
        ConnectionId = connectionId;
        ProviderId = providerId;
        Status = status;
        SafeErrorCode = safeErrorCode;
        SafeUserMessage = safeUserMessage;
        CorrelationId = correlationId;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public ProviderOperationStatus Status { get; private set; }
    public string SafeErrorCode { get; private set; } = string.Empty;
    public string SafeUserMessage { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
}

public sealed class ProviderObservationRecord
{
    private ProviderObservationRecord()
    {
    }

    public ProviderObservationRecord(
        Guid id,
        Guid checkRunId,
        Guid connectionId,
        string providerId,
        ProviderCapability capability,
        string metricName,
        string value,
        string unit,
        string? currency,
        ProviderObservationSource source,
        ProviderObservationScope scope,
        DateTimeOffset? providerReportedAtUtc,
        DateTimeOffset observedAtUtc,
        DateTimeOffset? validUntilUtc,
        ProviderObservationStatus status)
    {
        Id = id;
        CheckRunId = checkRunId;
        ConnectionId = connectionId;
        ProviderId = providerId;
        Capability = capability;
        MetricName = metricName;
        Value = value;
        Unit = unit;
        Currency = currency;
        Source = source;
        Scope = scope;
        ProviderReportedAtUtc = providerReportedAtUtc;
        ObservedAtUtc = observedAtUtc;
        ValidUntilUtc = validUntilUtc;
        Status = status;
    }

    public Guid Id { get; private set; }
    public Guid CheckRunId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public ProviderCapability Capability { get; private set; }
    public string MetricName { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public string? Currency { get; private set; }
    public ProviderObservationSource Source { get; private set; }
    public ProviderObservationScope Scope { get; private set; }
    public DateTimeOffset? ProviderReportedAtUtc { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }
    public DateTimeOffset? ValidUntilUtc { get; private set; }
    public ProviderObservationStatus Status { get; private set; }
}

public sealed class ProviderSyncStateRecord
{
    private ProviderSyncStateRecord()
    {
    }

    public ProviderSyncStateRecord(
        Guid id,
        Guid connectionId,
        string providerId,
        bool enabled,
        DateTimeOffset? nextEligibleAtUtc,
        DateTimeOffset? lastSuccessfulSyncAtUtc,
        int failureCount,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        ConnectionId = connectionId;
        ProviderId = providerId;
        Enabled = enabled;
        NextEligibleAtUtc = nextEligibleAtUtc;
        LastSuccessfulSyncAtUtc = lastSuccessfulSyncAtUtc;
        FailureCount = failureCount;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset? NextEligibleAtUtc { get; private set; }
    public DateTimeOffset? LastSuccessfulSyncAtUtc { get; private set; }
    public int FailureCount { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void UpdateSchedule(
        DateTimeOffset? nextEligibleAtUtc,
        DateTimeOffset? lastSuccessfulSyncAtUtc,
        int failureCount,
        DateTimeOffset updatedAtUtc)
    {
        NextEligibleAtUtc = nextEligibleAtUtc;
        LastSuccessfulSyncAtUtc = lastSuccessfulSyncAtUtc;
        FailureCount = Math.Max(0, failureCount);
        UpdatedAtUtc = updatedAtUtc;
    }
}

public sealed class ProviderAccountObservationRecord
{
    private ProviderAccountObservationRecord()
    {
    }

    public ProviderAccountObservationRecord(
        Guid id,
        Guid accountId,
        Guid attemptId,
        Guid browserProfileBindingId,
        string providerId,
        string browserContextId,
        DateTimeOffset observedAtUtc,
        string locale,
        bool authenticatedShellDetected,
        bool accountMenuDetected,
        string observationStatus,
        string evidenceSurface,
        string? failureReasonCode)
    {
        Id = id;
        AccountId = accountId;
        AttemptId = attemptId;
        BrowserProfileBindingId = browserProfileBindingId;
        ProviderId = providerId;
        BrowserContextId = browserContextId;
        ObservedAtUtc = observedAtUtc;
        Locale = locale;
        AuthenticatedShellDetected = authenticatedShellDetected;
        AccountMenuDetected = accountMenuDetected;
        ObservationStatus = observationStatus;
        EvidenceSurface = evidenceSurface;
        FailureReasonCode = failureReasonCode;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid BrowserProfileBindingId { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;
    public string BrowserContextId { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; private set; }
    public string Locale { get; private set; } = string.Empty;
    public bool AuthenticatedShellDetected { get; private set; }
    public bool AccountMenuDetected { get; private set; }
    public string ObservationStatus { get; private set; } = string.Empty;
    public string EvidenceSurface { get; private set; } = string.Empty;
    public string? FailureReasonCode { get; private set; }
    public List<ProviderAccountObservationFieldRecord> Fields { get; private set; } = [];
}

public sealed class ProviderAccountObservationFieldRecord
{
    private ProviderAccountObservationFieldRecord()
    {
    }

    public ProviderAccountObservationFieldRecord(
        Guid id,
        Guid observationId,
        ProviderAccountObservationField field,
        string? value,
        string source,
        string evidenceType,
        string confidence,
        ProviderAccountObservationFieldStatus status,
        DateTimeOffset observedAtUtc,
        string? failureReasonCode)
    {
        Id = id;
        ObservationId = observationId;
        Field = field;
        Value = value;
        Source = source;
        EvidenceType = evidenceType;
        Confidence = confidence;
        Status = status;
        ObservedAtUtc = observedAtUtc;
        FailureReasonCode = failureReasonCode;
    }

    public Guid Id { get; private set; }
    public Guid ObservationId { get; private set; }
    public ProviderAccountObservationField Field { get; private set; }
    public string? Value { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string EvidenceType { get; private set; } = string.Empty;
    public string Confidence { get; private set; } = string.Empty;
    public ProviderAccountObservationFieldStatus Status { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }
    public string? FailureReasonCode { get; private set; }
}
