using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Activity.Application;

public sealed class ActivityBoundary
{
    public Result RejectUntilAuditStoreExists()
        => Result.Failure("ACTIVITY_NOT_ENABLED", "The durable audit store is deferred to Phase 0B.");
}
