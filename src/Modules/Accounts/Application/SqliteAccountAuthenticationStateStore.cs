using FlashAI.Foundation.Contracts;
using FlashAI.Foundation.Persistence;
using FlashAI.Modules.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashAI.Modules.Accounts.Application;

public sealed class SqliteAccountAuthenticationStateStore : IAccountAuthenticationStateStore, IAccountAuthenticationStateReader
{
    private readonly IDbContextFactory<FlashAiDbContext> _contextFactory;

    public SqliteAccountAuthenticationStateStore(IDbContextFactory<FlashAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task RecordAsync(
        AccountAuthenticationStateUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (update.AccountId == Guid.Empty || update.AttemptId == Guid.Empty)
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var account = await context.Set<AccountRecord>()
            .SingleOrDefaultAsync(item => item.Id == update.AccountId, cancellationToken);
        if (account is null || account.IsArchived)
        {
            return;
        }

        // Unknown polling can observe the same state many times. Avoid a
        // needless row update when no durable authentication value changed.
        if (account.LastAuthenticationAttemptId == update.AttemptId
            && account.LastAuthenticationAttemptAtUtc == update.AttemptedAtUtc
            && string.Equals(account.AuthenticationState, update.AuthenticationState, StringComparison.Ordinal)
            && string.Equals(account.IdentityVerificationState, update.IdentityVerificationState, StringComparison.Ordinal)
            && account.LastBrowserSessionObservedAtUtc == update.BrowserSessionObservedAtUtc
            && account.LastAuthenticatedSessionConfirmedAtUtc == update.AuthenticatedSessionConfirmedAtUtc
            && account.LastIdentityVerifiedAtUtc == update.IdentityVerifiedAtUtc
            && string.Equals(account.LastObservationEvidenceSource, update.EvidenceSource, StringComparison.Ordinal)
            && string.Equals(account.LastObservationErrorCode, update.SanitizedFailureCode, StringComparison.Ordinal)
            && update.BrowserSessionObservedAtUtc is null
            && update.AuthenticatedSessionConfirmedAtUtc is null
            && update.IdentityVerifiedAtUtc is null)
        {
            return;
        }

        account.RecordAuthenticationState(
            update.AttemptId,
            update.AuthenticationState,
            update.IdentityVerificationState,
            update.AttemptedAtUtc,
            update.BrowserSessionObservedAtUtc,
            update.AuthenticatedSessionConfirmedAtUtc,
            update.IdentityVerifiedAtUtc,
            update.ObservedAtUtc,
            update.EvidenceSource,
            update.SanitizedFailureCode);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccountAuthenticationStateSnapshot?> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<AccountRecord>()
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Select(account => new AccountAuthenticationStateSnapshot(
                account.Id,
                account.LastAuthenticationAttemptId,
                account.Provider,
                account.AuthenticationState,
                account.IdentityVerificationState,
                account.LastAuthenticationAttemptAtUtc,
                account.LastBrowserSessionObservedAtUtc,
                account.LastAuthenticatedSessionConfirmedAtUtc,
                account.LastIdentityVerifiedAtUtc,
                account.LastObservationAtUtc,
                account.LastObservationEvidenceSource,
                account.LastObservationErrorCode))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
