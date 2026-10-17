using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using FlashAI.Modules.Connections.Contracts;

namespace FlashAI.Modules.Connections.Application;

public sealed class AutoLoginConsoleBuffer
{
    private static readonly Regex CredentialLikeValue = new(
        @"(?i)(password|pass|secret|seed|token|code|credential)\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly int _capacity;
    private readonly LinkedList<AutoLoginConsoleEvent> _events = [];
    private long _sequence;

    public AutoLoginConsoleBuffer(int capacity = 250)
    {
        _capacity = Math.Max(20, capacity);
    }

    public IReadOnlyList<AutoLoginConsoleEvent> Snapshot()
    {
        lock (_events)
        {
            return new ReadOnlyCollection<AutoLoginConsoleEvent>(_events.ToList());
        }
    }

    public AutoLoginConsoleEvent Publish(
        Guid attemptId,
        string stage,
        ConsoleEventSeverity severity,
        string message,
        string? eventCode = null,
        long? durationMs = null,
        bool isTerminal = false)
    {
        var entry = new AutoLoginConsoleEvent(
            Interlocked.Increment(ref _sequence),
            attemptId,
            DateTimeOffset.UtcNow,
            severity,
            stage,
            Sanitize(message),
            eventCode,
            durationMs,
            isTerminal);
        lock (_events)
        {
            _events.AddLast(entry);
            while (_events.Count > _capacity)
            {
                _events.RemoveFirst();
            }
        }

        return entry;
    }

    public void Clear() 
    {
        lock (_events)
        {
            _events.Clear();
        }
    }

    public string CopyText(IEnumerable<ConsoleEventSeverity>? severities = null)
    {
        var allowed = severities is null ? null : severities.ToHashSet();
        return string.Join(
            Environment.NewLine,
            Snapshot()
                .Where(item => allowed is null || allowed.Contains(item.Severity))
                .Select(item =>
                    $"[{item.OccurredAtUtc.ToLocalTime():HH:mm:ss}] [{item.Severity.ToString().ToUpperInvariant()}] "
                    + $"[{item.EventCode ?? item.Stage}] {item.Message}"
                    + (item.DurationMs is long duration ? $" ({duration}ms)" : string.Empty)));
    }

    private static string Sanitize(string message)
        => CredentialLikeValue.Replace(message ?? string.Empty, "$1=[REDACTED]");
}
