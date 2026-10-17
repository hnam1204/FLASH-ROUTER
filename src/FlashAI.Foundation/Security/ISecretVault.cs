namespace FlashAI.Foundation.Security;

public interface ISecretVault
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    ValueTask StoreAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default);

    ValueTask<ReadOnlyMemory<byte>?> ReadAsync(string key, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
