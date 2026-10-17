using Microsoft.Data.Sqlite;

namespace FlashAI.Foundation.Persistence;

public static class SqliteRetryPolicy
{
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default,
        int maxAttempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation();
            }
            catch (Exception exception) when (IsBusy(exception) && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsBusy(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is SqliteException { SqliteErrorCode: 5 or 6 })
            {
                return true;
            }

            current = current.InnerException!;
        }

        return false;
    }
}
