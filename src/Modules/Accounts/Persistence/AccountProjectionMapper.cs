using FlashAI.Modules.Accounts.Application;
using FlashAI.Modules.Accounts.Contracts;
using FlashAI.Modules.Accounts.Domain;

namespace FlashAI.Modules.Accounts.Persistence;

public static class AccountProjectionMapper
{
    public static AccountListItemVm ToListItem(AccountRecord account) => new(
        account.Id,
        account.NumericId,
        account.Email,
        account.DisplayName,
        account.Provider,
        account.IsFavorite,
        account.ConnectionStatus,
        AccountInputValidator.StatusLabel(account.ConnectionStatus),
        account.RecordedPlan,
        account.TrackingDateUtc,
        account.Tags.OrderBy(tag => tag.NormalizedValue).Select(tag => tag.Value).ToArray(),
        account.IsArchived);

    public static AccountDetailVm ToDetail(AccountRecord account) => new(
        account.Id,
        account.NumericId,
        account.Email,
        account.DisplayName,
        account.Notes,
        account.Provider,
        account.IsFavorite,
        account.ConnectionStatus,
        AccountInputValidator.StatusLabel(account.ConnectionStatus),
        account.RecordedPlan,
        account.TrackingDateUtc,
        account.DataSource,
        account.CreatedAtUtc,
        account.UpdatedAtUtc,
        account.LastCheckedAtUtc,
        account.RecordedBy,
        account.Tags.OrderBy(tag => tag.NormalizedValue).Select(tag => tag.Value).ToArray(),
        account.IsArchived,
        account.Version);
}
