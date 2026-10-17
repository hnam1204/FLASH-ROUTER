namespace FlashAI.Modules.Dashboard.Contracts;

public interface IDashboardContribution
{
    string Key { get; }

    ValueTask<DashboardContributionSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record DashboardContributionSnapshot(string Key, string Status, string? Summary);
