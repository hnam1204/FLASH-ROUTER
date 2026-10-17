using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Accounts.Application;

public sealed class AccountApplicationBoundary
{
    public Result<Guid> RejectUntilPhaseOne(CreateAccountCommand command)
        => Result<Guid>.Failure(ErrorCodes.NotImplemented, "Account persistence is deferred to Phase 1.");
}

internal static class ErrorCodes
{
    public const string NotImplemented = "NOT_IMPLEMENTED";
}
