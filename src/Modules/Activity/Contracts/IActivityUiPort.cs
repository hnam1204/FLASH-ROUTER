namespace FlashAI.Modules.Activity.Contracts;

public interface IActivityUiPort
{
    Task<IReadOnlyList<ActivityEventVm>> GetEventsAsync(
        Guid? accountId = null,
        string? severity = null,
        CancellationToken cancellationToken = default);
}
