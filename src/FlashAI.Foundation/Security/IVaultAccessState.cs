namespace FlashAI.Foundation.Security;

public interface IVaultAccessState
{
    bool IsUnlocked { get; }

    void SetUnlocked(bool unlocked);
}

public sealed class VaultAccessState : IVaultAccessState
{
    public bool IsUnlocked { get; private set; }

    public void SetUnlocked(bool unlocked) => IsUnlocked = unlocked;
}
