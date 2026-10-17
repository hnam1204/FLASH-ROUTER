using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Subscriptions.Application;

public sealed class ObservationBoundary
{
    public Result RejectUntilVerifiedSource()
        => Result.Failure("OBSERVATION_NOT_ENABLED", "Manual observations are deferred to the durable data phase.");
}
