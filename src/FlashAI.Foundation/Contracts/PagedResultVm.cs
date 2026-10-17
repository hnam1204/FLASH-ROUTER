namespace FlashAI.Foundation.Contracts;

public sealed record PagedResultVm<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber = 1,
    int PageSize = 50)
{
    public static PagedResultVm<T> Empty() =>
        new([], 0, 1, 50);
}
