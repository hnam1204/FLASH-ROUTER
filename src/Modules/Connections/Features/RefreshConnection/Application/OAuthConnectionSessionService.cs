using System.Collections.Concurrent;
using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Security;
using FlashAI.Modules.Connections.Contracts;
using FlashAI.Modules.Connections.Domain;
using FlashAI.Modules.Connections.Features.ConnectProvider.Application;

namespace FlashAI.Modules.Connections.Features.RefreshConnection.Application;

public sealed class OAuthConnectionSessionService
{
    private readonly IClock _clock;
    private readonly IOAuthTokenClient _tokenClient;
    private readonly OAuthTokenVaultStore _tokenVaultStore;
    private readonly IOAuthConnectionStore _connectionStore;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _refreshGates = new();

    public OAuthConnectionSessionService(
        IClock clock,
        IOAuthTokenClient tokenClient,
        OAuthTokenVaultStore tokenVaultStore,
        IOAuthConnectionStore connectionStore)
    {
        _clock = clock;
        _tokenClient = tokenClient;
        _tokenVaultStore = tokenVaultStore;
        _connectionStore = connectionStore;
    }

    public async Task<OperationResultVm> RefreshAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        if (connectionId == Guid.Empty)
        {
            return OperationResultVm.Failure("CONNECTION_REQUIRED", "A connection is required.");
        }

        var gate = _refreshGates.GetOrAdd(connectionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _connectionStore.GetAsync(connectionId, cancellationToken);
            if (snapshot is null)
            {
                return OperationResultVm.Failure("CONNECTION_NOT_FOUND", "The connection was not found.");
            }

            if (snapshot.Status is ConnectionStatus.Disconnected or ConnectionStatus.Revoked)
            {
                return OperationResultVm.Failure("CONNECTION_DISCONNECTED", "The connection is disconnected.");
            }

            var secondsRemaining = Math.Max(1, (int)(_clock.UtcNow - snapshot.AccessTokenExpiresAtUtc).TotalSeconds);
            var current = await _tokenVaultStore.ReadAsync(connectionId, snapshot.GrantedScopes, secondsRemaining, cancellationToken);
            if (current?.RefreshToken is null)
            {
                await _connectionStore.MarkStatusAsync(connectionId, ConnectionStatus.NeedsReauthorization, _clock.UtcNow, cancellationToken);
                return OperationResultVm.Failure("REFRESH_TOKEN_UNAVAILABLE", "Reauthorization is required for this connection.");
            }

            OAuthTokenSet replacement;
            try
            {
                replacement = await _tokenClient.RefreshAsync(snapshot.IssuedClientId, current.RefreshToken, cancellationToken);
            }
            catch (OAuthTokenClientException exception) when (IsUnusableRefreshToken(exception.ErrorCode))
            {
                await _tokenVaultStore.ClearAsync(connectionId, CancellationToken.None);
                await _connectionStore.MarkStatusAsync(connectionId, ConnectionStatus.NeedsReauthorization, _clock.UtcNow, cancellationToken);
                return OperationResultVm.Failure("REAUTHORIZATION_REQUIRED", "The provider refresh session is no longer valid.");
            }
            catch (OAuthTokenClientException)
            {
                return OperationResultVm.Failure("REFRESH_FAILED", "The provider could not refresh the connection; the existing session was preserved.");
            }

            var merged = replacement with
            {
                RefreshToken = replacement.RefreshToken ?? current.RefreshToken,
                IdToken = replacement.IdToken ?? current.IdToken,
                GrantedScopes = replacement.GrantedScopes.Count == 0 ? current.GrantedScopes : replacement.GrantedScopes
            };
            var refreshedAt = _clock.UtcNow;
            await _tokenVaultStore.StoreAsync(connectionId, merged, cancellationToken);
            await _connectionStore.MarkRefreshedAsync(
                connectionId,
                refreshedAt,
                refreshedAt.AddSeconds(merged.ExpiresIn),
                cancellationToken);
            return OperationResultVm.Success("The connection token set was refreshed.");
        }
        catch (SecretVaultException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.Message);
        }
        catch (OAuthConnectionStoreException exception)
        {
            return OperationResultVm.Failure(exception.ErrorCode, exception.SafeMessage);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OperationResultVm> DisconnectAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _connectionStore.GetAsync(connectionId, cancellationToken);
        if (snapshot is null)
        {
            return OperationResultVm.Failure("CONNECTION_NOT_FOUND", "The connection was not found.");
        }

        var current = await _tokenVaultStore.ReadAsync(connectionId, snapshot.GrantedScopes, 1, cancellationToken);
        var remoteConfirmed = true;
        if (!string.IsNullOrWhiteSpace(current?.RefreshToken))
        {
            try
            {
                remoteConfirmed = await _tokenClient.RevokeAsync(snapshot.IssuedClientId, current.RefreshToken, cancellationToken);
            }
            catch (OAuthTokenClientException)
            {
                remoteConfirmed = false;
            }
        }

        await _tokenVaultStore.ClearAsync(connectionId, CancellationToken.None);
        await _connectionStore.MarkStatusAsync(connectionId, ConnectionStatus.Disconnected, _clock.UtcNow, cancellationToken);
        return remoteConfirmed
            ? OperationResultVm.Success("The local connection was disconnected and remote revocation was confirmed.")
            : OperationResultVm.Failure("REVOCATION_UNCONFIRMED", "The local connection was disconnected, but remote revocation was not confirmed.");
    }

    private static bool IsUnusableRefreshToken(string errorCode)
        => errorCode.Equals("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("invalid_refresh_token", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("token_expired", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("refresh_token_expired", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("refresh_token_invalidated", StringComparison.OrdinalIgnoreCase)
            || errorCode.Equals("refresh_token_reused", StringComparison.OrdinalIgnoreCase);
}
