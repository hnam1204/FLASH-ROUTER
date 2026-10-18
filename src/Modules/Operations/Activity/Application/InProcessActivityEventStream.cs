using System.Collections.Concurrent;
using FlashAI.Modules.Activity.Contracts;

namespace FlashAI.Modules.Activity.Application;

public sealed class InProcessActivityEventStream : IActivityEventStream
{
    private const int MaximumSubscribers = 32;
    private readonly ConcurrentDictionary<Guid, Func<ActivityEventVm, Task>> _handlers = new();

    public IDisposable Subscribe(Func<ActivityEventVm, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.Count >= MaximumSubscribers)
        {
            throw new InvalidOperationException("Activity event subscriber limit reached.");
        }

        var id = Guid.NewGuid();
        _handlers[id] = handler;
        return new Subscription(() => _handlers.TryRemove(id, out _));
    }

    public void Publish(ActivityEventVm activityEvent)
    {
        foreach (var handler in _handlers.Values)
        {
            _ = DeliverAsync(handler, activityEvent);
        }
    }

    private static async Task DeliverAsync(Func<ActivityEventVm, Task> handler, ActivityEventVm activityEvent)
    {
        try
        {
            await handler(activityEvent);
        }
        catch
        {
            // A disconnected Blazor circuit must not block audit or auth workers.
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }
}
