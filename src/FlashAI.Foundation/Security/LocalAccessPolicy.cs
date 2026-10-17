namespace FlashAI.Foundation.Security;

public sealed class LocalAccessPolicy
{
    public bool RequiresLocalHost => true;

    public bool AllowsSensitiveOperations(bool isLocalRequest, bool isVaultUnlocked)
        => isLocalRequest && isVaultUnlocked;
}
