using FlashAI.Modules.Connections.Features.ProviderIntegration.Abstractions;

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
        DateTimeOffset updatedAtUtc)
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
}
