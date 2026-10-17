using FlashAI.Foundation.Contracts;

namespace FlashAI.Modules.Authenticator.Application;

public sealed class AuthenticatorBoundary
{
    public static Result VaultIsAvailable()
        => Result.Success();
}
