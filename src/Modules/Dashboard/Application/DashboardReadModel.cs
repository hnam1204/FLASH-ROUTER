namespace FlashAI.Modules.Dashboard.Application;

public sealed record DashboardReadModel(
    string AccountsStatus,
    string ConnectionsStatus,
    string RemindersStatus,
    string StorageStatus)
{
    public static DashboardReadModel EmptyPhase0A => new(
        "UNAVAILABLE",
        "UNAVAILABLE",
        "NOT RUNNING",
        "DESIGNED ONLY");
}
