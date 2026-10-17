using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Features.OAuthCallback.Security;

public sealed class StaticJwksResolver : IOpenAiJwksResolver
{
    private readonly IReadOnlyList<OpenAiJsonWebKey> _keys;

    public StaticJwksResolver(IReadOnlyList<OpenAiJsonWebKey> keys)
    {
        _keys = keys;
    }

    public Task<IReadOnlyList<OpenAiJsonWebKey>> GetKeysAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_keys);
    }
}
